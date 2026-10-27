using JoyNect.Core.Hid;
using JoyNect.Core.Protocol;
using Xunit;

namespace JoyNect.Core.Tests.Protocol;

/// <summary>Report layout tests, built byte by byte so every offset is pinned down.</summary>
public sealed class InputReportTests
{
    private static byte[] BuildFullStateReport(
        SwitchButtons buttons = SwitchButtons.None,
        StickRaw? left = null,
        StickRaw? right = null,
        byte batteryAndConnection = 0x8A,
        byte timer = 0x11)
    {
        var report = new byte[FullStateReport.Length];
        report[0] = ProtocolConstants.InputFullState;
        report[1] = timer;
        report[2] = batteryAndConnection;

        var mask = (uint)buttons;
        report[3] = (byte)(mask & 0xFF);
        report[4] = (byte)((mask >> 8) & 0xFF);
        report[5] = (byte)((mask >> 16) & 0xFF);

        (left ?? new StickRaw(StickRaw.Center, StickRaw.Center)).Pack(report.AsSpan(6, 3));
        (right ?? new StickRaw(StickRaw.Center, StickRaw.Center)).Pack(report.AsSpan(9, 3));

        // Three motion samples with different values, so an off-by-one in the stride is caught.
        for (var sample = 0; sample < ProtocolConstants.ImuSamplesPerReport; sample++)
        {
            var offset = 13 + (sample * 12);
            Write(report, offset, (short)(100 + sample));
            Write(report, offset + 2, (short)(200 + sample));
            Write(report, offset + 4, (short)(300 + sample));
            Write(report, offset + 6, (short)(400 + sample));
            Write(report, offset + 8, (short)(500 + sample));
            Write(report, offset + 10, (short)(600 + sample));
        }

        return report;
    }

    private static void Write(byte[] target, int offset, short value)
    {
        target[offset] = (byte)(value & 0xFF);
        target[offset + 1] = (byte)((value >> 8) & 0xFF);
    }

    [Fact]
    public void ParsesButtonsFromAllThreeButtonBytes()
    {
        var buttons = SwitchButtons.A | SwitchButtons.Plus | SwitchButtons.Down | SwitchButtons.Zr | SwitchButtons.Home;

        Assert.True(FullStateReport.TryParse(BuildFullStateReport(buttons), out var state));
        Assert.Equal(buttons, state.Buttons);
    }

    [Theory]
    [InlineData(0u, 0u, 0u, 0u)]
    // byte 3 holds the right hand side, least significant bit first: Y, X, B, A, SR, SL, R, ZR.
    [InlineData(0x01u, 0u, 0u, (uint)SwitchButtons.Y)]
    [InlineData(0x80u, 0u, 0u, (uint)SwitchButtons.Zr)]
    // byte 4 holds the shared buttons: Minus, Plus, stick clicks, Home, Capture, charging grip.
    [InlineData(0u, 0x80u, 0u, (uint)SwitchButtons.ChargingGrip)]
    // byte 5 holds the left hand side: Down, Up, Right, Left, SR, SL, L, ZL.
    [InlineData(0u, 0u, 0x80u, (uint)SwitchButtons.Zl)]
    [InlineData(0x01u, 0x80u, 0u, (uint)(SwitchButtons.Y | SwitchButtons.ChargingGrip))]
    public void MapsButtonBytesToTheDocumentedBitLayout(uint byte3, uint byte4, uint byte5, uint expectedMask)
    {
        var report = BuildFullStateReport();
        report[3] = (byte)byte3;
        report[4] = (byte)byte4;
        report[5] = (byte)byte5;

        Assert.True(FullStateReport.TryParse(report, out var state));
        Assert.Equal((SwitchButtons)expectedMask, state.Buttons);
    }

    [Fact]
    public void ParsesSticksBatteryAndMotionSamples()
    {
        var left = new StickRaw(0x4F7, 0x424);
        var right = new StickRaw(1234, 3000);

        Assert.True(FullStateReport.TryParse(
            BuildFullStateReport(left: left, right: right, batteryAndConnection: 0x8A),
            out var state));

        Assert.Equal(left, state.LeftStick);
        Assert.Equal(right, state.RightStick);
        Assert.Equal(BatteryLevel.Full, state.Battery);
        Assert.False(state.Charging);
        Assert.Equal(1, state.ConnectionInfo);

        Assert.Equal(new ImuSampleRaw(100, 200, 300, 400, 500, 600), state.Sample0);
        Assert.Equal(new ImuSampleRaw(101, 201, 301, 401, 501, 601), state.Sample1);
        Assert.Equal(new ImuSampleRaw(102, 202, 302, 402, 502, 602), state.Sample2);
    }

