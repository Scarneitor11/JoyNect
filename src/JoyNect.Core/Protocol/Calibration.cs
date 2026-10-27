using System.Numerics;
using System.Runtime.InteropServices;

namespace JoyNect.Core.Protocol;

/// <summary>
/// Analog stick calibration read from the controller's SPI flash.
/// </summary>
/// <remarks>
/// Each stick is described by three packed pairs (nine bytes):
/// <c>x/y maximum above centre</c>, <c>x/y centre</c> and <c>x/y minimum below centre</c>.
/// The values are <em>deltas</em> from the centre, not absolutes.
///
/// The reference notes document the two sticks in different orders: the left stick block is
/// (above, centre, below) and the right stick block is (centre, below, above). Because that is easy
/// to get wrong and a wrong reading silently ruins every game's aim, <see cref="Decode"/> evaluates
/// both readings and keeps the self-consistent one instead of trusting the order.
/// </remarks>
public sealed class StickCalibration
{
    private const int MinPlausibleDelta = 0x100;

    // A stick's half-range cannot exceed half of the 12 bit range: the extremes would fall outside
    // what the hardware can report. This bound is what rejects an all-0xFF (erased) block.
    private const int MaxPlausibleDelta = 0x800;
    private const int CentreTolerance = 512;

    public StickCalibration(
        int centerX,
        int centerY,
        int deltaAboveX,
        int deltaAboveY,
        int deltaBelowX,
        int deltaBelowY)
    {
        CenterX = centerX;
        CenterY = centerY;
        DeltaAboveX = deltaAboveX;
        DeltaAboveY = deltaAboveY;
        DeltaBelowX = deltaBelowX;
        DeltaBelowY = deltaBelowY;
    }

    public int CenterX { get; }

    public int CenterY { get; }

    public int DeltaAboveX { get; }

    public int DeltaAboveY { get; }

    public int DeltaBelowX { get; }

    public int DeltaBelowY { get; }

    /// <summary>
    /// Fallback calibration: the values a typical Pro Controller reports.
    /// </summary>
    /// <remarks>
    /// Deliberately not "the whole 12 bit range": the reference notes warn that the console itself
    /// falls back to a non-trivial default when the flash values fail its sanity checks, and a
    /// full-range default makes sticks feel dead at the edges.
    /// </remarks>
    public static StickCalibration Default { get; } = new(
        centerX: 0x79F,
        centerY: 0x8A0,
        deltaAboveX: 0x4F7,
        deltaAboveY: 0x424,
        deltaBelowX: 0x510,
        deltaBelowY: 0x479);

    /// <summary>True when the deltas are large enough to be a real calibration.</summary>
    public bool IsPlausible => IsPlausibleDelta(DeltaAboveX) && IsPlausibleDelta(DeltaAboveY)
        && IsPlausibleDelta(DeltaBelowX) && IsPlausibleDelta(DeltaBelowY)
        && Math.Abs(CenterX - StickRaw.Center) <= CentreTolerance
        && Math.Abs(CenterY - StickRaw.Center) <= CentreTolerance;

    /// <summary>Decodes the nine byte calibration block of one stick, validating the reading.</summary>
    public static StickCalibration Decode(ReadOnlySpan<byte> nineBytes)
    {
        if (nineBytes.Length < 9)
        {
            return Default;
        }

        var data = new int[6];
        for (var group = 0; group < 3; group++)
        {
            var offset = group * 3;
            var decoded = StickRaw.Unpack(nineBytes[offset], nineBytes[offset + 1], nineBytes[offset + 2]);
            data[group * 2] = decoded.X;
            data[group * 2 + 1] = decoded.Y;
        }

        // Reading used by the left stick: maximum, centre, minimum.
        var asDocumentedForLeftStick = new StickCalibration(
            centerX: data[2],
            centerY: data[3],
            deltaAboveX: data[0],
            deltaAboveY: data[1],
            deltaBelowX: data[4],
            deltaBelowY: data[5]);

        // Reading used by the right stick: centre, minimum, maximum.
        var asDocumentedForRightStick = new StickCalibration(
            centerX: data[0],
            centerY: data[1],
            deltaAboveX: data[4],
            deltaAboveY: data[5],
            deltaBelowX: data[2],
            deltaBelowY: data[3]);

        var leftScore = Score(asDocumentedForLeftStick);
        var rightScore = Score(asDocumentedForRightStick);

        if (leftScore <= 0 && rightScore <= 0)
        {
            return Default;
        }

        return leftScore >= rightScore ? asDocumentedForLeftStick : asDocumentedForRightStick;
    }

