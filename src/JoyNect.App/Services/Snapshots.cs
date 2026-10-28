using JoyNect.Core.Devices;
using JoyNect.Core.Hid;
using JoyNect.Core.Mapping;
using JoyNect.Core.Motion;
using JoyNect.Core.Output;
using JoyNect.Core.Protocol;

namespace JoyNect.App.Services;

/// <summary>
/// A controller as the interface sees it.
/// </summary>
/// <remarks>
/// Pages read snapshots rather than live sessions: a session belongs to a reader thread, and a
/// snapshot taken on the UI thread is the simplest way to guarantee that a page cannot see a
/// half-updated controller. It also makes the empty and demo states trivial to produce.
/// </remarks>
public sealed record ControllerSnapshot
{
    public required string Id { get; init; }

    public required string Model { get; init; }

    public required string SupportNote { get; init; }

    public SupportLevel Support { get; init; } = SupportLevel.Verified;

    public required string Transport { get; init; }

    public required ConnectionKind Connection { get; init; }

    public string MacAddress { get; init; } = string.Empty;

    public int ProductId { get; init; }

    public ControllerLinkState State { get; init; }

    public int Slot { get; init; }

    public BatteryLevel Battery { get; init; } = BatteryLevel.Unknown;

    public bool Charging { get; init; }

    public double? ReportsPerSecond { get; init; }

    public string Statistics { get; init; } = string.Empty;

    public bool CalibrationLoaded { get; init; }

    public bool HasMotion { get; init; }

    public MotionSample Motion { get; init; } = MotionSample.Zero;

    public SwitchButtons Buttons { get; init; }

    public StickRaw LeftStick { get; init; }

    public StickRaw RightStick { get; init; }

    /// <summary>A one word state for a chip.</summary>
    public string StateLabel => State switch
    {
        ControllerLinkState.Idle => "Waiting",
        ControllerLinkState.Handshaking => "Connecting",
        ControllerLinkState.Ready => "Ready",
        ControllerLinkState.Stale => "No signal",
        ControllerLinkState.Failed => "Failed",
        ControllerLinkState.Stopped => "Disconnected",
        _ => State.ToString(),
    };

    /// <summary>Whether this controller is currently feeding a pad.</summary>
    public bool IsReady => State == ControllerLinkState.Ready;

    /// <summary>The battery as a fraction, for a progress bar.</summary>
    public double BatteryFraction => Battery switch
    {
        BatteryLevel.Full => 1.0,
        BatteryLevel.Medium => 0.75,
        BatteryLevel.Low => 0.5,
        BatteryLevel.Critical => 0.25,
        BatteryLevel.Empty => 0.02,
        _ => 0.0,
    };

    public string BatteryLabel => Battery switch
    {
        BatteryLevel.Full => "Full",
        BatteryLevel.Medium => "Good",
        BatteryLevel.Low => "Low",
        BatteryLevel.Critical => "Critical",
        BatteryLevel.Empty => "Empty",
        _ when Charging => "Charging",
        _ => "Unknown",
    };
}

/// <summary>The Cemuhook DSU motion server, as the Motion page sees it.</summary>
public sealed record MotionServerSnapshot
{
    public required bool Enabled { get; init; }

    public required bool Running { get; init; }

    public required int Port { get; init; }

    public string? Error { get; init; }

    public string Endpoint => $"127.0.0.1:{Port}";

    public bool Listening => Enabled && Running;
}

/// <summary>One virtual pad and the controllers feeding it.</summary>
public sealed record PadSnapshot
{
    public required int Slot { get; init; }

    public required string Description { get; init; }

    public required Xbox360PadState State { get; init; }

    public required long SubmitCount { get; init; }

    public required IReadOnlyList<string> Controllers { get; init; }

    public required MappingProfile Profile { get; init; }

    /// <summary>A four character summary for a chip, for the cases where space is tight.</summary>
    public string ShortName => $"Pad {Slot}";
}
