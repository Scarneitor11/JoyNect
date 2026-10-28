namespace JoyNect.Core.Design;

/// <summary>
/// Colour space conversions used by the Material colour system.
/// </summary>
/// <remarks>
/// This is a focused C# port of the parts of Google's Material Color Utilities that JoyNect needs
/// (Apache-2.0). The algorithm is the published one: sRGB ↔ XYZ with the D65 white point, CIE L* for
/// tone, and CAM16 for hue and chroma. Keeping the port small and verified against fixtures generated
/// by the original library is what makes the resulting palette match what Material would produce
/// rather than merely resembling it.
/// </remarks>
public static class ColorUtils
{
    /// <summary>D65 white point, on a 0 to 100 Y scale.</summary>
    public static IReadOnlyList<double> WhitePointD65 { get; } = [95.047, 100.0, 108.883];

    private static readonly double[,] SrgbToXyz =
    {
        { 0.41233895, 0.35762064, 0.18051042 },
        { 0.2126, 0.7152, 0.0722 },
        { 0.01932141, 0.11916382, 0.95034478 },
    };

    private static readonly double[,] XyzToSrgb =
    {
        { 3.2413774792388685, -1.5376652402851851, -0.49885366846268053 },
        { -0.9691452513005321, 1.8758853451067872, 0.04156585616912061 },
        { 0.05562093689691305, -0.20395524564742123, 1.0571799111220335 },
    };

    /// <summary>Packs an opaque colour from 8 bit components.</summary>
    public static uint ArgbFromRgb(int red, int green, int blue) =>
        0xFF000000u
        | ((uint)(red & 0xFF) << 16)
        | ((uint)(green & 0xFF) << 8)
        | (uint)(blue & 0xFF);

    public static int RedFromArgb(uint argb) => (int)((argb >> 16) & 0xFF);

    public static int GreenFromArgb(uint argb) => (int)((argb >> 8) & 0xFF);

    public static int BlueFromArgb(uint argb) => (int)(argb & 0xFF);

    /// <summary>Formats a colour as <c>#RRGGBB</c>.</summary>
    public static string ToHex(uint argb) => $"#{RedFromArgb(argb):X2}{GreenFromArgb(argb):X2}{BlueFromArgb(argb):X2}";

    /// <summary>Parses <c>#RRGGBB</c> or <c>#AARRGGBB</c>; returns false instead of throwing.</summary>
    public static bool TryParseHex(string? text, out uint argb)
    {
        argb = 0xFF000000u;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var value = text.Trim();
        if (value.StartsWith('#'))
        {
            value = value[1..];
        }

        if (value.Length is not (6 or 8))
        {
            return false;
        }

        if (!uint.TryParse(value, System.Globalization.NumberStyles.HexNumber, null, out var parsed))
        {
            return false;
        }

        argb = value.Length == 6 ? parsed | 0xFF000000u : parsed;
        return true;
    }

    /// <summary>Linearises an 8 bit sRGB component onto the 0 to 100 linear scale.</summary>
    public static double Linearized(double component0To255)
    {
        var normalized = component0To255 / 255.0;
        return normalized <= 0.040449936
            ? normalized / 12.92 * 100.0
            : Math.Pow((normalized + 0.055) / 1.055, 2.4) * 100.0;
    }

    /// <summary>
    /// Converts a linear component back to an 8 bit sRGB value.
    /// </summary>
    /// <remarks>
    /// The rounding here matches the reference implementation exactly. It is worth being pedantic:
    /// a palette is generated from its own tones, so a one unit difference compounds across a theme.
    /// </remarks>
    public static int Delinearized(double component0To100)
    {
        var normalized = component0To100 / 100.0;
        var delinearized = normalized <= 0.0031308
            ? normalized * 12.92
            : (1.055 * Math.Pow(normalized, 1.0 / 2.4)) - 0.055;

        return Math.Clamp((int)Math.Round(delinearized * 255.0, MidpointRounding.AwayFromZero), 0, 255);
    }

    /// <summary>Like <see cref="Delinearized"/> but returns the unrounded value in 0 to 255.</summary>
    public static double TrueDelinearized(double component0To100)
    {
        var normalized = component0To100 / 100.0;
        var delinearized = normalized <= 0.0031308
            ? normalized * 12.92
            : (1.055 * Math.Pow(normalized, 1.0 / 2.4)) - 0.055;

        return delinearized * 255.0;
    }

    /// <summary>CIE Lab companding, used to move between L* and Y.</summary>
    public static double LabF(double t)
    {
        const double e = 216.0 / 24389.0;
        const double kappa = 24389.0 / 27.0;
        return t > e ? Math.Pow(t, 1.0 / 3.0) : ((kappa * t) + 16.0) / 116.0;
    }

    /// <summary>Inverse of <see cref="LabF"/>.</summary>
    public static double LabInvF(double ft)
    {
        const double e = 216.0 / 24389.0;
        const double kappa = 24389.0 / 27.0;
        var ft3 = ft * ft * ft;
        return ft3 > e ? ft3 : ((116.0 * ft) - 16.0) / kappa;
    }

