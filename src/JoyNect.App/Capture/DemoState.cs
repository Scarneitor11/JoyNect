using System.Numerics;
using JoyNect.App.Services;
using JoyNect.Core.Design;
using JoyNect.Core.Devices;
using JoyNect.Core.Hid;
using JoyNect.Core.Mapping;
using JoyNect.Core.Motion;
using JoyNect.Core.Output;
using JoyNect.Core.Protocol;
using JoyNect.Core.Settings;
using JoyNect.Ui.Theme;

namespace JoyNect.App.Capture;

/// <summary>
/// A believable app state with no hardware behind it.
/// </summary>
/// <remarks>
/// Used by the screenshot renderer. It exists so the visual design can be reviewed — and regressions
/// caught — on a build machine with no Switch controller and no virtual pad driver. The numbers are
/// chosen to exercise the layouts: a full battery, a low one, a pad with a held button, a controller
/// still connecting.
/// </remarks>
internal sealed class DemoState : IAppState
{
    private readonly MappingProfile _profile;
    private int _slot = 1;

    public DemoState(bool dark)
    {
        Settings = new AppSettings
        {
            Appearance = dark ? AppearanceMode.Dark : AppearanceMode.Light,
            FollowWallpaper = false,
            SeedColour = 0xFF6750A4,
            Variant = SchemeVariant.TonalSpot,
            Contrast = ContrastLevel.Standard,
            RumbleEnabled = true,
            RumbleStrength = 0.6,
            MaxPads = 4,
            MotionServerEnabled = true,
            MotionPort = 26760,
        };

        M3ThemeManager.Initialize(new ThemeSettings
        {
            Mode = dark ? ThemeMode.Dark : ThemeMode.Light,
            Seed = Settings.SeedColour,
            FollowWallpaper = false,
            Variant = SchemeVariant.TonalSpot,
            Contrast = ContrastLevel.Standard,
        });

        _profile = MappingProfile.CreateDefault(SwitchModel.ProController, JoyConMode.Sideways);

        Pads =
        [
            new PadSnapshot
            {
                Slot = 1,
                Description = "Xbox 360 Controller (XInput)",
                State = new Xbox360PadState(
                    Xbox360Buttons.A | Xbox360Buttons.Start | Xbox360Buttons.LeftShoulder,
                    LeftThumbX: 12000,
                    LeftThumbY: -7000,
                    RightThumbX: 8000,
                    RightThumbY: 3000,
                    LeftTrigger: 0,
                    RightTrigger: 96),
                SubmitCount = 18432,
                Controllers = ["Pro Controller"],
                Profile = _profile,
            },
        ];

        Controllers =
        [
            new ControllerSnapshot
            {
                Id = "demo-pro",
                Model = "Pro Controller",
                Support = SupportLevel.Unverified,
                SupportNote = "Driven from Nintendo's documented report format. Sticks, buttons, motion and rumble are read, but this model has not been tested against every hardware revision; report anything that misbehaves.",
                Transport = "Bluetooth HID",
                Connection = ConnectionKind.Bluetooth,
                MacAddress = "98:B6:E9:41:2C:07",
                ProductId = SwitchModels.ProductIdProController,
                State = ControllerLinkState.Ready,
                Slot = 1,
                Battery = BatteryLevel.Medium,
                Charging = false,
                ReportsPerSecond = 59.8,
                Statistics = "18,433 reports received (16.7 ms median), 0 unrecognised",
                CalibrationLoaded = true,
                HasMotion = true,
                Motion = new MotionSample(new Vector3(0.04f, 0.02f, 1.0f), new Vector3(0.6f, -0.4f, 0.2f), 0),
                Buttons = SwitchButtons.A | SwitchButtons.Plus | SwitchButtons.L,
                LeftStick = new StickRaw(2450, 1620),
                RightStick = new StickRaw(2600, 1750),
            },
            new ControllerSnapshot
            {
                Id = "demo-joycon",
                Model = "Joy-Con (R)",
                Support = SupportLevel.Unverified,
                SupportNote = "A single Joy-Con is used sideways, with its rail buttons as shoulders. Implemented from the protocol documentation; not yet verified against every Joy-Con revision.",
                Transport = "Bluetooth HID",
                Connection = ConnectionKind.Bluetooth,
                MacAddress = "98:B6:E9:3A:11:F2",
                ProductId = SwitchModels.ProductIdJoyConRight,
                State = ControllerLinkState.Handshaking,
                Slot = 2,
                Battery = BatteryLevel.Low,
                ReportsPerSecond = null,
                Statistics = "handshake attempt 2 of 3",
                CalibrationLoaded = false,
                HasMotion = true,
            },
        ];
    }

