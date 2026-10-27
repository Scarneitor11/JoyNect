using System.Numerics;
using JoyNect.Core.Protocol;
using Xunit;

namespace JoyNect.Core.Tests.Protocol;

/// <summary>
/// Calibration decoding tests.
/// </summary>
/// <remarks>
/// The input bytes are the documented sample values for a Pro Controller, packed with the same 12
/// bit scheme the controller uses. The right stick block is built in the reference's rotated byte
/// order, so this suite is what pins down that the decoder tells the two orders apart.
/// </remarks>
public sealed class CalibrationTests
{
    private static readonly StickRaw LeftMaxAbove = new(0x4F7, 0x424);
    private static readonly StickRaw LeftCentre = new(0x79F, 0x8A0);
    private static readonly StickRaw LeftMinBelow = new(0x510, 0x479);

    private static byte[] Pack(params StickRaw[] groups)
    {
        var bytes = new byte[groups.Length * 3];
        for (var i = 0; i < groups.Length; i++)
        {
            groups[i].Pack(bytes.AsSpan(i * 3, 3));
        }

        return bytes;
    }

    [Fact]
    public void DecodesTheDocumentedLeftStickOrder()
    {
        // Documented order: maximum above centre, centre, minimum below centre.
        var calibration = StickCalibration.Decode(Pack(LeftMaxAbove, LeftCentre, LeftMinBelow));

        Assert.Equal(0x79F, calibration.CenterX);
        Assert.Equal(0x8A0, calibration.CenterY);
        Assert.Equal(0x4F7, calibration.DeltaAboveX);
        Assert.Equal(0x424, calibration.DeltaAboveY);
        Assert.Equal(0x510, calibration.DeltaBelowX);
        Assert.Equal(0x479, calibration.DeltaBelowY);
        Assert.True(calibration.IsPlausible);
    }

    [Fact]
    public void DecodesTheDocumentedRightStickOrder()
    {
        // Documented order for the right stick block: centre, minimum below centre, maximum above.
        var calibration = StickCalibration.Decode(Pack(LeftCentre, LeftMinBelow, LeftMaxAbove));

        Assert.Equal(0x79F, calibration.CenterX);
        Assert.Equal(0x8A0, calibration.CenterY);
        Assert.Equal(0x4F7, calibration.DeltaAboveX);
        Assert.Equal(0x424, calibration.DeltaAboveY);
        Assert.Equal(0x510, calibration.DeltaBelowX);
        Assert.Equal(0x479, calibration.DeltaBelowY);
    }

    [Fact]
    public void FallsBackToDefaultsForAnErasedBlock()
    {
        var erased = new byte[9];
        Array.Fill(erased, (byte)0xFF);

        var calibration = StickCalibration.Decode(erased);
        Assert.Equal(StickCalibration.Default.CenterX, calibration.CenterX);
        Assert.Equal(StickCalibration.Default.DeltaAboveX, calibration.DeltaAboveX);
    }

    [Fact]
    public void FallsBackToDefaultsForShortInput()
    {
        var calibration = StickCalibration.Decode(new byte[4]);
        Assert.Equal(StickCalibration.Default.CenterX, calibration.CenterX);
    }

    [Fact]
    public void NormalisesSticksAroundTheCalibratedCentre()
    {
        var calibration = StickCalibration.Decode(Pack(LeftMaxAbove, LeftCentre, LeftMinBelow));

        var atCentre = calibration.Normalise(new StickRaw(calibration.CenterX, calibration.CenterY));
        Assert.Equal(0, atCentre.X, 6);
        Assert.Equal(0, atCentre.Y, 6);

        var atMax = calibration.Normalise(new StickRaw(
            calibration.CenterX + calibration.DeltaAboveX,
            calibration.CenterY + calibration.DeltaAboveY));
        Assert.Equal(1.0, atMax.X, 6);
        Assert.Equal(1.0, atMax.Y, 6);

        var atMin = calibration.Normalise(new StickRaw(
            calibration.CenterX - calibration.DeltaBelowX,
            calibration.CenterY - calibration.DeltaBelowY));
        Assert.Equal(-1.0, atMin.X, 6);
        Assert.Equal(-1.0, atMin.Y, 6);
    }

    [Fact]
    public void ClampsRawValuesBeyondTheCalibratedRange()
    {
        var calibration = StickCalibration.Decode(Pack(LeftMaxAbove, LeftCentre, LeftMinBelow));

        var beyond = calibration.Normalise(new StickRaw(4095, 0));
        Assert.InRange(beyond.X, 0.999, 1.0);
        Assert.InRange(beyond.Y, -1.0, -0.999);
    }

    [Fact]
    public void DecodesDocumentedMotionCalibrationAndConvertsUnits()
    {
        // Documented sample: accelerometer origin FFB0 FEB9 00E0, sensitivity 4000 4000 4000,
        // gyro origin 000E FFDF FFD0, gyro sensitivity 343B 343B 343B.
        byte[] block =
        [
            0xB0, 0xFF, 0xB9, 0xFE, 0xE0, 0x00,
            0x00, 0x40, 0x00, 0x40, 0x00, 0x40,
            0x0E, 0x00, 0xDF, 0xFF, 0xD0, 0xFF,
            0x3B, 0x34, 0x3B, 0x34, 0x3B, 0x34,
        ];

        var calibration = ImuCalibration.Decode(block);

        // acc_coeff = 4.0 / (sensitivity - origin) with the origin read as int16.
        Assert.Equal(-80f, calibration.AccelerometerOrigin.X);
        Assert.Equal(4.0 / (0x4000 + 80), calibration.AccelerometerCoefficient.X, 9);

        // gyro_coeff = 936.0 / (sensitivity - origin).
        Assert.Equal(14f, calibration.GyroscopeOrigin.X);
        Assert.Equal(936.0 / (0x343B - 14), calibration.GyroscopeCoefficient.X, 6);

        // A rest sample with a one count gyro reading converts to a small real rotation rate.
        calibration.Convert(
            new ImuSampleRaw(AccelX: 0, AccelY: 0, AccelZ: 0, GyroX: 15, GyroY: 15, GyroZ: 15),
            out var acceleration,
            out var angularVelocity);

        Assert.Equal(Vector3.Zero, acceleration);
        Assert.InRange(angularVelocity.X, 0.05, 0.08);
    }

    [Fact]
    public void FallsBackToDefaultsWhenTheCoefficientsAreUnusable()
    {
        var zeros = new byte[24];
        var calibration = ImuCalibration.Decode(zeros);

        Assert.Equal(ImuCalibration.Default.GyroscopeSensitivity, calibration.GyroscopeSensitivity);
        Assert.Equal(ImuCalibration.Default.AccelerometerSensitivity, calibration.AccelerometerSensitivity);
    }

    [Fact]
    public void DefaultMotionCalibrationUsesTheDocumentedFactoryCoefficients()
    {
        var calibration = ImuCalibration.Default;

        Assert.Equal(4.0 / 0x4000, calibration.AccelerometerCoefficient.X, 9);
        Assert.Equal(936.0 / 0x343B, calibration.GyroscopeCoefficient.X, 6);
    }
}