    /// <summary>L* to relative luminance Y.</summary>
    public static double YFromLstar(double lstar) => 100.0 * LabInvF((lstar + 16.0) / 116.0);

    /// <summary>Relative luminance Y to L*.</summary>
    public static double LstarFromY(double y) => (LabF(y / 100.0) * 116.0) - 16.0;

    /// <summary>L* of a colour, which is also its Material tone.</summary>
    public static double LstarFromArgb(uint argb) => 116.0 * LabF(XyzFromArgb(argb)[1] / 100.0) - 16.0;

    /// <summary>XYZ of an sRGB colour, Y on a 0 to 100 scale.</summary>
    public static double[] XyzFromArgb(uint argb)
    {
        var red = Linearized(RedFromArgb(argb));
        var green = Linearized(GreenFromArgb(argb));
        var blue = Linearized(BlueFromArgb(argb));

        return
        [
            (SrgbToXyz[0, 0] * red) + (SrgbToXyz[0, 1] * green) + (SrgbToXyz[0, 2] * blue),
            (SrgbToXyz[1, 0] * red) + (SrgbToXyz[1, 1] * green) + (SrgbToXyz[1, 2] * blue),
            (SrgbToXyz[2, 0] * red) + (SrgbToXyz[2, 1] * green) + (SrgbToXyz[2, 2] * blue),
        ];
    }

    /// <summary>sRGB colour of an XYZ triplet.</summary>
    public static uint ArgbFromXyz(double x, double y, double z)
    {
        var linearRed = (XyzToSrgb[0, 0] * x) + (XyzToSrgb[0, 1] * y) + (XyzToSrgb[0, 2] * z);
        var linearGreen = (XyzToSrgb[1, 0] * x) + (XyzToSrgb[1, 1] * y) + (XyzToSrgb[1, 2] * z);
        var linearBlue = (XyzToSrgb[2, 0] * x) + (XyzToSrgb[2, 1] * y) + (XyzToSrgb[2, 2] * z);

        return ArgbFromRgb(
            Delinearized(linearRed),
            Delinearized(linearGreen),
            Delinearized(linearBlue));
    }

    /// <summary>sRGB colour of a linear RGB triplet.</summary>
    public static uint ArgbFromLinrgb(ReadOnlySpan<double> linrgb) => ArgbFromRgb(
        Delinearized(linrgb[0]),
        Delinearized(linrgb[1]),
        Delinearized(linrgb[2]));

    /// <summary>The grey whose L* is the given value.</summary>
    public static uint ArgbFromLstar(double lstar)
    {
        var component = Delinearized(YFromLstar(lstar));
        return ArgbFromRgb(component, component, component);
    }

    /// <summary>sRGB colour of a CIE L*a*b* triplet.</summary>
    public static uint ArgbFromLab(double l, double a, double b)
    {
        var fy = (l + 16.0) / 116.0;
        var fx = (a / 500.0) + fy;
        var fz = fy - (b / 200.0);

        var x = LabInvF(fx) * WhitePointD65[0];
        var y = LabInvF(fy) * WhitePointD65[1];
        var z = LabInvF(fz) * WhitePointD65[2];

        return ArgbFromXyz(x, y, z);
    }

    /// <summary>Multiplies a row vector by a 3x3 matrix, as the reference implementation does.</summary>
    public static double[] Multiply(ReadOnlySpan<double> source, double[,] matrix) =>
    [
        (source[0] * matrix[0, 0]) + (source[1] * matrix[0, 1]) + (source[2] * matrix[0, 2]),
        (source[0] * matrix[1, 0]) + (source[1] * matrix[1, 1]) + (source[2] * matrix[1, 2]),
        (source[0] * matrix[2, 0]) + (source[1] * matrix[2, 1]) + (source[2] * matrix[2, 2]),
    ];

    public static int Signum(double value) => value < 0.0 ? -1 : value > 0.0 ? 1 : 0;

    /// <summary>Normalises an angle in degrees onto [0, 360).</summary>
    public static double SanitizeDegrees(double degrees)
    {
        if (degrees < 0.0)
        {
            return 360.0 + (degrees % 360.0);
        }

        return degrees >= 360.0 ? degrees % 360.0 : degrees;
    }

    /// <summary>Interpolates between two values.</summary>
    public static double Lerp(double start, double stop, double amount) => start + ((stop - start) * amount);

    /// <summary>WCAG contrast ratio between two relative luminances.</summary>
    public static double ContrastRatio(double luminanceA, double luminanceB)
    {
        var lighter = Math.Max(luminanceA, luminanceB);
        var darker = Math.Min(luminanceA, luminanceB);
        return (lighter + 0.05) / (darker + 0.05);
    }

    /// <summary>WCAG contrast ratio between two colours.</summary>
    public static double ContrastRatio(uint argbA, uint argbB)
    {
        var a = XyzFromArgb(argbA)[1] / 100.0;
        var b = XyzFromArgb(argbB)[1] / 100.0;
        return ContrastRatio(a, b);
    }
}
