using System.Globalization;
using System.Numerics;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using JoyNect.App.Services;
using JoyNect.Core.Motion;
using JoyNect.Ui.Controls;
using JoyNect.Ui.Theme;

namespace JoyNect.App.Pages;

/// <summary>
/// A live look at the gyroscope and accelerometer, including a drift check.
/// </summary>
/// <remarks>
/// Motion is the part of a Switch controller that no Xbox pad has, so it is published separately over
/// the Cemuhook DSU protocol for emulators that understand it. The live readout is also a drift check:
/// a Joy-Con that reports a constant turn rate while sitting still is damaged, and the resting rate
/// here is how you find that out before blaming a game.
/// </remarks>
internal sealed class MotionPage : AppPage
{
    private MotionScope? _scope;
    private TextBlock? _status;
    private TextBlock? _resting;
    private M3Button? _recentre;
    private Vector3 _bias;
    private int _captureRemaining;
    private readonly List<Vector3> _capture = [];

    public MotionPage()
        : base("motion", "Motion", "The gyroscope and accelerometer inside the controller, live.", M3Glyph.Motion)
    {
    }

    public override void OnHidden()
    {
        // A capture that spans a page change would collect samples from whatever controller is next.
        _captureRemaining = 0;
        _capture.Clear();
        base.OnHidden();
    }

