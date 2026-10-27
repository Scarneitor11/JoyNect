using System.Diagnostics;
using JoyNect.Core.Hid;
using JoyNect.Core.Protocol;

namespace JoyNect.Core.Hid;

/// <summary>Knobs that let tests reproduce the awkward things real controllers do.</summary>
public sealed class SimulatedControllerOptions
{
    /// <summary>How often the simulated controller pushes a full state report.</summary>
    public TimeSpan ReportInterval { get; init; } = TimeSpan.FromMilliseconds(8);

    /// <summary>Link type being simulated; USB adds the 0x80 bootstrap to the handshake.</summary>
    public ConnectionKind Connection { get; init; } = ConnectionKind.Bluetooth;

    public ushort ProductId { get; init; } = SwitchModels.ProductIdProController;

    public ulong MacAddress { get; init; } = 0x001122334455;

    public byte FirmwareMajor { get; init; } = 4;

    public byte FirmwareMinor { get; init; } = 1;

    /// <summary>
    /// Device type reported over the protocol, when it should differ from the product id.
    /// A real example is a retro controller that reuses the Pro Controller product id.
    /// </summary>
    public byte? ReportedDeviceType { get; init; }

    /// <summary>When true no subcommand is ever acknowledged, exercising the retry and fallback paths.</summary>
    public bool DropSubcommandReplies { get; init; }

    /// <summary>
    /// When true only device info and report mode are acknowledged, which is what a controller with
    /// a quirky firmware looks like: the link must still come up.
    /// </summary>
    public bool DropOptionalReplies { get; init; }

    /// <summary>Delay added before each reply, exercising the reply timeout.</summary>
    public TimeSpan ReplyDelay { get; init; } = TimeSpan.Zero;

    /// <summary>When true the SPI calibration read returns a corrupt block.</summary>
    public bool CorruptCalibrationBlock { get; init; }

    /// <summary>Number of reports to deliver before the link goes silent (0 = never).</summary>
    public int ReportsBeforeSilence { get; init; }

    /// <summary>Number of writes to accept before every write throws (simulates unplugging).</summary>
    public int WritesBeforeFailure { get; init; }
}

/// <summary>
/// A scripted Switch controller.
/// </summary>
/// <remarks>
/// Answers subcommands the way a real controller does (0x21 acks, 0x31 SPI reads) and emits full
/// state reports on a fixed cadence, so the handshake, the decode path, the watchdog and the
/// rumble encoding can all be exercised end to end without hardware.
/// </remarks>
public sealed class SimulatedControllerTransport : IControllerTransport
{
    private readonly SimulatedControllerOptions _options;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly Queue<byte[]> _pendingReplies = new();
    private readonly List<byte[]> _writtenReports = [];
    private readonly byte[] _report = new byte[ProtocolConstants.FullStateReportLength];

    private TimeSpan _nextReportAt;
    private int _reportCounter;
    private int _writeCount;
    private int _deliveredReports;

    public SimulatedControllerTransport(SimulatedControllerOptions? options = null)
    {
        _options = options ?? new SimulatedControllerOptions();
        BuildCalibrationImage();
    }

    /// <summary>Buttons the simulated controller is currently reporting.</summary>
    public SwitchButtons Buttons { get; set; }

    public StickRaw LeftStick { get; set; } = new(StickRaw.Center, StickRaw.Center);

    public StickRaw RightStick { get; set; } = new(StickRaw.Center, StickRaw.Center);

    public byte BatteryAndConnection { get; set; } = 0x80;

    /// <summary>Accelerometer/gyro raw sample values emitted in every motion slot.</summary>
    public ImuSampleRaw Imu { get; set; } = new(0, 4096, 0, 0, 0, 0);

    public string Description => "simulated controller";

    public ConnectionKind Connection => _options.Connection;

    public int MaxInputReportLength => ProtocolConstants.FullStateReportLength;

    public int OutputReportLength => OutputFrames.FrameLength;

