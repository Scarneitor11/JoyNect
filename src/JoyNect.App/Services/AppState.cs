using System.Windows;
using JoyNect.Core.Devices;
using JoyNect.Core.Diagnostics;
using JoyNect.Core.Hid;
using JoyNect.Core.Mapping;
using JoyNect.Core.Motion;
using JoyNect.Core.Output;
using JoyNect.Core.Protocol;
using JoyNect.Core.Settings;
using JoyNect.Ui.Theme;
using Microsoft.Extensions.Logging;

namespace JoyNect.App.Services;

/// <summary>
/// The running application: settings, the virtual pad backend and the controller manager.
/// </summary>
/// <remarks>
/// This is the only place that knows how those three fit together. Every save goes through
/// <see cref="Save"/>, which decides whether a change can be applied to the live objects or needs the
/// manager to be rebuilt — reconnecting controllers on a colour change would be a bug, and silently
/// ignoring a change to how many pads may exist would be another.
/// </remarks>
public sealed class AppState : IAppState
{
    private readonly SettingsStore _store;
    private readonly ILogger<AppState> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly Dictionary<SwitchButtons, Xbox360Target> _remaps = [];

    private AppSettings _settings;
    private IVirtualPadBackend _backend;
    private ControllerManager _manager;
    private readonly MotionServerHost _motionServer;
    private MappingProfile _template;
    private bool _paused;
    private bool _disposed;

    public AppState(SettingsStore store, ILoggerFactory loggerFactory)
    {
        _store = store;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<AppState>();
        _settings = store.Load();

        M3ThemeManager.Initialize(ToThemeSettings(_settings));
        LoadRemaps();
        _template = CreateTemplate();

        (_backend, _manager) = BuildPipeline(_settings);

        _motionServer = new MotionServerHost(
            new DsuSlotSource(() => _manager.Sessions),
            loggerFactory);

        var motionStatus = _motionServer.Apply(_settings.MotionServerEnabled, _settings.MotionPort);
        if (motionStatus.Error is not null)
        {
            _logger.LogWarning("The DSU motion server did not start: {Error}", motionStatus.Error);
        }
    }

    public AppSettings Settings => _settings;

    public DriverStatus Driver { get; private set; } = new()
    {
        Available = false,
        Summary = "Not checked yet",
    };

    public bool IsRunning => !_paused;

    public MappingProfile Mapping => _template;

    public string SettingsPath => _store.FilePath;

    public IReadOnlyList<ControllerSnapshot> Controllers
    {
        get
        {
            var sessions = _manager.Sessions;
            var snapshots = new List<ControllerSnapshot>(sessions.Count);

            foreach (var session in sessions)
            {
                var hasState = session.TryGetLatest(out var latest);
                var motion = hasState && HasMotion(session)
                    ? MotionPipeline.Convert(latest.Sample2, session.ImuCalibration, 0)
                    : MotionSample.Zero;

                snapshots.Add(new ControllerSnapshot
                {
                    Id = session.DeviceKey,
                    Model = session.Model.DisplayName,
                    SupportNote = session.Model.Note,
                    Support = session.Model.Support,
                    Transport = session.TransportDescription,
                    Connection = session.Connection,
                    MacAddress = session.Info?.MacAddressText ?? string.Empty,
                    ProductId = session.ProductId,
                    State = session.State,
                    Slot = session.Slot,
                    Battery = hasState ? latest.Battery : BatteryLevel.Unknown,
                    Charging = hasState && latest.Charging,
                    ReportsPerSecond = session.Statistics.ReportsPerSecond,
                    Statistics = session.Statistics.DescribeSummary(),
                    CalibrationLoaded = session.CalibrationLoaded,
                    HasMotion = HasMotion(session),
                    Motion = motion,
                    Buttons = hasState ? latest.Buttons : SwitchButtons.None,
                    LeftStick = hasState ? latest.LeftStick : new StickRaw(StickRaw.Center, StickRaw.Center),
                    RightStick = hasState ? latest.RightStick : new StickRaw(StickRaw.Center, StickRaw.Center),
                });
            }

            return snapshots;
        }
    }

    public MotionServerSnapshot MotionServer => _motionServer.Status;

