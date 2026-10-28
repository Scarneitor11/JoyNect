using System.Buffers.Binary;
using System.Numerics;

namespace JoyNect.Core.Motion;

/// <summary>One slot as it is published to a DSU client.</summary>
/// <remarks>
/// The slot number is the protocol's zero-based slot (0-3), not JoyNect's one-based player number.
/// Buttons and sticks are deliberately absent: the virtual Xbox pad already carries input, and this
/// server exists only to publish the motion an Xbox pad cannot represent.
/// </remarks>
public sealed record DsuSlot
{
    public required int Slot { get; init; }

    public bool Connected { get; init; }

    public bool HasMotion { get; init; }

    /// <summary>DSU connection type: 0 unknown, 1 USB, 2 Bluetooth.</summary>
    public byte Connection { get; init; }

    /// <summary>The controller's MAC address, most significant byte first.</summary>
    public ulong MacAddress { get; init; }

    /// <summary>DSU battery value (0 unknown, 1 dying, 2 low, 3 medium, 4 high, 5 full, 0xEE charging, 0xEF charged).</summary>
    public byte Battery { get; init; }

    public Vector3 AccelerationG { get; init; }

    public Vector3 AngularVelocityDps { get; init; }

    public long TimestampMicroseconds { get; init; }

    public static DsuSlot Disconnected(int slot) => new() { Slot = slot };
}

public enum DsuRequestKind
{
    Version,
    ControllerInfo,
    PadData,
}

/// <summary>A validated request from a DSU client.</summary>
public sealed record DsuRequest
{
    public required DsuRequestKind Kind { get; init; }

    public required uint ClientId { get; init; }

    public uint Type { get; init; }

    /// <summary>Subscription flags, only meaningful for <see cref="DsuRequestKind.PadData"/>.</summary>
    public byte Flags { get; init; }

    /// <summary>The requested slot, only meaningful for slot-based subscriptions.</summary>
    public int Slot { get; init; }

    /// <summary>The requested MAC, only meaningful for MAC-based subscriptions.</summary>
    public ulong Mac { get; init; }

    /// <summary>The requested slots, only populated for controller-info requests.</summary>
    public IReadOnlyList<int> Slots { get; init; } = [];
}

/// <summary>
/// The Cemuhook DSU wire format, as used by Cemu, Dolphin, Citron and other emulators.
/// </summary>
/// <remarks>
/// Everything is little endian. The first sixteen bytes are the header; <see cref="HeaderSize"/> + the
/// declared payload length is the frame the CRC covers. The CRC is the reflected CRC-32 (polynomial
/// 0xEDB88320) computed with the CRC field itself zeroed. Keeping the byte layout in one place means
/// the server cannot drift from the tests, and the tests can assert the bytes emulators actually read.
/// </remarks>
public static class DsuProtocol
{
    public const ushort Version = 1001;

    public const int HeaderSize = 16;

    public const int MaxSlots = 4;

    public const uint MessageVersion = 0x0010_0000;

    public const uint MessageControllerInfo = 0x0010_0001;

    public const uint MessagePadData = 0x0010_0002;

    public const int VersionResponseLength = 22;

    public const int ControllerInfoLength = 32;

    public const int PadDataLength = 100;

    public const int PadDataPayloadLength = PadDataLength - HeaderSize;

    private static readonly uint[] CrcTable = CreateCrcTable();

    /// <summary>Standard reflected CRC-32, used by the protocol's checksum field.</summary>
    public static uint Crc32(ReadOnlySpan<byte> data)
    {
        var crc = 0xFFFF_FFFFu;
        foreach (var value in data)
        {
            crc = (crc >> 8) ^ CrcTable[(crc ^ value) & 0xFF];
        }

        return crc ^ 0xFFFF_FFFFu;
    }