    /// <summary>Every report the session has written, in order.</summary>
    public IReadOnlyList<byte[]> WrittenReports => _writtenReports;

    /// <summary>The eight rumble bytes of the most recent write.</summary>
    public byte[] LastRumble { get; private set; } = [0x00, 0x01, 0x40, 0x40, 0x00, 0x01, 0x40, 0x40];

    /// <summary>Player light value most recently requested, or null when never asked.</summary>
    public byte? PlayerLights { get; private set; }

    /// <summary>True once the session asked for full reports.</summary>
    public bool FullReportsRequested { get; private set; }

    /// <summary>True once the session enabled vibration.</summary>
    public bool VibrationEnabled { get; private set; }

    /// <summary>True once the session enabled the IMU.</summary>
    public bool ImuEnabled { get; private set; }

    /// <summary>Subcommands received, in order.</summary>
    public IReadOnlyList<byte> ReceivedSubcommands { get; private set; } = [];

    /// <summary>USB bootstrap commands received, in order.</summary>
    public IReadOnlyList<byte> ReceivedUsbBootstrapCommands { get; private set; } = [];

    /// <summary>Synthetic SPI flash image served for calibration reads.</summary>
    public byte[] FlashImage { get; } = new byte[0x10000];

    public int Read(Span<byte> buffer, int timeoutMilliseconds)
    {
        var started = _clock.Elapsed;
        var timeout = TimeSpan.FromMilliseconds(Math.Max(0, timeoutMilliseconds));

        while (true)
        {
            if (_pendingReplies.Count > 0)
            {
                var reply = _pendingReplies.Dequeue();
                var replyCount = Math.Min(reply.Length, buffer.Length);
                reply.AsSpan(0, replyCount).CopyTo(buffer);
                return replyCount;
            }

            var silent = _options.ReportsBeforeSilence > 0 && _deliveredReports >= _options.ReportsBeforeSilence;
            var now = _clock.Elapsed;

            if (!silent && now >= _nextReportAt)
            {
                _nextReportAt = now + _options.ReportInterval;
                BuildStateReport();
                _deliveredReports++;
                var reportCount = Math.Min(_report.Length, buffer.Length);
                _report.AsSpan(0, reportCount).CopyTo(buffer);
                return reportCount;
            }

            // Non-blocking read, or the caller's time budget is spent.
            var waited = now - started;
            if (timeout <= TimeSpan.Zero || waited >= timeout)
            {
                return 0;
            }

            var untilReport = silent ? timeout : _nextReportAt - now;
            var untilDeadline = timeout - waited;
            var sleep = untilReport < untilDeadline ? untilReport : untilDeadline;
            Thread.Sleep((int)Math.Clamp(sleep.TotalMilliseconds, 1, 20));
        }
    }

    public void Write(ReadOnlySpan<byte> report)
    {
        _writeCount++;
        if (_options.WritesBeforeFailure > 0 && _writeCount >= _options.WritesBeforeFailure)
        {
            throw new ControllerLinkException("The simulated controller was unplugged.");
        }

        if (report.Length == 0)
        {
            return;
        }

        _writtenReports.Add(report.ToArray());

        if (report[0] == ProtocolConstants.OutputUsbBootstrap)
        {
            if (report.Length >= 2)
            {
                ReceivedUsbBootstrapCommands = [.. ReceivedUsbBootstrapCommands, report[1]];
            }

            return;
        }

        if (report[0] != ProtocolConstants.OutputRumbleAndSubcommand || report.Length < OutputFrames.SubcommandOffset + 1)
        {
            return;
        }

        LastRumble = report.Slice(OutputFrames.LeftRumbleOffset, 8).ToArray();

        var subcommand = report[OutputFrames.SubcommandOffset];
        if (subcommand == 0)
        {
            return;
        }

        ReceivedSubcommands = [.. ReceivedSubcommands, subcommand];
        HandleSubcommand(subcommand, report[OutputFrames.SubcommandArgumentOffset..]);
    }

    public void Dispose()
    {
    }

