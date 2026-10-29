using JoyNect.Core.Hid;
using JoyNect.Core.Protocol;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace JoyNect.Core.Devices;

/// <summary>Lifecycle of one controller link.</summary>
public enum ControllerLinkState
{
    /// <summary>Not started yet.</summary>
    Idle,

    /// <summary>Sending the handshake sequence.</summary>
    Handshaking,

    /// <summary>Handshake done and reports are flowing.</summary>
    Ready,

    /// <summary>Handshake done but reports stopped arriving.</summary>
    Stale,

    /// <summary>Gave up after repeated failures; the manager will retry later.</summary>
    Failed,

    /// <summary>Shut down.</summary>
    Stopped,
}

public sealed class SwitchControllerSessionOptions
{
    /// <summary>How long to wait for a subcommand acknowledgement before retrying.</summary>
    public TimeSpan HandshakeStepTimeout { get; init; } = TimeSpan.FromMilliseconds(300);

    /// <summary>Delay used for the USB bootstrap steps, which are fire and forget.</summary>
    public TimeSpan UsbBootstrapStepDelay { get; init; } = TimeSpan.FromMilliseconds(120);

    /// <summary>Attempts per handshake step before giving up (essential steps) or moving on.</summary>
    public int HandshakeAttempts { get; init; } = 3;

    /// <summary>No report for this long marks the link stale.</summary>
    public TimeSpan StaleAfter { get; init; } = TimeSpan.FromMilliseconds(400);

    /// <summary>No report for this long abandons the session.</summary>
    public TimeSpan AbandonAfter { get; init; } = TimeSpan.FromSeconds(6);

    /// <summary>Read timeout for one iteration of the loop.</summary>
    public int ReadTimeoutMilliseconds { get; init; } = 10;

    /// <summary>Read factory calibration from the controller's flash.</summary>
    public bool ReadCalibration { get; init; } = true;

    /// <summary>User rumble strength multiplier in [0, 1].</summary>
    public double RumbleStrength { get; init; } = 0.6;
}

/// <summary>
/// Drives one controller: handshake, report decoding, rumble output and link supervision.
/// </summary>
/// <remarks>
/// Single threaded by design. <see cref="RunLoop"/> owns the read side and is the only writer, which
/// is the only way to keep the packet counter, the rumble keepalive and subcommand acknowledgements
/// sane. Everything the rest of the app does goes through fields (<see cref="SetRumble"/>) or the
/// pending queue, never a second write path.
/// </remarks>
public sealed class SwitchControllerSession : IDisposable
{
    private readonly IControllerTransport _transport;
    private readonly SwitchControllerSessionOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger _logger;
    private readonly byte[] _readBuffer;
    private readonly byte[] _frameBuffer = new byte[OutputFrames.FrameLength];
    private readonly byte[] _rumbleLeft = [0x00, 0x01, 0x40, 0x40];
    private readonly byte[] _rumbleRight = [0x00, 0x01, 0x40, 0x40];
    private readonly byte[] _lastSentRumble = [0x00, 0x01, 0x40, 0x40, 0x00, 0x01, 0x40, 0x40];
    private readonly Queue<byte[]> _pendingSubcommands = new();

    private HandshakeStep _step = HandshakeStep.UsbBootstrapConnectionStatus;
    private bool _stepSent;
    private bool _stepAcknowledged;
    private long _stepStartedAt;
    private int _stepAttempts;
    private long _lastReportAt;
    private long _lastWriteAt;
    private bool _sticksLoaded;
    private bool _imuLoaded;
    private byte _packetCounter;
    private volatile byte _playerLight;
    private volatile byte _requestedRumbleLarge;
    private volatile byte _requestedRumbleSmall;
    private double _rumbleStrength;
    private bool _rehandshakePending;
    private bool _disposed;
    private readonly object _stateGate = new();
    private FullStateReport _latest;
    private bool _hasState;

