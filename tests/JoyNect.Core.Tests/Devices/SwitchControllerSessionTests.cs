using System.Diagnostics;
using JoyNect.Core.Devices;
using JoyNect.Core.Hid;
using JoyNect.Core.Protocol;
using Xunit;

namespace JoyNect.Core.Tests.Devices;

/// <summary>
/// End to end tests for the link state machine, driven by a scripted controller.
/// </summary>
/// <remarks>
/// These exercise the real handshake, the real report decoding and the real write path; only the USB
/// or Bluetooth transport underneath is replaced.
/// </remarks>
public sealed class SwitchControllerSessionTests
{
    [Fact]
    public void HandshakeReachesReadyAndIdentifiesTheController()
    {
        using var harness = new Harness();

        Assert.True(harness.WaitForState(ControllerLinkState.Ready), $"state was {harness.Session.State}");

        Assert.Equal("00:11:22:33:44:55", harness.Session.Info?.MacAddressText);
        Assert.Equal(SwitchModel.ProController, harness.Session.Model.Model);
        Assert.Equal("4.1", harness.Session.Info?.FirmwareText);

        Assert.True(harness.Transport.FullReportsRequested);
        Assert.True(harness.Transport.ImuEnabled);
        Assert.True(harness.Transport.VibrationEnabled);
        Assert.Equal((byte?)0x01, harness.Transport.PlayerLights);
        Assert.True(harness.Session.CalibrationLoaded);
        Assert.Equal(0x79F, harness.Session.LeftStickCalibration.CenterX);
        Assert.Equal(0x8A0, harness.Session.LeftStickCalibration.CenterY);
        Assert.Equal(0x79F, harness.Session.RightStickCalibration.CenterX);
    }

    [Fact]
    public void UsesTheControllerReportedModelWhenTheProductIdIsMisleading()
    {
        // A controller whose product id says "Pro Controller" but which reports itself as Joy-Con (L):
        // the protocol answer must win, because that is what decides the button layout.
        var options = new SimulatedControllerOptions
        {
            ProductId = SwitchModels.ProductIdProController,
            ReportedDeviceType = 0x01,
        };

        using var harness = new Harness(options);
        Assert.True(harness.WaitForState(ControllerLinkState.Ready));
        Assert.Equal(SwitchModel.JoyConLeft, harness.Session.Model.Model);
    }

    [Fact]
    public void UsbConnectionsRunTheBootstrapSequenceFirst()
    {
        using var harness = new Harness(new SimulatedControllerOptions { Connection = ConnectionKind.Usb });

        Assert.True(harness.WaitForState(ControllerLinkState.Ready), $"state was {harness.Session.State}");
        Assert.Equal(new byte[] { 0x01, 0x02, 0x03, 0x02, 0x04 }, harness.Transport.ReceivedUsbBootstrapCommands);
    }

    [Fact]
    public void BluetoothConnectionsSkipTheBootstrap()
    {
        using var harness = new Harness(new SimulatedControllerOptions { Connection = ConnectionKind.Bluetooth });

        Assert.True(harness.WaitForState(ControllerLinkState.Ready));
        Assert.Empty(harness.Transport.ReceivedUsbBootstrapCommands);
        Assert.Equal(ProtocolConstants.SubcommandRequestDeviceInfo, harness.Transport.ReceivedSubcommands[0]);
    }

    [Fact]
    public void DecodesLiveReportsAndRaisesUpdates()
    {
        using var harness = new Harness();
        Assert.True(harness.WaitForState(ControllerLinkState.Ready));

        var updates = 0;
        harness.Session.StateUpdated += _ => Interlocked.Increment(ref updates);

        harness.Transport.Buttons = SwitchButtons.A | SwitchButtons.Zr | SwitchButtons.LeftStickClick;
        harness.Transport.LeftStick = new StickRaw(1200, 3000);

        Assert.True(WaitFor(() => harness.Session.Latest.Buttons.Has(SwitchButtons.A), 3000));
        Assert.Equal(SwitchButtons.A | SwitchButtons.Zr | SwitchButtons.LeftStickClick, harness.Session.Latest.Buttons);
        Assert.Equal(new StickRaw(1200, 3000), harness.Session.Latest.LeftStick);
        Assert.True(WaitFor(() => updates > 0, 3000), "StateUpdated was not raised after the input change");
    }

