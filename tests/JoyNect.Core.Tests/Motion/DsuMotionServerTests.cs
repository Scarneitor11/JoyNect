using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using JoyNect.Core.Devices;
using JoyNect.Core.Hid;
using JoyNect.Core.Motion;
using JoyNect.Core.Output;
using JoyNect.Core.Protocol;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace JoyNect.Core.Tests.Motion;

/// <summary>
/// Socket and timing sensitive, so it runs outside the rest of the parallel test collections.
/// </summary>
[CollectionDefinition("DSU server", DisableParallelization = true)]
public sealed class DsuServerCollection
{
}

/// <summary>
/// Drives the real DSU server with real UDP sockets while simulated controllers stream motion.
/// </summary>
/// <remarks>
/// The only replacement in this path is the HID transport: the manager, the handshake, the calibration
/// read, MotionPipeline, slot assignment and the socket server are all production code. A client sees
/// the same packets Cemu or Dolphin would.
/// </remarks>
[Collection("DSU server")]
public sealed class DsuMotionServerTests
{
    private const ulong FirstMac = 0x001122334455;
    private const ulong SecondMac = 0x001122334466;

    [Fact]
    public void ARealUdpClientGetsVersionInfoAndLiveMotionForEveryActiveSlot()
    {
        var discovery = new ScriptedDiscovery();
        var first = AddController(discovery, 1, FirstMac);
        var second = AddController(discovery, 2, SecondMac);

        using var manager = new ControllerManager(
            new RecordingBackend(),
            new ControllerManagerOptions { CreateVirtualPads = true },
            NullLogger.Instance,
            TimeProvider.System,
            discovery);

        manager.DiscoverOnce();

        Assert.True(
            WaitFor(() => manager.Sessions.Count == 2 && manager.Sessions.All(s =>
                s.State == ControllerLinkState.Ready && s.Statistics.ReportsReceived > 2), 6000),
            "the simulated controllers never became ready");

        // Put distinct motion on the two controllers so the test cannot pass by reading a stale or
        // swapped slot. The raw values are then converted through the session's factory calibration,
        // exactly as the product does.
        first.Imu = new ImuSampleRaw(0, 4096, 0, 13371, 0, 0);
        second.Imu = new ImuSampleRaw(0, -4096, 0, 0, 6685, 0);
        var firstSession = manager.Sessions.Single(s => s.Info?.MacAddress == FirstMac);
        var secondSession = manager.Sessions.Single(s => s.Info?.MacAddress == SecondMac);

        Assert.True(
            WaitFor(() =>
                firstSession.TryGetLatest(out var firstState) && firstState.Sample2 == first.Imu
                && secondSession.TryGetLatest(out var secondState) && secondState.Sample2 == second.Imu),
            "the motion samples never reached the sessions");

        var source = new DsuSlotSource(() => manager.Sessions);
        var slots = source.Read();
        Assert.Equal([0, 1], slots.Select(s => s.Slot).ToArray());
        Assert.Equal(firstSession.Slot - 1, slots.Single(s => s.MacAddress == FirstMac).Slot);
        Assert.Equal(secondSession.Slot - 1, slots.Single(s => s.MacAddress == SecondMac).Slot);

        var slotOneSession = manager.Sessions.Single(s => s.Slot == 1);
        var slotTwoSession = manager.Sessions.Single(s => s.Slot == 2);
        var slotOneTransport = slotOneSession.Info?.MacAddress == FirstMac ? first : second;
        var expected = MotionPipeline.Convert(slotOneTransport.Imu, slotOneSession.ImuCalibration, 0);
        Assert.Equal(expected.AccelerationG, slots[0].AccelerationG);
        Assert.Equal(expected.AngularVelocityDps, slots[0].AngularVelocityDps);

        using var server = new DsuMotionServer(source.Read, pollIntervalMilliseconds: 5);
        Assert.True(server.Start(0), server.LastError);

        using var client = new UdpClient(AddressFamily.InterNetwork);
        client.Connect(IPAddress.Loopback, server.Port);
        client.Client.ReceiveTimeout = 3000;

        var version = Exchange(client, Request(DsuProtocol.MessageVersion, []));
        Assert.Equal(22, version.Length);
        Assert.Equal("DSUS"u8.ToArray(), version[..4].ToArray());
        Assert.Equal(DsuProtocol.MessageVersion, ReadUInt32(version, 16));
        Assert.Equal(1001, BinaryPrimitives.ReadUInt16LittleEndian(version.AsSpan(20)));
        AssertCrc(version);

        var infoRequest = InfoRequest(0, 1, 2, 3);
        client.Send(infoRequest, infoRequest.Length);
        var infos = new byte[DsuProtocol.MaxSlots][];
        for (var i = 0; i < infos.Length; i++)
        {
            infos[i] = Receive(client);
        }

        for (var slot = 0; slot < infos.Length; slot++)
        {
            var packet = infos[slot];
            Assert.Equal(DsuProtocol.ControllerInfoLength, packet.Length);
            Assert.Equal(DsuProtocol.MessageControllerInfo, ReadUInt32(packet, 16));
            Assert.Equal(slot, packet[20]);
            AssertCrc(packet);

            if (slot < 2)
            {
                Assert.True(packet[21] == 2, $"slot {slot} was not connected: {Convert.ToHexString(packet)}");
                Assert.Equal(2, packet[22]);
                Assert.Equal(2, packet[23]);
                Assert.Equal(
                    slot == 0 ? slotOneSession.Info!.MacAddress : slotTwoSession.Info!.MacAddress,
                    ReadMac(packet));
                Assert.Equal(5, packet[30]);
            }
            else
            {
                Assert.Equal(0, packet[21]);
                Assert.Equal(0, packet[22]);
                Assert.All(packet[24..32].ToArray(), value => Assert.Equal(0, value));
            }
        }

        // Subscribe to all slots, the way an emulator that wants every controller does.
        var allSlots = Subscription(flags: 0, slot: 0, mac: 0);
        client.Send(allSlots, allSlots.Length);

        var seenSlots = new HashSet<int>();
        var deadline = Stopwatch.StartNew();
        while (seenSlots.Count < 2 && deadline.ElapsedMilliseconds < 3000)
        {
            var packet = Receive(client);
            if (packet.Length == DsuProtocol.PadDataLength && ReadUInt32(packet, 16) == DsuProtocol.MessagePadData)
            {
                seenSlots.Add(packet[20]);
            }
        }

        Assert.Equal([0, 1], seenSlots.Order().ToArray());

        var firstData = ReceiveSlot(client, 0);
        var secondData = ReceiveSlot(client, 0);

        Assert.Equal(DsuProtocol.PadDataLength, firstData.Length);
        Assert.Equal(2, firstData[21]);
        Assert.Equal(2, firstData[22]);
        Assert.Equal(2, firstData[23]);
        Assert.Equal(slotOneSession.Info!.MacAddress, ReadMac(firstData));
        Assert.Equal(5, firstData[30]);
        Assert.Equal(1, firstData[31]);
        Assert.All(firstData[36..40].ToArray(), value => Assert.Equal(0, value));
        Assert.All(firstData[40..44].ToArray(), value => Assert.Equal(128, value));
        Assert.True(BinaryPrimitives.ReadInt64LittleEndian(firstData.AsSpan(68)) > 0);
        AssertCrc(firstData);

        var firstCounter = ReadUInt32(firstData, 32);
        Assert.Equal(firstCounter + 1, ReadUInt32(secondData, 32));

        Assert.Equal(expected.AccelerationG.X, ReadSingle(firstData, 76), 4);
        Assert.Equal(expected.AccelerationG.Y, ReadSingle(firstData, 80), 4);
        Assert.Equal(expected.AccelerationG.Z, ReadSingle(firstData, 84), 4);
        Assert.Equal(expected.AngularVelocityDps.X, ReadSingle(firstData, 88), 3);
        Assert.Equal(expected.AngularVelocityDps.Y, ReadSingle(firstData, 92), 3);
        Assert.Equal(expected.AngularVelocityDps.Z, ReadSingle(firstData, 96), 3);

        Assert.True(server.SubscriberCount >= 1);
        Assert.True(server.IsRunning);
    }