    protected override void Build()
    {
        _recentre = new M3Button("Recentre", M3ButtonVariant.Tonal, M3Glyph.Refresh);
        _recentre.Click += (_, _) => BeginCapture();

        Body.Children.Add(Hero(null, _recentre));
        Body.Children.Add(new Border { Height = 20 });

        var columns = new Grid();
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(360) });

        columns.Children.Add(BuildScopeCard());

        var side = M3Layout.Column(16, BuildCalibrationCard(), BuildDriftCard());
        Grid.SetColumn(side, 2);
        columns.Children.Add(side);

        Body.Children.Add(columns);
        Body.Children.Add(new Border { Height = 20 });
        Body.Children.Add(BuildMotionServerCard());
        Body.Children.Add(new Border { Height = 24 });

        Body.Children.Add(Widgets.Paragraph(
            "Tilt comes from gravity, so it is only accurate while the controller is relatively still. Turn rate comes from the gyroscope and is calibrated from the factory data stored in the controller, which JoyNect reads when it connects.",
            M3Typography.BodyMedium));
    }

    public override void Refresh()
    {
        if (_scope is null)
        {
            return;
        }

        var controller = State.Controllers.FirstOrDefault(c => c.HasMotion && c.IsReady);
        if (controller is null)
        {
            _scope.SetIdle();
            SetStatus("No controller with motion is ready");
            _resting?.SetCurrentValue(TextBlock.TextProperty, "–");
            if (_recentre is not null)
            {
                _recentre.IsEnabled = false;
            }

            return;
        }

        var sample = MotionPipeline.ApplyBias(controller.Motion, _bias);
        _scope.SetSample(sample);

        if (_captureRemaining == 0)
        {
            SetStatus("Live · the newest IMU sample from each report");
        }

        if (_recentre is not null)
        {
            _recentre.IsEnabled = _captureRemaining == 0;
        }

        if (_captureRemaining > 0)
        {
            _capture.Add(controller.Motion.AngularVelocityDps);
            _captureRemaining--;
            SetStatus($"Hold still… {_capture.Count} of 12");

            if (_captureRemaining == 0)
            {
                _bias = MotionPipeline.EstimateGyroBias(_capture);
                _capture.Clear();
                SetStatus($"Recentred: removed {_bias.Length().ToString("0.0", CultureInfo.InvariantCulture)} °/s of resting bias");
                if (_recentre is not null)
                {
                    _recentre.IsEnabled = true;
                }
            }
        }

        var resting = sample.AngularVelocityDps.Length();
        _resting?.SetCurrentValue(
            TextBlock.TextProperty,
            resting < 0.5 ? "still" : $"{resting.ToString("0.0", CultureInfo.InvariantCulture)} °/s");
    }

    private UIElement BuildScopeCard()
    {
        _scope = new MotionScope { Height = 230 };
        _status = M3Typography.Create("Waiting for motion data", M3Typography.BodySmall, JoyNect.Core.Design.ColorRole.OnSurfaceVariant);

        var content = M3Layout.Column(
            6,
            M3Typography.Create("Live", M3Typography.TitleMedium),
            _scope,
            _status);

        return new M3Card(M3CardVariant.Elevated) { Child = content };
    }

    private UIElement BuildCalibrationCard()
    {
        var controller = State.Controllers.FirstOrDefault(c => c.HasMotion);
        var hasMotion = controller is not null;

        var content = new StackPanel();
        content.Children.Add(M3Typography.Create("Calibration", M3Typography.TitleMedium));

        if (!hasMotion)
        {
            var empty = Widgets.Paragraph(
                "Connect a Pro Controller or a Joy-Con to see its motion sensors. The NES, SNES, N64 and Genesis controllers have no motion hardware.",
                M3Typography.BodySmall);
            empty.Margin = new Thickness(0, 8, 0, 0);
            content.Children.Add(empty);
            return new M3Card(M3CardVariant.Filled) { Child = content };
        }

        content.Children.Add(Widgets.Detail("Controller", controller!.Model, wrap: false));
        content.Children.Add(Widgets.Detail("Factory data", controller.CalibrationLoaded ? "read successfully" : "not available; using documented defaults", wrap: false));
        content.Children.Add(Widgets.Detail("Gyroscope", "±2000 °/s, 16 bit", wrap: false));
        content.Children.Add(Widgets.Detail("Accelerometer", "±8 G, 16 bit", wrap: false));
        return new M3Card(M3CardVariant.Filled) { Child = content };
    }

    private UIElement BuildDriftCard()
    {
        _resting = M3Typography.Create("–", M3Typography.TitleLarge);

        var content = M3Layout.Column(
            8,
            M3Typography.Create("Drift check", M3Typography.TitleMedium),
            Widgets.Paragraph(
                "Put the controller down and leave it alone. The resting turn rate should read \"still\"; a constant value above a degree per second means the gyroscope has drifted, and recentring will compensate for it.",
                M3Typography.BodySmall),
            M3Layout.Row(10, _resting, M3Typography.Create("resting turn rate", M3Typography.LabelMedium, JoyNect.Core.Design.ColorRole.OnSurfaceVariant)));

        return new M3Card(M3CardVariant.Outlined) { Child = content };
    }

    private UIElement BuildMotionServerCard()
    {
        var theme = M3ThemeManager.Current;
        var status = State.MotionServer;
        var hasMotion = State.Controllers.Any(c => c.HasMotion && c.IsReady);

        var toggle = new M3Switch { IsSelected = State.Settings.MotionServerEnabled };
        toggle.AccessibilityName = "Publish motion to emulators";
        toggle.Click += (_, _) =>
        {
            var settings = State.Settings.Clone();
            settings.MotionServerEnabled = toggle.IsSelected;
            State.Save(settings);
            RebuildPage();
        };

        var heading = M3Typography.Create("Emulators (DSU)", M3Typography.TitleMedium);
        var headingRow = new Grid();
        headingRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headingRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        headingRow.Children.Add(heading);
        toggle.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(toggle, 1);
        headingRow.Children.Add(toggle);

        var description = Widgets.Paragraph(
            "Publish the same calibrated gyro and accelerometer data this page shows over the Cemuhook DSU protocol. Cemu, Dolphin and other DSU clients can use it while the virtual Xbox pad keeps handling buttons and sticks. JoyNect only listens on this machine.",
            M3Typography.BodySmall,
            JoyNect.Core.Design.ColorRole.OnSurfaceVariant,
            760);

        var portBox = new TextBox
        {
            Text = State.Settings.MotionPort.ToString(CultureInfo.InvariantCulture),
            Width = 76,
            Height = 24,
            MaxLength = 5,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = theme.OnSurface,
            CaretBrush = theme.Primary,
            SelectionBrush = theme.PrimaryContainer,
            FontFamily = M3Typography.Text,
            FontSize = 14,
            VerticalContentAlignment = VerticalAlignment.Center,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            TextAlignment = TextAlignment.Left,
        };
        AutomationProperties.SetName(portBox, "DSU port");
        AutomationProperties.SetHelpText(portBox, "The UDP port DSU clients connect to.");

        var portFrame = new Border
        {
            Background = theme.SurfaceContainerHighest,
            CornerRadius = new CornerRadius(12),
            Height = 40,
            Padding = new Thickness(12, 0, 12, 0),
            BorderThickness = new Thickness(0),
            BorderBrush = theme.Error,
            Child = portBox,
        };

        var apply = new M3Button("Apply", M3ButtonVariant.Tonal, M3Glyph.Check);
        apply.AccessibilityName = "Apply DSU port";

        void ApplyPort()
        {
            if (!int.TryParse(portBox.Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var value)
                || value is < 1024 or > 65535)
            {
                portFrame.BorderThickness = new Thickness(1);
                State.Notify("Use a port between 1024 and 65535", true);
                portBox.Focus();
                portBox.SelectAll();
                return;
            }

            portFrame.BorderThickness = new Thickness(0);
            var settings = State.Settings.Clone();

            if (value == settings.MotionPort)
            {
                // Applying the same port is the natural retry after the port was busy or the bind
                // failed. Save is idempotent for every other setting, and AppState retries the
                // server when it is enabled but not listening.
                if (!State.MotionServer.Enabled || State.MotionServer.Running)
                {
                    return;
                }
            }
            else
            {
                settings.MotionPort = value;
            }

            State.Save(settings);
            RebuildPage();
        }

        apply.Click += (_, _) => ApplyPort();
        portBox.KeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Enter)
            {
                ApplyPort();
                e.Handled = true;
            }
        };

        portBox.TextChanged += (_, _) => portFrame.BorderThickness = new Thickness(0);

        var portRow = M3Layout.Row(
            12,
            M3Typography.Create("UDP port", M3Typography.LabelLarge),
            portFrame,
            apply);

        var (pillText, container, content, glyph) = status switch
        {
            { Error: not null } => ("Not listening", JoyNect.Core.Design.ColorRole.ErrorContainer, JoyNect.Core.Design.ColorRole.OnErrorContainer, M3Glyph.Warning),
            { Listening: true } => ("Listening", JoyNect.Core.Design.ColorRole.SecondaryContainer, JoyNect.Core.Design.ColorRole.OnSecondaryContainer, M3Glyph.Check),
            _ => ("Off", JoyNect.Core.Design.ColorRole.SurfaceContainerHighest, JoyNect.Core.Design.ColorRole.OnSurfaceVariant, M3Glyph.Motion),
        };

        var detail = status switch
        {
            { Error: not null } => $"{status.Error} Change the port or close the application using it, then apply again.",
            { Listening: true } when hasMotion => $"{status.Endpoint} is live. Point your emulator's DSU client at that address.",
            { Listening: true } => $"{status.Endpoint} is ready. Pair a Pro Controller or Joy-Con to publish motion.",
            _ => "Off. The Xbox pad still works; only the separate motion feed is disabled.",
        };

        var statusText = Widgets.Paragraph(detail, M3Typography.BodySmall, JoyNect.Core.Design.ColorRole.OnSurfaceVariant, 760);
        var statusRow = M3Layout.Row(12, Widgets.Pill(pillText, container, content, glyph), statusText);
        statusRow.VerticalAlignment = VerticalAlignment.Center;

        var card = M3Layout.Column(
            12,
            headingRow,
            description,
            M3Layout.Divider(0.7),
            portRow,
            statusRow);

        return new M3Card(M3CardVariant.Outlined) { Child = card };
    }

    private void BeginCapture()
    {
        _capture.Clear();
        _captureRemaining = 12;
        _bias = Vector3.Zero;
        SetStatus("Hold still…");

        if (_recentre is not null)
        {
            _recentre.IsEnabled = false;
        }
    }

    private void SetStatus(string text)
    {
        if (_status is not null)
        {
            _status.Text = text;
        }
    }
}