    [Fact]
    public void ForwardsRumbleUsingTheDocumentedEncoding()
    {
        using var harness = new Harness();
        Assert.True(harness.WaitForState(ControllerLinkState.Ready));

        harness.Session.SetRumble(largeMotor: 255, smallMotor: 0);
        Assert.True(WaitFor(() => !RumbleEncoder.IsNeutral(harness.Transport.LastRumble.AsSpan(0, 4), harness.Transport.LastRumble.AsSpan(4, 4)), 3000));

        Span<byte> expectedLeft = stackalloc byte[4];
        Span<byte> expectedRight = stackalloc byte[4];
        RumbleEncoder.EncodeBoth(255, 0, harness.Session.RumbleStrength, expectedLeft, expectedRight);

        Assert.Equal(expectedLeft.ToArray(), harness.Transport.LastRumble.AsSpan(0, 4).ToArray());
        Assert.Equal(expectedRight.ToArray(), harness.Transport.LastRumble.AsSpan(4, 4).ToArray());
    }

    [Fact]
    public void StopsRumblingWhenTheRequestIsCleared()
    {
        using var harness = new Harness();
        Assert.True(harness.WaitForState(ControllerLinkState.Ready));

        harness.Session.SetRumble(200, 200);
        Assert.True(WaitFor(() => !RumbleEncoder.IsNeutral(
            harness.Transport.LastRumble.AsSpan(0, 4),
            harness.Transport.LastRumble.AsSpan(4, 4)), 3000));

        harness.Session.SetRumble(0, 0);
        Assert.True(WaitFor(() => RumbleEncoder.IsNeutral(
            harness.Transport.LastRumble.AsSpan(0, 4),
            harness.Transport.LastRumble.AsSpan(4, 4)), 3000));
    }

    [Fact]
    public void FailsTheLinkWhenEssentialSubcommandsAreNeverAcknowledged()
    {
        using var harness = new Harness(new SimulatedControllerOptions { DropSubcommandReplies = true });

        Assert.True(harness.WaitForState(ControllerLinkState.Failed, 6000), $"state was {harness.Session.State}");
        Assert.Equal(1, harness.Session.Statistics.HandshakeFailures);
        Assert.Equal(ControllerLinkState.Failed, harness.Session.State);
    }

    [Fact]
    public void OptionalSubcommandsMayFailWithoutBreakingTheLink()
    {
        using var harness = new Harness(new SimulatedControllerOptions { DropOptionalReplies = true });

        Assert.True(harness.WaitForState(ControllerLinkState.Ready), $"state was {harness.Session.State}");

        // Lights, IMU, vibration and calibration were never acknowledged, so we fall back to defaults
        // and carry on: a working axis beats no controller at all.
        Assert.False(harness.Session.CalibrationLoaded);
        Assert.Equal(StickCalibration.Default.CenterX, harness.Session.LeftStickCalibration.CenterX);

        // Reports still flow and nothing was misunderstood along the way.
        Assert.True(WaitFor(() => harness.Session.Statistics.ReportsReceived > 0, 2000));
        Assert.Equal(0, harness.Session.Statistics.UnrecognisedReports);
    }

    [Fact]
    public void DetectsAStalledLinkAndRehandshakes()
    {
        using var harness = new Harness(
            new SimulatedControllerOptions { ReportsBeforeSilence = 20 },
            new SwitchControllerSessionOptions
            {
                StaleAfter = TimeSpan.FromMilliseconds(200),
                AbandonAfter = TimeSpan.FromMilliseconds(900),
            });

        Assert.True(harness.WaitForState(ControllerLinkState.Ready));

        // The controller goes quiet: the session must notice, re-handshake, and then give up rather
        // than leaving a virtual pad stuck at whatever it last received.
        Assert.True(WaitFor(() => harness.Session.Statistics.Rehandshakes > 0, 4000), "no re-handshake was recorded");
        Assert.True(harness.WaitForState(ControllerLinkState.Failed, 5000), $"state was {harness.Session.State}");
    }