    [Theory]
    [InlineData(0x80, BatteryLevel.Full, false)]
    [InlineData(0x81, BatteryLevel.Full, true)]
    [InlineData(0x60, BatteryLevel.Medium, false)]
    [InlineData(0x40, BatteryLevel.Low, false)]
    [InlineData(0x20, BatteryLevel.Critical, false)]
    [InlineData(0x00, BatteryLevel.Empty, false)]
    public void ReadsBatteryFromTheHighNibbleAndChargingFromTheLowBit(byte raw, BatteryLevel expected, bool charging)
    {
        Assert.True(FullStateReport.TryParse(BuildFullStateReport(batteryAndConnection: raw), out var state));
        Assert.Equal(expected, state.Battery);
        Assert.Equal(charging, state.Charging);
    }

    [Fact]
    public void RejectsOtherReportIdsAndShortBuffers()
    {
        var subcommandReply = BuildFullStateReport();
        subcommandReply[0] = ProtocolConstants.InputSubcommandReply;
        Assert.False(FullStateReport.TryParse(subcommandReply, out _));

        Assert.False(FullStateReport.TryParse(BuildFullStateReport().AsSpan(0, 48), out _));
        Assert.False(FullStateReport.TryParse([], out _));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(4095, 4095)]
    [InlineData(2048, 2048)]
    [InlineData(1, 4094)]
    [InlineData(4095, 0)]
    public void PacksAndUnpacksSticksLosslessly(int x, int y)
    {
        Span<byte> packed = stackalloc byte[3];
        new StickRaw(x, y).Pack(packed);

        Assert.Equal(new StickRaw(x, y), StickRaw.Unpack(packed[0], packed[1], packed[2]));
    }

    [Fact]
    public void ParsesSubcommandReplies()
    {
        var report = new byte[FullStateReport.Length];
        report[0] = ProtocolConstants.InputSubcommandReply;
        report[13] = 0x90;
        report[14] = ProtocolConstants.SubcommandRequestDeviceInfo;
        report[15] = 0xAB;
        report[16] = 0xCD;

        Assert.True(SubcommandReply.TryParse(report, out var reply));
        Assert.True(reply.IsSuccess);
        Assert.True(reply.HasData);
        Assert.Equal(ProtocolConstants.SubcommandRequestDeviceInfo, reply.Subcommand);
        Assert.Equal((byte)FullStateReport.Length, reply.RawLength);
        Assert.Equal(new byte[] { 0xAB, 0xCD }, reply.Data[..2].ToArray());
    }

    [Fact]
    public void MarksRefusedSubcommandsAsFailed()
    {
        var report = new byte[FullStateReport.Length];
        report[0] = ProtocolConstants.InputSubcommandReply;
        report[13] = 0x03;
        report[14] = ProtocolConstants.SubcommandSetInputReportMode;

        Assert.True(SubcommandReply.TryParse(report, out var reply));
        Assert.False(reply.IsSuccess);
        Assert.False(reply.HasData);
    }

    [Fact]
    public void ParsesSpiReadReplies()
    {
        var report = new byte[26];
        report[0] = SpiReadReply.SpiReadReplyReportId;
        report[14] = 0x90;
        report[15] = ProtocolConstants.SubcommandSpiFlashRead;
        report[16] = 0x3D;
        report[17] = 0x60;
        report[20] = 5;
        report[21] = 1;
        report[22] = 2;
        report[23] = 3;
        report[24] = 4;
        report[25] = 5;

        Assert.True(SpiReadReply.TryParse(report, out var reply));
        Assert.True(reply.IsSuccess);
        Assert.Equal(ProtocolConstants.SpiAddressStickCalibration, reply.Address);
        Assert.Equal(5, reply.RequestedLength);
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5 }, reply.Data.ToArray());
    }

    [Fact]
    public void ReassemblesTheReversedMacAddressFromDeviceInfo()
    {
        // The controller stores the MAC least significant byte first.
        byte[] payload =
        [
            0x04, 0x01, 0x03, 0x02,
            0x55, 0x44, 0x33, 0x22, 0x11, 0x00,
            0x01, 0x03,
        ];

        Assert.True(DeviceInfo.TryParse(payload, out var info));
        Assert.Equal("00:11:22:33:44:55", info.MacAddressText);
        Assert.Equal("4.1", info.FirmwareText);
        Assert.Equal(SwitchModel.ProController, SwitchModels.FromDeviceInfoType(info.ControllerType));
    }
}