    public SwitchControllerSession(
        string deviceKey,
        int productId,
        IControllerTransport transport,
        SwitchControllerSessionOptions options,
        TimeProvider? timeProvider = null,
        ILogger? logger = null)
    {
        DeviceKey = deviceKey;
        ProductId = productId;
        _transport = transport;
        _options = options;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _logger = logger ?? NullLogger.Instance;
        _readBuffer = new byte[Math.Max(transport.MaxInputReportLength, ProtocolConstants.FullStateReportLength)];
        _rumbleStrength = Math.Clamp(options.RumbleStrength, 0, 1);

        _step = transport.Connection == ConnectionKind.Usb
            ? HandshakeStep.UsbBootstrapConnectionStatus
            : HandshakeStep.DeviceInfo;
    }

    /// <summary>Key derived from the HID device path, stable for the lifetime of the link.</summary>
    public string DeviceKey { get; }

    /// <summary>
    /// Identity the controller reports about itself (its MAC address), or the device path until the
    /// handshake has run. This is what player slots and per-controller settings are keyed on, so a
    /// controller keeps its slot across reconnects.
    /// </summary>
    public string IdentityKey => Info?.MacAddressText ?? DeviceKey;

    public int ProductId { get; }

    public ControllerLinkState State { get; private set; } = ControllerLinkState.Idle;

    /// <summary>Identity reported by the controller, available after the handshake.</summary>
    public DeviceInfo? Info { get; private set; }

    /// <summary>Model from the product id, upgraded by what the controller reports about itself.</summary>
    public ModelInfo Model { get; private set; } = ModelInfo.Unknown;

    public StickCalibration LeftStickCalibration { get; private set; } = StickCalibration.Default;

    public StickCalibration RightStickCalibration { get; private set; } = StickCalibration.Default;

    public ImuCalibration ImuCalibration { get; private set; } = ImuCalibration.Default;

    /// <summary>True when factory calibration was read successfully.</summary>
    public bool CalibrationLoaded { get; private set; }

    /// <summary>
    /// Most recent decoded state, copied under a lock because the state is larger than a machine word
    /// and a torn read would show a momentary mix of two frames as a phantom input.
    /// </summary>
    public FullStateReport Latest
    {
        get
        {
            lock (_stateGate)
            {
                return _latest;
            }
        }
    }

    /// <summary>True once at least one full state report has been decoded.</summary>
    public bool HasState
    {
        get
        {
            lock (_stateGate)
            {
                return _hasState;
            }
        }
    }

    /// <summary>Reads the latest state without tearing.</summary>
    public bool TryGetLatest(out FullStateReport state)
    {
        lock (_stateGate)
        {
            state = _latest;
            return _hasState;
        }
    }

    /// <summary>Player slot (1-4) currently mapped; drives the player lights.</summary>
    private volatile int _slot = 1;

    public int Slot
    {
        get => _slot;
        private set => _slot = value;
    }

    /// <summary>
    /// Rumble strength multiplier in [0, 1], adjustable while the link runs.
    /// </summary>
    /// <remarks>
    /// This is what keeps the "rumble is too strong / so weak I cannot feel it" complaint tunable
    /// without reconnecting: the next output frame simply carries a different amplitude.
    /// </remarks>
    public double RumbleStrength
    {
        get => Volatile.Read(ref _rumbleStrength);
        set => Volatile.Write(ref _rumbleStrength, Math.Clamp(value, 0, 1));
    }

    public SessionStatistics Statistics { get; } = new();

    /// <summary>Raised on the session's own thread after every decoded report.</summary>
    public event Action<SwitchControllerSession>? StateUpdated;

    /// <summary>Raised when <see cref="State"/> changes.</summary>
    public event Action<SwitchControllerSession>? LinkStateChanged;

    public ConnectionKind Connection => _transport.Connection;

    public string TransportDescription => _transport.Description;

    /// <summary>Requests rumble output. Safe to call from any thread.</summary>
    public void SetRumble(byte largeMotor, byte smallMotor)
    {
        _requestedRumbleLarge = largeMotor;
        _requestedRumbleSmall = smallMotor;
    }

    /// <summary>Sets the player slot; the lights are updated on the next loop iteration.</summary>
    public void SetSlot(int slot)
    {
        if (slot is < 1 or > 4 || slot == Slot)
        {
            return;
        }

        Slot = slot;
        var light = OutputFrames.PlayerLightForSlot(slot);
        if (light != _playerLight)
        {
            _playerLight = light;
            QueueSubcommand(ProtocolConstants.SubcommandSetPlayerLights, [light]);
        }
    }

