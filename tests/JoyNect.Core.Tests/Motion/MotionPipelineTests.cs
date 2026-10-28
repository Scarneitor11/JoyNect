using System.Numerics;
using JoyNect.Core.Motion;
using JoyNect.Core.Protocol;
using Xunit;

namespace JoyNect.Core.Tests.Motion;

public sealed class MotionPipelineTests
{
    [Fact]
    public void TheDefaultCalibrationUsesDocumentedUnits()
    {
        var calibration = ImuCalibration.Default;

        // The factory accelerometer sensitivity of 0x4000 is four G per 16384 counts, so a full
        // scale reading of 16384 counts is 4 G.
        Assert.Equal(4.0, 16384 * calibration.AccelerometerCoefficient.X, 3);

        // 0x343B (13371) is the documented +/-2000 dps full scale; 936/13371 per count.
        var fullScale = 13371 * calibration.GyroscopeCoefficient.X;
        Assert.InRange(fullScale, 900.0, 950.0);
    }

    [Fact]
    public void ConversionAppliesTheCalibrationIncludingTheGyroOrigin()
    {
        var calibration = new ImuCalibration(
            accelerometerOrigin: Vector3.Zero,
            accelerometerSensitivity: new Vector3(16384),
            gyroscopeOrigin: new Vector3(100, -50, 25),
            gyroscopeSensitivity: new Vector3(13371));

        var raw = new ImuSampleRaw(AccelX: 16384, AccelY: 0, AccelZ: 0, GyroX: 100, GyroY: -50, GyroZ: 25);
        var sample = MotionPipeline.Convert(raw, calibration, timestamp: 12.5);

        Assert.Equal(4.0, sample.AccelerationG.X, 3);
        Assert.Equal(Vector3.Zero, sample.AngularVelocityDps);
        Assert.Equal(12.5, sample.Timestamp, 3);
    }

    [Fact]
    public void TiltComesFromGravity()
    {
        // Lying flat on its back, a controller reports one G along its own Z axis.
        var flat = new MotionSample(new Vector3(0, 0, 1), Vector3.Zero, 0);
        var (pitch, roll) = flat.TiltDegrees;

        Assert.Equal(0.0, pitch, 1);
        Assert.Equal(0.0, roll, 1);

        var onItsSide = new MotionSample(new Vector3(1, 0, 0), Vector3.Zero, 0);
        var (sidePitch, sideRoll) = onItsSide.TiltDegrees;

        Assert.Equal(0.0, sidePitch, 1);
        Assert.Equal(90.0, sideRoll, 1);
    }

    [Fact]
    public void BiasIsTheMeanAndIsRemovedWithADeadband()
    {
        var bias = MotionPipeline.EstimateGyroBias(
        [
            new Vector3(1.0f, -0.5f, 0.2f),
            new Vector3(1.2f, -0.6f, 0.3f),
            new Vector3(0.8f, -0.4f, 0.1f),
        ]);

        Assert.Equal(1.0, bias.X, 3);
        Assert.Equal(-0.5, bias.Y, 3);
        Assert.Equal(0.2, bias.Z, 3);

        var still = new MotionSample(Vector3.UnitZ, new Vector3(1.1f, -0.5f, 0.2f), 0);
        var filtered = MotionPipeline.ApplyBias(still, bias);

        // Residual noise below the deadband reads as exactly zero, which stops the motion page from
        // jittering while a controller sits on a desk.
        Assert.Equal(Vector3.Zero, filtered.AngularVelocityDps);

        var moving = new MotionSample(Vector3.UnitZ, new Vector3(31.0f, -0.5f, 0.2f), 0);
        Assert.Equal(30.0, MotionPipeline.ApplyBias(moving, bias).AngularVelocityDps.X, 3);
    }

    [Fact]
    public void AnEmptyBiasSampleIsZeroRatherThanNaN()
    {
        Assert.Equal(Vector3.Zero, MotionPipeline.EstimateGyroBias([]));
    }
}
