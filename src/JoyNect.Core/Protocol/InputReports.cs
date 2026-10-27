using System.Globalization;
using System.Runtime.InteropServices;

namespace JoyNect.Core.Protocol;

/// <summary>Battery charge level as reported by the controller.</summary>
/// <remarks>
/// The controller only reports even levels 0, 2, 4, 6, 8 (empty, critical, low, medium, full).
/// </remarks>
public enum BatteryLevel
{
    Unknown = -1,
    Empty = 0,
    Critical = 2,
    Low = 4,
    Medium = 6,
    Full = 8,
}

/// <summary>Raw 12 bit stick position as delivered by the controller (0..4095).</summary>
[StructLayout(LayoutKind.Auto)]
public readonly record struct StickRaw(int X, int Y)
{
    public const int MaxValue = 4095;
    public const int Center = 2048;

    /// <summary>Decodes the packed three byte form used by both sticks and by calibration data.</summary>
    /// <remarks>
    /// Layout: <c>x = b0 | ((b1 &amp; 0x0F) &lt;&lt; 8)</c>, <c>y = (b1 &gt;&gt; 4) | (b2 &lt;&lt; 4)</c>.
    /// </remarks>
    public static StickRaw Unpack(byte b0, byte b1, byte b2) => new(
        b0 | ((b1 & 0x0F) << 8),
        (b1 >> 4) | (b2 << 4));

    /// <summary>Inverse of <see cref="Unpack"/>; used when generating reports (tests, simulator).</summary>
    public void Pack(Span<byte> destination)
    {
        if (destination.Length < 3)
        {
            throw new ArgumentException("A packed stick needs three bytes.", nameof(destination));
        }

        var x = Math.Clamp(X, 0, MaxValue);
        var y = Math.Clamp(Y, 0, MaxValue);
        destination[0] = (byte)(x & 0xFF);
        destination[1] = (byte)(((x >> 8) & 0x0F) | ((y & 0x0F) << 4));
        destination[2] = (byte)(y >> 4);
    }

    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"({X}, {Y})");
}

/// <summary>One raw motion sample: accelerometer then gyroscope, three axes each.</summary>
[StructLayout(LayoutKind.Auto)]
public readonly record struct ImuSampleRaw(short AccelX, short AccelY, short AccelZ, short GyroX, short GyroY, short GyroZ)
{
    public static ImuSampleRaw Unpack(ReadOnlySpan<byte> source)
    {
        if (source.Length < 12)
        {
            throw new ArgumentException("An IMU sample needs 12 bytes.", nameof(source));
        }

        return new ImuSampleRaw(
            MemoryMarshal.Read<short>(source),
            MemoryMarshal.Read<short>(source[2..]),
            MemoryMarshal.Read<short>(source[4..]),
            MemoryMarshal.Read<short>(source[6..]),
            MemoryMarshal.Read<short>(source[8..]),
            MemoryMarshal.Read<short>(source[10..]));
    }
}

/// <summary>Decoded full state report (input report 0x30).</summary>
public readonly struct FullStateReport
{
    public const int Length = ProtocolConstants.FullStateReportLength;

    public FullStateReport(
        byte timer,
        SwitchButtons buttons,
        StickRaw leftStick,
        StickRaw rightStick,
        byte batteryAndConnection,
        byte vibratorState,
        ImuSampleRaw sample0,
        ImuSampleRaw sample1,
        ImuSampleRaw sample2)
    {
        Timer = timer;
        Buttons = buttons;
        LeftStick = leftStick;
        RightStick = rightStick;
        BatteryAndConnection = batteryAndConnection;
        VibratorState = vibratorState;
        Sample0 = sample0;
        Sample1 = sample1;
        Sample2 = sample2;
    }

    public byte Timer { get; }

    public SwitchButtons Buttons { get; }

    public StickRaw LeftStick { get; }

    public StickRaw RightStick { get; }

    public byte BatteryAndConnection { get; }

    public byte VibratorState { get; }

    public ImuSampleRaw Sample0 { get; }

    public ImuSampleRaw Sample1 { get; }

    public ImuSampleRaw Sample2 { get; }

    public BatteryLevel Battery => (BatteryLevel)(BatteryAndConnection >> 4);

    /// <summary>Low nibble bit 0: the controller is on external power (USB or the console).</summary>
    public bool Charging => (BatteryAndConnection & 0x01) != 0;

    /// <summary>Low nibble bits 1-2: how the controller is connected.</summary>
    public int ConnectionInfo => (BatteryAndConnection >> 1) & 0x03;

    /// <summary>
    /// Parses a full state report. Returns false for any other report id, or a short buffer.
    /// </summary>
    public static bool TryParse(ReadOnlySpan<byte> report, out FullStateReport state)
    {
        state = default;

        if (report.Length < Length || report[0] != ProtocolConstants.InputFullState)
        {
            return false;
        }

        var buttons = (SwitchButtons)(report[3] | (report[4] << 8) | (report[5] << 16));

        state = new FullStateReport(
            timer: report[1],
            buttons: buttons,
            leftStick: StickRaw.Unpack(report[6], report[7], report[8]),
            rightStick: StickRaw.Unpack(report[9], report[10], report[11]),
            batteryAndConnection: report[2],
            vibratorState: report[12],
            sample0: ImuSampleRaw.Unpack(report[13..]),
            sample1: ImuSampleRaw.Unpack(report[25..]),
            sample2: ImuSampleRaw.Unpack(report[37..]));

        return true;
    }
}