    /// <summary>Queues an arbitrary subcommand for the normal (post handshake) path.</summary>
    public void QueueSubcommand(byte subcommand, byte[] arguments)
    {
        var payload = new byte[arguments.Length + 1];
        payload[0] = subcommand;
        arguments.CopyTo(payload, 1);
        lock (_pendingSubcommands)
        {
            _pendingSubcommands.Enqueue(payload);
        }
    }

    /// <summary>
    /// Runs the link until cancelled. Blocking; run it on a dedicated thread.
    /// </summary>
    public void RunLoop(CancellationToken cancellationToken)
    {
        _logger.LogDebug("Session for {Key} starting on {Transport}", DeviceKey, _transport.Description);
        _lastReportAt = _timeProvider.GetTimestamp();

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                if (State == ControllerLinkState.Failed)
                {
                    break;
                }

                var read = ReadOnce();
                if (read > 0)
                {
                    HandleReport(_readBuffer.AsSpan(0, read));
                }

                AdvanceHandshake();
                PumpOutput();
                SuperviseLink();
            }
        }
        catch (ControllerLinkException ex)
        {
            _logger.LogWarning(ex, "Link for {Key} failed", DeviceKey);
            SetState(ControllerLinkState.Failed);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Session for {Key} stopped unexpectedly", DeviceKey);
            SetState(ControllerLinkState.Failed);
        }
        finally
        {
            // Always leave the actuators neutral: a controller that keeps buzzing after the app
            // exits is the worst bug this program could have.
            _requestedRumbleLarge = 0;
            _requestedRumbleSmall = 0;
            try
            {
                UpdateRumbleEncoding();
                WriteFrameCore(subcommand: null, default);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Could not send the final neutral rumble frame to {Key}", DeviceKey);
            }

            if (State != ControllerLinkState.Failed)
            {
                SetState(ControllerLinkState.Stopped);
            }
        }
    }

    private int ReadOnce()
    {
        try
        {
            var timeout = _step == HandshakeStep.Complete ? _options.ReadTimeoutMilliseconds : 5;
            return _transport.Read(_readBuffer, timeout);
        }
        catch (ControllerLinkException)
        {
            throw;
        }
        catch (ObjectDisposedException)
        {
            // Transport was closed underneath us: treat as a normal shutdown.
            return 0;
        }
    }

    private void HandleReport(ReadOnlySpan<byte> report)
    {
        switch (report[0])
        {
            case ProtocolConstants.InputFullState:
                HandleFullState(report);
                break;

            case ProtocolConstants.InputSubcommandReply:
                HandleSubcommandReply(report);
                break;

            case SpiReadReply.SpiReadReplyReportId:
                HandleSpiReadReply(report);
                break;

            case ProtocolConstants.InputNsoFullState:
            case ProtocolConstants.InputNsoSubcommandReply:
                // Recognised as an NSO retro controller report, which JoyNect does not decode yet.
                Statistics.RecordUnrecognised(report);
                break;

            default:
                Statistics.RecordUnrecognised(report);
                break;
        }
    }

    private void HandleFullState(ReadOnlySpan<byte> report)
    {
        if (!FullStateReport.TryParse(report, out var state))
        {
            Statistics.RecordUnrecognised(report);
            return;
        }

        var now = _timeProvider.GetTimestamp();
        TimeSpan? interval = _lastReportAt == 0 ? null : _timeProvider.GetElapsedTime(_lastReportAt, now);
        _lastReportAt = now;

        Statistics.RecordReport(interval, _timeProvider.GetUtcNow());
        lock (_stateGate)
        {
            _latest = state;
            _hasState = true;
        }


        if (State == ControllerLinkState.Stale)
        {
            SetState(ControllerLinkState.Ready);
        }

        StateUpdated?.Invoke(this);
    }

    private void HandleSubcommandReply(ReadOnlySpan<byte> report)
    {
        if (!SubcommandReply.TryParse(report, out var reply))
        {
            Statistics.RecordUnrecognised(report);
            return;
        }

        if (reply.Subcommand == CurrentStepSubcommand())
        {
            _stepAcknowledged = reply.IsSuccess;
            if (!reply.IsSuccess)
            {
                _logger.LogWarning(
                    "Controller {Key} refused subcommand 0x{Subcommand:X2} with ack 0x{Ack:X2}",
                    DeviceKey, reply.Subcommand, reply.Ack);
            }
        }

        if (reply.Subcommand == ProtocolConstants.SubcommandRequestDeviceInfo && DeviceInfo.TryParse(reply.Data, out var info))
        {
            Info = info;
            Model = DescribeModel(info, ProductId);
        }
    }

    private void HandleSpiReadReply(ReadOnlySpan<byte> report)
    {
        if (!SpiReadReply.TryParse(report, out var reply) || !reply.IsSuccess)
        {
            return;
        }

        if (_step is HandshakeStep.Calibration or HandshakeStep.ImuCalibration)
        {
            _stepAcknowledged = true;
        }

        ApplyCalibration(reply.Address, reply.Data);
    }

    private void ApplyCalibration(int address, ReadOnlySpan<byte> data)
    {
        switch (address)
        {
            case ProtocolConstants.SpiAddressStickCalibration when data.Length >= 18:
                LeftStickCalibration = StickCalibration.Decode(data[..9]);
                RightStickCalibration = StickCalibration.Decode(data.Slice(9, 9));
                _sticksLoaded = true;
                break;

            case ProtocolConstants.SpiAddressImuCalibration when data.Length >= 24:
                ImuCalibration = ImuCalibration.Decode(data[..24]);
                _imuLoaded = true;
                break;

            default:
                _logger.LogDebug("Ignoring calibration read for address 0x{Address:X4}", address);
                break;
        }

        CalibrationLoaded = _sticksLoaded || _imuLoaded;

        if (CalibrationLoaded)
        {
            _logger.LogInformation(
                "Calibration loaded for {Key}: left centre {LeftX},{LeftY} right centre {RightX},{RightY}",
                DeviceKey,
                LeftStickCalibration.CenterX, LeftStickCalibration.CenterY,
                RightStickCalibration.CenterX, RightStickCalibration.CenterY);
        }
    }

    private static ModelInfo DescribeModel(DeviceInfo info, int productId)
    {
        var byProductId = SwitchModels.Describe(productId);
        var byProtocol = SwitchModels.FromDeviceInfoType(info.ControllerType);

        // The controller's own answer wins: retro controllers reuse the Pro Controller product id.
        if (byProtocol != SwitchModel.Unknown && byProtocol != byProductId.Model)
        {
            return SwitchModels.DescribeProduct(byProtocol);
        }

        return byProductId;
    }

    private void AdvanceHandshake()
    {
        if (_step == HandshakeStep.Complete)
        {
            return;
        }

        var now = _timeProvider.GetTimestamp();

        if (!_stepSent)
        {
            if (_stepAttempts == 0)
            {
                SetState(ControllerLinkState.Handshaking);
            }

            SendStep();
            _stepSent = true;
            _stepAcknowledged = false;
            _stepStartedAt = now;
            return;
        }

        var timeout = IsUsbBootstrapStep(_step) ? _options.UsbBootstrapStepDelay : _options.HandshakeStepTimeout;
        var elapsed = _timeProvider.GetElapsedTime(_stepStartedAt, now);
        var timedOut = elapsed >= timeout;
        if (!_stepAcknowledged && !timedOut)
        {
            return;
        }

        if (!_stepAcknowledged)
        {
            _stepAttempts++;
            if (_stepAttempts >= CurrentStepAttempts)
            {
                if (IsEssentialStep(_step))
                {
                    Statistics.RecordHandshakeFailure();
                    _logger.LogWarning(
                        "Giving up on {Key}: subcommand 0x{Subcommand:X2} was never acknowledged",
                        DeviceKey, CurrentStepSubcommand());
                    SetState(ControllerLinkState.Failed);
                    return;
                }

                _logger.LogDebug(
                    "Continuing without acknowledgement for optional step {Step} on {Key}",
                    _step, DeviceKey);
            }
            else
            {
                _stepSent = false;
                return;
            }
        }

        _stepAttempts = 0;
        _stepSent = false;
        _stepAcknowledged = false;
        _step = NextStep(_step);

        if (_step == HandshakeStep.Complete)
        {
            _logger.LogInformation(
                "Controller {Key} ready ({Model}, firmware {Firmware}, calibration {Calibration})",
                DeviceKey, Model.DisplayName, Info?.FirmwareText ?? "unknown",
                CalibrationLoaded ? "loaded" : "defaults");
            SetState(ControllerLinkState.Ready);
        }
    }

    private void SendStep()
    {
        switch (_step)
        {
            case HandshakeStep.UsbBootstrapConnectionStatus:
                WriteUsbBootstrap(ProtocolConstants.UsbBootstrapRequestConnectionStatus);
                break;

            case HandshakeStep.UsbBootstrapHandshake:
            case HandshakeStep.UsbBootstrapHandshakeAgain:
                WriteUsbBootstrap(ProtocolConstants.UsbBootstrapHandshake);
                break;

            case HandshakeStep.UsbBootstrapBaudRate:
                WriteUsbBootstrap(ProtocolConstants.UsbBootstrapSetBaudRate);
                break;

            case HandshakeStep.UsbBootstrapDisableTimeout:
                WriteUsbBootstrap(ProtocolConstants.UsbBootstrapDisableTimeout);
                break;

            case HandshakeStep.DeviceInfo:
                TryWriteFrame(ProtocolConstants.SubcommandRequestDeviceInfo, default);
                break;

            case HandshakeStep.ReportMode:
                TryWriteFrame(ProtocolConstants.SubcommandSetInputReportMode, [ProtocolConstants.ReportModeFull]);
                break;

            case HandshakeStep.EnableImu:
                TryWriteFrame(ProtocolConstants.SubcommandEnableImu, [0x01]);
                break;

            case HandshakeStep.ImuSensitivity:
                // +/-2000 dps gyro, +/-8G accelerometer, 208 Hz gyro filter, 100 Hz accel filter.
                TryWriteFrame(ProtocolConstants.SubcommandSetImuSensitivity, [0x03, 0x00, 0x01, 0x01]);
                break;

            case HandshakeStep.EnableVibration:
                TryWriteFrame(ProtocolConstants.SubcommandEnableVibration, [0x01]);
                break;

            case HandshakeStep.PlayerLights:
                _playerLight = OutputFrames.PlayerLightForSlot(Slot);
                TryWriteFrame(ProtocolConstants.SubcommandSetPlayerLights, [_playerLight]);
                break;

            case HandshakeStep.Calibration:
                if (_options.ReadCalibration)
                {
                    Span<byte> arguments = stackalloc byte[9];
                    OutputFrames.BuildSpiReadArguments(arguments, ProtocolConstants.SpiAddressStickCalibration, 18);
                    TryWriteFrame(ProtocolConstants.SubcommandSpiFlashRead, arguments);
                }
                else
                {
                    _stepAcknowledged = true;
                }

                break;

            case HandshakeStep.ImuCalibration:
                if (_options.ReadCalibration)
                {
                    Span<byte> arguments = stackalloc byte[9];
                    OutputFrames.BuildSpiReadArguments(arguments, ProtocolConstants.SpiAddressImuCalibration, 24);
                    TryWriteFrame(ProtocolConstants.SubcommandSpiFlashRead, arguments);
                }
                else
                {
                    _stepAcknowledged = true;
                }

                break;

            default:
                _stepAcknowledged = true;
                break;
        }
    }

    private static HandshakeStep NextStep(HandshakeStep step) => step switch
    {
        HandshakeStep.UsbBootstrapConnectionStatus => HandshakeStep.UsbBootstrapHandshake,
        HandshakeStep.UsbBootstrapHandshake => HandshakeStep.UsbBootstrapBaudRate,
        HandshakeStep.UsbBootstrapBaudRate => HandshakeStep.UsbBootstrapHandshakeAgain,
        HandshakeStep.UsbBootstrapHandshakeAgain => HandshakeStep.UsbBootstrapDisableTimeout,
        HandshakeStep.UsbBootstrapDisableTimeout => HandshakeStep.DeviceInfo,
        HandshakeStep.DeviceInfo => HandshakeStep.ReportMode,
        HandshakeStep.ReportMode => HandshakeStep.EnableImu,
        HandshakeStep.EnableImu => HandshakeStep.ImuSensitivity,
        HandshakeStep.ImuSensitivity => HandshakeStep.EnableVibration,
        HandshakeStep.EnableVibration => HandshakeStep.PlayerLights,
        HandshakeStep.PlayerLights => HandshakeStep.Calibration,
        HandshakeStep.Calibration => HandshakeStep.ImuCalibration,
        HandshakeStep.ImuCalibration => HandshakeStep.Complete,
        _ => HandshakeStep.Complete,
    };

    private byte? CurrentStepSubcommand() => _step switch
    {
        HandshakeStep.DeviceInfo => ProtocolConstants.SubcommandRequestDeviceInfo,
        HandshakeStep.ReportMode => ProtocolConstants.SubcommandSetInputReportMode,
        HandshakeStep.EnableImu => ProtocolConstants.SubcommandEnableImu,
        HandshakeStep.ImuSensitivity => ProtocolConstants.SubcommandSetImuSensitivity,
        HandshakeStep.EnableVibration => ProtocolConstants.SubcommandEnableVibration,
        HandshakeStep.PlayerLights => ProtocolConstants.SubcommandSetPlayerLights,
        HandshakeStep.Calibration => ProtocolConstants.SubcommandSpiFlashRead,
        HandshakeStep.ImuCalibration => ProtocolConstants.SubcommandSpiFlashRead,
        _ => null,
    };

    private static bool IsUsbBootstrapStep(HandshakeStep step) =>
        step is >= HandshakeStep.UsbBootstrapConnectionStatus and <= HandshakeStep.UsbBootstrapDisableTimeout;

    /// <summary>
    /// Attempts allowed for the current step.
    /// </summary>
    /// <remarks>
    /// Optional steps get a single attempt: waiting three timeouts each for lights, IMU, vibration
    /// and two flash reads would add seconds to every connection for no benefit, since a controller
    /// that ignores them will ignore the retries too.
    /// </remarks>
    private int CurrentStepAttempts => IsEssentialStep(_step) ? _options.HandshakeAttempts : 1;

    /// <summary>
    /// Steps without which the link is useless. Everything else (lights, IMU, vibration) is a nice
    /// to have and must not stop the controller from working.
    /// </summary>
    private static bool IsEssentialStep(HandshakeStep step) =>
        step is HandshakeStep.DeviceInfo or HandshakeStep.ReportMode;

    private void PumpOutput()
    {
        var now = _timeProvider.GetTimestamp();
        var sinceLastWrite = _lastWriteAt == 0
            ? TimeSpan.MaxValue
            : _timeProvider.GetElapsedTime(_lastWriteAt, now);

        byte[]? pending = null;
        lock (_pendingSubcommands)
        {
            if (_pendingSubcommands.Count > 0 && _step == HandshakeStep.Complete)
            {
                pending = _pendingSubcommands.Peek();
            }
        }

        if (pending is not null)
        {
            // Respect the minimum spacing between frames; the head stays queued until it is safe.
            if (sinceLastWrite < OutputFrames.MinimumWriteInterval)
            {
                return;
            }

            lock (_pendingSubcommands)
            {
                _pendingSubcommands.Dequeue();
            }

            TryWriteFrame(pending[0], pending.AsSpan(1));
            return;
        }

        if (_step != HandshakeStep.Complete)
        {
            return;
        }

        UpdateRumbleEncoding();

        var rumbleChanged = !_lastSentRumble.AsSpan(0, 4).SequenceEqual(_rumbleLeft)
            || !_lastSentRumble.AsSpan(4, 4).SequenceEqual(_rumbleRight);
        var keepAliveDue = !RumbleEncoder.IsNeutral(_rumbleLeft, _rumbleRight)
            && sinceLastWrite >= TimeSpan.FromMilliseconds(1000.0 / ProtocolConstants.MaxRumbleWriteRateHz);

        if ((rumbleChanged || keepAliveDue) && sinceLastWrite >= OutputFrames.MinimumWriteInterval)
        {
            TryWriteFrame(subcommand: null, default);
        }
    }

    private void UpdateRumbleEncoding()
    {
        RumbleEncoder.EncodeBoth(
            _requestedRumbleLarge,
            _requestedRumbleSmall,
            RumbleStrength,
            _rumbleLeft,
            _rumbleRight);
    }

    private void WriteUsbBootstrap(byte usbCommand)
    {
        Span<byte> frame = stackalloc byte[2];
        var length = OutputFrames.BuildUsbBootstrap(frame, usbCommand);
        WriteFrame(frame[..length]);
    }

    private void TryWriteFrame(byte? subcommand, ReadOnlySpan<byte> arguments)
    {
        try
        {
            WriteFrameCore(subcommand, arguments);
        }
        catch (ControllerLinkException ex)
        {
            _logger.LogWarning(ex, "Writing to {Key} failed", DeviceKey);
            throw;
        }
    }

    private void WriteFrameCore(byte? subcommand, ReadOnlySpan<byte> arguments)
    {
        var length = OutputFrames.BuildSubcommand(
            _frameBuffer,
            _packetCounter,
            _rumbleLeft,
            _rumbleRight,
            subcommand,
            arguments);

        _packetCounter = (byte)((_packetCounter + 1) & 0x0F);
        WriteFrame(_frameBuffer.AsSpan(0, length));
        _lastSentRumble[0] = _rumbleLeft[0];
        _lastSentRumble[1] = _rumbleLeft[1];
        _lastSentRumble[2] = _rumbleLeft[2];
        _lastSentRumble[3] = _rumbleLeft[3];
        _lastSentRumble[4] = _rumbleRight[0];
        _lastSentRumble[5] = _rumbleRight[1];
        _lastSentRumble[6] = _rumbleRight[2];
        _lastSentRumble[7] = _rumbleRight[3];
    }

    private void WriteFrame(ReadOnlySpan<byte> frame)
    {
        _transport.Write(frame);
        _lastWriteAt = _timeProvider.GetTimestamp();
        Statistics.RecordWrite();
    }

    private void SuperviseLink()
    {
        if (_step != HandshakeStep.Complete || State is ControllerLinkState.Failed or ControllerLinkState.Stopped)
        {
            return;
        }

        var now = _timeProvider.GetTimestamp();
        var sinceLastReport = _lastReportAt == 0 ? TimeSpan.Zero : _timeProvider.GetElapsedTime(_lastReportAt, now);

        if (sinceLastReport < _options.StaleAfter)
        {
            return;
        }

        if (sinceLastReport >= _options.AbandonAfter)
        {
            _logger.LogWarning("Controller {Key} stopped reporting entirely", DeviceKey);
            SetState(ControllerLinkState.Failed);
            return;
        }

        if (State != ControllerLinkState.Stale)
        {
            Statistics.RecordRehandshake();
            _logger.LogInformation("Controller {Key} went quiet; re-running the handshake", DeviceKey);
            SetState(ControllerLinkState.Stale);

            // A controller that dropped off usually comes back with its report mode reset, so the
            // handshake is replayed from the report mode step onwards.
            _rehandshakePending = true;
        }

        if (_rehandshakePending)
        {
            _rehandshakePending = false;
            _step = HandshakeStep.ReportMode;
            _stepSent = false;
            _stepAcknowledged = false;
            _stepAttempts = 0;
        }
    }

    private void SetState(ControllerLinkState state)
    {
        if (State == state)
        {
            return;
        }

        State = state;
        LinkStateChanged?.Invoke(this);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _transport.Dispose();
    }

    private enum HandshakeStep
    {
        UsbBootstrapConnectionStatus,
        UsbBootstrapHandshake,
        UsbBootstrapBaudRate,
        UsbBootstrapHandshakeAgain,
        UsbBootstrapDisableTimeout,
        DeviceInfo,
        ReportMode,
        EnableImu,
        ImuSensitivity,
        EnableVibration,
        PlayerLights,
        Calibration,
        ImuCalibration,
        Complete,
    }
}
