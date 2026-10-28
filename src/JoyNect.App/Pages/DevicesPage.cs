using System.Windows;
using System.Windows.Controls;
using JoyNect.App.Services;
using JoyNect.Core.Devices;
using JoyNect.Core.Hid;
using JoyNect.Core.Mapping;
using JoyNect.Ui.Controls;
using JoyNect.Ui.Theme;

namespace JoyNect.App.Pages;

/// <summary>
/// Every controller JoyNect can see, with the details that matter when something is wrong.
/// </summary>
/// <remarks>
/// The diagnostic details live here rather than being hidden behind a log: a controller that connects
/// but does not work is almost always a wrong HID collection, a stale pairing or a flat battery, and
/// those are all visible in this list.
/// </remarks>
internal sealed class DevicesPage : AppPage
{
    private string _signature = string.Empty;

    public DevicesPage()
        : base("devices", "Devices", "What is connected, how it is linked, and which player it is.", M3Glyph.Bluetooth)
    {
    }

    protected override void Build()
    {
        var scan = new M3Button("Scan now", M3ButtonVariant.Tonal, M3Glyph.Refresh);
        scan.Click += (_, _) => State.Rescan();

        Body.Children.Add(Hero(null, scan));

        if (!State.Driver.Available)
        {
            Body.Children.Add(new Border { Height = 18 });
            Body.Children.Add(Widgets.Banner(
                "No virtual pad driver",
                "Controllers can be read and tested without ViGEmBus, but no game will see a pad until it is installed.",
                M3Glyph.Warning,
                Widgets.LinkButton("Get ViGEmBus", JoyNect.Core.Output.DriverProbe.OfficialInstallerUri)));
        }

        Body.Children.Add(new Border { Height = 24 });

        var controllers = State.Controllers;
        if (controllers.Count == 0)
        {
            Body.Children.Add(Widgets.Empty(
                M3Glyph.Search,
                "No controllers found",
                "Hold SYNC on the controller for two seconds, then pair it in Windows. Over USB it appears within a couple of seconds. JoyNect looks again every two seconds.",
                Widgets.LinkButton("Open Windows Bluetooth", "ms-settings:bluetooth")));
        }
        else
        {
            foreach (var controller in controllers)
            {
                Body.Children.Add(BuildDeviceCard(controller));
                Body.Children.Add(new Border { Height = 16 });
            }
        }

        Body.Children.Add(new Border { Height = 8 });
        Body.Children.Add(M3Layout.Section(
            "Joy-Con layout",
            "Two Joy-Con can act as separate sideways pads, or as one pad with both sticks.",
            BuildJoyConModeCard(),
            16));

        Body.Children.Add(new Border { Height = 24 });
        Body.Children.Add(M3Layout.Section(
            "Virtual pads",
            "A pad is created for each controller that connects, up to the XInput limit of four.",
            BuildPadSettingsCard(),
            16));
    }

    public override void Refresh()
    {
        var signature = string.Join(
            ';',
            State.Controllers.Select(c =>
                $"{c.Id}:{c.State}:{c.Slot}:{c.Battery}:{c.CalibrationLoaded}:{(c.ReportsPerSecond is null ? 0 : (int)c.ReportsPerSecond)}"));

        signature += $"|{State.Settings.JoyConMode}|{State.Settings.CreateVirtualPads}|{State.Settings.MaxPads}|{State.Driver.Available}";

        if (signature == _signature)
        {
            return;
        }

        _signature = signature;
        RebuildPage();
    }