/// <summary>
/// Reply to an output subcommand (input report 0x21).
/// </summary>
/// <remarks>
/// Offsets are absolute, counting the report id as byte 0:
/// byte 13 = ACK, byte 14 = echoed subcommand id, bytes 15+ = reply data.
/// <see cref="SubcommandReply.Ack"/> has its high bit set on success (0x80, or 0x90 when the
/// reply carries data).
/// </remarks>
public readonly struct SubcommandReply
{
    private readonly byte[] _data;

    private SubcommandReply(byte ack, byte subcommand, byte[] data, byte rawLength)
    {
        Ack = ack;
        Subcommand = subcommand;
        _data = data;
        RawLength = rawLength;
    }

    public byte Ack { get; }

    public byte Subcommand { get; }

    /// <summary>Raw report length, kept for diagnostics.</summary>
    public byte RawLength { get; }

    /// <summary>The documented success marker.</summary>
    public bool IsSuccess => (Ack & 0x80) != 0;

    /// <summary>True when the reply carries a payload (ACK 0x90 rather than 0x80).</summary>
    public bool HasData => (Ack & 0x10) != 0;

    public ReadOnlySpan<byte> Data => _data;

    public static bool TryParse(ReadOnlySpan<byte> report, out SubcommandReply reply)
    {
        reply = default;

        if (report.Length < 16 || report[0] != ProtocolConstants.InputSubcommandReply)
        {
            return false;
        }

        var data = report[15..].ToArray();
        reply = new SubcommandReply(report[13], report[14], data, (byte)report.Length);
        return true;
    }
}

/// <summary>Reply to an SPI flash read (input report 0x31).</summary>
public readonly struct SpiReadReply
{
    private readonly byte[] _data;

    private SpiReadReply(byte ack, byte subcommand, int address, byte requestedLength, byte[] data)
    {
        Ack = ack;
        Subcommand = subcommand;
        Address = address;
        RequestedLength = requestedLength;
        _data = data;
    }

    public byte Ack { get; }

    public byte Subcommand { get; }

    public int Address { get; }

    public byte RequestedLength { get; }

    public bool IsSuccess => (Ack & 0x80) != 0;

    public ReadOnlySpan<byte> Data => _data;

    public static bool TryParse(ReadOnlySpan<byte> report, out SpiReadReply reply)
    {
        reply = default;

        if (report.Length < 21 || report[0] != SpiReadReplyReportId)
        {
            return false;
        }

        var address = report[16] | (report[17] << 8) | (report[18] << 16) | (report[19] << 24);
        var length = report[20];
        var available = Math.Min(length, Math.Max(0, report.Length - 21));
        var data = report.Slice(21, available).ToArray();

        reply = new SpiReadReply(report[14], report[15], address, length, data);
        return true;
    }

    /// <summary>Input report id used for SPI flash read replies.</summary>
    public const byte SpiReadReplyReportId = 0x31;
}

/// <summary>
/// Device information as answered by subcommand 0x02.
/// </summary>
public sealed record DeviceInfo(
    byte FirmwareMajor,
    byte FirmwareMinor,
    byte ControllerType,
    ulong MacAddress)
{
    /// <summary>
    /// Parses the reply payload of subcommand 0x02.
    /// </summary>
    /// <remarks>
    /// The MAC bytes are stored in reverse order by the controller, so they are reassembled least
    /// significant byte first to match the address Windows shows in Bluetooth settings.
    /// </remarks>
    public static bool TryParse(ReadOnlySpan<byte> data, out DeviceInfo info)
    {
        info = default!;

        if (data.Length < 10)
        {
            return false;
        }

        ulong mac = 0;
        for (var i = 9; i >= 4; i--)
        {
            mac = (mac << 8) | data[i];
        }

        info = new DeviceInfo(data[0], data[1], data[2], mac);
        return true;
    }

    public string MacAddressText => string.Create(
        CultureInfo.InvariantCulture,
        $"{(MacAddress >> 40) & 0xFF:X2}:{(MacAddress >> 32) & 0xFF:X2}:{(MacAddress >> 24) & 0xFF:X2}:" +
        $"{(MacAddress >> 16) & 0xFF:X2}:{(MacAddress >> 8) & 0xFF:X2}:{MacAddress & 0xFF:X2}");

    public string FirmwareText => string.Create(CultureInfo.InvariantCulture, $"{FirmwareMajor}.{FirmwareMinor}");
}
