namespace JoyNect.Core.Design;

/// <summary>
/// Finds the sRGB colour matching a requested hue, chroma and tone.
/// </summary>
/// <remarks>
/// A faithful port of the reference solver. The problem it solves is that hue, chroma and tone are
/// not independent in sRGB: asking for all three can describe a colour no display can show. The
/// solver first tries a Newton iteration for the exact answer and, when that is out of gamut, walks
/// the boundary of the RGB cube to find the colour with the requested hue and tone and the largest
/// chroma that fits. That is why a palette's saturated tones still look saturated at the extremes
/// instead of turning muddy or clipping to a wrong hue.
/// </remarks>
internal static class HctSolver
{
    private static readonly double[,] ScaledDiscountFromLinrgb =
    {
        { 0.001200833568784504, 0.002389694492170889, 0.0002795742885861124 },
        { 0.0005891086651375999, 0.0029785502573438758, 0.0003270666104008398 },
        { 0.00010146692491640572, 0.0005364214359186694, 0.0032979401770712076 },
    };

    private static readonly double[,] LinrgbFromScaledDiscount =
    {
        { 1373.2198709594231, -1100.4251190754821, -7.278681089101213 },
        { -271.815969077903, 559.6580465940733, -32.46047482791194 },
        { 1.9622899599665666, -57.173814538844006, 308.7233197812385 },
    };

    private static readonly double[] YFromLinrgb = [0.2126, 0.7152, 0.0722];

    /// <summary>
    /// The linear RGB value at the middle of each 8 bit step.
    /// </summary>
    /// <remarks>
    /// The boundary search snaps to these planes rather than a smooth boundary: a colour that is
    /// rounded to 8 bits must not end up outside the gamut, so the search stops on the midpoint
    /// between two representable values.
    /// </remarks>
    private static readonly double[] CriticalPlanes = BuildCriticalPlanes();

    /// <summary>Solves for a colour, always returning something displayable.</summary>
    public static uint SolveToInt(double hueDegrees, double chroma, double lstar)
    {
        if (chroma < 0.0001 || lstar < 0.0001 || lstar > 99.9999)
        {
            // A near-black, near-white or grey request is a pure grey, and the general solver is
            // ill-conditioned at the extremes.
            return ColorUtils.ArgbFromLstar(lstar);
        }

        hueDegrees = ColorUtils.SanitizeDegrees(hueDegrees);
        var hueRadians = hueDegrees / 180.0 * Math.PI;
        var y = ColorUtils.YFromLstar(lstar);

        var exact = FindResultByJ(hueRadians, chroma, y);
        if (exact != 0)
        {
            return exact;
        }

        var linrgb = BisectToLimit(y, hueRadians);
        return ColorUtils.ArgbFromLinrgb(linrgb);
    }

    private static double[] BuildCriticalPlanes()
    {
        var planes = new double[255];
        for (var i = 0; i < planes.Length; i++)
        {
            planes[i] = ColorUtils.Linearized(i + 0.5);
        }

        return planes;
    }

    private static double SanitizeRadians(double angle) => (angle + (Math.PI * 8)) % (Math.PI * 2);

    private static double ChromaticAdaptation(double component)
    {
        var af = Math.Pow(Math.Abs(component), 0.42);
        return ColorUtils.Signum(component) * 400.0 * af / (af + 27.13);
    }

    private static double InverseChromaticAdaptation(double adapted)
    {
        var adaptedAbs = Math.Abs(adapted);
        var baseValue = Math.Max(0, 27.13 * adaptedAbs / (400.0 - adaptedAbs));
        return ColorUtils.Signum(adapted) * Math.Pow(baseValue, 1.0 / 0.42);
    }

    /// <summary>CAM16 hue of a linear RGB colour, in radians.</summary>
    private static double HueOf(ReadOnlySpan<double> linrgb)
    {
        var scaledDiscount = ColorUtils.Multiply(linrgb, ScaledDiscountFromLinrgb);
        var rA = ChromaticAdaptation(scaledDiscount[0]);
        var gA = ChromaticAdaptation(scaledDiscount[1]);
        var bA = ChromaticAdaptation(scaledDiscount[2]);

        var a = ((11.0 * rA) - (12.0 * gA) + bA) / 11.0;
        var b = (rA + gA - (2.0 * bA)) / 9.0;
        return Math.Atan2(b, a);
    }

