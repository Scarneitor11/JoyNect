using System.Diagnostics;
using JoyNect.Core.Devices;
using JoyNect.Core.Hid;
using JoyNect.Core.Mapping;
using JoyNect.Core.Output;
using JoyNect.Core.Protocol;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace JoyNect.Core.Tests.Devices;

/// <summary>
/// End to end tests for the piece that ties everything together: discovery, link, mapping and the
/// virtual pad.
/// </summary>
/// <remarks>
/// Real controllers cannot be scripted, so discovery is replaced with a source of simulated
/// controllers and the driver is replaced with a backend that records what it was told. Everything
/// in between — handshake, decoding, calibration, mapping, composition, slot assignment and release
/// on link loss — is the production code path.
/// </remarks>
public sealed class ControllerManagerTests
{
    [Fact]
    public void PresentsAControllerAsAVirtualPadAndForwardsInput()
    {
        using var harness = new ManagerHarness();
        var transport = harness.AddController();
        using var manager = harness.Start();

        Assert.True(WaitFor(() => harness.Backend.Pads.Count == 1), "no virtual pad was created");

        var pad = harness.Backend.Pads[0];
        Assert.Equal(1, pad.Slot);
        Assert.Equal(1, manager.Sessions[0].Slot);

        transport.Buttons = SwitchButtons.A;
        transport.LeftStick = PushRight;

        Assert.True(
            WaitFor(() => pad.LastState.Buttons.HasFlag(Xbox360Buttons.B) && pad.LastState.LeftThumbX > 30000),
            $"pad state was {pad.LastState.Describe()}");

        // The trigger must stay released: A is a button, not a trigger.
        Assert.Equal(0, pad.LastState.LeftTrigger);
    }

    [Fact]
    public void GivesEachControllerItsOwnPlayerSlot()
    {
        using var harness = new ManagerHarness();
        harness.AddController(mac: 0x001122334455);
        harness.AddController(mac: 0x009988776655);
        using var manager = harness.Start();

        Assert.True(WaitFor(() => harness.Backend.Pads.Count == 2), "expected two virtual pads");

        Assert.Equal([1, 2], harness.Backend.Pads.Select(p => p.Slot).Order().ToArray());
        Assert.Equal(2, manager.Pads.Count);
    }

    [Fact]
    public void CombinesAJoyConPairIntoOnePadWithBothSticks()
    {
        using var harness = new ManagerHarness(JoyConMode.Combined);
        var left = harness.AddController(SwitchModel.JoyConLeft, mac: 0x001122334455);
        var right = harness.AddController(SwitchModel.JoyConRight, mac: 0x001122334466);
        using var manager = harness.Start();

        Assert.True(WaitFor(() => harness.Backend.Pads.Count == 1), "a Joy-Con pair should become one pad");

        var binding = manager.Pads[0];
        Assert.True(WaitFor(() => binding.Sources.Count == 2), "both halves should feed the same pad");

        // Each half keeps its own stick, and the halves' buttons merge into one set.
        left.LeftStick = PushRight;
        right.RightStick = PushLeft;
        left.Buttons = SwitchButtons.Up;
        right.Buttons = SwitchButtons.A;

        var pad = harness.Backend.Pads[0];
        Assert.True(
            WaitFor(() => pad.LastState.LeftThumbX > 30000 && pad.LastState.RightThumbX < -30000),
            $"sticks were L{pad.LastState.LeftThumbX} R{pad.LastState.RightThumbX}");

        Assert.True(pad.LastState.Buttons.HasFlag(Xbox360Buttons.DpadUp));
        Assert.True(pad.LastState.Buttons.HasFlag(Xbox360Buttons.B));
    }

