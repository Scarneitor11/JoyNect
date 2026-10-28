using JoyNect.Core.Design;
using JoyNect.Core.Mapping;

namespace JoyNect.Core.Settings;

/// <summary>Which appearance the app uses.</summary>
public enum AppearanceMode
{
    System,
    Light,
    Dark,
}

/// <summary>
/// Everything the app remembers between runs.
/// </summary>
/// <remarks>
/// A plain mutable class with defaults, because this is a serialisation format first and a domain
/// object second: every new setting has to keep working when an older file is read, so absent values
/// must fall back to a sensible default rather than fail. The store normalises whatever it reads, so
/// a hand-edited or truncated file cannot put the app into an invalid state.
/// </remarks>
public sealed class AppSettings
{
    /// <summary>Bumped only when a migration is genuinely needed.</summary>
    public int SchemaVersion { get; set; } = 1;

    public AppearanceMode Appearance { get; set; } = AppearanceMode.System;

    /// <summary>The colour the palette is generated from.</summary>
    public uint SeedColour { get; set; } = MaterialSchemes.BaselineSeed;

    /// <summary>Derive the seed from the current desktop wallpaper.</summary>
    public bool FollowWallpaper { get; set; } = true;

    /// <summary>
    /// The palette family. Tonal spot is the harmonious Material You default; the expressive variant
    /// deliberately detaches the accent hues from the seed, which is striking but much louder.
    /// </summary>
    public SchemeVariant Variant { get; set; } = SchemeVariant.TonalSpot;

    public ContrastLevel Contrast { get; set; } = ContrastLevel.Standard;

    /// <summary>Start with Windows, minimised to the tray.</summary>
    public bool StartWithWindows { get; set; }

    /// <summary>Closing the window hides it to the tray instead of quitting.</summary>
    public bool CloseToTray { get; set; } = true;

    /// <summary>Show a notification when a controller connects or disconnects.</summary>
    public bool ShowConnectionNotifications { get; set; } = true;

    /// <summary>Create virtual pads as soon as a controller is ready.</summary>
    public bool CreateVirtualPads { get; set; } = true;

    /// <summary>How many virtual pads may be created at once.</summary>
    public int MaxPads { get; set; } = 4;

    public JoyConMode JoyConMode { get; set; } = JoyConMode.Sideways;

    public bool RumbleEnabled { get; set; } = true;

    public double RumbleStrength { get; set; } = 0.6;

    public double LeftDeadzone { get; set; } = 0.08;

    public double LeftCurve { get; set; } = 1.0;

    public double RightDeadzone { get; set; } = 0.08;

    public double RightCurve { get; set; } = 1.0;

    public bool LeftInvertX { get; set; }

    public bool LeftInvertY { get; set; }

    public bool RightInvertX { get; set; }

    public bool RightInvertY { get; set; }

    /// <summary>
    /// Button remaps, as pairs of <c>SwitchButtons</c> and <c>Xbox360Target</c> names.
    /// </summary>
    /// <remarks>
    /// Stored by name rather than by value so a renumbered enum cannot silently remap a button to
    /// something else after an update. Unknown names are skipped when applied.
    /// </remarks>
    public Dictionary<string, string> Remaps { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Expose motion to Cemu, Citron and other DSU clients.</summary>
    public bool MotionServerEnabled { get; set; }

    /// <summary>The UDP port for DSU motion.</summary>
    public int MotionPort { get; set; } = 26760;

    /// <summary>Whether the first-run guide has been completed.</summary>
    public bool OnboardingComplete { get; set; }

    /// <summary>The last page that was open, so the app reopens where it was left.</summary>
    public string LastPage { get; set; } = "home";

    /// <summary>Copies the settings, for editing before saving.</summary>
    public AppSettings Clone() => (AppSettings)MemberwiseClone();

    /// <summary>
    /// Forces every value into the range the app can actually use.
    /// </summary>
    /// <remarks>
    /// Values come from a file on disk that a user may edit, so they are treated as untrusted input.
    /// Clamping here means no page has to defend itself against a dead zone of 12 or a port of -1.
    /// </remarks>
    public AppSettings Normalise()
    {
        if (!Enum.IsDefined(Appearance))
        {
            Appearance = AppearanceMode.System;
        }

        if (!Enum.IsDefined(Variant))
        {
            Variant = SchemeVariant.Expressive;
        }

        if (!Enum.IsDefined(Contrast))
        {
            Contrast = ContrastLevel.Standard;
        }

        if (!Enum.IsDefined(JoyConMode))
        {
            JoyConMode = JoyConMode.Sideways;
        }

        MaxPads = Math.Clamp(MaxPads, 1, 4);
        RumbleStrength = Math.Clamp(RumbleStrength, 0.0, 1.0);
        LeftDeadzone = Math.Clamp(LeftDeadzone, 0.0, 0.4);
        RightDeadzone = Math.Clamp(RightDeadzone, 0.0, 0.4);
        LeftCurve = Math.Clamp(LeftCurve, 0.5, 2.0);
        RightCurve = Math.Clamp(RightCurve, 0.5, 2.0);
        MotionPort = Math.Clamp(MotionPort, 1024, 65535);
        SeedColour |= 0xFF000000;

        if (string.IsNullOrWhiteSpace(LastPage))
        {
            LastPage = "home";
        }

        return this;
    }
}
