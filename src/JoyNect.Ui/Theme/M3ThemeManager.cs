using Microsoft.Win32;
using JoyNect.Core.Design;

namespace JoyNect.Ui.Theme;

/// <summary>Which appearance the app should use.</summary>
public enum ThemeMode
{
    /// <summary>Follow the Windows setting.</summary>
    System,
    Light,
    Dark,
}

/// <summary>The user's appearance choices.</summary>
public sealed record ThemeSettings
{
    public ThemeMode Mode { get; init; } = ThemeMode.System;

    /// <summary>The colour the palette is generated from when the wallpaper is not used.</summary>
    public uint Seed { get; init; } = MaterialSchemes.BaselineSeed;

    /// <summary>Derive the seed from the desktop wallpaper.</summary>
    public bool FollowWallpaper { get; init; } = true;

    public SchemeVariant Variant { get; init; } = SchemeVariant.TonalSpot;

    public ContrastLevel Contrast { get; init; } = ContrastLevel.Standard;
}

/// <summary>
/// Owns the current theme and keeps it in step with the system and the user's settings.
/// </summary>
/// <remarks>
/// Every control reads brushes from <see cref="Current"/> and refreshes when <see cref="Changed"/>
/// fires, so a theme change is a single assignment rather than a walk over the visual tree. The
/// manager never touches the registry to write — it only reads the appearance settings to follow the
/// system theme, which is what a well-behaved Windows app does.
/// </remarks>
public static class M3ThemeManager
{
    private static bool? _lastSystemDark;
    private static bool _initialised;

    /// <summary>The active theme.</summary>
    public static M3Theme Current { get; private set; } =
        new(MaterialSchemes.Create(MaterialSchemes.BaselineSeed, isDark: false));

    /// <summary>The current settings.</summary>
    public static ThemeSettings Settings { get; private set; } = new();

    /// <summary>The wallpaper colour, when one could be extracted.</summary>
    public static uint? WallpaperSeed { get; private set; }

    /// <summary>Raised after <see cref="Current"/> changes.</summary>
    public static event EventHandler? Changed;

    /// <summary>The seed actually used, which is the wallpaper colour when it is available.</summary>
    public static uint EffectiveSeed => Settings.FollowWallpaper && WallpaperSeed is { } seed ? seed : Settings.Seed;

    /// <summary>Reads the system appearance and applies the stored settings.</summary>
    public static void Initialize(ThemeSettings? settings = null)
    {
        Settings = settings ?? new ThemeSettings();
        WallpaperSeed = Settings.FollowWallpaper ? WallpaperColour.Extract() : null;
        _lastSystemDark = IsSystemDark();
        _initialised = true;
        Rebuild();
    }

    /// <summary>Applies new settings and rebuilds the theme.</summary>
    public static void Apply(ThemeSettings settings)
    {
        var needsWallpaper = settings.FollowWallpaper && (WallpaperSeed is null || !Settings.FollowWallpaper);
        Settings = settings;

        if (needsWallpaper)
        {
            WallpaperSeed = WallpaperColour.Extract();
        }
        else if (!settings.FollowWallpaper)
        {
            WallpaperSeed = null;
        }

        if (!_initialised)
        {
            _initialised = true;
        }

        _lastSystemDark = IsSystemDark();
        Rebuild();
    }

    /// <summary>
    /// Picks up a changed Windows appearance or wallpaper.
    /// </summary>
    /// <returns>True when the theme changed.</returns>
    public static bool Refresh()
    {
        if (!_initialised)
        {
            return false;
        }

        var changed = false;

        if (Settings.Mode == ThemeMode.System)
        {
            var systemDark = IsSystemDark();
            if (systemDark != _lastSystemDark)
            {
                _lastSystemDark = systemDark;
                changed = true;
            }
        }

        if (Settings.FollowWallpaper)
        {
            var seed = WallpaperColour.Extract();
            if (seed is not null && seed != WallpaperSeed)
            {
                WallpaperSeed = seed;
                changed = true;
            }
        }

        if (changed)
        {
            Rebuild();
        }

        return changed;
    }

    /// <summary>Switches between light and dark without touching the stored preference.</summary>
    public static void SetDark(bool dark)
    {
        Settings = Settings with { Mode = dark ? ThemeMode.Dark : ThemeMode.Light };
        Rebuild();
    }

    /// <summary>Whether Windows is currently using a dark app appearance.</summary>
    public static bool IsSystemDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                writable: false);

            if (key?.GetValue("AppsUseLightTheme") is int value)
            {
                return value == 0;
            }
        }
        catch (Exception)
        {
            // A locked-down machine may deny read access; a light theme is the safe assumption.
        }

        return false;
    }

    /// <summary>True when the current theme is dark.</summary>
    public static bool IsDark => Current.IsDark;

    private static void Rebuild()
    {
        var isDark = Settings.Mode switch
        {
            ThemeMode.Dark => true,
            ThemeMode.Light => false,
            _ => IsSystemDark(),
        };

        Current = new M3Theme(MaterialSchemes.Create(EffectiveSeed, isDark, Settings.Variant, Settings.Contrast));
        Changed?.Invoke(null, EventArgs.Empty);
    }
}