    [Fact]
    public void InSidewaysModeEachJoyConBecomesItsOwnPadAndUsesItsOwnStick()
    {
        using var harness = new ManagerHarness(JoyConMode.Sideways);
        harness.AddController(SwitchModel.JoyConLeft, mac: 0x001122334455);
        var right = harness.AddController(SwitchModel.JoyConRight, mac: 0x001122334466);
        using var manager = harness.Start();

        Assert.True(WaitFor(() => harness.Backend.Pads.Count == 2), "each Joy-Con should be its own pad");
        Assert.True(WaitFor(() => manager.Sessions.Count == 2), "both Joy-Con should be connected");

        // Either Joy-Con may claim the first free slot, so the pad is found through the controller
        // feeding it rather than by its player number.
        var session = manager.Sessions.Single(s => s.Model.Model == SwitchModel.JoyConRight);
        var pad = PadFor(manager, harness, session);

        // The right Joy-Con reports its stick in the right hand field, but used alone it must drive
        // the pad's left stick or the pad would appear to have no stick at all.
        right.RightStick = PushRight;
        right.Buttons = SwitchButtons.Plus;

        Assert.True(
            WaitFor(() => pad.LastState.LeftThumbX > 30000),
            $"left stick was {pad.LastState.LeftThumbX}");

        Assert.Equal(0, pad.LastState.RightThumbX);
        Assert.True(pad.LastState.Buttons.HasFlag(Xbox360Buttons.Start));
    }

    [Fact]
    public void ALostLinkReleasesHeldInputsAndThenRetiresThePad()
    {
        using var harness = new ManagerHarness(
            sessionOptions: new SwitchControllerSessionOptions
            {
                StaleAfter = TimeSpan.FromMilliseconds(150),
                AbandonAfter = TimeSpan.FromMilliseconds(600),
            });

        // Roughly a second of reports, which is long enough for the test to hold an input down
        // before the controller goes quiet.
        var transport = harness.AddController(options: new SimulatedControllerOptions
        {
            ReportInterval = TimeSpan.FromMilliseconds(10),
            ReportsBeforeSilence = 120,
        });

        using var manager = harness.Start();
        Assert.True(WaitFor(() => harness.Backend.Pads.Count == 1), "no virtual pad was created");

        var pad = harness.Backend.Pads[0];
        transport.Buttons = SwitchButtons.A | SwitchButtons.Zr;
        transport.LeftStick = PushRight;

        Assert.True(
            WaitFor(() => !pad.LastState.IsNeutral),
            "the pad never received any input to latch");

        // The controller goes quiet: every input it was holding must be released, but the pad stays
        // while a reconnect is still possible.
        Assert.True(
            WaitFor(() => pad.LastState.IsNeutral && manager.Pads.Count == 1, 3000),
            $"inputs stayed latched: {pad.LastState.Describe()}");

        // ...and once the link is given up, the pad goes away entirely rather than staying as a
        // controller that looks connected but does nothing.
        Assert.True(WaitFor(() => manager.Pads.Count == 0, 3000), "the pad outlived its controller");

        manager.ReapDeadLinks();
        Assert.Empty(manager.Sessions);
    }

    [Fact]
    public void WithoutADriverControllersAreStillReadableAndTheFailureIsReported()
    {
        // Monitor only mode: this is what a machine without ViGEmBus looks like, and the app must
        // still be able to show what the controllers are doing.
        using var harness = new ManagerHarness(backend: new NullPadBackend());
        harness.AddController();
        using var manager = harness.Start();

        var failures = new List<string>();
        manager.PadCreationFailed += (_, reason) => failures.Add(reason);

        Assert.True(
            WaitFor(() => manager.Sessions.Count == 1 && manager.Sessions[0].State == ControllerLinkState.Ready),
            "the controller should still come up");

        Assert.True(WaitFor(() => failures.Count == 1), "the failure to create a pad should be reported");
        Assert.Empty(manager.Pads);
        Assert.True(
            WaitFor(() => manager.Sessions[0].Statistics.ReportsReceived > 0),
            "reports should still be decoded");
    }

    [Fact]
    public void DoesNotAttachTheSameControllerTwice()
    {
        // Windows exposes a Pro Controller as two HID collections, and both are reported to the
        // picker. Claiming a slot twice would leave a phantom pad behind.
        using var harness = new ManagerHarness();
        harness.AddController();
        using var manager = harness.Start();

        Assert.True(WaitFor(() => harness.Backend.Pads.Count == 1));
        manager.DiscoverOnce();
        manager.DiscoverOnce();

        Assert.Single(harness.Backend.Pads);
        Assert.Single(manager.Sessions);
    }