    private void HandleSubcommand(byte subcommand, ReadOnlySpan<byte> arguments)
    {
        switch (subcommand)
        {
            case ProtocolConstants.SubcommandRequestDeviceInfo:
                QueueReply(subcommand, SubcommandReplyReport(subcommand, BuildDeviceInfoPayload()));
                break;

            case ProtocolConstants.SubcommandSetInputReportMode:
                FullReportsRequested = arguments.Length > 0 && arguments[0] == ProtocolConstants.ReportModeFull;
                QueueReply(subcommand, SubcommandReplyReport(subcommand, []));
                break;

            case ProtocolConstants.SubcommandEnableImu:
                ImuEnabled = arguments.Length > 0 && arguments[0] != 0;
                QueueReply(subcommand, SubcommandReplyReport(subcommand, []));
                break;

            case ProtocolConstants.SubcommandSetImuSensitivity:
                QueueReply(subcommand, SubcommandReplyReport(subcommand, []));
                break;

            case ProtocolConstants.SubcommandEnableVibration:
                VibrationEnabled = arguments.Length > 0 && arguments[0] != 0;
                QueueReply(subcommand, SubcommandReplyReport(subcommand, []));
                break;

            case ProtocolConstants.SubcommandSetPlayerLights:
                PlayerLights = arguments.Length > 0 ? arguments[0] : (byte)0;
                QueueReply(subcommand, SubcommandReplyReport(subcommand, []));
                break;

            case ProtocolConstants.SubcommandSpiFlashRead:
                QueueSpiReply(subcommand, arguments);
                break;

            default:
                QueueReply(subcommand, SubcommandReplyReport(subcommand, []));
                break;
        }
    }

    private void QueueReply(byte subcommand, byte[] reply)
    {
        if (_options.DropSubcommandReplies)
        {
            return;
        }

        // Device info and report mode are the two steps the link cannot work without.
        var essential = subcommand is ProtocolConstants.SubcommandRequestDeviceInfo
            or ProtocolConstants.SubcommandSetInputReportMode;
        if (_options.DropOptionalReplies && !essential)
        {
            return;
        }

        if (_options.ReplyDelay > TimeSpan.Zero)
        {
            Thread.Sleep(_options.ReplyDelay);
        }

        _pendingReplies.Enqueue(reply);
    }

    private byte[] SubcommandReplyReport(byte subcommand, byte[] payload)
    {
        var report = new byte[ProtocolConstants.FullStateReportLength];
        report[0] = ProtocolConstants.InputSubcommandReply;
        report[1] = (byte)(_reportCounter & 0xFF);
        report[2] = BatteryAndConnection;
        report[13] = payload.Length > 0 ? (byte)0x90 : (byte)0x80;
        report[14] = subcommand;
        payload.AsSpan(0, Math.Min(payload.Length, report.Length - 15)).CopyTo(report.AsSpan(15));
        return report;
    }

    private byte[] BuildDeviceInfoPayload() =>
    [
        _options.FirmwareMajor,
        _options.FirmwareMinor,
        _options.ReportedDeviceType ?? SwitchModels.FromProductIdToDeviceType(_options.ProductId),
        0x02,
        (byte)(_options.MacAddress & 0xFF),
        (byte)((_options.MacAddress >> 8) & 0xFF),
        (byte)((_options.MacAddress >> 16) & 0xFF),
        (byte)((_options.MacAddress >> 24) & 0xFF),
        (byte)((_options.MacAddress >> 32) & 0xFF),
        (byte)((_options.MacAddress >> 40) & 0xFF),
        0x01,
        0x03,
    ];

