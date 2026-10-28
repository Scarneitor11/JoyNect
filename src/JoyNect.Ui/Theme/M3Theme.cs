using System.Windows.Media;
using JoyNect.Core.Design;

namespace JoyNect.Ui.Theme;

/// <summary>A Material scheme plus the brushes and state layers built from it.</summary>
/// <remarks>
/// Brushes are frozen and cached per role: a theme is read many times per frame and a frozen brush
/// costs nothing to share, whereas creating one per control would allocate on every theme change.
/// </remarks>
public sealed class M3Theme
{
    /// <summary>State layer opacity for a pointer hovering a control.</summary>
    public const double HoverOpacity = 0.08;

    /// <summary>State layer opacity for a focused control.</summary>
    public const double FocusOpacity = 0.10;

    /// <summary>State layer opacity while a control is held down.</summary>
    public const double PressedOpacity = 0.10;

    /// <summary>State layer opacity for disabled content, which Material renders at 38%.</summary>
    public const double DisabledOpacity = 0.38;

    private readonly Dictionary<ColorRole, SolidColorBrush> _brushes = [];
    private readonly Dictionary<(ColorRole Role, int Opacity), SolidColorBrush> _overlays = [];

    public M3Theme(M3Scheme scheme)
    {
        Scheme = scheme;
    }

    public M3Scheme Scheme { get; }

    public bool IsDark => Scheme.IsDark;

    /// <summary>The brush for a role.</summary>
    public SolidColorBrush Brush(ColorRole role)
    {
        if (_brushes.TryGetValue(role, out var cached))
        {
            return cached;
        }

        var brush = new SolidColorBrush(ToColor(Scheme[role]));
        brush.Freeze();
        _brushes[role] = brush;
        return brush;
    }

    /// <summary>A translucent overlay of a role, used for state layers and scrims.</summary>
    public SolidColorBrush Overlay(ColorRole role, double opacity)
    {
        var key = (role, (int)Math.Round(Math.Clamp(opacity, 0.0, 1.0) * 100.0));
        if (_overlays.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var colour = ToColor(Scheme[role]);
        colour.A = (byte)Math.Clamp(key.Item2 * 255 / 100, 0, 255);
        var brush = new SolidColorBrush(colour);
        brush.Freeze();
        _overlays[key] = brush;
        return brush;
    }

    public SolidColorBrush Primary => Brush(ColorRole.Primary);

    public SolidColorBrush OnPrimary => Brush(ColorRole.OnPrimary);

    public SolidColorBrush PrimaryContainer => Brush(ColorRole.PrimaryContainer);

    public SolidColorBrush OnPrimaryContainer => Brush(ColorRole.OnPrimaryContainer);

    public SolidColorBrush Secondary => Brush(ColorRole.Secondary);

    public SolidColorBrush SecondaryContainer => Brush(ColorRole.SecondaryContainer);

    public SolidColorBrush OnSecondaryContainer => Brush(ColorRole.OnSecondaryContainer);

    public SolidColorBrush Tertiary => Brush(ColorRole.Tertiary);

    public SolidColorBrush TertiaryContainer => Brush(ColorRole.TertiaryContainer);

    public SolidColorBrush OnTertiaryContainer => Brush(ColorRole.OnTertiaryContainer);

    public SolidColorBrush Error => Brush(ColorRole.Error);

    public SolidColorBrush OnError => Brush(ColorRole.OnError);

    public SolidColorBrush ErrorContainer => Brush(ColorRole.ErrorContainer);

    public SolidColorBrush OnErrorContainer => Brush(ColorRole.OnErrorContainer);

    public SolidColorBrush Surface => Brush(ColorRole.Surface);

    public SolidColorBrush OnSurface => Brush(ColorRole.OnSurface);

    public SolidColorBrush SurfaceVariant => Brush(ColorRole.SurfaceVariant);

    public SolidColorBrush OnSurfaceVariant => Brush(ColorRole.OnSurfaceVariant);

    public SolidColorBrush SurfaceContainerLowest => Brush(ColorRole.SurfaceContainerLowest);

    public SolidColorBrush SurfaceContainerLow => Brush(ColorRole.SurfaceContainerLow);

    public SolidColorBrush SurfaceContainer => Brush(ColorRole.SurfaceContainer);

    public SolidColorBrush SurfaceContainerHigh => Brush(ColorRole.SurfaceContainerHigh);

    public SolidColorBrush SurfaceContainerHighest => Brush(ColorRole.SurfaceContainerHighest);

    public SolidColorBrush SurfaceDim => Brush(ColorRole.SurfaceDim);

    public SolidColorBrush SurfaceBright => Brush(ColorRole.SurfaceBright);

    public SolidColorBrush Outline => Brush(ColorRole.Outline);

    public SolidColorBrush OutlineVariant => Brush(ColorRole.OutlineVariant);

    public SolidColorBrush InverseSurface => Brush(ColorRole.InverseSurface);

    public SolidColorBrush InverseOnSurface => Brush(ColorRole.InverseOnSurface);

    public SolidColorBrush InversePrimary => Brush(ColorRole.InversePrimary);

    public SolidColorBrush Scrim => Brush(ColorRole.Scrim);

    public SolidColorBrush Background => Brush(ColorRole.Background);

    public SolidColorBrush OnBackground => Brush(ColorRole.OnBackground);

    /// <summary>Converts an ARGB integer into a WPF colour.</summary>
    public static Color ToColor(uint argb) => Color.FromArgb(
        (byte)(argb >> 24),
        (byte)((argb >> 16) & 0xFF),
        (byte)((argb >> 8) & 0xFF),
        (byte)(argb & 0xFF));

    /// <summary>Converts a WPF colour into an ARGB integer.</summary>
    public static uint ToArgb(Color colour) =>
        ((uint)colour.A << 24) | ((uint)colour.R << 16) | ((uint)colour.G << 8) | colour.B;

    /// <summary>True when the scheme is dark enough that light text is needed.</summary>
    public Color ContrastingOnSurface => ToColor(Scheme.OnSurface);
}