    public IReadOnlyList<PadSnapshot> Pads
    {
        get
        {
            var pads = _manager.Pads;
            var snapshots = new List<PadSnapshot>(pads.Count);

            foreach (var pad in pads.OrderBy(p => p.Slot))
            {
                snapshots.Add(new PadSnapshot
                {
                    Slot = pad.Slot,
                    Description = pad.Description,
                    State = pad.LastState,
                    SubmitCount = pad.SubmitCount,
                    Controllers = pad.Sources.Select(s => s.Model.DisplayName).ToArray(),
                    Profile = pad.Profile,
                });
            }

            return snapshots;
        }
    }

    public event Action? Changed;

    public event Action<string, bool>? Notified;

    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var normalised = settings.Normalise();
        var previous = _settings;
        var needsNewPipeline = normalised.CreateVirtualPads != previous.CreateVirtualPads
            || normalised.MaxPads != previous.MaxPads
            || normalised.JoyConMode != previous.JoyConMode;
        var motionChanged = normalised.MotionServerEnabled != previous.MotionServerEnabled
            || normalised.MotionPort != previous.MotionPort;
        var retryMotion = normalised.MotionServerEnabled && !MotionServer.Running;

        _settings = normalised;
        _store.Save(normalised);

        ApplyAppearance(previous, normalised);
        ApplyProfileChanges(normalised);

        if (normalised.StartWithWindows != previous.StartWithWindows)
        {
            StartupRegistration.Set(normalised.StartWithWindows, _logger);
        }

        if ((motionChanged || retryMotion) && !_disposed)
        {
            var status = _motionServer.Apply(normalised.MotionServerEnabled, normalised.MotionPort);

            if (status.Error is not null)
            {
                Notified?.Invoke(status.Error, true);
            }
            else if (normalised.MotionServerEnabled)
            {
                Notified?.Invoke($"Motion server listening on {status.Endpoint}", false);
            }
            else if (previous.MotionServerEnabled)
            {
                Notified?.Invoke("Motion server stopped", false);
            }
        }

        if (needsNewPipeline && !_disposed)
        {
            DisposePipeline();
            (_backend, _manager) = BuildPipeline(normalised);
            Notified?.Invoke("Reconnecting controllers to apply the change", false);
        }

