using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using JoyNect.Core.Design;
using JoyNect.Ui.Theme;

namespace JoyNect.Ui.Controls;

/// <summary>The three Material card emphases.</summary>
public enum M3CardVariant
{
    /// <summary>A raised surface with a soft shadow.</summary>
    Elevated,

    /// <summary>A tinted surface with no shadow.</summary>
    Filled,

    /// <summary>A plain surface with a hairline outline.</summary>
    Outlined,
}

/// <summary>A Material card: a container for related content.</summary>
/// <remarks>
/// Cards are the backbone of the app's layout, so they also carry the padding and radius conventions
/// the pages rely on rather than leaving every page to invent its own spacing.
/// </remarks>
public sealed class M3Card : Border
{
    private M3CardVariant _variant;
    private bool _interactive;

    public M3Card(M3CardVariant variant = M3CardVariant.Filled, double padding = 20.0)
    {
        _variant = variant;
        Padding = new Thickness(padding);
        CornerRadius = new CornerRadius(M3Shape.Large);
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;
        RefreshTheme();
        Loaded += (_, _) => RefreshTheme();
        Unloaded += (_, _) => M3ThemeManager.Changed -= OnThemeChanged;
        Loaded += (_, _) => M3ThemeManager.Changed += OnThemeChanged;
    }

    /// <summary>Whether the card responds to the pointer like a surface you can open.</summary>
    public bool Interactive
    {
        get => _interactive;
        set
        {
            _interactive = value;
            Cursor = value ? System.Windows.Input.Cursors.Hand : null;
            RefreshTheme();
        }
    }

    /// <summary>Which emphasis the card uses.</summary>
    public M3CardVariant Variant
    {
        get => _variant;
        set
        {
            _variant = value;
            RefreshTheme();
        }
    }

    /// <summary>Applies the theme colours.</summary>
    public void RefreshTheme()
    {
        var theme = M3ThemeManager.Current;

        (Background, BorderBrush, BorderThickness) = _variant switch
        {
            M3CardVariant.Elevated => (
                (Brush)theme.SurfaceContainerLow,
                (Brush)Brushes.Transparent,
                new Thickness(0)),
            M3CardVariant.Outlined => (
                (Brush)theme.Surface,
                (Brush)theme.OutlineVariant,
                new Thickness(1)),
            _ => (
                (Brush)theme.SurfaceContainer,
                (Brush)Brushes.Transparent,
                new Thickness(0)),
        };

        M3Elevation.Apply(this, _variant == M3CardVariant.Elevated ? 1 : 0);
    }

    private void OnThemeChanged(object? sender, EventArgs e) => RefreshTheme();
}