    /// <summary>Applies calibration and returns a normalised position where 1.0 is the stick's limit.</summary>
    public (double X, double Y) Normalise(StickRaw raw) =>
        (NormaliseAxis(raw.X, CenterX, DeltaAboveX, DeltaBelowX),
         NormaliseAxis(raw.Y, CenterY, DeltaAboveY, DeltaBelowY));

    private static double NormaliseAxis(int value, int center, int deltaAbove, int deltaBelow)
    {
        var delta = value - center;
        if (delta > 0)
        {
            return deltaAbove <= 0 ? 0 : Math.Clamp((double)delta / deltaAbove, 0, 1);
        }

        return deltaBelow <= 0 ? 0 : Math.Clamp((double)delta / deltaBelow, -1, 0);
    }

    /// <summary>
    /// Scores how plausible a reading is; higher is better and anything below zero is rejected.
    /// </summary>
    /// <remarks>
    /// The centre is the decisive signal: a stick's centre must sit near the middle of the 12 bit
    /// range, while a misread block puts a half-range value there instead. Hence plausible deltas
    /// are worth a little and distance from mid-scale costs a lot. This is what lets the two
    /// documented byte orders be told apart on real data instead of guessed.
    /// </remarks>
    private static int Score(StickCalibration candidate)
    {
        var score = 0;

        foreach (var delta in new[] { candidate.DeltaAboveX, candidate.DeltaAboveY, candidate.DeltaBelowX, candidate.DeltaBelowY })
        {
            score += IsPlausibleDelta(delta) ? 2 : -4;
        }

        var centreDistance = Math.Abs(candidate.CenterX - StickRaw.Center) + Math.Abs(candidate.CenterY - StickRaw.Center);
        score -= (int)(centreDistance / 64.0);

        return score;
    }

    private static bool IsPlausibleDelta(int delta) => delta is >= MinPlausibleDelta and <= MaxPlausibleDelta;
}

/// <summary>
/// Motion sensor calibration read from the controller's SPI flash (24 bytes).
/// </summary>
/// <remarks>
/// Four groups of three little-endian int16: accelerometer origin, accelerometer sensitivity
/// coefficient, gyroscope origin and gyroscope sensitivity coefficient.
///
/// Conversions follow the reference notes:
/// <c>acc_coeff = 4.0 / (acc_coeff_value - int16(acc_origin))</c> and
/// <c>gyro_coeff = 936.0 / (gyro_coeff_value - int16(gyro_origin))</c>, the latter being the
/// saturation-free LSM6DS3 variant (70 mdps/digit).
/// </remarks>
public sealed class ImuCalibration
{
    private const float AccelerometerCoefficientMultiplier = 4.0f;
    private const float GyroscopeCoefficientMultiplier = 936.0f;

    public ImuCalibration(
        Vector3 accelerometerOrigin,
        Vector3 accelerometerSensitivity,
        Vector3 gyroscopeOrigin,
        Vector3 gyroscopeSensitivity)
    {
        AccelerometerOrigin = accelerometerOrigin;
        AccelerometerSensitivity = accelerometerSensitivity;
        GyroscopeOrigin = gyroscopeOrigin;
        GyroscopeSensitivity = gyroscopeSensitivity;
        AccelerometerCoefficient = BuildAccelerometerCoefficient(accelerometerSensitivity, accelerometerOrigin);
        GyroscopeCoefficient = BuildGyroscopeCoefficient(gyroscopeSensitivity, gyroscopeOrigin);
    }

    public Vector3 AccelerometerOrigin { get; }

    public Vector3 AccelerometerSensitivity { get; }

    public Vector3 GyroscopeOrigin { get; }

    public Vector3 GyroscopeSensitivity { get; }

    /// <summary>Per-axis multiplier turning raw accelerometer counts into G.</summary>
    public Vector3 AccelerometerCoefficient { get; }