    // Full deflection on one axis, with the other axis at the controller's calibrated centre so that
    // only the axis under test moves.
    private static readonly StickRaw PushRight = SimulatedControllerTransport.CentredStick with { X = 3800 };
    private static readonly StickRaw PushLeft = SimulatedControllerTransport.CentredStick with { X = 400 };

    /// <summary>Finds the virtual pad that a particular controller is feeding.</summary>
    private static NullPad PadFor(ControllerManager manager, ManagerHarness harness, SwitchControllerSession session)
    {
        var slot = manager.Pads.Single(p => p.Sources.Contains(session)).Slot;
        return harness.Backend.Pads.Single(p => p.Slot == slot);
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

    [Fact]
    public async Task DisposingTheManagerStopsReadingBeforeTheTransportIsClosed()
    {
        // A device's reader thread must be stopped before its transport is closed. Otherwise the
        // thread keeps reading and writing a device that has already been released.
        var tracked = new List<DisposeAwareTransport>();
        using var harness = new ManagerHarness
        {
            WrapTransport = inner =>
            {
                var wrapped = new DisposeAwareTransport(inner);
                tracked.Add(wrapped);
                return wrapped;
            },
        };

        harness.AddController();
        var manager = harness.Start();

        // Make sure the reader is running before disposing, so the check is not vacuous.
        await tracked[0].FirstRead.WaitAsync(TimeSpan.FromSeconds(5));

        manager.Dispose();

        // A fixed manager stops the reader before disposing the transport, so no call arrives and the
        // bounded wait expires. The old code's reader keeps calling, which completes ReadAfterDispose.
        await Task.WhenAny(tracked[0].ReadAfterDispose, Task.Delay(TimeSpan.FromSeconds(2)));

        Assert.Equal(0, tracked[0].CallsAfterDispose);
    }

    /// <summary>
    /// Counts reads and writes made after the transport was disposed. <see cref="FirstRead"/> signals that the
    /// reader has started; <see cref="ReadAfterDispose"/> signals the first call made after disposal.
    /// </summary>
    private sealed class DisposeAwareTransport(IControllerTransport inner) : IControllerTransport
    {
        private readonly TaskCompletionSource _firstRead = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _readAfterDispose = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private volatile bool _disposed;
        private int _callsAfterDispose;

        public Task FirstRead => _firstRead.Task;

        public Task ReadAfterDispose => _readAfterDispose.Task;

        public int CallsAfterDispose => _callsAfterDispose;

        public string Description => inner.Description;

        public ConnectionKind Connection => inner.Connection;

        public int MaxInputReportLength => inner.MaxInputReportLength;

        public int OutputReportLength => inner.OutputReportLength;

        public int Read(Span<byte> buffer, int timeoutMilliseconds)
        {
            _firstRead.TrySetResult();
            Record();
            return inner.Read(buffer, timeoutMilliseconds);
        }

        public void Write(ReadOnlySpan<byte> report)
        {
            Record();
            inner.Write(report);
        }

        public void Dispose()
        {
            _disposed = true;
            inner.Dispose();
        }

        private void Record()
        {
            if (_disposed)
            {
                System.Threading.Interlocked.Increment(ref _callsAfterDispose);
                _readAfterDispose.TrySetResult();
            }
        }
    }

    /// <summary>Discovery backed by a fixed list of simulated controllers.</summary>
    private sealed class ScriptedDiscovery : IDeviceDiscovery
    {
        private readonly List<DiscoveredDevice> _devices = [];

        public void Add(HidCandidate candidate, IControllerTransport transport) =>
            _devices.Add(new DiscoveredDevice(candidate, () => transport));

        public IReadOnlyList<DiscoveredDevice> Discover() => _devices.ToArray();
    }

    /// <summary>Pad backend that hands out recording pads instead of talking to a driver.</summary>
    private sealed class RecordingBackend : IVirtualPadBackend
    {
        private readonly List<NullPad> _pads = [];

        public string Name => "test backend";

        public DriverStatus Status { get; } = new() { Available = true, Summary = "ready" };

        public IReadOnlyList<NullPad> Pads
        {
            get
            {
                lock (_pads)
                {
                    return _pads.ToArray();
                }
            }
        }

        /// <summary>When set, creating a pad fails the way a missing driver does.</summary>
        public string? FailureReason { get; init; }

        public IVirtualPad? CreatePad(int slot, string description, out string? error)
        {
            if (FailureReason is not null)
            {
                error = FailureReason;
                return null;
            }

            error = null;
            var pad = new NullPad(slot, description);
            lock (_pads)
            {
                _pads.Add(pad);
            }

            return pad;
        }

        public void Dispose()
        {
        }
    }

    private sealed class ManagerHarness : IDisposable
    {
        private readonly ScriptedDiscovery _discovery = new();
        private readonly List<SimulatedControllerTransport> _transports = [];
        private readonly ControllerManagerOptions _options;
        private ControllerManager? _manager;
        private ulong _nextMac = 0x001122334400UL;
        private int _nextIndex = 1;

        public ManagerHarness(
            JoyConMode joyConMode = JoyConMode.Sideways,
            IVirtualPadBackend? backend = null,
            SwitchControllerSessionOptions? sessionOptions = null)
        {
            Backend = backend as RecordingBackend ?? new RecordingBackend();
            ExternalBackend = backend;
            _options = new ControllerManagerOptions
            {
                JoyConMode = joyConMode,
                SessionOptions = sessionOptions ?? new SwitchControllerSessionOptions(),
            };
        }

        public RecordingBackend Backend { get; }

        /// <summary>Lets a test interpose on the transport the manager is handed.</summary>
        public Func<IControllerTransport, IControllerTransport>? WrapTransport { get; set; }

        private IVirtualPadBackend? ExternalBackend { get; }

        public SimulatedControllerTransport AddController(
            SwitchModel model = SwitchModel.ProController,
            ulong? mac = null,
            SimulatedControllerOptions? options = null)
        {
            var productId = model switch
            {
                SwitchModel.JoyConLeft => SwitchModels.ProductIdJoyConLeft,
                SwitchModel.JoyConRight => SwitchModels.ProductIdJoyConRight,
                _ => SwitchModels.ProductIdProController,
            };

            var address = mac ?? _nextMac++;
            var index = _nextIndex++;

            var transport = new SimulatedControllerTransport(options ?? new SimulatedControllerOptions
            {
                ProductId = (ushort)productId,
                MacAddress = address,
            });

            _transports.Add(transport);

            var path = $"\\\\?\\HID#VID_057E&PID_{productId:X4}#8&{index:x8}&0&0000#{{4d1e55b2-f16f-11cf-88cb-001111000030}}";
            _discovery.Add(
                new HidCandidate
                {
                    DevicePath = path,
                    VendorId = 0x057E,
                    ProductId = productId,
                    ProductName = SwitchModels.Describe(productId).DisplayName,
                    Connection = ConnectionKind.Bluetooth,
                    MaxInputReportLength = ProtocolConstants.FullStateReportLength,
                    MaxOutputReportLength = OutputFrames.FrameLength,
                    InputReports = [new ReportInfo(0x30, ProtocolConstants.FullStateReportLength, "input")],
                    OutputReports = [new ReportInfo(0x01, OutputFrames.FrameLength, "output")],
                    PhysicalDeviceKey = HidDevicePath.PhysicalDeviceKey(path, 0x057E, productId),
                },
                WrapTransport?.Invoke(transport) ?? transport);

            return transport;
        }

        public ControllerManager Start()
        {
            _manager = new ControllerManager(
                ExternalBackend ?? Backend,
                _options,
                NullLogger.Instance,
                TimeProvider.System,
                _discovery);

            _manager.DiscoverOnce();
            return _manager;
        }

        public void Dispose()
        {
            // The manager owns the sessions and their reader threads, so it has to be stopped before
            // the transports go away.
            _manager?.Dispose();
            foreach (var transport in _transports)
            {
                transport.Dispose();
            }
        }
    }
}
