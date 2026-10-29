using JoyNect.Core.Hid;
using JoyNect.Core.Mapping;
using JoyNect.Core.Output;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace JoyNect.Core.Devices;

public sealed class ControllerManagerOptions
{
    /// <summary>How often to look for newly attached controllers.</summary>
    public TimeSpan DiscoveryInterval { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>How long to wait before retrying a device that failed.</summary>
    public TimeSpan RetryFailedDevicesAfter { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>XInput exposes four pads.</summary>
    public int MaxControllers { get; init; } = 4;

    /// <summary>When false, controllers are read and shown but no virtual pad is created.</summary>
    public bool CreateVirtualPads { get; init; } = true;

    /// <summary>When false, discovery reports devices but does not connect to them.</summary>
    public bool AutoConnect { get; init; } = true;

    public JoyConMode JoyConMode { get; init; } = JoyConMode.Sideways;

    public SwitchControllerSessionOptions SessionOptions { get; init; } = new();

    /// <summary>Builds the mapping profile for a controller that just connected.</summary>
    public Func<SwitchControllerSession, MappingProfile>? ProfileFactory { get; init; }
}

/// <summary>
/// Finds controllers, keeps them connected, and gives each one a virtual pad.
/// </summary>
/// <remarks>
/// Everything here is driven from a single discovery thread, so pad and slot bookkeeping needs no
/// locking. The reader threads only ever report state changes through <see cref="PadBinding"/>, which
/// does its own locking.
/// </remarks>
public sealed class ControllerManager : IDisposable
{
    private readonly IVirtualPadBackend _backend;
    private readonly ControllerManagerOptions _options;
    private readonly ILogger _logger;
    private readonly TimeProvider _timeProvider;
    private readonly IDeviceDiscovery _discovery;

    /// <summary>HID collections already tried per physical device, so a wrong pick is not permanent.</summary>
    private readonly Dictionary<string, HashSet<string>> _attemptedPaths = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Guards the bookkeeping dictionaries.</summary>
    private readonly object _gate = new();

    /// <summary>
    /// Serialises pad creation and removal.
    /// </summary>
    /// <remarks>
    /// Link state callbacks arrive on each controller's own reader thread, so two controllers can
    /// reach for a slot at the same time. Holding one lock across the whole "attach a pad" step is
    /// fine: it happens a handful of times per session at most.
    /// </remarks>
    private readonly object _padGate = new();

    private readonly Dictionary<string, DeviceRuntime> _devices = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTimeOffset> _retryAfter = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<int, PadBinding> _pads = [];
    private readonly Dictionary<SwitchControllerSession, PadBinding> _bindingBySession = [];
    private readonly Dictionary<string, int> _slotByController = new(StringComparer.OrdinalIgnoreCase);

    private CancellationTokenSource? _cancellation;
    private Task? _discoveryLoop;
    private bool _disposed;

    public ControllerManager(
        IVirtualPadBackend backend,
        ControllerManagerOptions? options = null,
        ILogger? logger = null,
        TimeProvider? timeProvider = null,
        IDeviceDiscovery? discovery = null)
    {
        _backend = backend;
        _options = options ?? new ControllerManagerOptions();
        _logger = logger ?? NullLogger.Instance;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _discovery = discovery ?? new HidSharpDeviceDiscovery();
    }

    /// <summary>Raised whenever the set of controllers or pads changes, from any thread.</summary>
    public event Action? Changed;

    /// <summary>Raised when a controller link changes state, from the controller's own thread.</summary>
    public event Action<SwitchControllerSession, ControllerLinkState, ControllerLinkState>? LinkStateChanged;

    /// <summary>Raised when a virtual pad could not be created, with the reason.</summary>
    public event Action<SwitchControllerSession, string>? PadCreationFailed;

    public JoyConMode JoyConMode => _options.JoyConMode;

    public bool VirtualPadsEnabled => _options.CreateVirtualPads;

    public IReadOnlyList<SwitchControllerSession> Sessions
    {
        get
        {
            lock (_gate)
            {
                return _devices.Values.Select(d => d.Session).ToArray();
            }
        }
    }

    public IReadOnlyList<PadBinding> Pads
    {
        get
        {
            lock (_gate)
            {
                return _pads.Values.ToArray();
            }
        }
    }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_discoveryLoop is not null)
        {
            return;
        }

        _cancellation = new CancellationTokenSource();
        var token = _cancellation.Token;
        _discoveryLoop = Task.Factory.StartNew(
            () => DiscoveryLoop(token),
            token,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
    }

    public void Stop()
    {
        _cancellation?.Cancel();
        try
        {
            _discoveryLoop?.Wait(TimeSpan.FromSeconds(5));
        }
        catch (AggregateException)
        {
            // Cancellation races are expected while shutting down.
        }

        _discoveryLoop = null;
        _cancellation?.Dispose();
        _cancellation = null;

        // Stop every controller reader thread and clear device/pad bookkeeping so a subsequent
        // Start() redisconnects cleanly instead of resuming against stale reader threads and
        // disposed transports. Without this, Pause does not actually pause controller I/O.
        TeardownRuntimesAndPads();
    }

    /// <summary>
    /// Stops all device reader threads, disposes their transports/sessions and pads, and clears the
    /// device and pad dictionaries. Used by both <see cref="Stop"/> (pause/resume) and
    /// <see cref="Dispose"/> (shutdown).
    /// </summary>
    private void TeardownRuntimesAndPads()
    {
        DeviceRuntime[] runtimes;
        PadBinding[] pads;
        lock (_gate)
        {
            runtimes = _devices.Values.ToArray();
            pads = _pads.Values.ToArray();
            _devices.Clear();
            _pads.Clear();
            _bindingBySession.Clear();
            _slotByController.Clear();
        }

        foreach (var runtime in runtimes)
        {
            StopRuntime(runtime);
            runtime.Dispose();
        }

        foreach (var pad in pads)
        {
            pad.Dispose();
        }
    }

    private void DiscoveryLoop(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                DiscoverOnce();
                ReapDeadLinks();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Controller discovery iteration failed");
            }

            try
            {
                Task.Delay(_options.DiscoveryInterval, _timeProvider, cancellationToken).GetAwaiter().GetResult();
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>Runs one discovery pass. Exposed so tests can drive it without waiting on timers.</summary>
    public void DiscoverOnce()
    {
        if (!_options.AutoConnect)
        {
            return;
        }

        var discovered = _discovery.Discover();
        var byDevice = discovered
            .GroupBy(d => d.Candidate.PhysicalDeviceKey, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var group in byDevice)
        {
            HashSet<string>? attempted;
            lock (_gate)
            {
                if (_devices.ContainsKey(group.Key)
                    || _devices.Count >= _options.MaxControllers
                    || IsWaitingForRetry(group.Key))
                {
                    continue;
                }

                attempted = _attemptedPaths.TryGetValue(group.Key, out var set)
                    ? new HashSet<string>(set, StringComparer.OrdinalIgnoreCase)
                    : null;
            }

            var match = PickBestCandidate(group.Select(d => d.Candidate), attempted);
            if (match is null)
            {
                lock (_gate)
                {
                    // Every collection has been tried: start over so a fixed problem can recover.
                    _attemptedPaths.Remove(group.Key);
                }

                continue;
            }

            var device = group.First(d => d.Candidate.DevicePath == match.DevicePath);
            Connect(device, match);
        }
    }

    /// <summary>
    /// Chooses which of a controller's HID collections to open.
    /// </summary>
    /// <remarks>
    /// Windows exposes a Switch controller as more than one top-level collection: the vendor one
    /// (which carries report 0x30) and a generic game controller collection. Only the vendor one
    /// speaks the protocol, so the descriptor decides.
    /// </remarks>
    public static HidCandidate? PickBestCandidate(IEnumerable<HidCandidate> candidates, ISet<string>? excludePaths = null)
    {
        var ordered = candidates
            .Where(c => excludePaths is null || !excludePaths.Contains(c.DevicePath))
            .OrderByDescending(c => c.DeclaresVendorReports)
            .ThenByDescending(c => c.InputReports.Count > 0)
            .ThenByDescending(c => c.MaxInputReportLength)
            .ThenBy(c => c.DevicePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return ordered.Length > 0 ? ordered[0] : null;
    }

    private void Connect(DiscoveredDevice device, HidCandidate candidate)
    {
        var transport = TryOpen(device, candidate);
        if (transport is null)
        {
            lock (_gate)
            {
                _retryAfter[candidate.PhysicalDeviceKey] = _timeProvider.GetUtcNow() + _options.RetryFailedDevicesAfter;
                if (!_attemptedPaths.TryGetValue(candidate.PhysicalDeviceKey, out var set))
                {
                    set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    _attemptedPaths[candidate.PhysicalDeviceKey] = set;
                }

                set.Add(candidate.DevicePath);
            }

            return;
        }

        var session = new SwitchControllerSession(
            candidate.PhysicalDeviceKey,
            candidate.ProductId,
            transport,
            _options.SessionOptions,
            _timeProvider,
            _logger);

        var cts = new CancellationTokenSource();
        var runtime = new DeviceRuntime(candidate, transport, session, cts);

        lock (_gate)
        {
            _devices[candidate.PhysicalDeviceKey] = runtime;
        }

        session.LinkStateChanged += s => OnLinkStateChanged(runtime, s);

        runtime.Thread = Task.Factory.StartNew(
            () => session.RunLoop(cts.Token),
            cts.Token,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);

        _logger.LogInformation(
            "Connecting to {Model} on {Connection} ({Path})",
            candidate.Model.DisplayName, candidate.Connection, candidate.DevicePath);

        RaiseChanged();
    }

    private IControllerTransport? TryOpen(DiscoveredDevice device, HidCandidate candidate)
    {
        try
        {
            var transport = device.Open();
            if (transport is null)
            {
                // Typically the device is held by another controller tool, or access was denied.
                _logger.LogInformation("Could not open {Path}", candidate.DevicePath);
            }

            return transport;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Opening {Path} threw", candidate.DevicePath);
            return null;
        }
    }

    private void OnLinkStateChanged(DeviceRuntime runtime, SwitchControllerSession session)
    {
        var previous = runtime.LastState;
        runtime.LastState = session.State;

        switch (session.State)
        {
            case ControllerLinkState.Ready:
                EnsureBinding(session);
                break;

            case ControllerLinkState.Failed:
                Detach(session, "link failed");
                break;

            case ControllerLinkState.Stopped:
                Detach(session, "stopped");
                break;
        }

        LinkStateChanged?.Invoke(session, previous, session.State);
        RaiseChanged();
    }

    /// <summary>Gives a controller a virtual pad, joining a Joy-Con pair when that mode is on.</summary>
    private void EnsureBinding(SwitchControllerSession session)
    {
        if (!_options.CreateVirtualPads)
        {
            return;
        }

        lock (_padGate)
        {
            lock (_gate)
            {
                if (_bindingBySession.ContainsKey(session))
                {
                    return;
                }
            }

            var profile = _options.ProfileFactory?.Invoke(session)
                ?? MappingProfile.CreateDefault(session.Model.Model, _options.JoyConMode);

            session.RumbleStrength = profile.RumbleStrength;

            if (_options.JoyConMode == JoyConMode.Combined && SwitchModels.IsJoyCon(session.Model.Model))
            {
                PadBinding? partner;
                lock (_gate)
                {
                    // A Joy-Con pair is worth more than the sum of its halves: join a pad that has
                    // exactly the opposite half waiting for a partner.
                    var wanted = session.Model.Model == SwitchModel.JoyConLeft
                        ? SwitchModel.JoyConRight
                        : SwitchModel.JoyConLeft;

                    partner = _pads.Values.FirstOrDefault(p =>
                        p.Sources.Count == 1 && p.Sources.Any(s => s.Model.Model == wanted));
                }

                if (partner is not null)
                {
                    partner.AddSource(session, PadBinding.LayoutFor(session, combined: true));
                    lock (_gate)
                    {
                        _bindingBySession[session] = partner;
                    }

                    _logger.LogInformation(
                        "Combined {Model} into slot {Slot}",
                        session.Model.DisplayName, partner.Slot);

                    RaiseChanged();
                    return;
                }
            }

            int slot;
            lock (_gate)
            {
                slot = ClaimSlot(session);
            }

            if (slot == 0)
            {
                _logger.LogWarning("No free player slot for {Model}", session.Model.DisplayName);
                PadCreationFailed?.Invoke(session, "All four player slots are in use.");
                return;
            }

            var pad = _backend.CreatePad(slot, $"{session.Model.DisplayName} (player {slot})", out var error);
            if (pad is null)
            {
                lock (_gate)
                {
                    _slotByController.Remove(session.IdentityKey);
                }

                _logger.LogWarning("Could not create a virtual pad for {Model}: {Error}", session.Model.DisplayName, error);
                PadCreationFailed?.Invoke(session, error ?? _backend.Status.Summary);
                return;
            }

            var binding = new PadBinding(slot, pad, profile, _logger);
            binding.AddSource(session, PadBinding.LayoutFor(session, combined: _options.JoyConMode == JoyConMode.Combined));

            lock (_gate)
            {
                _pads[slot] = binding;
                _bindingBySession[session] = binding;
            }

            session.SetSlot(slot);
            RaiseChanged();
        }
    }

    private int ClaimSlot(SwitchControllerSession session)
    {
        var identity = session.IdentityKey;

        // A controller that reconnects keeps its slot, which is what players expect.
        if (_slotByController.TryGetValue(identity, out var remembered) && !IsSlotInUse(remembered))
        {
            return remembered;
        }

        for (var slot = 1; slot <= _options.MaxControllers; slot++)
        {
            if (!IsSlotInUse(slot))
            {
                _slotByController[identity] = slot;
                return slot;
            }
        }

        return 0;
    }

    private bool IsSlotInUse(int slot) => _pads.ContainsKey(slot);

    private void Detach(SwitchControllerSession session, string reason)
    {
        PadBinding? binding;
        var retire = false;

        lock (_padGate)
        {
            lock (_gate)
            {
                if (!_bindingBySession.Remove(session, out binding))
                {
                    binding = null;
                }
                else if (_pads.TryGetValue(binding.Slot, out var registered) && ReferenceEquals(registered, binding))
                {
                    // The pad is retired when this was its last source, which is the normal case; a
                    // combined Joy-Con pair keeps its pad while one half is still there.
                    retire = binding.Sources.Count <= 1;
                    if (retire)
                    {
                        _pads.Remove(binding.Slot);
                    }
                }
            }

            if (binding is not null)
            {
                binding.RemoveSource(session);
                if (retire)
                {
                    binding.Dispose();
                }
            }
        }

        if (binding is not null)
        {
            _logger.LogInformation(
                "Dropping {Model} ({Reason}); slot {Slot} freed",
                session.Model.DisplayName, reason, retire ? binding.Slot : 0);
        }

        RaiseChanged();
    }

    /// <summary>Removes sessions whose threads have finished.</summary>
    public void ReapDeadLinks()
    {
        DeviceRuntime[] dead;
        lock (_gate)
        {
            dead = _devices.Values
                .Where(d => d.Session.State is ControllerLinkState.Failed or ControllerLinkState.Stopped)
                .ToArray();
        }

        foreach (var runtime in dead)
        {
            Detach(runtime.Session, "link ended");

            lock (_gate)
            {
                _devices.Remove(runtime.Candidate.PhysicalDeviceKey);

                // Forget the slot mapping so a reconnect can take the same player slot back.
                _slotByController.Remove(runtime.Session.IdentityKey);
                _slotByController.Remove(runtime.Session.DeviceKey);
                _retryAfter[runtime.Candidate.PhysicalDeviceKey] = _timeProvider.GetUtcNow() + _options.RetryFailedDevicesAfter;
            }

            StopRuntime(runtime);

            runtime.Dispose();
            RaiseChanged();
        }
    }

    /// <summary>Stops a device's reader thread. It must finish before the transport is closed.</summary>
    private static void StopRuntime(DeviceRuntime runtime)
    {
        try
        {
            runtime.Cancellation.Cancel();
            runtime.Thread?.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
            // Expected when the loop is cancelled while it is reading.
        }
    }

    private bool IsWaitingForRetry(string key)
    {
        if (!_retryAfter.TryGetValue(key, out var retryAt))
        {
            return false;
        }

        if (_timeProvider.GetUtcNow() < retryAt)
        {
            return true;
        }

        _retryAfter.Remove(key);
        return false;
    }

    private void RaiseChanged() => Changed?.Invoke();

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Stop();  // stops discovery loop and tears down all runtimes/pads
        _backend.Dispose();
    }

    private sealed class DeviceRuntime : IDisposable
    {
        public DeviceRuntime(
            HidCandidate candidate,
            IControllerTransport transport,
            SwitchControllerSession session,
            CancellationTokenSource cancellation)
        {
            Candidate = candidate;
            Transport = transport;
            Session = session;
            Cancellation = cancellation;
        }

        public HidCandidate Candidate { get; }

        public IControllerTransport Transport { get; }

        public SwitchControllerSession Session { get; }

        public CancellationTokenSource Cancellation { get; }

        public Task? Thread { get; set; }

        public ControllerLinkState LastState { get; set; } = ControllerLinkState.Idle;

        public void Dispose()
        {
            Cancellation.Dispose();
            Session.Dispose();
        }
    }
}
