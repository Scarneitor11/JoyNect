namespace JoyNect.Core.Design;

/// <summary>
/// CAM16 colour appearance model: hue, chroma and lightness as the eye sees them.
/// </summary>
/// <remarks>
/// Ported from Material Color Utilities (Apache-2.0) and reduced to what JoyNect needs: the forward
/// conversion from a colour, which provides the hue and chroma of an HCT colour. Material's hue and
/// chroma are CAM16 values, which is why a palette keeps its character across tones instead of
/// drifting as naive HSL does.
/// </remarks>
internal sealed class Cam16
{
    private Cam16(double hue, double chroma, double j)
    {
        Hue = hue;
        Chroma = chroma;
        J = j;
    }

    /// <summary>Hue in degrees, 0 to 360.</summary>
    public double Hue { get; }

    /// <summary>Colourfulness relative to the brightness of a similarly lit white.</summary>
    public double Chroma { get; }

    /// <summary>Lightness, 0 (black) to 100 (white).</summary>
    public double J { get; }

    public static Cam16 FromInt(uint argb) => FromIntInViewingConditions(argb, ViewingConditions.Default);

    public static Cam16 FromIntInViewingConditions(uint argb, ViewingConditions viewingConditions)
    {
        var redL = ColorUtils.Linearized(ColorUtils.RedFromArgb(argb));
        var greenL = ColorUtils.Linearized(ColorUtils.GreenFromArgb(argb));
        var blueL = ColorUtils.Linearized(ColorUtils.BlueFromArgb(argb));

        var x = (0.41233895 * redL) + (0.35762064 * greenL) + (0.18051042 * blueL);
        var y = (0.2126 * redL) + (0.7152 * greenL) + (0.0722 * blueL);
        var z = (0.01932141 * redL) + (0.11916382 * greenL) + (0.95034478 * blueL);

        var rC = (0.401288 * x) + (0.650173 * y) - (0.051461 * z);
        var gC = (-0.250268 * x) + (1.204414 * y) + (0.045854 * z);
        var bC = (-0.002079 * x) + (0.048952 * y) + (0.953127 * z);

        var rD = viewingConditions.RgbD[0] * rC;
        var gD = viewingConditions.RgbD[1] * gC;
        var bD = viewingConditions.RgbD[2] * bC;

        var rAF = Math.Pow(viewingConditions.Fl * Math.Abs(rD) / 100.0, 0.42);
        var gAF = Math.Pow(viewingConditions.Fl * Math.Abs(gD) / 100.0, 0.42);
        var bAF = Math.Pow(viewingConditions.Fl * Math.Abs(bD) / 100.0, 0.42);

        var rA = ColorUtils.Signum(rD) * 400.0 * rAF / (rAF + 27.13);
        var gA = ColorUtils.Signum(gD) * 400.0 * gAF / (gAF + 27.13);
        var bA = ColorUtils.Signum(bD) * 400.0 * bAF / (bAF + 27.13);

        var a = ((11.0 * rA) - (12.0 * gA) + bA) / 11.0;
        var b = (rA + gA - (2.0 * bA)) / 9.0;
        var u = ((20.0 * rA) + (20.0 * gA) + (21.0 * bA)) / 20.0;
        var p2 = ((40.0 * rA) + (20.0 * gA) + bA) / 20.0;

        var atanDegrees = Math.Atan2(b, a) * 180.0 / Math.PI;
        var hue = atanDegrees < 0.0 ? atanDegrees + 360.0 : atanDegrees >= 360.0 ? atanDegrees - 360.0 : atanDegrees;

        var ac = p2 * viewingConditions.Nbb;
        var j = 100.0 * Math.Pow(ac / viewingConditions.Aw, viewingConditions.C * viewingConditions.Z);

        // Hue is corrected near zero because the exponential term below is discontinuous there.
        var huePrime = hue < 20.14 ? hue + 360.0 : hue;
        var eHue = 0.25 * (Math.Cos((huePrime * Math.PI / 180.0) + 2.0) + 3.8);
        var p1 = 50000.0 / 13.0 * eHue * viewingConditions.Nc * viewingConditions.Ncb;
        var t = p1 * Math.Sqrt((a * a) + (b * b)) / (u + 0.305);

        var alpha = Math.Pow(t, 0.9) * Math.Pow(1.64 - Math.Pow(0.29, viewingConditions.N), 0.73);
        var chroma = alpha * Math.Sqrt(j / 100.0);

        return new Cam16(hue, chroma, j);
    }
}
