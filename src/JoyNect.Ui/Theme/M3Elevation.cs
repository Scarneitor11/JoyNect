using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace JoyNect.Ui.Theme;

/// <summary>
/// Material's elevation levels as soft shadows.
/// </summary>
/// <remarks>
/// Material on Android tints a surface as it rises; on Windows a shadow reads more clearly, so each
/// level is expressed as a shadow whose depth and blur grow together. The darkest levels are also the
/// most blurred, which is what keeps a dialog from looking like a hard-edged sticker.
/// </remarks>
public static class M3Elevation
{
    private static readonly DropShadowEffect[] Shadows =
    [
        Shadow(0.05, 0.0, 0.0),
        Shadow(0.10, 1.0, 2.0),
        Shadow(0.13, 2.0, 4.0),
        Shadow(0.16, 4.0, 8.0),
        Shadow(0.18, 6.0, 12.0),
        Shadow(0.20, 8.0, 18.0),
    ];

    /// <summary>The shadow for an elevation level, clamped to the supported range.</summary>
    public static DropShadowEffect For(int level) => Shadows[Math.Clamp(level, 0, Shadows.Length - 1)];

    /// <summary>Applies a level to an element.</summary>
    public static void Apply(UIElement element, int level)
    {
        element.Effect = level <= 0 ? null : For(level);
    }

    private static DropShadowEffect Shadow(double opacity, double depth, double blur)
    {
        var effect = new DropShadowEffect
        {
            Color = Colors.Black,
            Opacity = opacity,
            ShadowDepth = depth,
            BlurRadius = blur * 2.0,
            Direction = 270.0,
            RenderingBias = RenderingBias.Performance,
        };

        effect.Freeze();
        return effect;
    }
}