    /// <summary>Per-axis multiplier turning raw gyroscope counts into degrees per second.</summary>
    public Vector3 GyroscopeCoefficient { get; }

    /// <summary>
    /// Default calibration: the documented factory coefficients (16384 for +/-8G and 13371 for
    /// +/-2000 dps) with zero origins. Used when the flash cannot be read.
    /// </summary>
    public static ImuCalibration Default { get; } = new(
        accelerometerOrigin: Vector3.Zero,
        accelerometerSensitivity: new Vector3(0x4000),
        gyroscopeOrigin: Vector3.Zero,
        gyroscopeSensitivity: new Vector3(0x343B));

    public static ImuCalibration Decode(ReadOnlySpan<byte> twentyFourBytes)
    {
        if (twentyFourBytes.Length < 24)
        {
            return Default;
        }

        var accelOrigin = ReadVector(twentyFourBytes);
        var accelSensitivity = ReadVector(twentyFourBytes[6..]);
        var gyroOrigin = ReadVector(twentyFourBytes[12..]);
        var gyroSensitivity = ReadVector(twentyFourBytes[18..]);

        var calibration = new ImuCalibration(accelOrigin, accelSensitivity, gyroOrigin, gyroSensitivity);

        // A zero or absurd coefficient would produce infinities downstream, so fall back instead.
        return calibration.IsPlausible ? calibration : Default;
    }

    public bool IsPlausible =>
        IsPlausibleCoefficient(AccelerometerCoefficient) && IsPlausibleCoefficient(GyroscopeCoefficient);

    /// <summary>Converts one raw sample into G and degrees per second.</summary>
    public void Convert(ImuSampleRaw sample, out Vector3 accelerationG, out Vector3 angularVelocityDps)
    {
        accelerationG = new Vector3(
            sample.AccelX * AccelerometerCoefficient.X,
            sample.AccelY * AccelerometerCoefficient.Y,
            sample.AccelZ * AccelerometerCoefficient.Z);

        angularVelocityDps = new Vector3(
            (sample.GyroX - GyroscopeOrigin.X) * GyroscopeCoefficient.X,
            (sample.GyroY - GyroscopeOrigin.Y) * GyroscopeCoefficient.Y,
            (sample.GyroZ - GyroscopeOrigin.Z) * GyroscopeCoefficient.Z);
    }

    private static Vector3 BuildAccelerometerCoefficient(Vector3 sensitivity, Vector3 origin)
    {
        var originAsInt = origin;
        return new Vector3(
            Divisor(AccelerometerCoefficientMultiplier, sensitivity.X, originAsInt.X),
            Divisor(AccelerometerCoefficientMultiplier, sensitivity.Y, originAsInt.Y),
            Divisor(AccelerometerCoefficientMultiplier, sensitivity.Z, originAsInt.Z));
    }

    private static Vector3 BuildGyroscopeCoefficient(Vector3 sensitivity, Vector3 origin)
    {
        var originAsInt = origin;
        return new Vector3(
            Divisor(GyroscopeCoefficientMultiplier, sensitivity.X, originAsInt.X),
            Divisor(GyroscopeCoefficientMultiplier, sensitivity.Y, originAsInt.Y),
            Divisor(GyroscopeCoefficientMultiplier, sensitivity.Z, originAsInt.Z));
    }

    private static float Divisor(float multiplier, float sensitivityValue, float originValue)
    {
        var denominator = sensitivityValue - originValue;
        return Math.Abs(denominator) < 1f ? 0f : multiplier / denominator;
    }

    private static bool IsPlausibleCoefficient(Vector3 coefficient) =>
        IsPlausibleValue(coefficient.X) && IsPlausibleValue(coefficient.Y) && IsPlausibleValue(coefficient.Z);

    private static bool IsPlausibleValue(float value) =>
        !float.IsNaN(value) && !float.IsInfinity(value) && Math.Abs(value) is > 1e-6f and < 1e3f;

    private static Vector3 ReadVector(ReadOnlySpan<byte> source) => new(
        MemoryMarshal.Read<short>(source),
        MemoryMarshal.Read<short>(source[2..]),
        MemoryMarshal.Read<short>(source[4..]));
}
