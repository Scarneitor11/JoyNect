namespace JoyNect.Core.Protocol;

/// <summary>
/// Encodes rumble magnitudes into the Switch's four byte linear resonant actuator format.
/// </summary>
/// <remarks>
/// Unlike a classic rumble motor, a Switch actuator is driven by a waveform: the controller needs
/// both a frequency and an amplitude. The documented encodings are
/// <c>frequency_code = round(log2(frequency / 10) * 32)</c> and, for amplitude,
/// <c>round(log2(amplitude * 8.7) * 32)</c> above 0.2246 and <c>round(log2(amplitude * 17) * 16)</c>
/// between 0.1175 and 0.2246. Below 0.1175 the reference has no mapping, and amplitudes above 1.003
/// are documented as unsafe for the actuators, so the encoder clamps to [0, 1] and therefore can
/// never emit an out-of-range value.
///
/// Both actuators get the same physical encoding; the difference between a game's "large" and
/// "small" motor is expressed as frequency (a low frequency feels heavy, a high one feels buzzy).
/// </remarks>
public static class RumbleEncoder
{
    /// <summary>Frequency used for the game's low frequency (large) motor.</summary>
    public const double LowBandFrequencyHz = 160.0;

    /// <summary>Frequency used for the game's high frequency (small) motor.</summary>
    public const double HighBandFrequencyHz = 320.0;

    /// <summary>Amplitude below which the reference defines no mapping, so it is treated as off.</summary>
    public const double MinimumAmplitude = 0.1175;

    /// <summary>Amplitude where the reference switches between its two encoding ranges.</summary>
    public const double AmplitudeRangeBoundary = 0.2246;

    private const double AmplitudeLowRangeScale = 17.0;
    private const double AmplitudeHighRangeScale = 8.7;
    private const double MinimumFrequencyHz = 40.875885;
    private const double MaximumFrequencyHz = 1252.572266;

    /// <summary>Idle actuator state. Sending this repeatedly stops vibration.</summary>
    public static ReadOnlySpan<byte> Neutral => [0x00, 0x01, 0x40, 0x40];

    /// <summary>Encodes one actuator, returning false when the result is the neutral (off) state.</summary>
    public static bool TryEncode(double frequencyHz, double amplitude, Span<byte> destination)
    {
        if (destination.Length < 4)
        {
            throw new ArgumentException("An actuator encoding needs four bytes.", nameof(destination));
        }

        var clampedAmplitude = Math.Clamp(amplitude, 0.0, 1.0);
        if (clampedAmplitude <= 0 || clampedAmplitude < MinimumAmplitude)
        {
            Neutral.CopyTo(destination);
            return false;
        }

        var encodedFrequency = EncodeFrequency(frequencyHz);
        var encodedAmplitude = EncodeAmplitude(clampedAmplitude);

        // High frequency range: 0x0004-0x01FC in steps of four; may be negative for low frequencies,
        // in which case only the low frequency range carries the signal.
        var highFrequency = Math.Max(0, (encodedFrequency - 0x60) * 4);
        var lowFrequency = Math.Clamp(encodedFrequency - 0x40, 0x01, 0x7F);

        var highAmplitude = encodedAmplitude * 2;
        var lowAmplitude = (encodedAmplitude / 2) + 0x40;

        destination[0] = (byte)(highFrequency & 0xFF);
        destination[1] = (byte)(highAmplitude + ((highFrequency >> 8) & 0xFF));
        destination[2] = (byte)(lowFrequency + ((lowAmplitude >> 8) & 0xFF));
        destination[3] = (byte)(lowAmplitude & 0xFF);
        return true;
    }

    /// <summary>Encodes a full four byte actuator block, falling back to neutral when silent.</summary>
    public static void Encode(double frequencyHz, double amplitude, Span<byte> destination)
    {
        if (!TryEncode(frequencyHz, amplitude, destination) && destination.Length > 3)
        {
            // TryEncode already wrote the neutral block; nothing further to do.
        }
    }

    /// <summary>Maps a game's motor magnitude (0-255) onto an amplitude in [0, 1].</summary>
    public static double AmplitudeFromMotor(byte magnitude, double strength = 1.0)
    {
        if (magnitude == 0)
        {
            return 0;
        }

        var normalised = magnitude / 255.0;
        return Math.Clamp(normalised * Math.Clamp(strength, 0.0, 1.0), 0, 1);
    }

    /// <summary>Encodes the frequency part of the reference formula.</summary>
    public static int EncodeFrequency(double frequencyHz)
    {
        var frequency = Math.Clamp(frequencyHz, MinimumFrequencyHz, MaximumFrequencyHz);
        var encoded = (int)Math.Round(Math.Log2(frequency / 10.0) * 32.0);
        return Math.Clamp(encoded, 0, 0xFF);
    }

    /// <summary>
    /// Encodes the amplitude part of the reference formula.
    /// </summary>
    /// <remarks>
    /// Amplitude 1.0 lands exactly on the highest amplitude the reference marks as safe (HA byte
    /// 0xC8), which is why clamping the input to 1.0 is sufficient to protect the actuators.
    /// </remarks>
    public static int EncodeAmplitude(double amplitude)
    {
        var clamped = Math.Clamp(amplitude, 0.0, 1.0);
        if (clamped < MinimumAmplitude)
        {
            return 0;
        }

        var encoded = clamped > AmplitudeRangeBoundary
            ? Math.Log2(clamped * AmplitudeHighRangeScale) * 32.0
            : Math.Log2(clamped * AmplitudeLowRangeScale) * 16.0;

        return Math.Max(0, (int)Math.Round(encoded));
    }

    /// <summary>
    /// Encodes both actuators of a game's rumble request.
    /// </summary>
    /// <param name="largeMotor">Left actuator magnitude (0-255).</param>
    /// <param name="smallMotor">Right actuator magnitude (0-255).</param>
    /// <param name="strength">User configured strength multiplier in [0, 1].</param>
    /// <param name="left">Receives the four left actuator bytes.</param>
    /// <param name="right">Receives the four right actuator bytes.</param>
    public static void EncodeBoth(byte largeMotor, byte smallMotor, double strength, Span<byte> left, Span<byte> right)
    {
        Encode(LowBandFrequencyHz, AmplitudeFromMotor(largeMotor, strength), left);
        Encode(HighBandFrequencyHz, AmplitudeFromMotor(smallMotor, strength), right);
    }

    /// <summary>True when both actuator blocks equal the neutral state.</summary>
    public static bool IsNeutral(ReadOnlySpan<byte> left, ReadOnlySpan<byte> right) =>
        left.Length >= 4 && right.Length >= 4 && left[..4].SequenceEqual(Neutral) && right[..4].SequenceEqual(Neutral);
}
