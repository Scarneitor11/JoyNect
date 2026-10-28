using JoyNect.Core.Protocol;

namespace JoyNect.Core.Mapping;

/// <summary>
/// Shapes an analog stick: calibration, dead zone, response curve and axis inversion.
/// </summary>
/// <remarks>
/// The dead zone is radial and the remaining travel is rescaled, so leaving the dead zone starts
/// from zero rather than jumping. That rescaling is the difference between a stick that feels like a
/// controller and one that feels like it has a step in the middle.
/// </remarks>
public static class StickProcessor
{
    /// <summary>The largest magnitude XInput accepts for one axis.</summary>
    public const short MaxValue = short.MaxValue;

    /// <summary>Converts a raw stick reading into the virtual pad's axis range.</summary>
    public static (short X, short Y) ToPadStick(StickRaw raw, StickCalibration calibration, StickSettings settings)
    {
        var (normalisedX, normalisedY) = calibration.Normalise(raw);

        // The raw vertical axis increases downwards, while XInput reports positive as up. That flip
        // belongs to the raw format, and the user's InvertY setting is applied on top of it in Apply.
        return Apply(normalisedX, -normalisedY, settings);
    }

    /// <summary>Applies dead zone, curve and inversion to a normalised position.</summary>
    /// <param name="x">Horizontal position, positive right.</param>
    /// <param name="y">Vertical position, positive up (XInput convention).</param>
    /// <param name="settings">Shaping settings.</param>
    public static (short X, short Y) Apply(double x, double y, StickSettings settings)
    {
        if (settings.InvertX)
        {
            x = -x;
        }

        if (settings.InvertY)
        {
            y = -y;
        }

        var magnitude = Math.Sqrt((x * x) + (y * y));
        var deadzone = Math.Clamp(settings.Deadzone, 0, 0.95);

        if (magnitude <= deadzone || magnitude <= double.Epsilon)
        {
            return (0, 0);
        }

        // Rescale the travel that remains after the dead zone so the stick starts from zero.
        var scaled = Math.Min((magnitude - deadzone) / (1.0 - deadzone), 1.0);
        var shaped = settings.Curve is > 0 and not 1.0
            ? Math.Pow(scaled, settings.Curve)
            : scaled;

        var unitX = x / magnitude;
        var unitY = y / magnitude;

        return (Scale(unitX * shaped), Scale(unitY * shaped));
    }

    private static short Scale(double value) =>
        (short)Math.Clamp(Math.Round(Math.Clamp(value, -1.0, 1.0) * MaxValue), short.MinValue + 1, MaxValue);

    /// <summary>True when both axes are centred, used to decide whether a pad update is needed.</summary>
    public static bool IsCentred((short X, short Y) stick) => stick.X == 0 && stick.Y == 0;
}
