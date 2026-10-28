using JoyNect.Core.Mapping;
using JoyNect.Core.Output;
using Microsoft.Extensions.Logging;

namespace JoyNect.Core.Devices;

/// <summary>
/// A virtual pad together with the controllers feeding it.
/// </summary>
/// <remarks>
/// One source is the normal case (a Pro Controller). Two sources happen when a left and a right
/// Joy-Con are combined, which is why this type exists: the pad state has to be recomposed from
/// whichever halves are currently present, from two different reader threads, and the result must be
/// based on the newest input from each half rather than on the order the callbacks happen to arrive.
/// </remarks>
public sealed class PadBinding : IDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<SwitchControllerSession, SourceEntry> _sources = [];
    private readonly List<ComposeSource> _composeBuffer = [];
    private readonly IVirtualPad _pad;
    private readonly ILogger _logger;
    private readonly PadComposer _composer;
    private Xbox360PadState _lastState = Xbox360PadState.Neutral;
    private long _submitCount;
    private bool _disposed;

    public PadBinding(int slot, IVirtualPad pad, MappingProfile profile, ILogger logger)
    {
        Slot = slot;
        _pad = pad;
        _logger = logger;
        _composer = new PadComposer(profile);

        _pad.RumbleRequested += OnRumbleRequested;
        _pad.LedRequested += OnLedRequested;
    }

    public int Slot { get; }

    public string Description => _pad.Description;

    public MappingProfile Profile
    {
        get => _composer.Profile;
        set => _composer.Profile = value;
    }

    /// <summary>Raised when the last source is removed, so the manager can retire the pad.</summary>
    public event Action<PadBinding>? Emptied;

    public IReadOnlyList<SwitchControllerSession> Sources
    {
        get
        {
            lock (_gate)
            {
                return _sources.Keys.ToArray();
            }
        }
    }

    public bool IsEmpty
    {
        get
        {
            lock (_gate)
            {
                return _sources.Count == 0;
            }
        }
    }

    /// <summary>
    /// Last state submitted to the pad, for diagnostics and for the live pad view.
    /// </summary>
    /// <remarks>
    /// Read from other threads than the one that composes, and a pad state is too wide to be copied
    /// atomically, so it is read under the same lock rather than torn.
    /// </remarks>
    public Xbox360PadState LastState
    {
        get
        {
            lock (_gate)
            {
                return _lastState;
            }
        }
    }

    public long SubmitCount
    {
        get
        {
            lock (_gate)
            {
                return _submitCount;
            }
        }
    }

    public void AddSource(SwitchControllerSession session, StickLayout layout)
    {
        lock (_gate)
        {
            _sources[session] = new SourceEntry(layout, session.State == ControllerLinkState.Ready);
        }

        session.StateUpdated += OnSourceStateUpdated;
        session.LinkStateChanged += OnSourceLinkStateChanged;

        _logger.LogInformation(
            "Slot {Slot} now fed by {Controller} ({Layout})",
            Slot, session.Model.DisplayName, layout);

        Recompose();
    }

    public void RemoveSource(SwitchControllerSession session)
    {
        session.StateUpdated -= OnSourceStateUpdated;
        session.LinkStateChanged -= OnSourceLinkStateChanged;

        bool empty;
        lock (_gate)
        {
            _sources.Remove(session);
            empty = _sources.Count == 0;
            if (empty)
            {
                // Drive the pad to neutral and take it away rather than leaving a frozen controller:
                // a stuck stick in a game is worse than a controller that disappears.
                Submit(Xbox360PadState.Neutral);
            }
        }

        if (empty)
        {
            Emptied?.Invoke(this);
            return;
        }

        Recompose();
    }

    /// <summary>Recomputes and publishes the pad state; called from each source's reader thread.</summary>
    public void OnSourceUpdated(SwitchControllerSession session)
    {
        lock (_gate)
        {
            if (!_sources.ContainsKey(session))
            {
                return;
            }

            Recompose();
        }
    }

    /// <summary>
    /// Marks whether a source is currently delivering reports.
    /// </summary>
    /// <remarks>
    /// A controller that drops off mid-game must not leave its last input latched: an inactive
    /// source contributes a neutral state, so its buttons are released (and its stick centred) while
    /// the other half of a combined pair keeps working.
    /// </remarks>
    public void SetSourceActive(SwitchControllerSession session, bool active)
    {
        var changed = false;
        lock (_gate)
        {
            if (_sources.TryGetValue(session, out var entry) && entry.Active != active)
            {
                _sources[session] = entry with { Active = active };
                changed = true;
            }
        }

        if (changed)
        {
            Recompose();
        }
    }

    /// <summary>
    /// Recomputes and publishes the pad state; called from each source's reader thread.
    /// </summary>
    /// <remarks>
    /// The whole of this runs under the binding's lock, which matters more than it looks. A combined
    /// Joy-Con pad is recomposed from two reader threads, and if they interleave, one thread's state
    /// can be replaced by the other's between filling the scratch buffer and reading it, which drops
    /// a whole half of the pad: the stick of the half that lost the race stops moving. Serialising
    /// also keeps <see cref="LastState"/> coherent for the user interface, which reads it from yet
    /// another thread.
    /// </remarks>
    private void Recompose()
    {
        lock (_gate)
        {
            _composeBuffer.Clear();

            // In combined mode each half keeps its own stick, so the pair behaves like one pad.
            var combined = _composer.Profile.JoyConMode == JoyConMode.Combined;

            foreach (var (session, entry) in _sources)
            {
                var layout = combined ? LayoutFor(session, combined: true) : entry.Layout;
                _composeBuffer.Add(BuildSource(session, layout, entry.Active));
            }

            Submit(_composer.Compose(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_composeBuffer)));
        }
    }

    /// <summary>Publishes a state; must be called while holding the binding's lock.</summary>
    private void Submit(Xbox360PadState state)
    {
        _lastState = state;
        _submitCount++;
        _pad.Submit(state);
    }

    private static ComposeSource BuildSource(SwitchControllerSession session, StickLayout layout, bool active)
    {
        var input = SwitchPadInput.Neutral;
        if (active && session.TryGetLatest(out var latest))
        {
            input = new SwitchPadInput(latest.Buttons, latest.LeftStick, latest.RightStick);
        }

        return new ComposeSource(
            input,
            layout,
            session.LeftStickCalibration,
            session.RightStickCalibration,
            active);
    }

    /// <summary>
    /// Decides which of a controller's stick fields is real and where it should land on the pad.
    /// </summary>
    /// <remarks>
    /// A Joy-Con (R) reports its stick in the right hand field. Used alone it must still drive the
    /// pad's left stick, but paired with a left Joy-Con it becomes the pad's right stick.
    /// </remarks>
    public static StickLayout LayoutFor(SwitchControllerSession session, bool combined)
    {
        if (!Hid.SwitchModels.IsJoyCon(session.Model.Model))
        {
            return StickLayout.BothSticks;
        }

        return session.Model.Model switch
        {
            Hid.SwitchModel.JoyConLeft => combined ? StickLayout.LeftFieldToLeft : StickLayout.LeftFieldToLeft,
            Hid.SwitchModel.JoyConRight => combined ? StickLayout.RightFieldToRight : StickLayout.RightFieldToLeft,
            _ => StickLayout.BothSticks,
        };
    }

    private void OnSourceStateUpdated(SwitchControllerSession session) => OnSourceUpdated(session);

    private void OnSourceLinkStateChanged(SwitchControllerSession session)
    {
        // Ready means reports are flowing; anything else means this source must not hold inputs.
        SetSourceActive(session, session.State == ControllerLinkState.Ready);
    }

    private void OnRumbleRequested(byte large, byte small)
    {
        if (!_composer.Profile.RumbleEnabled)
        {
            return;
        }

        SwitchControllerSession[] sources;
        lock (_gate)
        {
            sources = _sources.Keys.ToArray();
        }

        foreach (var session in sources)
        {
            session.SetRumble(large, small);
        }
    }

    private void OnLedRequested(byte led)
    {
        // The driver's pad indicator matches the player slot, so the controller's own player lights
        // track the same number.
        var slot = led is >= 1 and <= 4 ? led : (byte)Slot;
        foreach (var session in Sources)
        {
            session.SetSlot(slot);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _pad.RumbleRequested -= OnRumbleRequested;
        _pad.LedRequested -= OnLedRequested;
        _pad.Dispose();
    }

    private sealed record SourceEntry(StickLayout Layout, bool Active);
}
