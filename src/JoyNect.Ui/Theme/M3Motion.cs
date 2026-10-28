using System.Windows;
using System.Windows.Media.Animation;

namespace JoyNect.Ui.Theme;

/// <summary>
/// A cubic bezier timing function, the same curve family Material's motion system is specified in.
/// </summary>
/// <remarks>
/// WPF only ships a fixed set of easing classes, none of which can express Material's curves. This
/// solves the bezier directly: x is monotonic along a Material curve, so a few Newton steps find the
/// parameter for a given progress.
/// </remarks>
public sealed class CubicBezierEasing : EasingFunctionBase
{
    private readonly double _x1;
    private readonly double _y1;
    private readonly double _x2;
    private readonly double _y2;

    public CubicBezierEasing(double x1, double y1, double x2, double y2)
    {
        _x1 = x1;
        _y1 = y1;
        _x2 = x2;
        _y2 = y2;
    }

    /// <inheritdoc />
    protected override Freezable CreateInstanceCore() => new CubicBezierEasing(_x1, _y1, _x2, _y2);

    protected override double EaseInCore(double normalizedTime)
    {
        if (normalizedTime <= 0.0)
        {
            return 0.0;
        }

        if (normalizedTime >= 1.0)
        {
            return 1.0;
        }

        var t = normalizedTime;
        for (var i = 0; i < 8; i++)
        {
            var x = Bezier(t, _x1, _x2);
            var error = x - normalizedTime;
            if (Math.Abs(error) < 1e-6)
            {
                break;
            }

            var slope = BezierSlope(t, _x1, _x2);
            if (Math.Abs(slope) < 1e-6)
            {
                break;
            }

            t -= error / slope;
            t = Math.Clamp(t, 0.0, 1.0);
        }

        return Bezier(t, _y1, _y2);
    }

    /// <summary>A cubic bezier whose end points are fixed at 0 and 1.</summary>
    private static double Bezier(double t, double a, double b)
    {
        var inverse = 1.0 - t;
        return (3.0 * inverse * inverse * t * a) + (3.0 * inverse * t * t * b) + (t * t * t);
    }

    private static double BezierSlope(double t, double a, double b)
    {
        var inverse = 1.0 - t;
        return (3.0 * inverse * inverse * a) + (6.0 * inverse * t * (b - a)) + (3.0 * t * t * (1.0 - b));
    }
}

/// <summary>Material 3 easing curves.</summary>
public static class M3Easing
{
    /// <summary>For elements that start and end on screen.</summary>
    public static IEasingFunction Standard { get; } = Freeze(new CubicBezierEasing(0.2, 0.0, 0.0, 1.0));

    /// <summary>For elements entering the screen.</summary>
    public static IEasingFunction StandardDecelerate { get; } = Freeze(new CubicBezierEasing(0.0, 0.0, 0.0, 1.0));

    /// <summary>For elements leaving the screen.</summary>
    public static IEasingFunction StandardAccelerate { get; } = Freeze(new CubicBezierEasing(0.3, 0.0, 1.0, 1.0));

    /// <summary>The expressive curve: a long, confident settle.</summary>
    public static IEasingFunction Emphasized { get; } = Freeze(new CubicBezierEasing(0.2, 0.0, 0.0, 1.0));

    /// <summary>Expressive easing for entrances.</summary>
    public static IEasingFunction EmphasizedDecelerate { get; } = Freeze(new CubicBezierEasing(0.05, 0.7, 0.1, 1.0));

    /// <summary>Expressive easing for exits.</summary>
    public static IEasingFunction EmphasizedAccelerate { get; } = Freeze(new CubicBezierEasing(0.3, 0.0, 0.8, 0.15));

    private static IEasingFunction Freeze(CubicBezierEasing easing)
    {
        easing.Freeze();
        return easing;
    }
}

/// <summary>Material 3 duration tokens, in milliseconds.</summary>
public static class M3Duration
{
    public const int Short1 = 50;
    public const int Short2 = 100;
    public const int Short3 = 150;
    public const int Short4 = 200;
    public const int Medium1 = 250;
    public const int Medium2 = 300;
    public const int Medium3 = 350;
    public const int Medium4 = 400;
    public const int Long1 = 450;
    public const int Long2 = 500;
    public const int Long3 = 550;
    public const int Long4 = 600;
    public const int ExtraLong1 = 700;
    public const int ExtraLong2 = 800;
    public const int ExtraLong3 = 900;
    public const int ExtraLong4 = 1000;
}

/// <summary>Helpers that build Material motion animations.</summary>
public static class M3Motion
{
    /// <summary>
    /// Whether the user asked Windows to minimise animation.
    /// </summary>
    /// <remarks>
    /// Every animation in the app goes through this, so honouring the system setting is one flag
    /// rather than twenty checks. When it is on, animations complete instantly instead of being
    /// skipped, so control state still ends up correct.
    /// </remarks>
    public static bool ReducedMotion => SystemParameters.ClientAreaAnimation == false;

    /// <summary>Creates an animation for a double property.</summary>
    public static DoubleAnimation Animate(
        double to,
        int durationMilliseconds = M3Duration.Short4,
        IEasingFunction? easing = null,
        double? from = null)
    {
        var animation = new DoubleAnimation
        {
            To = to,
            Duration = new Duration(TimeSpan.FromMilliseconds(ReducedMotion ? 0 : durationMilliseconds)),
            EasingFunction = easing ?? M3Easing.Standard,
        };

        if (from is not null)
        {
            animation.From = from;
        }

        animation.Freeze();
        return animation;
    }

    /// <summary>Runs an animation on a freezable-friendly dependency object.</summary>
    public static void Begin(
        System.Windows.Media.Animation.Animatable target,
        System.Windows.DependencyProperty property,
        double to,
        int durationMilliseconds = M3Duration.Short4,
        IEasingFunction? easing = null)
    {
        target.BeginAnimation(property, Animate(to, durationMilliseconds, easing));
    }
}