        Changed?.Invoke();
    }

    public void Notify(string message, bool error = false) => Notified?.Invoke(message, error);

    public void SetRunning(bool running)
    {
        if (_paused == !running)
        {
            return;
        }

        _paused = !running;
        if (running)
        {
            _manager.Start();
        }
        else
        {
            _manager.Stop();
        }

        Notified?.Invoke(running ? "Connecting to controllers" : "Paused: controllers are not being read", false);
        Changed?.Invoke();
    }

    public void Rescan()
    {
        if (_paused)
        {
            SetRunning(true);
            return;
        }

        try
        {
            _manager.DiscoverOnce();
            _manager.ReapDeadLinks();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "A manual scan failed");
            Notified?.Invoke("The scan failed; see Diagnostics for details", true);
        }

        Changed?.Invoke();
    }

    public void TestRumble(byte large, byte small)
    {
        var sessions = _manager.Sessions.Where(s => s.State == ControllerLinkState.Ready).ToArray();
        if (sessions.Length == 0)
        {
            Notified?.Invoke("No controller is ready to rumble", true);
            return;
        }

        foreach (var session in sessions)
        {
            session.SetRumble(large, small);
        }

        Notified?.Invoke($"Rumble {large} / {small} sent to {sessions.Length} controller{(sessions.Length == 1 ? string.Empty : "s")}", false);
    }

    public void SetSlot(string controllerId, int slot)
    {
        var session = _manager.Sessions.FirstOrDefault(s =>
            string.Equals(s.DeviceKey, controllerId, StringComparison.OrdinalIgnoreCase));
        if (session is null)
        {
            return;
        }

        session.SetSlot(slot);
        Notified?.Invoke($"{session.Model.DisplayName} lights set to player {slot}", false);
        Changed?.Invoke();
    }

    public void Remap(SwitchButtons source, Xbox360Target? target)
    {
        var value = target ?? Xbox360Target.None;
        if (value == Xbox360Target.None)
        {
            _remaps.Remove(source);
            _settings.Remaps.Remove(source.ToString());
        }
        else
        {
            _remaps[source] = value;
            _settings.Remaps[source.ToString()] = value.ToString();
        }

        foreach (var pad in _manager.Pads)
        {
            pad.Profile.Remap(source, value);
        }

        _template.Remap(source, value);
        _store.Save(_settings);
        Changed?.Invoke();
    }

    public void SetStickSettings(bool right, StickSettings settings)
    {
        if (right)
        {
            _settings.RightDeadzone = settings.Deadzone;
            _settings.RightCurve = settings.Curve;
            _settings.RightInvertX = settings.InvertX;
            _settings.RightInvertY = settings.InvertY;
        }
        else
        {
            _settings.LeftDeadzone = settings.Deadzone;
            _settings.LeftCurve = settings.Curve;
            _settings.LeftInvertX = settings.InvertX;
            _settings.LeftInvertY = settings.InvertY;
        }

        ApplyProfileChanges(_settings);
        _store.Save(_settings);
        Changed?.Invoke();
    }

    public void SetProfileRumble(bool enabled, double strength)
    {
        _settings.RumbleEnabled = enabled;
        _settings.RumbleStrength = Math.Clamp(strength, 0.0, 1.0);
        ApplyProfileChanges(_settings);
        _store.Save(_settings);
        Changed?.Invoke();
    }

    public void ResetMapping()
    {
        _remaps.Clear();
        _settings.Remaps.Clear();

        // Restore every connected pad to its model-specific default button layout, then re-apply
        // (now empty) remaps. Without this, connected pads kept their custom mappings after a reset.
        foreach (var pad in _manager.Pads)
        {
            var model = pad.Sources.FirstOrDefault()?.Model.Model ?? SwitchModel.ProController;
            var defaults = MappingProfile.CreateDefault(model, _settings.JoyConMode).Buttons;
            pad.Profile.Buttons.Clear();
            foreach (var kv in defaults)
            {
                pad.Profile.Buttons[kv.Key] = kv.Value;
            }
        }

        _template = CreateTemplate();
        _store.Save(_settings);
        Notified?.Invoke("Button mapping restored to the default layout", false);
        Changed?.Invoke();
    }

    public Task<string> BuildDiagnosticReportAsync() => Task.Run(() =>
    {
        var motion = MotionServer;
        var notes = new List<string>
        {
            $"settings file: {_store.FilePath}",
            $"controller app running: {IsRunning}",
            $"virtual pads created: {Pads.Count} of {_settings.MaxPads}",
            $"DSU motion server: {(motion.Running ? $"listening on {motion.Endpoint}" : motion.Enabled ? motion.Error ?? "not running" : "disabled")}",
        };

        foreach (var session in _manager.Sessions)
        {
            notes.Add($"{session.Model.DisplayName}: {session.Statistics.DescribeSummary()}");
        }

        foreach (var pad in _manager.Pads)
        {
            notes.Add($"pad {pad.Slot} ({pad.Description}): {pad.SubmitCount} submissions, sources {string.Join(", ", pad.Sources.Select(s => s.Model.DisplayName))}");
        }

        return DiagnosticReport.Collect(driver: Driver, notes: notes).ToText();
    });

    public Task<IReadOnlyList<HidCandidate>> EnumerateHidAsync() => Task.Run<IReadOnlyList<HidCandidate>>(() =>
        new DeviceEnumerator(_loggerFactory.CreateLogger<DeviceEnumerator>()).EnumerateCandidates());

    public void RefreshDriver()
    {
        var status = DriverProbe.Probe(_logger);
        var availabilityChanged = status.Available != Driver.Available;
        Driver = status;

        if (availabilityChanged && !_disposed)
        {
            DisposePipeline();
            (_backend, _manager) = BuildPipeline(_settings);
        }

        Changed?.Invoke();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _motionServer.Dispose();
        DisposePipeline();
    }

    private void DisposePipeline()
    {
        try
        {
            _manager.Changed -= OnManagerChanged;
            _manager.LinkStateChanged -= OnLinkStateChanged;
            _manager.PadCreationFailed -= OnPadCreationFailed;
            _manager.Stop();
            _manager.Dispose();
            _backend.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Shutting down the controller pipeline failed");
        }
    }

    private (IVirtualPadBackend Backend, ControllerManager Manager) BuildPipeline(AppSettings settings)
    {
        var backend = new ViGEmBackend(_loggerFactory.CreateLogger<ViGEmBackend>());
        Driver = backend.Status;

        var options = new ControllerManagerOptions
        {
            MaxControllers = settings.MaxPads,
            CreateVirtualPads = settings.CreateVirtualPads && Driver.Available,
            JoyConMode = settings.JoyConMode,
            ProfileFactory = session => CreateProfile(session.Model.Model),
        };

        var manager = new ControllerManager(
            backend,
            options,
            _loggerFactory.CreateLogger<ControllerManager>());

        manager.Changed += OnManagerChanged;
        manager.LinkStateChanged += OnLinkStateChanged;
        manager.PadCreationFailed += OnPadCreationFailed;

        if (!_paused)
        {
            manager.Start();
        }

        return (backend, manager);
    }

    private void OnManagerChanged() => Changed?.Invoke();

    private void OnPadCreationFailed(SwitchControllerSession session, string error)
    {
        _logger.LogWarning("Virtual pad creation failed for {Controller}: {Error}", session.Model.DisplayName, error);
        Notified?.Invoke($"Could not create a virtual pad: {error}", true);
    }

    private void OnLinkStateChanged(SwitchControllerSession session, ControllerLinkState previous, ControllerLinkState current)
    {
        if (!_settings.ShowConnectionNotifications)
        {
            return;
        }

        if (current == ControllerLinkState.Ready && previous != ControllerLinkState.Ready)
        {
            Notified?.Invoke($"{session.Model.DisplayName} connected", false);
        }
        else if (current is ControllerLinkState.Stopped or ControllerLinkState.Failed
            && previous is ControllerLinkState.Ready or ControllerLinkState.Handshaking)
        {
            Notified?.Invoke($"{session.Model.DisplayName} disconnected", true);
        }
    }

    private MappingProfile CreateProfile(SwitchModel model)
    {
        var profile = MappingProfile.CreateDefault(model, _settings.JoyConMode);
        ApplyProfileChanges(profile, _settings);
        RefreshButtons(profile);
        return profile;
    }

    private MappingProfile CreateTemplate() => CreateProfile(SwitchModel.ProController);

    private void ApplyProfileChanges(AppSettings settings)
    {
        foreach (var pad in _manager.Pads)
        {
            ApplyProfileChanges(pad.Profile, settings);
        }

        ApplyProfileChanges(_template, settings);

        foreach (var session in _manager.Sessions)
        {
            session.RumbleStrength = settings.RumbleEnabled ? settings.RumbleStrength : 0;
        }
    }

    private static void ApplyProfileChanges(MappingProfile profile, AppSettings settings)
    {
        profile.LeftStick = new StickSettings
        {
            Deadzone = settings.LeftDeadzone,
            Curve = settings.LeftCurve,
            InvertX = settings.LeftInvertX,
            InvertY = settings.LeftInvertY,
        };

        profile.RightStick = new StickSettings
        {
            Deadzone = settings.RightDeadzone,
            Curve = settings.RightCurve,
            InvertX = settings.RightInvertX,
            InvertY = settings.RightInvertY,
        };

        profile.RumbleEnabled = settings.RumbleEnabled;
        profile.RumbleStrength = settings.RumbleStrength;
        profile.JoyConMode = settings.JoyConMode;
    }

    private void RefreshButtons(MappingProfile profile)
    {
        foreach (var (source, target) in _remaps)
        {
            profile.Remap(source, target);
        }
    }

    private void LoadRemaps()
    {
        _remaps.Clear();

        foreach (var (sourceName, targetName) in _settings.Remaps)
        {
            if (!Enum.TryParse<SwitchButtons>(sourceName, out var source))
            {
                continue;
            }

            if (!Enum.TryParse<Xbox360Target>(targetName, out var target))
            {
                continue;
            }

            _remaps[source] = target;
        }
    }

    private static bool HasMotion(SwitchControllerSession session) =>
        !SwitchModels.IsNsoRetro(session.Model.Model);

    private static ThemeSettings ToThemeSettings(AppSettings settings) => new()
    {
        Mode = settings.Appearance switch
        {
            AppearanceMode.Light => ThemeMode.Light,
            AppearanceMode.Dark => ThemeMode.Dark,
            _ => ThemeMode.System,
        },
        Seed = settings.SeedColour,
        FollowWallpaper = settings.FollowWallpaper,
        Variant = settings.Variant,
        Contrast = settings.Contrast,
    };

    private static void ApplyAppearance(AppSettings previous, AppSettings current)
    {
        var before = ToThemeSettings(previous);
        var after = ToThemeSettings(current);

        if (before == after)
        {
            return;
        }

        if (Application.Current?.Dispatcher is { } dispatcher && !dispatcher.CheckAccess())
        {
            dispatcher.Invoke(() => M3ThemeManager.Apply(after));
            return;
        }

        M3ThemeManager.Apply(after);
    }
}
