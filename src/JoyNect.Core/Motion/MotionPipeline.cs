using System.Numerics;
using JoyNect.Core.Protocol;

namespace JoyNect.Core.Motion;

/// <summary>One motion sample in physical units.</summary>
/// <param name="AccelerationG">Acceleration in G, in the controller's own axes.</param>
/// <param name="AngularVelocityDps">Angular velocity in degrees per second.</param>
/// <param name="Timestamp">When the sample was taken, in milliseconds.</param>
public readonly record struct MotionSample(Vector3 AccelerationG, Vector3 AngularVelocityDps, double Timestamp)
{
    public static MotionSample Zero => new(Vector3.Zero, Vector3.Zero, 0);

    /// <summary>Tilt in degrees, from the gravity vector.</summary>
    /// <remarks>
    /// Only meaningful when the controller is close to still: acceleration and gravity cannot be
    /// separated from a single sample. The motion page says as much rather than pretending otherwise.
    /// </remarks>
    public (double Pitch, double Roll) TiltDegrees
    {
        get
        {
            var pitch = Math.Atan2(-AccelerationG.Y, Math.Sqrt((AccelerationG.X * AccelerationG.X) + (AccelerationG.Z * AccelerationG.Z)));
            var roll = Math.Atan2(AccelerationG.X, AccelerationG.Z);
            return (RadiansToDegrees(pitch), RadiansToDegrees(roll));
        }
    }

    private static double RadiansToDegrees(double radians) => radians * 180.0 / Math.PI;
}

/// <summary>
/// Turns raw IMU counts into degrees per second and G.
/// </summary>
/// <remarks>
/// The controller's accelerometer and gyroscope are calibrated at the factory; that calibration is
/// read from flash at connect time and is what makes the numbers usable. Applying it here rather than
/// in the UI means the motion page, the DSU motion server and the tests all agree on units.
/// </remarks>
public static class MotionPipeline
{
    /// <summary>Default sample period for a controller streaming at about 60 Hz.</summary>
    public const double DefaultPeriodMilliseconds = 1000.0 / 60.0;

    /// <summary>Converts one raw IMU sample using the controller's calibration.</summary>
    public static MotionSample Convert(in ImuSampleRaw raw, ImuCalibration calibration, double timestamp)
    {
        ArgumentNullException.ThrowIfNull(calibration);

        // The calibration already knows how to remove the gyroscope's resting offset; doing that
        // twice would bias every reading in the opposite direction.
        calibration.Convert(raw, out var acceleration, out var angular);
        return new MotionSample(acceleration, angular, timestamp);
    }

    /// <summary>Converts the newest of the three samples inside a full state report.</summary>
    public static MotionSample FromReport(in FullStateReport report, ImuCalibration calibration, double timestamp) =>
        Convert(report.Sample2, calibration, timestamp);

    /// <summary>
    /// Removes the gyroscope's resting bias, which is small but never exactly zero.
    /// </summary>
    /// <remarks>
    /// Averaging samples taken while the controller is still gives a usable bias. Two hundred
    /// samples at 60 Hz is a little over three seconds, which is short enough to ask of a user.
    /// </remarks>
    public static Vector3 EstimateGyroBias(IEnumerable<Vector3> samples)
    {
        var sum = Vector3.Zero;
        var count = 0;

        foreach (var sample in samples)
        {
            sum += sample;
            count++;
        }

        return count == 0 ? Vector3.Zero : sum / count;
    }

    /// <summary>Applies a bias estimate to a sample, and clamps tiny residual noise to zero.</summary>
    public static MotionSample ApplyBias(in MotionSample sample, Vector3 bias, double deadbandDps = 0.4)
    {
        var angular = sample.AngularVelocityDps - bias;
        angular = new Vector3(
            Math.Abs(angular.X) < deadbandDps ? 0 : angular.X,
            Math.Abs(angular.Y) < deadbandDps ? 0 : angular.Y,
            Math.Abs(angular.Z) < deadbandDps ? 0 : angular.Z);

        return sample with { AngularVelocityDps = angular };
    }
}
