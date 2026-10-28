using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using JoyNect.App.Services;
using JoyNect.Core.Devices;
using JoyNect.Ui.Controls;
using JoyNect.Ui.Theme;

namespace JoyNect.App.Pages;

/// <summary>
/// The landing page: whether the app is working, what is connected, and what to do next.
/// </summary>
/// <remarks>
/// Everything here answers one question at a glance — "is my controller ready to play?". The pad
/// drawing is the honest answer: it mirrors the exact state the virtual pad is reporting, so if a
/// stick is drifting or a button is stuck, it is visible before a game is opened.
/// </remarks>
internal sealed class HomePage : AppPage
{
    private readonly DispatcherTimer _rumbleTimer;
    private XboxPadCanvas? _pad;
    private TextBlock? _padDetail;
    private string _signature = string.Empty;

    public HomePage()
        : base("home", "Home", "Your Switch controllers, played as Xbox 360 pads.", M3Glyph.Gamepad)
    {
        _rumbleTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(420) };
        _rumbleTimer.Tick += (_, _) =>
        {
            _rumbleTimer.Stop();
            State.TestRumble(0, 0);
        };
    }

    public override void OnHidden()
    {
        _rumbleTimer.Stop();
        base.OnHidden();
    }

    protected override void Build()
    {
        var heading = M3Typography.Create(Heading(), M3Typography.DisplaySmall);
        var subheading = Widgets.Paragraph(
            "Pair a controller, and JoyNect gives it a real Xbox 360 pad that every game already understands.",
            M3Typography.BodyLarge);

        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        header.Children.Add(M3Layout.Column(10, heading, subheading));

        var actions = BuildHeaderActions();
        Grid.SetColumn(actions, 1);
        header.Children.Add(actions);

        Body.Children.Add(header);

        if (!State.Driver.Available)
        {
            Body.Children.Add(new Border { Height = 20 });
            Body.Children.Add(Widgets.Banner(
                "The virtual pad driver is not available",
                State.Driver.Remediation ?? "JoyNect needs the ViGEmBus driver to create an Xbox 360 pad. Controllers can still be read without it.",
                M3Glyph.Warning,
                Widgets.LinkButton("Get ViGEmBus", JoyNect.Core.Output.DriverProbe.OfficialInstallerUri)));
        }

        Body.Children.Add(new Border { Height = 28 });
        Body.Children.Add(BuildMainColumns());
    }

    public override void Refresh()
    {
        var signature = Signature();
        if (signature != _signature)
        {
            _signature = signature;
            RebuildPage();
            return;
        }

        if (_pad is not null)
        {
            var pads = State.Pads;
            if (pads.Count > 0)
            {
                _pad.SetState(pads[0].State);
            }
            else
            {
                _pad.SetIdle();
            }

            if (_padDetail is not null)
            {
                _padDetail.Text = pads.Count == 0
                    ? "No virtual pad yet. Connect a controller to create one."
                    : $"{pads[0].Description} · {pads[0].SubmitCount:N0} reports sent";
            }
        }
    }

    private UIElement BuildHeaderActions()
    {
        var scan = new M3Button("Scan", M3ButtonVariant.Tonal, M3Glyph.Refresh);
        scan.Click += (_, _) => State.Rescan();
        ToolTipService.SetToolTip(scan, "Look for controllers now (Ctrl+R)");

        var pause = new M3Button(State.IsRunning ? "Pause" : "Resume", M3ButtonVariant.Text, M3Glyph.Power);
        pause.Click += (_, _) =>
        {
            State.SetRunning(!State.IsRunning);
            RebuildPage();
        };

        var actions = M3Layout.Row(8, pause, scan);
        actions.VerticalAlignment = VerticalAlignment.Center;
        return actions;
    }

    private string Heading()
    {
        if (!State.Driver.Available)
        {
            return "Almost there";
        }

        if (State.Pads.Count > 0)
        {
            return "Ready to play";
        }

        return State.Controllers.Count > 0 ? "Connecting" : "Waiting for a controller";
    }

    private UIElement BuildMainColumns()
    {
        var top = new Grid();
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(340) });

        top.Children.Add(BuildPadCard());

        var right = M3Layout.Column(16, BuildGlanceCard(), BuildPairingHintCard());
        right.VerticalAlignment = VerticalAlignment.Top;
        Grid.SetColumn(right, 2);
        top.Children.Add(right);

        return M3Layout.Column(24, top, BuildControllersCard());
    }

    private UIElement BuildPadCard()
    {
        var pads = State.Pads;
        var pad = pads.Count > 0 ? pads[0] : null;

        _pad = new XboxPadCanvas { MinHeight = 340, VerticalAlignment = VerticalAlignment.Stretch };

        if (pad is not null)
        {
            _pad.SetState(pad.State);
        }
        else
        {
            _pad.SetIdle();
        }

        var title = M3Typography.Create(
            pad is null ? "Virtual pad" : $"Virtual pad · player {pad.Slot}",
            M3Typography.TitleLarge);

        var pill = pad is null
            ? Widgets.Pill("Not created", JoyNect.Core.Design.ColorRole.SurfaceContainerHighest, JoyNect.Core.Design.ColorRole.OnSurfaceVariant)
            : Widgets.Pill("Live", JoyNect.Core.Design.ColorRole.SecondaryContainer, JoyNect.Core.Design.ColorRole.OnSecondaryContainer, M3Glyph.Pulse);

        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(title);
        Grid.SetColumn(pill, 1);
        header.Children.Add(pill);

        _padDetail = M3Typography.Create(
            pad is null ? "No virtual pad yet. Connect a controller to create one." : $"{pad.Description} · {pad.SubmitCount:N0} reports sent",
            M3Typography.LabelMedium,
            JoyNect.Core.Design.ColorRole.OnSurfaceVariant);
        _padDetail.Margin = new Thickness(0, 4, 0, 0);

        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        layout.Children.Add(M3Layout.Column(4, header, _padDetail));
        Grid.SetRow(_pad, 1);
        _pad.Margin = new Thickness(0, 8, 0, 0);
        layout.Children.Add(_pad);

        var footer = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        foreach (var controller in pad?.Controllers ?? (IReadOnlyList<string>)Array.Empty<string>())
        {
            footer.Children.Add(Widgets.Pill(
                controller,
                JoyNect.Core.Design.ColorRole.TertiaryContainer,
                JoyNect.Core.Design.ColorRole.OnTertiaryContainer,
                M3Glyph.Gamepad));
            footer.Children.Add(new Border { Width = 8 });
        }

        var rumble = new M3IconButton(M3Glyph.Vibration, M3IconButtonVariant.Standard)
        {
            IsEnabled = pad is not null,
            ToolTip = "Pulse the rumble motors",
        };

        rumble.AccessibilityName = "Test rumble";
        rumble.Click += (_, _) =>
        {
            State.TestRumble(170, 170);
            _rumbleTimer.Stop();
            _rumbleTimer.Start();
        };
        rumble.Margin = new Thickness(0, 10, 0, 0);

        var footerRow = M3Layout.Row(8, footer, rumble);
        Grid.SetRow(footerRow, 2);
        layout.Children.Add(footerRow);

        return new M3Card(M3CardVariant.Elevated) { Child = layout };
    }

    private UIElement BuildControllersCard()
    {
        var list = new StackPanel { Orientation = Orientation.Vertical };

        var controllers = State.Controllers;
        if (controllers.Count == 0)
        {
            list.Children.Add(Widgets.Empty(
                M3Glyph.Bluetooth,
                "Pair a controller",
                "Hold the SYNC button on top of the controller for two seconds, then choose it in Windows Bluetooth settings. Over USB it appears by itself.",
                Widgets.LinkButton("Open Windows Bluetooth", "ms-settings:bluetooth")));
        }
        else
        {
            var cards = new UniformGrid { Columns = controllers.Count > 1 ? 2 : 1 };
            foreach (var controller in controllers)
            {
                var card = BuildControllerRow(controller);
                card.Margin = new Thickness(0, 0, 12, 10);
                cards.Children.Add(card);
            }

            list.Children.Add(cards);
        }

        var ready = controllers.Count(c => c.State == ControllerLinkState.Ready);
        string? supporting = controllers.Count switch
        {
            0 => null,
            _ when ready == controllers.Count => $"{controllers.Count} connected · controllers stay paired so they come back by themselves",
            _ when ready == 0 => $"{controllers.Count} paired · waiting for the first report",
            _ => $"{ready} of {controllers.Count} ready · the rest are still connecting",
        };

        return M3Layout.Section("Controllers", supporting, list, 12);
    }

    private M3Card BuildControllerRow(ControllerSnapshot controller)
    {
        var iconHost = new Border
        {
            Width = 44,
            Height = 44,
            CornerRadius = new CornerRadius(22),
            Background = M3ThemeManager.Current.SecondaryContainer,
            Child = M3Icons.Create(M3Glyph.Gamepad, 22, M3ThemeManager.Current.OnSecondaryContainer),
        };

        var name = M3Typography.Create(controller.Model, M3Typography.TitleSmall);
        var detail = M3Typography.Create(
            $"{Widgets.ConnectionLabel(controller)} · {(string.IsNullOrEmpty(controller.MacAddress) ? "address pending" : controller.MacAddress)}",
            M3Typography.BodySmall,
            JoyNect.Core.Design.ColorRole.OnSurfaceVariant);

        var text = M3Layout.Column(2, name, detail);
        text.VerticalAlignment = VerticalAlignment.Center;

        var open = new M3IconButton(M3Glyph.ChevronRight) { Diameter = 36, ToolTip = "Open device details" };
        open.AccessibilityName = $"Details for {controller.Model}";
        open.Click += (_, _) => Navigate("devices");

        var right = M3Layout.Row(10, Widgets.StatePill(controller), open);
        right.VerticalAlignment = VerticalAlignment.Center;

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(iconHost);

        var middle = M3Layout.Column(8, text, Widgets.Battery(controller));
        middle.Margin = new Thickness(14, 0, 12, 0);
        middle.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(middle, 1);
        grid.Children.Add(middle);

        Grid.SetColumn(right, 2);
        grid.Children.Add(right);

        return new M3Card(M3CardVariant.Filled, 14) { Child = grid };
    }

    private UIElement BuildGlanceCard()
    {
        var pads = State.Pads;
        var controllers = State.Controllers;
        var ready = controllers.Count(c => c.State == ControllerLinkState.Ready);
        var rate = controllers
            .Where(c => c.ReportsPerSecond is > 0)
            .Select(c => c.ReportsPerSecond!.Value)
            .DefaultIfEmpty(0)
            .Average();

        var stats = new Grid();
        stats.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        stats.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        stats.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var controllerStat = Widgets.Stat("controllers", $"{ready}/{controllers.Count}");
        var padStat = Widgets.Stat("virtual pads", pads.Count.ToString());
        var rateStat = Widgets.Stat("reports/s", rate > 0 ? $"{rate:0}" : "–");

        stats.Children.Add(controllerStat);
        Grid.SetColumn(padStat, 1);
        stats.Children.Add(padStat);
        Grid.SetColumn(rateStat, 2);
        stats.Children.Add(rateStat);

        var driverRow = new Grid { Margin = new Thickness(0, 18, 0, 0) };
        driverRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        driverRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var driverText = M3Layout.Column(
            3,
            M3Typography.Create("ViGEmBus", M3Typography.LabelLarge),
            M3Typography.Create(State.Driver.Summary, M3Typography.BodySmall, JoyNect.Core.Design.ColorRole.OnSurfaceVariant));

        driverRow.Children.Add(driverText);
        var status = State.Driver.Available
            ? Widgets.Pill("Ready", JoyNect.Core.Design.ColorRole.SecondaryContainer, JoyNect.Core.Design.ColorRole.OnSecondaryContainer, M3Glyph.Check)
            : Widgets.Pill("Missing", JoyNect.Core.Design.ColorRole.ErrorContainer, JoyNect.Core.Design.ColorRole.OnErrorContainer, M3Glyph.Warning);

        status.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(status, 1);
        driverRow.Children.Add(status);

        var button = new M3Button("Open diagnostics", M3ButtonVariant.Text, M3Glyph.Pulse)
        {
            Margin = new Thickness(0, 10, 0, 0),
        };

        button.Click += (_, _) => Navigate("diagnostics");

        return new M3Card(M3CardVariant.Filled)
        {
            Child = M3Layout.Column(0, M3Typography.Create("At a glance", M3Typography.TitleMedium), stats, driverRow, button),
        };
    }

    private UIElement BuildPairingHintCard()
    {
        var open = Widgets.LinkButton("Open Windows Bluetooth", "ms-settings:bluetooth");

        return new M3Card(M3CardVariant.Outlined)
        {
            Child = M3Layout.Column(
                8,
                M3Typography.Create("Pairing a controller", M3Typography.TitleMedium),
                Widgets.Paragraph(
                    "Hold SYNC for two seconds, then add it under Bluetooth in Windows settings. JoyNect notices within a couple of seconds.",
                    M3Typography.BodySmall),
                open),
        };
    }

    private string Signature()
    {
        var controllers = string.Join(
            ';',
            State.Controllers.Select(c => $"{c.Id}:{c.State}:{c.Slot}:{c.Battery}:{c.ReportsPerSecond is null}"));
        var pads = string.Join(';', State.Pads.Select(p => p.Slot));
        return $"{controllers}|{pads}|{State.Driver.Available}|{State.IsRunning}|{State.Settings.CreateVirtualPads}";
    }
}