    private void QueueSpiReply(byte subcommand, ReadOnlySpan<byte> arguments)
    {
        if (arguments.Length < 9)
        {
            QueueReply(subcommand, SubcommandReplyReport(subcommand, []));
            return;
        }

        var address = arguments[4] | (arguments[5] << 8) | (arguments[6] << 16) | (arguments[7] << 24);
        var length = arguments[8];

        var report = new byte[21 + length];
        report[0] = SpiReadReply.SpiReadReplyReportId;
        report[14] = 0x90;
        report[15] = subcommand;
        report[16] = (byte)(address & 0xFF);
        report[17] = (byte)((address >> 8) & 0xFF);
        report[18] = (byte)((address >> 16) & 0xFF);
        report[19] = (byte)((address >> 24) & 0xFF);
        report[20] = length;

        if (address >= 0 && address + length <= FlashImage.Length)
        {
            FlashImage.AsSpan(address, length).CopyTo(report.AsSpan(21));
        }

        QueueReply(subcommand, report);
    }

    private void BuildCalibrationImage()
    {
        // Stick calibration: documented Pro Controller values, packed into the reference layout.
        WriteStick(0x603D, new StickRaw(0x4F7, 0x424), new StickRaw(0x79F, 0x8A0), new StickRaw(0x510, 0x479));
        WriteStick(0x6046, new StickRaw(0x79F, 0x8A0), new StickRaw(0x510, 0x479), new StickRaw(0x4F7, 0x424));

        if (_options.CorruptCalibrationBlock)
        {
            for (var i = 0; i < 18; i++)
            {
                FlashImage[0x603D + i] = 0xFF;
            }
        }

        // Motion calibration: zero origins, documented sensitivity coefficients.
        WriteInt16(0x6020, 0);
        WriteInt16(0x6022, 0);
        WriteInt16(0x6024, 0);
        WriteInt16(0x6026, 0x4000);
        WriteInt16(0x6028, 0x4000);
        WriteInt16(0x602A, 0x4000);
        WriteInt16(0x602C, 0x000E);
        WriteInt16(0x602E, unchecked((short)0xFFDF));
        WriteInt16(0x6030, unchecked((short)0xFFD0));
        WriteInt16(0x6032, 0x343B);
        WriteInt16(0x6034, 0x343B);
        WriteInt16(0x6036, 0x343B);
    }

    private void WriteStick(int address, StickRaw first, StickRaw second, StickRaw third)
    {
        first.Pack(FlashImage.AsSpan(address, 3));
        second.Pack(FlashImage.AsSpan(address + 3, 3));
        third.Pack(FlashImage.AsSpan(address + 6, 3));
    }

    private void WriteInt16(int address, short value)
    {
        FlashImage[address] = (byte)(value & 0xFF);
        FlashImage[address + 1] = (byte)((value >> 8) & 0xFF);
    }

    private void BuildStateReport()
    {
        _report.AsSpan().Clear();
        _report[0] = ProtocolConstants.InputFullState;
        _report[1] = (byte)(_reportCounter++ & 0xFF);
        _report[2] = BatteryAndConnection;

        var buttons = (uint)Buttons;
        _report[3] = (byte)(buttons & 0xFF);
        _report[4] = (byte)((buttons >> 8) & 0xFF);
        _report[5] = (byte)((buttons >> 16) & 0xFF);

        LeftStick.Pack(_report.AsSpan(6, 3));
        RightStick.Pack(_report.AsSpan(9, 3));
        // Byte 12 mirrors the vibrator state; a real controller reports its own view here.
        _report[12] = 0x00;

        for (var i = 0; i < ProtocolConstants.ImuSamplesPerReport; i++)
        {
            var offset = 13 + (i * 12);
            WriteInt16(_report, offset, Imu.AccelX);
            WriteInt16(_report, offset + 2, Imu.AccelY);
            WriteInt16(_report, offset + 4, Imu.AccelZ);
            WriteInt16(_report, offset + 6, Imu.GyroX);
            WriteInt16(_report, offset + 8, Imu.GyroY);
            WriteInt16(_report, offset + 10, Imu.GyroZ);
        }
    }

    private static void WriteInt16(byte[] target, int offset, short value)
    {
        target[offset] = (byte)(value & 0xFF);
        target[offset + 1] = (byte)((value >> 8) & 0xFF);
    }
}
