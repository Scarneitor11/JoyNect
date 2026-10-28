using System.Windows;

namespace JoyNect.Ui.Theme;

/// <summary>Material 3 shape tokens.</summary>
public enum M3ShapeToken
{
    None,
    ExtraSmall,
    Small,
    Medium,
    Large,
    ExtraLarge,
    ExtraExtraLarge,

    /// <summary>A pill: fully rounded ends, whatever the size of the element.</summary>
    Full,
}

/// <summary>Converts shape tokens into the radii WPF needs.</summary>
/// <remarks>
/// Material's numbers are used directly. They are small enough to look like a mistake and load
/// bearing: a dialog at 28 is a different object from a dialog at 16, and the ramp is what makes
/// nested surfaces (card in a container in a page) read as a hierarchy rather than a pile.
/// </remarks>
public static class M3Shape
{
    public const double ExtraSmall = 4.0;
    public const double Small = 8.0;
    public const double Medium = 12.0;
    public const double Large = 16.0;
    public const double ExtraLarge = 28.0;
    public const double ExtraExtraLarge = 48.0;

    public static double Value(M3ShapeToken token) => token switch
    {
        M3ShapeToken.None => 0.0,
        M3ShapeToken.ExtraSmall => ExtraSmall,
        M3ShapeToken.Small => Small,
        M3ShapeToken.Medium => Medium,
        M3ShapeToken.Large => Large,
        M3ShapeToken.ExtraLarge => ExtraLarge,
        M3ShapeToken.ExtraExtraLarge => ExtraExtraLarge,
        M3ShapeToken.Full => double.PositiveInfinity,
        _ => Medium,
    };

    /// <summary>The corner radius for a token, using the element's height for pill shapes.</summary>
    public static CornerRadius Radius(M3ShapeToken token, double height = 0.0)
    {
        var value = Value(token);
        if (double.IsPositiveInfinity(value))
        {
            return new CornerRadius(Math.Max(height, 0.0) / 2.0);
        }

        return new CornerRadius(value);
    }

    /// <summary>The largest radius a box of this size can use without distorting.</summary>
    public static double Clamp(double radius, double width, double height) =>
        Math.Min(radius, Math.Min(Math.Max(width, 0.0), Math.Max(height, 0.0)) / 2.0);

    /// <summary>A radius that follows the box size, for pills and circles.</summary>
    public static CornerRadius Pill(double width, double height) => new(Clamp(double.PositiveInfinity, width, height));

    public static string Describe(M3ShapeToken token) => token switch
    {
        M3ShapeToken.None => "square",
        M3ShapeToken.Full => "pill",
        _ => $"{Value(token):0} radius",
    };
}