    private static SimulatedControllerTransport AddController(
        ScriptedDiscovery discovery,
        int index,
        ulong mac,
        SwitchModel model = SwitchModel.ProController)
    {
        var productId = model switch
        {
            SwitchModel.JoyConLeft => SwitchModels.ProductIdJoyConLeft,
            SwitchModel.JoyConRight => SwitchModels.ProductIdJoyConRight,
            _ => SwitchModels.ProductIdProController,
        };

        var transport = new SimulatedControllerTransport(new SimulatedControllerOptions
        {
            ProductId = (ushort)productId,
            MacAddress = mac,
            Connection = ConnectionKind.Bluetooth,
        });

        discovery.Add(
            new HidCandidate
            {
                DevicePath = $@"\\?\HID#VID_057E&PID_{productId:X4}#8&{index:x8}&0&0000#{{4d1e55b2-f16f-11cf-88cb-001111000030}}",
                VendorId = 0x057E,
                ProductId = productId,
                ProductName = SwitchModels.Describe(productId).DisplayName,
                Connection = ConnectionKind.Bluetooth,
                MaxInputReportLength = ProtocolConstants.FullStateReportLength,
                MaxOutputReportLength = OutputFrames.FrameLength,
                InputReports = [new ReportInfo(0x30, ProtocolConstants.FullStateReportLength, "input")],
                OutputReports = [new ReportInfo(0x01, OutputFrames.FrameLength, "output")],
                PhysicalDeviceKey = $"Bluetooth · {mac:X12}",
            },
            transport);

        return transport;
    }

