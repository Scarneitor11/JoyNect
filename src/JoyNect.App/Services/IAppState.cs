using JoyNect.Core.Hid;
using JoyNect.Core.Mapping;
using JoyNect.Core.Output;
using JoyNect.Core.Protocol;
using JoyNect.Core.Settings;

namespace JoyNect.App.Services;

/// <summary>
/// Everything the pages are allowed to know about the running app.
/// </summary>
/// <remarks>
/// The seam exists for two reasons. It keeps the pages free of the controller manager's threading
/// model, and it lets the screenshot renderer produce every screen with believable data without a
/// controller or a driver present — which is how the visual design gets checked on a build machine.
/// </remarks>
public interface IAppState : IDisposable
{
    AppSettings Settings { get; }

    DriverStatus Driver { get; }

    /// <summary>False when the user paused connecting.</summary>
    bool IsRunning { get; }

    /// <summary>The profile edits apply to: the first pad's, or the template for the next controller.</summary>
    MappingProfile Mapping { get; }

    IReadOnlyList<ControllerSnapshot> Controllers { get; }

    IReadOnlyList<PadSnapshot> Pads { get; }

    /// <summary>The DSU motion server's current state.</summary>
    MotionServerSnapshot MotionServer { get; }

    /// <summary>Raised when controllers, pads or settings change.</summary>
    event Action? Changed;

    /// <summary>Raised with a message for the snackbar and whether it reports a problem.</summary>
    event Action<string, bool>? Notified;

    /// <summary>Persists settings, applying appearance and reconnecting controllers if needed.</summary>
    void Save(AppSettings settings);

    /// <summary>Shows a message in the snackbar.</summary>
    void Notify(string message, bool error = false);

    /// <summary>Starts or stops connecting to controllers.</summary>
    void SetRunning(bool running);

    /// <summary>Looks for controllers now instead of waiting for the next pass.</summary>
    void Rescan();

    /// <summary>Sends a rumble pulse to every ready controller.</summary>
    void TestRumble(byte large, byte small);

    /// <summary>Sets the player slot (and therefore the player lights) of one controller.</summary>
    void SetSlot(string controllerId, int slot);

    /// <summary>Remaps one Switch button on every pad and for future controllers.</summary>
    void Remap(SwitchButtons source, Xbox360Target? target);

    /// <summary>Changes stick shaping on every pad and for future controllers.</summary>
    void SetStickSettings(bool right, StickSettings settings);

    /// <summary>Changes rumble behaviour for every pad and for future controllers.</summary>
    void SetProfileRumble(bool enabled, double strength);

    /// <summary>Restores the model's default button layout.</summary>
    void ResetMapping();

    /// <summary>Collects a full diagnostic report; safe to call from the UI.</summary>
    Task<string> BuildDiagnosticReportAsync();

    /// <summary>Lists Nintendo HID interfaces, for the diagnostics page.</summary>
    Task<IReadOnlyList<HidCandidate>> EnumerateHidAsync();

    /// <summary>Re-checks the virtual pad driver, rebuilding the pipeline if it appeared or vanished.</summary>
    void RefreshDriver();

    /// <summary>Where settings and logs live, for the About page.</summary>
    string SettingsPath { get; }
}