    public AppSettings Settings { get; private set; }

    public DriverStatus Driver { get; } = new()
    {
        Available = true,
        Summary = "ViGEmBus 1.22.0 is installed and accepts new pads",
        TestPadCreated = true,
    };

    public bool IsRunning { get; private set; } = true;

    public MappingProfile Mapping => _profile;

    public IReadOnlyList<ControllerSnapshot> Controllers { get; private set; }

    public IReadOnlyList<PadSnapshot> Pads { get; }

    public MotionServerSnapshot MotionServer { get; private set; } = new()
    {
        Enabled = true,
        Running = true,
        Port = 26760,
    };

    public string SettingsPath => @"C:\Users\you\AppData\Roaming\JoyNect\settings.json";

    public event Action? Changed;

    public event Action<string, bool>? Notified;

    public void Save(AppSettings settings)
    {
        Settings = settings.Normalise();
        MotionServer = new MotionServerSnapshot
        {
            Enabled = Settings.MotionServerEnabled,
            Running = Settings.MotionServerEnabled,
            Port = Settings.MotionPort,
        };

        Changed?.Invoke();
    }

    /// <summary>
    /// When true, notifications are dropped.
    /// </summary>
    /// <remarks>
    /// A screenshot should never contain a message left over from a page that has already been
    /// rendered, so the demo state stays quiet by default.
    /// </remarks>
    public bool Silent { get; set; } = true;

    public void Notify(string message, bool error = false)
    {
        if (!Silent)
        {
            Notified?.Invoke(message, error);
        }
    }

    public void SetRunning(bool running)
    {
        IsRunning = running;
        Changed?.Invoke();
    }

    public void Rescan()
    {
    }

    public void TestRumble(byte large, byte small)
    {
    }

    public void SetSlot(string controllerId, int slot)
    {
        _slot = slot;
        Controllers = Controllers
            .Select(c => c.Id == controllerId ? c with { Slot = slot } : c)
            .ToArray();

        Changed?.Invoke();
    }

    public void Remap(SwitchButtons source, Xbox360Target? target) => _profile.Remap(source, target ?? Xbox360Target.None);

    public void SetStickSettings(bool right, StickSettings settings)
    {
        if (right)
        {
            _profile.RightStick = settings;
        }
        else
        {
            _profile.LeftStick = settings;
        }
    }

    public void SetProfileRumble(bool enabled, double strength)
    {
        _profile.RumbleEnabled = enabled;
        _profile.RumbleStrength = strength;
        Settings.RumbleEnabled = enabled;
        Settings.RumbleStrength = strength;
    }

    public void ResetMapping()
    {
    }

    public Task<string> BuildDiagnosticReportAsync() => Task.FromResult(
        "JoyNect diagnostic report\n(this is a demo state used by the screenshot renderer)\n");

    public Task<IReadOnlyList<HidCandidate>> EnumerateHidAsync() => Task.FromResult<IReadOnlyList<HidCandidate>>(
    [
        new()
        {
            DevicePath = @"\\?\HID#VID_057E&PID_2009&Col01#7&2b1f3c4d&0&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}",
            VendorId = 0x057E,
            ProductId = 0x2009,
            ProductName = "Pro Controller",
            Manufacturer = "Nintendo",
            Connection = ConnectionKind.Bluetooth,
            MaxInputReportLength = 362,
            MaxOutputReportLength = 49,
            InputReports = [new(0x30, 362, "full state"), new(0x21, 49, "subcommand reply")],
            PhysicalDeviceKey = "Bluetooth · 98:B6:E9:41:2C:07",
        },
        new()
        {
            DevicePath = @"\\?\HID#VID_057E&PID_2009&Col02#7&2b1f3c4d&0&0001#{4d1e55b2-f16f-11cf-88cb-001111000030}",
            VendorId = 0x057E,
            ProductId = 0x2009,
            ProductName = "Pro Controller",
            Manufacturer = "Nintendo",
            Connection = ConnectionKind.Bluetooth,
            MaxInputReportLength = 64,
            MaxOutputReportLength = 1,
            InputReports = [new(0x3F, 64, "gamepad")],
            PhysicalDeviceKey = "Bluetooth · 98:B6:E9:41:2C:07",
        },
    ]);

    public void RefreshDriver()
    {
    }

    public void Dispose()
    {
    }
}