    private UIElement BuildDeviceCard(ControllerSnapshot controller)
    {
        var theme = M3ThemeManager.Current;

        var icon = new Border
        {
            Width = 52,
            Height = 52,
            CornerRadius = new CornerRadius(16),
            Background = theme.TertiaryContainer,
            Child = M3Icons.Create(M3Glyph.Gamepad, 26, theme.OnTertiaryContainer),
        };

        var name = M3Typography.Create(controller.Model, M3Typography.TitleLarge);
        var identity = M3Typography.Create(
            string.IsNullOrEmpty(controller.MacAddress) ? "Reading identity…" : controller.MacAddress,
            M3Typography.BodySmall,
            JoyNect.Core.Design.ColorRole.OnSurfaceVariant);

        var titleBlock = M3Layout.Column(2, name, identity);
        titleBlock.VerticalAlignment = VerticalAlignment.Center;

        var pills = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        pills.Children.Add(Widgets.StatePill(controller));
        pills.Children.Add(new Border { Width = 8 });
        pills.Children.Add(Widgets.Pill(
            $"Player {controller.Slot}",
            JoyNect.Core.Design.ColorRole.SecondaryContainer,
            JoyNect.Core.Design.ColorRole.OnSecondaryContainer,
            M3Glyph.Person));

        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(icon);

        var middle = M3Layout.Row(14, titleBlock);
        middle.Margin = new Thickness(14, 0, 12, 0);
        middle.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(middle, 1);
        header.Children.Add(middle);

        Grid.SetColumn(pills, 2);
        header.Children.Add(pills);

        var details = new Grid { Margin = new Thickness(0, 18, 0, 0) };
        details.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        details.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
        details.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var left = new StackPanel();
        left.Children.Add(Widgets.Detail("Connection", $"{Widgets.ConnectionLabel(controller)} · {controller.Transport}"));
        left.Children.Add(Widgets.Detail("Report rate", controller.ReportsPerSecond is { } rate ? $"{rate:0} per second" : "waiting for reports"));
        left.Children.Add(Widgets.Detail("Stick calibration", controller.CalibrationLoaded ? "read from the controller" : "using factory defaults"));

        var right = new StackPanel();
        right.Children.Add(Widgets.Detail("Product id", $"0x{controller.ProductId:X4}"));
        right.Children.Add(Widgets.Detail("Motion", controller.HasMotion ? "gyroscope and accelerometer" : "not available on this controller"));
        right.Children.Add(Widgets.Detail("Support", controller.Support switch
        {
            SupportLevel.Verified => "verified against hardware",
            SupportLevel.Unverified => "implemented, not verified",
            _ => "best effort",
        }));
        right.Children.Add(Widgets.Detail("Statistics", controller.Statistics, wrap: true));

        details.Children.Add(left);
        Grid.SetColumn(right, 2);
        details.Children.Add(right);

        var body = new StackPanel();
        body.Children.Add(header);

        if (controller.Support != SupportLevel.Verified)
        {
            var banner = Widgets.Banner("Support note", controller.SupportNote, M3Glyph.Info, action: null, error: false);
            banner.Margin = new Thickness(0, 18, 0, 0);
            body.Children.Add(banner);
        }

        body.Children.Add(details);

        if (controller.Support == SupportLevel.Verified && !string.IsNullOrWhiteSpace(controller.SupportNote))
        {
            var note = M3Typography.Create(controller.SupportNote, M3Typography.BodySmall, JoyNect.Core.Design.ColorRole.OnSurfaceVariant);
            note.MaxWidth = 760;
            note.HorizontalAlignment = HorizontalAlignment.Left;
            note.Margin = new Thickness(0, 6, 0, 0);
            body.Children.Add(note);
        }

        var battery = Widgets.Battery(controller);
        battery.Margin = new Thickness(0, 14, 0, 0);
        body.Children.Add(battery);

        body.Children.Add(new Border
        {
            Height = 1,
            Background = theme.OutlineVariant,
            Margin = new Thickness(0, 18, 0, 14),
        });

        var lights = new M3SegmentedButtons { Width = 240, HorizontalAlignment = HorizontalAlignment.Left };
        lights.SetSegments("1", "2", "3", "4");
        lights.SelectedIndex = Math.Clamp(controller.Slot - 1, 0, 3);
        lights.SelectionChanged += (_, _) =>
        {
            if (lights.SelectedIndex >= 0)
            {
                State.SetSlot(controller.Id, lights.SelectedIndex + 1);
            }
        };

        var lightsRow = new Grid();
        lightsRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        lightsRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var lightsText = M3Layout.Column(
            2,
            M3Typography.Create("Player light", M3Typography.LabelLarge),
            M3Typography.Create("The four lights on the controller match the pad's player number.", M3Typography.BodySmall, JoyNect.Core.Design.ColorRole.OnSurfaceVariant));

        lightsRow.Children.Add(lightsText);
        lights.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(lights, 1);
        lightsRow.Children.Add(lights);

        body.Children.Add(lightsRow);

        return new M3Card(M3CardVariant.Filled) { Child = body };
    }

    private UIElement BuildJoyConModeCard()
    {
        var segmented = new M3SegmentedButtons { Width = 320 };
        segmented.SetSegments("Sideways", "Combined");
        segmented.SelectedIndex = State.Settings.JoyConMode == JoyConMode.Combined ? 1 : 0;

        var description = Widgets.Paragraph(
            segmented.SelectedIndex == 1
                ? "A left and a right Joy-Con become one pad: both sticks, the full button set and motion."
                : "Each Joy-Con is a pad of its own, held sideways. Good for two players with two Joy-Con.",
            M3Typography.BodyMedium);

        segmented.SelectionChanged += (_, _) =>
        {
            var mode = segmented.SelectedIndex == 1 ? JoyConMode.Combined : JoyConMode.Sideways;
            if (mode == State.Settings.JoyConMode)
            {
                return;
            }

            var settings = State.Settings.Clone();
            settings.JoyConMode = mode;
            State.Save(settings);
            description.Text = segmented.SelectedIndex == 1
                ? "A left and a right Joy-Con become one pad: both sticks, the full button set and motion."
                : "Each Joy-Con is a pad of its own, held sideways. Good for two players with two Joy-Con.";
            State.Notify("Joy-Con layout changed; controllers reconnect");
        };

        var content = M3Layout.Column(14, segmented, description);

        var note = Widgets.Paragraph(
            "Changing this reconnects controllers, so do it before starting a game.",
            M3Typography.BodySmall);
        note.Margin = new Thickness(0, 10, 0, 0);
        content.Children.Add(note);

        return new M3Card(M3CardVariant.Filled) { Child = content };
    }

    private UIElement BuildPadSettingsCard()
    {
        var create = new M3Switch { IsSelected = State.Settings.CreateVirtualPads };
        create.AccessibilityName = "Create virtual pads";
        create.Click += (_, _) =>
        {
            var settings = State.Settings.Clone();
            settings.CreateVirtualPads = create.IsSelected;
            State.Save(settings);
        };

        var padsCard = M3Layout.SettingCard(
            "Create virtual pads",
            "Off means controllers are read and can be inspected, but no game sees a pad.",
            create);

        var limit = new M3SegmentedButtons { Width = 220 };
        limit.SetSegments("1", "2", "3", "4");
        limit.SelectedIndex = Math.Clamp(State.Settings.MaxPads - 1, 0, 3);
        limit.SelectionChanged += (_, _) =>
        {
            var settings = State.Settings.Clone();
            settings.MaxPads = limit.SelectedIndex + 1;
            State.Save(settings);
        };

        var limitCard = M3Layout.SettingCard(
            "Maximum pads",
            "XInput exposes four pads; more controllers can connect than can play at once.",
            limit);

        return M3Layout.Column(12, padsCard, limitCard);
    }

}