    private static bool AreInCyclicOrder(double a, double b, double c)
    {
        var deltaAB = SanitizeRadians(b - a);
        var deltaAC = SanitizeRadians(c - a);
        return deltaAB < deltaAC;
    }

    private static double Intercept(double source, double mid, double target) => (mid - source) / (target - source);

    private static double[] LerpPoint(double[] source, double t, double[] target) =>
    [
        source[0] + ((target[0] - source[0]) * t),
        source[1] + ((target[1] - source[1]) * t),
        source[2] + ((target[2] - source[2]) * t),
    ];

    private static double[] SetCoordinate(double[] source, double coordinate, double[] target, int axis)
    {
        var t = Intercept(source[axis], coordinate, target[axis]);
        return LerpPoint(source, t, target);
    }

    private static bool IsBounded(double x) => x is >= 0.0 and <= 100.0;

    /// <summary>Vertex <paramref name="n"/> of the intersection between the Y plane and the cube.</summary>
    private static double[] NthVertex(double y, int n)
    {
        var kR = YFromLinrgb[0];
        var kG = YFromLinrgb[1];
        var kB = YFromLinrgb[2];

        var coordinateA = n % 4 <= 1 ? 0.0 : 100.0;
        var coordinateB = n % 2 == 0 ? 0.0 : 100.0;

        if (n < 4)
        {
            var g = coordinateA;
            var b = coordinateB;
            var r = (y - (g * kG) - (b * kB)) / kR;
            return IsBounded(r) ? [r, g, b] : [-1.0, -1.0, -1.0];
        }

        if (n < 8)
        {
            var b = coordinateA;
            var r = coordinateB;
            var g = (y - (r * kR) - (b * kB)) / kG;
            return IsBounded(g) ? [r, g, b] : [-1.0, -1.0, -1.0];
        }
        else
        {
            var r = coordinateA;
            var g = coordinateB;
            var b = (y - (r * kR) - (g * kG)) / kB;
            return IsBounded(b) ? [r, g, b] : [-1.0, -1.0, -1.0];
        }
    }

    /// <summary>The edge of the cube's Y cross-section that contains the target hue.</summary>
    private static (double[] Left, double[] Right) BisectToSegment(double y, double targetHue)
    {
        double[] left = [-1.0, -1.0, -1.0];
        double[] right = left;
        var leftHue = 0.0;
        var rightHue = 0.0;
        var initialized = false;
        var uncut = true;

        for (var n = 0; n < 12; n++)
        {
            var mid = NthVertex(y, n);
            if (mid[0] < 0)
            {
                continue;
            }

            var midHue = HueOf(mid);
            if (!initialized)
            {
                left = mid;
                right = mid;
                leftHue = midHue;
                rightHue = midHue;
                initialized = true;
                continue;
            }

            if (uncut || AreInCyclicOrder(leftHue, midHue, rightHue))
            {
                uncut = false;
                if (AreInCyclicOrder(leftHue, targetHue, midHue))
                {
                    right = mid;
                    rightHue = midHue;
                }
                else
                {
                    left = mid;
                    leftHue = midHue;
                }
            }
        }

        return (left, right);
    }

    private static double[] Midpoint(double[] a, double[] b) =>
    [
        (a[0] + b[0]) / 2.0,
        (a[1] + b[1]) / 2.0,
        (a[2] + b[2]) / 2.0,
    ];

    private static int CriticalPlaneBelow(double x) => (int)Math.Floor(x - 0.5);

    private static int CriticalPlaneAbove(double x) => (int)Math.Ceiling(x - 0.5);