    /// <summary>Builds the answer to a version request.</summary>
    public static byte[] BuildVersionResponse(uint serverId)
    {
        // The specification puts a single uint16 after the message type. Unlike some implementations
        // there is no padding: a 22 byte packet with a six byte payload is the documented shape.
        var packet = new byte[VersionResponseLength];
        WriteHeader(packet, VersionResponseLength - HeaderSize, serverId, MessageVersion);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(20), Version);
        Finish(packet);
        return packet;
    }

    /// <summary>Builds the answer to one controller-info slot request.</summary>
    public static byte[] BuildControllerInfo(DsuSlot slot, uint serverId)
    {
        ArgumentNullException.ThrowIfNull(slot);
        ValidateSlot(slot.Slot);

        var packet = new byte[ControllerInfoLength];
        WriteHeader(packet, ControllerInfoLength - HeaderSize, serverId, MessageControllerInfo);

        var body = packet.AsSpan(20);
        body[0] = (byte)slot.Slot;
        if (slot.Connected)
        {
            body[1] = 2;
            body[2] = slot.HasMotion ? (byte)2 : (byte)1;
            body[3] = slot.Connection;
            WriteMac(body[4..10], slot.MacAddress);
            body[10] = slot.Battery;
        }

        // body[11] is the documented trailing zero. A disconnected slot keeps its number and zeroes
        // everything else, which is the permissive form the specification allows and is easier for a
        // client to match up than four indistinguishable empty responses.
        Finish(packet);
        return packet;
    }

    /// <summary>Builds one live motion packet for a subscribed client.</summary>
    public static byte[] BuildPadData(DsuSlot slot, uint packetNumber, uint serverId)
    {
        ArgumentNullException.ThrowIfNull(slot);
        ValidateSlot(slot.Slot);

        var packet = new byte[PadDataLength];
        WriteHeader(packet, PadDataPayloadLength, serverId, MessagePadData);

        var body = packet.AsSpan(20);
        body[0] = (byte)slot.Slot;
        body[1] = slot.Connected ? (byte)2 : (byte)0;
        body[2] = slot.Connected ? (slot.HasMotion ? (byte)2 : (byte)1) : (byte)0;
        body[3] = slot.Connected ? slot.Connection : (byte)0;
        WriteMac(body[4..10], slot.MacAddress);
        body[10] = slot.Battery;
        body[11] = slot.Connected ? (byte)1 : (byte)0;
        BinaryPrimitives.WriteUInt32LittleEndian(body[12..16], packetNumber);

        // Buttons, d-pad pressures and touch points stay neutral. Motion is this server's job; input
        // reaches the game through the virtual Xbox pad, so claiming a second set of buttons here
        // would only risk the two paths disagreeing.
        body[16] = 0; // digital buttons 1
        body[17] = 0; // digital buttons 2
        body[18] = 0; // HOME
        body[19] = 0; // touch click
        body[20] = 128;
        body[21] = 128;
        body[22] = 128;
        body[23] = 128;

        BinaryPrimitives.WriteInt64LittleEndian(body[48..56], slot.TimestampMicroseconds);
        BinaryPrimitives.WriteSingleLittleEndian(body[56..60], slot.AccelerationG.X);
        BinaryPrimitives.WriteSingleLittleEndian(body[60..64], slot.AccelerationG.Y);
        BinaryPrimitives.WriteSingleLittleEndian(body[64..68], slot.AccelerationG.Z);
        BinaryPrimitives.WriteSingleLittleEndian(body[68..72], slot.AngularVelocityDps.X);
        BinaryPrimitives.WriteSingleLittleEndian(body[72..76], slot.AngularVelocityDps.Y);
        BinaryPrimitives.WriteSingleLittleEndian(body[76..80], slot.AngularVelocityDps.Z);

        Finish(packet);
        return packet;
    }

    /// <summary>
    /// Validates and decodes a datagram from a client.
    /// </summary>
    /// <remarks>
    /// The declared frame, not the datagram length, is what gets validated and later dispatched: bytes
    /// past it are outside the CRC and must not reach a handler.
    /// </remarks>
    public static bool TryParseRequest(ReadOnlySpan<byte> datagram, out DsuRequest request)
    {
        request = null!;

        if (datagram.Length < HeaderSize + 4)
        {
            return false;
        }

        if (!datagram[..4].SequenceEqual("DSUC"u8))
        {
            return false;
        }

        var version = BinaryPrimitives.ReadUInt16LittleEndian(datagram[4..]);
        if (version > Version)
        {
            return false;
        }

        var payloadLength = BinaryPrimitives.ReadUInt16LittleEndian(datagram[6..]);
        if (payloadLength < 4 || HeaderSize + payloadLength > datagram.Length)
        {
            return false;
        }

        var frameLength = HeaderSize + payloadLength;
        var frame = datagram[..frameLength];
        var expectedCrc = BinaryPrimitives.ReadUInt32LittleEndian(frame[8..]);
        if (ComputeFrameCrc(frame) != expectedCrc)
        {
            return false;
        }

        var type = BinaryPrimitives.ReadUInt32LittleEndian(frame[16..]);
        var clientId = BinaryPrimitives.ReadUInt32LittleEndian(frame[12..]);

        switch (type)
        {
            case MessageVersion when frameLength == HeaderSize + 4:
                request = new DsuRequest { Kind = DsuRequestKind.Version, ClientId = clientId, Type = type };
                return true;

            case MessageControllerInfo:
            {
                if (frameLength < HeaderSize + 8)
                {
                    return false;
                }

                var count = BinaryPrimitives.ReadInt32LittleEndian(frame[20..]);
                if (count is < 0 or > MaxSlots || frameLength < HeaderSize + 8 + count)
                {
                    return false;
                }

                var slots = new int[count];
                for (var i = 0; i < count; i++)
                {
                    var slot = frame[24 + i];
                    if (slot >= MaxSlots)
                    {
                        return false;
                    }

                    slots[i] = slot;
                }

                request = new DsuRequest
                {
                    Kind = DsuRequestKind.ControllerInfo,
                    ClientId = clientId,
                    Type = type,
                    Slots = slots,
                };
                return true;
            }

            case MessagePadData:
            {
                if (frameLength < HeaderSize + 12)
                {
                    return false;
                }

                var flags = frame[20];
                if ((flags & ~0x03) != 0)
                {
                    return false;
                }

                var slot = frame[21];
                if ((flags & 0x01) != 0 && slot >= MaxSlots)
                {
                    return false;
                }

                request = new DsuRequest
                {
                    Kind = DsuRequestKind.PadData,
                    ClientId = clientId,
                    Type = type,
                    Flags = flags,
                    Slot = slot,
                    Mac = ReadMac(frame[22..28]),
                };
                return true;
            }

            default:
                return false;
        }
    }

    private static void WriteHeader(Span<byte> packet, int payloadLength, uint serverId, uint messageType)
    {
        packet[..4].Clear();
        "DSUS"u8.CopyTo(packet);
        BinaryPrimitives.WriteUInt16LittleEndian(packet[4..], Version);
        BinaryPrimitives.WriteUInt16LittleEndian(packet[6..], checked((ushort)payloadLength));
        BinaryPrimitives.WriteUInt32LittleEndian(packet[8..], 0);
        BinaryPrimitives.WriteUInt32LittleEndian(packet[12..], serverId);
        BinaryPrimitives.WriteUInt32LittleEndian(packet[16..], messageType);
    }

    private static void Finish(byte[] packet) =>
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(8), ComputeFrameCrc(packet));

    private static uint ComputeFrameCrc(ReadOnlySpan<byte> frame)
    {
        var crc = 0xFFFF_FFFFu;
        for (var i = 0; i < frame.Length; i++)
        {
            var value = i is >= 8 and < 12 ? (byte)0 : frame[i];
            crc = (crc >> 8) ^ CrcTable[(crc ^ value) & 0xFF];
        }

        return crc ^ 0xFFFF_FFFFu;
    }

    private static void WriteMac(Span<byte> destination, ulong mac)
    {
        for (var i = 0; i < 6; i++)
        {
            destination[i] = (byte)(mac >> (40 - (i * 8)));
        }
    }

    private static ulong ReadMac(ReadOnlySpan<byte> source)
    {
        ulong mac = 0;
        for (var i = 0; i < 6; i++)
        {
            mac = (mac << 8) | source[i];
        }

        return mac;
    }

    private static void ValidateSlot(int slot)
    {
        if (slot is < 0 or >= MaxSlots)
        {
            throw new ArgumentOutOfRangeException(nameof(slot), slot, "A DSU slot is zero to three.");
        }
    }

    private static uint[] CreateCrcTable()
    {
        var table = new uint[256];
        for (uint i = 0; i < table.Length; i++)
        {
            var entry = i;
            for (var bit = 0; bit < 8; bit++)
            {
                entry = (entry & 1) != 0 ? (entry >> 1) ^ 0xEDB88320u : entry >> 1;
            }

            table[i] = entry;
        }

        return table;
    }
}
