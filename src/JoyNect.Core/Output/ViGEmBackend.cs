using System.Reflection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.Xbox360;

namespace JoyNect.Core.Output;

/// <summary>
/// Virtual pads backed by the ViGEmBus kernel driver.
/// </summary>
/// <remarks>
/// The client library embeds its own native client library, so JoyNect ships no native binaries and
/// needs no administrator rights: it talks to an already installed driver.
/// </remarks>
public sealed class ViGEmBackend : IVirtualPadBackend
{
    private readonly ILogger _logger;
    private readonly List<ViGEmPad> _pads = [];
    private ViGEmClient? _client;
    private bool _disposed;

    public ViGEmBackend(ILogger? logger = null)
    {
        _logger = logger ?? NullLogger.Instance;
        Status = DriverProbe.Probe(_logger);
    }

    public string Name => "ViGEmBus";

    public DriverStatus Status { get; }

    /// <summary>Result of comparing JoyNect's button bits with the client library's.</summary>
    public IReadOnlyList<string> MappingProblems { get; private set; } = [];

    public IVirtualPad? CreatePad(int slot, string description, out string? error)
    {
        error = null;

        if (!Status.Available)
        {
            error = Status.Summary;
            return null;
        }

        try
        {
            _client ??= CreateClient();
            var target = _client.CreateXbox360Controller();
            var pad = new ViGEmPad(slot, description, target, _logger);
            pad.Connect();
            _pads.Add(pad);
            return pad;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not create a virtual pad for slot {Slot}", slot);
            error = DriverProbe.ClassifyFailure(ex).Summary;
            return null;
        }
    }

    private ViGEmClient CreateClient()
    {
        var client = new ViGEmClient();
        MappingProblems = VerifyButtonMapping();
        foreach (var problem in MappingProblems)
        {
            _logger.LogWarning("Virtual pad button mapping mismatch: {Problem}", problem);
        }

        return client;
    }

    /// <summary>
    /// Checks JoyNect's button constants against the client library's own values.
    /// </summary>
    /// <remarks>
    /// A wrong bit here would silently swap buttons in every game, and the values come from a library
    /// that has been archived since 2023, so they are compared at runtime instead of trusted.
    /// </remarks>
    public static IReadOnlyList<string> VerifyButtonMapping()
    {
        var problems = new List<string>();
        var expectations = new (Xbox360Buttons Ours, string TheirField)[]
        {
            (Xbox360Buttons.DpadUp, "Up"),
            (Xbox360Buttons.DpadDown, "Down"),
            (Xbox360Buttons.DpadLeft, "Left"),
            (Xbox360Buttons.DpadRight, "Right"),
            (Xbox360Buttons.Start, "Start"),
            (Xbox360Buttons.Back, "Back"),
            (Xbox360Buttons.LeftThumb, "LeftThumb"),
            (Xbox360Buttons.RightThumb, "RightThumb"),
            (Xbox360Buttons.LeftShoulder, "LeftShoulder"),
            (Xbox360Buttons.RightShoulder, "RightShoulder"),
            (Xbox360Buttons.Guide, "Guide"),
            (Xbox360Buttons.A, "A"),
            (Xbox360Buttons.B, "B"),
            (Xbox360Buttons.X, "X"),
            (Xbox360Buttons.Y, "Y"),
        };

        var type = typeof(Xbox360Button);
        foreach (var (ours, theirField) in expectations)
        {
            var field = type.GetField(theirField, BindingFlags.Public | BindingFlags.Static);
            if (field?.GetValue(null) is not Xbox360Button button)
            {
                problems.Add($"{theirField} is missing from the installed ViGEm client library");
                continue;
            }

            if (button.Value != (ushort)ours)
            {
                problems.Add($"{theirField} is 0x{button.Value:X4} in the client library but 0x{(ushort)ours:X4} in JoyNect");
            }
        }

        return problems;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        foreach (var pad in _pads.ToArray())
        {
            pad.Dispose();
        }

        _pads.Clear();

        try
        {
            _client?.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Disposing the ViGEm client failed");
        }

        _client = null;
    }
}

/// <summary>One Xbox 360 pad on the ViGEm bus.</summary>
internal sealed class ViGEmPad : IVirtualPad
{
    private readonly IXbox360Controller _target;
    private readonly ILogger _logger;
    private readonly object _gate = new();
    private Xbox360PadState _lastSubmitted = Xbox360PadState.Neutral;
    private bool _submittedOnce;
    private long _lastSubmitTicks;

    public ViGEmPad(int slot, string description, IXbox360Controller target, ILogger logger)
    {
        Slot = slot;
        Description = description;
        _target = target;
        _logger = logger;

        // One report per input frame: with AutoSubmitReport the library would push a report for
        // every single field it sets, which is dozens of writes per frame for no benefit.
        _target.AutoSubmitReport = false;
        _target.FeedbackReceived += OnFeedbackReceived;
    }

    public int Slot { get; }

    public string Description { get; }

    public bool IsConnected { get; private set; }

    public event Action<byte, byte>? RumbleRequested;

    public event Action<byte>? LedRequested;

    public void Connect()
    {
        _target.Connect();
        IsConnected = true;
    }

    public void Submit(in Xbox360PadState state)
    {
        if (!IsConnected)
        {
            return;
        }

        lock (_gate)
        {
            // The driver latches the last report, so identical states are not resent; a slow
            // keepalive covers the case of a game that expects to see traffic.
            var changed = !_submittedOnce || !state.Equals(_lastSubmitted);
            var keepAliveDue = Environment.TickCount64 - _lastSubmitTicks > 1000;
            if (!changed && !keepAliveDue)
            {
                return;
            }

            try
            {
                _target.SetButtonsFull((ushort)state.Buttons);
                _target.SetAxisValue(Xbox360Axis.LeftThumbX, state.LeftThumbX);
                _target.SetAxisValue(Xbox360Axis.LeftThumbY, state.LeftThumbY);
                _target.SetAxisValue(Xbox360Axis.RightThumbX, state.RightThumbX);
                _target.SetAxisValue(Xbox360Axis.RightThumbY, state.RightThumbY);
                _target.SetSliderValue(Xbox360Slider.LeftTrigger, state.LeftTrigger);
                _target.SetSliderValue(Xbox360Slider.RightTrigger, state.RightTrigger);
                _target.SubmitReport();

                _lastSubmitted = state;
                _submittedOnce = true;
                _lastSubmitTicks = Environment.TickCount64;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Submitting a report to virtual pad {Slot} failed", Slot);
            }
        }
    }

    private void OnFeedbackReceived(object? sender, Xbox360FeedbackReceivedEventArgs e)
    {
        RumbleRequested?.Invoke(e.LargeMotor, e.SmallMotor);
        LedRequested?.Invoke(e.LedNumber);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            try
            {
                _target.FeedbackReceived -= OnFeedbackReceived;

                // Leave the pad centred before removing it so a game never keeps a stuck input.
                _target.SetButtonsFull(0);
                _target.SetAxisValue(Xbox360Axis.LeftThumbX, 0);
                _target.SetAxisValue(Xbox360Axis.LeftThumbY, 0);
                _target.SetAxisValue(Xbox360Axis.RightThumbX, 0);
                _target.SetAxisValue(Xbox360Axis.RightThumbY, 0);
                _target.SetSliderValue(Xbox360Slider.LeftTrigger, 0);
                _target.SetSliderValue(Xbox360Slider.RightTrigger, 0);
                _target.SubmitReport();
                _target.Disconnect();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Disconnecting virtual pad {Slot} failed", Slot);
            }

            IsConnected = false;
        }
    }
}
