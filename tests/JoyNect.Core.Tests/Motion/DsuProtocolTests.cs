using System.Buffers.Binary;
using System.Numerics;
using JoyNect.Core.Motion;
using Xunit;

namespace JoyNect.Core.Tests.Motion;

/// <summary>
/// Pins the DSU wire format to the bytes emulators actually receive.
/// </summary>
/// <remarks>
/// These expected packets were generated independently from the cemuhook-protocol reference, not
/// from the production encoder. A field moving by one byte, a CRC covering the wrong range or a
/// version constant changing all fail here before a controller is ever connected.
/// </remarks>
public sealed class DsuProtocolTests
{
    private const uint ServerId = 0x11223344;

    [Fact]
    public void Crc32MatchesTheStandardCheckVector()
    {
        Assert.Equal(0xCBF43926u, DsuProtocol.Crc32("123456789"u8));
    }

    [Fact]
    public void VersionResponseHasTheDocumentedShape()
    {
        var packet = DsuProtocol.BuildVersionResponse(ServerId);

        Assert.Equal(
            Convert.FromHexString("44535553e90306003eb566f44433221100001000e903"),
            packet);
        Assert.Equal(1001, BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(20)));
        Assert.Equal(22, packet.Length);
        Assert.Equal(6, BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(6)));
    }

    [Fact]
    public void ControllerInfoUsesTheRealMacAndDsuValues()
    {
        var slot = new DsuSlot
        {
            Slot = 0,
            Connected = true,
            HasMotion = true,
            Connection = 2,
            MacAddress = 0x98B6E9412C07,
            Battery = 3,
        };

        var packet = DsuProtocol.BuildControllerInfo(slot, ServerId);

        Assert.Equal(
            Convert.FromHexString("44535553e90310002e5a3d3b44332211010010000002020298b6e9412c070300"),
            packet);
        Assert.Equal(32, packet.Length);
        Assert.Equal(16, BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(6)));
    }

    [Fact]
    public void ControllerInfoMarksAConnectedControllerWithoutMotionAsPartialGyro()
    {
        var packet = DsuProtocol.BuildControllerInfo(
            new DsuSlot
            {
                Slot = 2,
                Connected = true,
                HasMotion = false,
                Connection = 2,
                MacAddress = 0x001122334455,
                Battery = 2,
            },
            ServerId);

        Assert.Equal(2, packet[20]);
        Assert.Equal(2, packet[21]);
        Assert.Equal(1, packet[22]);
        Assert.Equal(2, packet[23]);
    }

    [Fact]
    public void PadDataMatchesTheOneHundredByteLayout()
    {
        var slot = new DsuSlot
        {
            Slot = 1,
            Connected = true,
            HasMotion = true,
            Connection = 1,
            MacAddress = 0x001122334455,
            Battery = 0xEE,
            AccelerationG = new Vector3(1.0f, -0.5f, 0.25f),
            AngularVelocityDps = new Vector3(10.0f, -20.0f, 30.5f),
            TimestampMicroseconds = 0x0102030405060708,
        };

        var packet = DsuProtocol.BuildPadData(slot, packetNumber: 0x01020304, ServerId);

        Assert.Equal(
            Convert.FromHexString(
                "44535553e903540027f0e51d44332211" +
                "0200100001020201001122334455ee01" +
                "04030201000000008080808000000000" +
                "00000000000000000000000000000000" +
                "0000000008070605040302010000803f" +
                "000000bf0000803e000020410000a0c1" +
                "0000f441"),
            packet);
        Assert.Equal(100, packet.Length);
        Assert.Equal(84, BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(6)));
        Assert.Equal(128, packet[40]);
        Assert.Equal(128, packet[43]);
    }

    [Fact]
    public void VersionRequestIsDecoded()
    {
        var packet = Request(DsuProtocol.MessageVersion, []);

        Assert.True(DsuProtocol.TryParseRequest(packet, out var request));
        Assert.Equal(DsuRequestKind.Version, request.Kind);
        Assert.Equal(0xAABBCCDDu, request.ClientId);
        Assert.Equal(DsuProtocol.MessageVersion, request.Type);
    }

    [Fact]
    public void ControllerInfoRequestIsDecodedWithEveryRequestedSlot()
    {
        var body = new byte[8];
        BinaryPrimitives.WriteInt32LittleEndian(body, 4);
        body[4] = 0;
        body[5] = 1;
        body[6] = 2;
        body[7] = 3;

        var packet = Request(DsuProtocol.MessageControllerInfo, body);

        Assert.True(DsuProtocol.TryParseRequest(packet, out var request));
        Assert.Equal(DsuRequestKind.ControllerInfo, request.Kind);
        Assert.Equal([0, 1, 2, 3], request.Slots);
    }

    [Fact]
    public void PadDataRequestCarriesFlagsSlotAndMac()
    {
        var body = new byte[8];
        body[0] = 0x02;
        body[1] = 1;
        Convert.FromHexString("001122334455").CopyTo(body, 2);

        var packet = Request(DsuProtocol.MessagePadData, body);

        Assert.True(DsuProtocol.TryParseRequest(packet, out var request));
        Assert.Equal(DsuRequestKind.PadData, request.Kind);
        Assert.Equal(0x02, request.Flags);
        Assert.Equal(1, request.Slot);
        Assert.Equal(0x001122334455ul, request.Mac);
    }

    [Fact]
    public void AllControllerSubscriptionHasNoSlotFilter()
    {
        var body = new byte[8];
        var packet = Request(DsuProtocol.MessagePadData, body);

        Assert.True(DsuProtocol.TryParseRequest(packet, out var request));
        Assert.Equal(0, request.Flags);
    }

    [Theory]
    [InlineData("magic")]
    [InlineData("crc")]
    [InlineData("version")]
    [InlineData("length")]
    [InlineData("type")]
    public void MalformedRequestsAreRejected(string damage)
    {
        var packet = Request(DsuProtocol.MessageVersion, []);

        switch (damage)
        {
            case "magic":
                packet[0] = (byte)'X';
                break;
            case "crc":
                packet[9] ^= 0x80;
                break;
            case "version":
                packet = Request(DsuProtocol.MessageVersion, [], version: 1002);
                break;
            case "length":
                BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(6), 20);
                break;
            case "type":
                packet = Request(0xDEADBEEF, []);
                break;
        }

        Assert.False(DsuProtocol.TryParseRequest(packet, out _));
    }

    [Fact]
    public void ControllerInfoRejectsASlotOutsideTheProtocolsFour()
    {
        var body = new byte[5];
        BinaryPrimitives.WriteInt32LittleEndian(body, 1);
        body[4] = 4;

        var packet = Request(DsuProtocol.MessageControllerInfo, body);

        Assert.False(DsuProtocol.TryParseRequest(packet, out _));
    }

    [Fact]
    public void PadDataRejectsAnInvalidSlotFlag()
    {
        var body = new byte[8];
        body[0] = 0x01;
        body[1] = 4;

        var packet = Request(DsuProtocol.MessagePadData, body);

        Assert.False(DsuProtocol.TryParseRequest(packet, out _));
    }

    [Fact]
    public void PadDataRejectsUnknownFlagBits()
    {
        var body = new byte[8];
        body[0] = 0x04;

        var packet = Request(DsuProtocol.MessagePadData, body);

        Assert.False(DsuProtocol.TryParseRequest(packet, out _));
    }

    private static byte[] Request(uint type, byte[] body, ushort version = 1001, uint clientId = 0xAABBCCDD)
    {
        var packet = new byte[20 + body.Length];
        "DSUC"u8.CopyTo(packet);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(4), version);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(6), (ushort)(4 + body.Length));
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(12), clientId);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(16), type);
        body.CopyTo(packet, 20);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(8), DsuProtocol.Crc32(packet));
        return packet;
    }
}