    /// <summary>The in-gamut colour with the requested Y and hue.</summary>
    private static double[] BisectToLimit(double y, double targetHue)
    {
        var (left, right) = BisectToSegment(y, targetHue);
        var leftHue = HueOf(left);

        for (var axis = 0; axis < 3; axis++)
        {
            if (left[axis] == right[axis])
            {
                continue;
            }

            int lPlane;
            int rPlane;
            if (left[axis] < right[axis])
            {
                lPlane = CriticalPlaneBelow(ColorUtils.TrueDelinearized(left[axis]));
                rPlane = CriticalPlaneAbove(ColorUtils.TrueDelinearized(right[axis]));
            }
            else
            {
                lPlane = CriticalPlaneAbove(ColorUtils.TrueDelinearized(left[axis]));
                rPlane = CriticalPlaneBelow(ColorUtils.TrueDelinearized(right[axis]));
            }

            for (var i = 0; i < 8; i++)
            {
                if (Math.Abs(rPlane - lPlane) <= 1)
                {
                    break;
                }

                var mPlane = (int)Math.Floor((lPlane + rPlane) / 2.0);
                var midPlaneCoordinate = CriticalPlanes[Math.Clamp(mPlane, 0, CriticalPlanes.Length - 1)];
                var mid = SetCoordinate(left, midPlaneCoordinate, right, axis);
                var midHue = HueOf(mid);

                if (AreInCyclicOrder(leftHue, targetHue, midHue))
                {
                    right = mid;
                    rPlane = mPlane;
                }
                else
                {
                    left = mid;
                    leftHue = midHue;
                    lPlane = mPlane;
                }
            }
        }

        return Midpoint(left, right);
    }

    /// <summary>Newton iteration for a colour with the exact hue, chroma and Y; 0 when out of gamut.</summary>
    private static uint FindResultByJ(double hueRadians, double chroma, double y)
    {
        var viewingConditions = ViewingConditions.Default;
        var j = Math.Sqrt(y) * 11.0;

        var tInnerCoeff = 1.0 / Math.Pow(1.64 - Math.Pow(0.29, viewingConditions.N), 0.73);
        var eHue = 0.25 * (Math.Cos(hueRadians + 2.0) + 3.8);
        var p1 = eHue * (50000.0 / 13.0) * viewingConditions.Nc * viewingConditions.Ncb;
        var hSin = Math.Sin(hueRadians);
        var hCos = Math.Cos(hueRadians);

        for (var iterationRound = 0; iterationRound < 5; iterationRound++)
        {
            var jNormalized = j / 100.0;
            var alpha = chroma == 0.0 || j == 0.0 ? 0.0 : chroma / Math.Sqrt(jNormalized);
            var t = Math.Pow(alpha * tInnerCoeff, 1.0 / 0.9);
            var ac = viewingConditions.Aw * Math.Pow(jNormalized, 1.0 / viewingConditions.C / viewingConditions.Z);
            var p2 = ac / viewingConditions.Nbb;
            var gamma = 23.0 * (p2 + 0.305) * t
                / ((23.0 * p1) + (11.0 * t * hCos) + (108.0 * t * hSin));

            var a = gamma * hCos;
            var b = gamma * hSin;
            var rA = ((460.0 * p2) + (451.0 * a) + (288.0 * b)) / 1403.0;
            var gA = ((460.0 * p2) - (891.0 * a) - (261.0 * b)) / 1403.0;
            var bA = ((460.0 * p2) - (220.0 * a) - (6300.0 * b)) / 1403.0;

            var rCScaled = InverseChromaticAdaptation(rA);
            var gCScaled = InverseChromaticAdaptation(gA);
            var bCScaled = InverseChromaticAdaptation(bA);

            var linrgb = ColorUtils.Multiply([rCScaled, gCScaled, bCScaled], LinrgbFromScaledDiscount);

            if (linrgb[0] < 0 || linrgb[1] < 0 || linrgb[2] < 0)
            {
                return 0;
            }

            var fnj = (YFromLinrgb[0] * linrgb[0]) + (YFromLinrgb[1] * linrgb[1]) + (YFromLinrgb[2] * linrgb[2]);
            if (fnj <= 0)
            {
                return 0;
            }

            if (iterationRound == 4 || Math.Abs(fnj - y) < 0.002)
            {
                if (linrgb[0] > 100.01 || linrgb[1] > 100.01 || linrgb[2] > 100.01)
                {
                    return 0;
                }

                return ColorUtils.ArgbFromLinrgb(linrgb);
            }

            // Newton's method, using 2 * fn(j) / j as the approximation of fn'(j).
            j -= (fnj - y) * j / (2.0 * fnj);
        }

        return 0;
    }
}
