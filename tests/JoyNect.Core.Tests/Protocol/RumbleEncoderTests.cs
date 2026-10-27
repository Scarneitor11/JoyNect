using JoyNect.Core.Protocol;
using Xunit;

namespace JoyNect.Core.Tests.Protocol;

/// <summary>
/// Rumble encoding tests.
/// </summary>
/// <remarks>
/// The expected bytes are derived from the documented formulas, not from a recording, so these tests
/// are the specification: <c>frequency_code = round(log2(f/10)*32)</c>,
/// <c>amplitude_code = round(log2(amp*8.7)*32)</c> above 0.2246 and
/// <c>round(log2(amp*17)*16)</c> between 0.1175 and 0.2246, packed as
/// <c>byte1 = 2*amp_code</c>, <c>byte3 = amp_code/2 + 0x40</c>.
/// </remarks>
public sealed class RumbleEncoderTests
{
    [Fact]
    public void SilenceProducesTheDocumentedNeutralBlock()
    {
        Span<byte> block = stackalloc byte[4];
        var active = RumbleEncoder.TryEncode(RumbleEncoder.LowBandFrequencyHz, 0, block);

        Assert.False(active);
        Assert.Equal(new byte[] { 0x00, 0x01, 0x40, 0x40 }, block.ToArray());
    }

    [Fact]
    public void AmplitudeBelowTheDocumentedMinimumIsTreatedAsSilence()
    {
        Span<byte> block = stackalloc byte[4];
        var active = RumbleEncoder.TryEncode(RumbleEncoder.LowBandFrequencyHz, 0.05, block);

        Assert.False(active);
        Assert.Equal(new byte[] { 0x00, 0x01, 0x40, 0x40 }, block.ToArray());
    }

    [Fact]
    public void EncodesTheDocumentedHalfAmplitudeAtOneSixtyHertz()
    {
        // frequency 160 Hz -> code 0x80 -> hf = 0x0080, lf = 0x40
        // amplitude 0.5 -> amp code 68 -> hf_amp 136 (0x88), lf_amp 98 (0x62)
        Span<byte> block = stackalloc byte[4];
        RumbleEncoder.TryEncode(RumbleEncoder.LowBandFrequencyHz, 0.5, block);

        Assert.Equal(new byte[] { 0x80, 0x88, 0x40, 0x62 }, block.ToArray());
    }

    [Fact]
    public void FullAmplitudeStopsExactlyAtTheDocumentedSafeCeiling()
    {
        // The reference marks the row HA=0xC8 (amplitude 1.003) as the last safe one, so full
        // amplitude must land on 0xC8 and never above it.
        Assert.Equal(0xC8 / 2, RumbleEncoder.EncodeAmplitude(1.0));

        // At 160 Hz the frequency contributes no high byte, so the amplitude byte is exactly 0xC8.
        Span<byte> block = stackalloc byte[4];
        RumbleEncoder.TryEncode(RumbleEncoder.LowBandFrequencyHz, 1.0, block);
        Assert.Equal(0xC8, block[1]);
    }

    [Fact]
    public void NeverExceedsTheSafeCeilingForAnyInput()
    {
        Span<byte> block = stackalloc byte[4];
        for (var i = 0; i <= 1000; i++)
        {
            var amplitude = i / 1000.0;

            // The amplitude code is the even part of high byte 1; the odd low bit carries the
            // high bits of the frequency, which is why the mask is needed here.
            RumbleEncoder.TryEncode(RumbleEncoder.HighBandFrequencyHz, amplitude, block);
            Assert.True((block[1] & 0xFE) <= 0xC8, $"amplitude {amplitude} produced high amplitude byte 0x{block[1]:X2}");

            RumbleEncoder.TryEncode(RumbleEncoder.LowBandFrequencyHz, amplitude, block);
            Assert.True((block[1] & 0xFE) <= 0xC8, $"amplitude {amplitude} produced high amplitude byte 0x{block[1]:X2}");
        }
    }

    [Fact]
    public void AmplitudeEncodingIsMonotonic()
    {
        var previous = 0;
        for (var i = 118; i <= 1000; i++)
        {
            var code = RumbleEncoder.EncodeAmplitude(i / 1000.0);
            Assert.True(code >= previous, $"amplitude {i / 1000.0} decreased the code from {previous} to {code}");
            previous = code;
        }
    }

    [Theory]
    [InlineData(40.875885, 0x41)]
    [InlineData(160.0, 0x80)]
    [InlineData(320.0, 0xA0)]
    [InlineData(1252.572266, 0xDF)]
    public void FrequencyEncodingMatchesTheDocumentedTable(double frequency, int expectedCode)
    {
        Assert.Equal(expectedCode, RumbleEncoder.EncodeFrequency(frequency));
    }

    [Fact]
    public void FrequencyIsClampedToTheDocumentedRange()
    {
        Assert.Equal(RumbleEncoder.EncodeFrequency(40.875885), RumbleEncoder.EncodeFrequency(1));
        Assert.Equal(RumbleEncoder.EncodeFrequency(1252.572266), RumbleEncoder.EncodeFrequency(100000));
    }

    [Fact]
    public void MapsGameMotorsToAmplitudes()
    {
        Assert.Equal(0, RumbleEncoder.AmplitudeFromMotor(0));
        Assert.Equal(1.0, RumbleEncoder.AmplitudeFromMotor(255), 6);
        Assert.Equal(0.5, RumbleEncoder.AmplitudeFromMotor(255, 0.5), 6);

        // Strength scales the result and can never push past full amplitude.
        Assert.Equal(1.0, RumbleEncoder.AmplitudeFromMotor(255, 4.0), 6);
    }

    [Fact]
    public void EncodesBothActuatorsIndependently()
    {
        Span<byte> left = stackalloc byte[4];
        Span<byte> right = stackalloc byte[4];

        RumbleEncoder.EncodeBoth(largeMotor: 255, smallMotor: 0, strength: 1.0, left, right);

        Assert.False(RumbleEncoder.IsNeutral(left, right));
        Assert.Equal(new byte[] { 0x00, 0x01, 0x40, 0x40 }, right.ToArray());

        RumbleEncoder.EncodeBoth(0, 0, 1.0, left, right);
        Assert.True(RumbleEncoder.IsNeutral(left, right));
    }

    [Fact]
    public void RejectsUndersizedBuffers()
    {
        Assert.Throws<ArgumentException>(() =>
        {
            Span<byte> tooSmall = stackalloc byte[3];
            RumbleEncoder.TryEncode(160, 1.0, tooSmall);
        });
    }
}