    private static byte[] InfoRequest(params int[] slots)
    {
        var body = new byte[4 + slots.Length];
        BinaryPrimitives.WriteInt32LittleEndian(body, slots.Length);
        for (var i = 0; i < slots.Length; i++)
        {
            body[4 + i] = (byte)slots[i];
        }

        return Request(DsuProtocol.MessageControllerInfo, body);
    }

    private static byte[] Subscription(byte flags, int slot, ulong mac)
    {
        var body = new byte[8];
        body[0] = flags;
        body[1] = (byte)slot;
        for (var i = 0; i < 6; i++)
        {
            body[2 + i] = (byte)(mac >> (40 - (i * 8)));
        }

        return Request(DsuProtocol.MessagePadData, body);
    }

    private static byte[] Request(uint type, byte[] body)
    {
        var packet = new byte[DsuProtocol.HeaderSize + 4 + body.Length];
        "DSUC"u8.CopyTo(packet);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(4), DsuProtocol.Version);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(6), (ushort)(4 + body.Length));
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(12), 0x55667788);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(16), type);
        body.CopyTo(packet, 20);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(8), DsuProtocol.Crc32(packet));
        return packet;
    }

    private static byte[] Exchange(UdpClient client, byte[] request)
    {
        client.Send(request, request.Length);
        return Receive(client);
    }

    private static byte[] Receive(UdpClient client)
    {
        var remote = new IPEndPoint(IPAddress.Any, 0);
        return client.Receive(ref remote);
    }

    private static byte[] ReceiveSlot(UdpClient client, int slot)
    {
        while (true)
        {
            var packet = Receive(client);
            if (packet.Length == DsuProtocol.PadDataLength
                && ReadUInt32(packet, 16) == DsuProtocol.MessagePadData
                && packet[20] == slot)
            {
                return packet;
            }
        }
    }

    private static uint ReadUInt32(byte[] packet, int offset) =>
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(offset));

    private static ulong ReadMac(byte[] packet)
    {
        ulong mac = 0;
        for (var i = 0; i < 6; i++)
        {
            mac = (mac << 8) | packet[24 + i];
        }

        return mac;
    }

    private static float ReadSingle(byte[] packet, int offset) =>
        BinaryPrimitives.ReadSingleLittleEndian(packet.AsSpan(offset));

    private static void AssertCrc(byte[] packet)
    {
        var expected = BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(8));
        var copy = packet.ToArray();
        copy.AsSpan(8, 4).Clear();
        Assert.Equal(expected, DsuProtocol.Crc32(copy));
    }

    private static bool WaitFor(Func<bool> condition, int timeoutMilliseconds = 5000)
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.ElapsedMilliseconds < timeoutMilliseconds)
        {
            if (condition())
            {
                return true;
            }

            Thread.Sleep(5);
        }

        return condition();
    }

    private sealed class ScriptedDiscovery : IDeviceDiscovery
    {
        private readonly List<DiscoveredDevice> _devices = [];

        public void Add(HidCandidate candidate, IControllerTransport transport) =>
            _devices.Add(new DiscoveredDevice(candidate, () => transport));

        public IReadOnlyList<DiscoveredDevice> Discover() => _devices.ToArray();
    }

    /// <summary>A successful pad backend, so the manager performs its real slot assignment.</summary>
    private sealed class RecordingBackend : IVirtualPadBackend
    {
        public string Name => "test";

        public DriverStatus Status { get; } = new() { Available = true, Summary = "ready" };

        public IVirtualPad? CreatePad(int slot, string description, out string? error)
        {
            error = null;
            return new NullPad(slot, description);
        }

        public void Dispose()
        {
        }
    }
}