    [Fact]
    public void RecordsReportCadenceStatistics()
    {
        using var harness = new Harness();
        Assert.True(harness.WaitForState(ControllerLinkState.Ready));
        Assert.True(WaitFor(() => harness.Session.Statistics.ReportsReceived > 10, 3000));

        Assert.True(harness.Session.Statistics.ReportIntervals.Count > 5);
        Assert.NotNull(harness.Session.Statistics.ReportIntervals.Percentile(50));
        Assert.Contains("median", harness.Session.Statistics.DescribeCadence());
    }

    [Fact]
    public void SendsNeutralRumbleWhenItShutsDown()
    {
        var harness = new Harness();
        Assert.True(harness.WaitForState(ControllerLinkState.Ready));

        harness.Session.SetRumble(255, 255);
        Assert.True(WaitFor(() => !RumbleEncoder.IsNeutral(
            harness.Transport.LastRumble.AsSpan(0, 4),
            harness.Transport.LastRumble.AsSpan(4, 4)), 3000));

        harness.Stop();

        Assert.True(RumbleEncoder.IsNeutral(
            harness.Transport.LastRumble.AsSpan(0, 4),
            harness.Transport.LastRumble.AsSpan(4, 4)));
        harness.Dispose();
    }

    [Fact]
    public void SurvivesAControllerThatDisappearsMidWrite()
    {
        using var harness = new Harness(new SimulatedControllerOptions { WritesBeforeFailure = 6 });

        // Writes start failing part way through the handshake; the session must end up Failed
        // instead of throwing out of the loop.
        Assert.True(harness.WaitForState(ControllerLinkState.Failed, 6000), $"state was {harness.Session.State}");
    }

    [Fact]
    public void RumbleStrengthZeroMeansSilence()
    {
        using var harness = new Harness(
            options: new SimulatedControllerOptions(),
            sessionOptions: new SwitchControllerSessionOptions { RumbleStrength = 0 });

        Assert.True(harness.WaitForState(ControllerLinkState.Ready));
        harness.Session.SetRumble(255, 255);

        Thread.Sleep(150);
        Assert.True(RumbleEncoder.IsNeutral(
            harness.Transport.LastRumble.AsSpan(0, 4),
            harness.Transport.LastRumble.AsSpan(4, 4)));
    }

    private static bool WaitFor(Func<bool> condition, int timeoutMilliseconds)
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

    private sealed class Harness : IDisposable
    {
        private readonly CancellationTokenSource _cancellation = new();
        private readonly Task _loop;
        private bool _stopped;

        public Harness(SimulatedControllerOptions? options = null, SwitchControllerSessionOptions? sessionOptions = null)
        {
            options ??= new SimulatedControllerOptions();
            Transport = new SimulatedControllerTransport(options);
            Session = new SwitchControllerSession(
                "test-device",
                options.ProductId,
                Transport,
                sessionOptions ?? new SwitchControllerSessionOptions(),
                TimeProvider.System);

            _loop = Task.Factory.StartNew(
                () => Session.RunLoop(_cancellation.Token),
                TaskCreationOptions.LongRunning);
        }

        public SimulatedControllerTransport Transport { get; }

        public SwitchControllerSession Session { get; }

        public bool WaitForState(ControllerLinkState state, int timeoutMilliseconds = 5000) =>
            WaitFor(() => Session.State == state, timeoutMilliseconds);

        public void Stop()
        {
            if (_stopped)
            {
                return;
            }

            _stopped = true;
            _cancellation.Cancel();
            _loop.Wait(TimeSpan.FromSeconds(5));
        }

        public void Dispose()
        {
            Stop();
            Session.Dispose();
            _cancellation.Dispose();
        }
    }
}
