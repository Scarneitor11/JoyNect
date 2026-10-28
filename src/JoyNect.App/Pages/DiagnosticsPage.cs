using System.IO;
using System.Windows;
using System.Windows.Controls;
using JoyNect.App.Services;
using JoyNect.Core.Hid;
using JoyNect.Core.Output;
using JoyNect.Ui.Controls;
using JoyNect.Ui.Theme;
using Microsoft.Win32;

namespace JoyNect.App.Pages;

/// <summary>
/// Driver state, live link statistics and the raw HID interfaces Windows exposes.
/// </summary>
/// <remarks>
/// This is the page someone opens when nothing works. It answers, in order: is the virtual pad driver
/// there, is the controller sending reports at a sane rate, and which of a controller's several HID
/// collections is JoyNect actually talking to. The report can be copied in one click for a bug report.
/// </remarks>
internal sealed class DiagnosticsPage : AppPage
{
    private readonly StackPanel _hidList = new();
    private readonly Dictionary<string, TextBlock> _statistics = [];
    private readonly Dictionary<string, TextBlock> _rates = [];
    private readonly TextBlock _driverSummary;
    private readonly StackPanel _driverActions = new() { Orientation = Orientation.Horizontal };
    private bool _loadedHid;
    private bool _loading;

    public DiagnosticsPage()
        : base("diagnostics", "Diagnostics", "Driver, link quality and the HID interfaces behind the scenes.", M3Glyph.Pulse)
    {
        _driverSummary = M3Typography.Create(string.Empty, M3Typography.BodyMedium);
    }

    public override void OnShown()
    {
        if (!_loadedHid && !_loading)
        {
            _ = LoadHidAsync();
        }

        base.OnShown();
    }

    protected override void Build()
    {
        var copy = new M3Button("Copy report", M3ButtonVariant.Tonal, M3Glyph.Copy);
        copy.Click += async (_, _) =>
        {
            var text = await State.BuildDiagnosticReportAsync();
            Widgets.Copy(text, message => State.Notify(message));
        };

        var save = new M3Button("Save report", M3ButtonVariant.Outlined, M3Glyph.Save);
        save.Click += async (_, _) => await SaveReportAsync();

        Body.Children.Add(Hero(null, copy, save));
        Body.Children.Add(new Border { Height = 20 });
        Body.Children.Add(BuildDriverCard());
        Body.Children.Add(new Border { Height = 24 });
        Body.Children.Add(M3Layout.Section("Controllers", "Cadence is measured over the last few seconds of reports.", BuildControllerList(), 12));
        Body.Children.Add(new Border { Height = 24 });
        Body.Children.Add(M3Layout.Section(
            "HID interfaces",
            "A Switch controller appears as more than one collection; JoyNect talks to the vendor one.",
            BuildHidSection(),
            12));
        Body.Children.Add(new Border { Height = 28 });
        Body.Children.Add(Widgets.Paragraph(
            "If a controller is listed but never leaves \"Connecting\", remove it in Windows Bluetooth settings and pair it again. A generic USB Bluetooth dongle can also be the cause: many of them drop the vendor collection a Switch controller needs.",
            M3Typography.BodySmall));
    }

    public override void Refresh()
    {
        foreach (var controller in State.Controllers)
        {
            if (_statistics.TryGetValue(controller.Id, out var statistics))
            {
                statistics.SetCurrentValue(TextBlock.TextProperty, controller.Statistics);
            }

            if (_rates.TryGetValue(controller.Id, out var rate))
            {
                rate.SetCurrentValue(
                    TextBlock.TextProperty,
                    controller.ReportsPerSecond is { } value ? $"{value:0} reports/s" : "waiting");
            }
        }

        if (_driverSummary.Text.Length == 0)
        {
            _driverSummary.SetCurrentValue(TextBlock.TextProperty, State.Driver.Summary);
        }
    }

    private UIElement BuildDriverCard()
    {
        var theme = M3ThemeManager.Current;
        var available = State.Driver.Available;

        _driverSummary.SetCurrentValue(TextBlock.TextProperty, State.Driver.Summary);
        _driverSummary.Foreground = theme.OnSurfaceVariant;

        _driverActions.Children.Clear();

        var probe = new M3Button("Check again", M3ButtonVariant.Outlined, M3Glyph.Refresh);
        probe.Click += (_, _) =>
        {
            State.RefreshDriver();
            State.Notify(State.Driver.Available ? "The driver is available" : "The driver is still not available", !State.Driver.Available);
            _driverSummary.SetCurrentValue(TextBlock.TextProperty, State.Driver.Summary);
        };

        _driverActions.Children.Add(probe);

        if (!available)
        {
            var installer = Widgets.LinkButton("Get ViGEmBus", DriverProbe.OfficialInstallerUri);
            installer.Margin = new Thickness(8, 0, 0, 0);
            _driverActions.Children.Add(installer);
        }

        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var title = M3Layout.Column(
            4,
            M3Typography.Create("ViGEmBus virtual pad driver", M3Typography.TitleMedium),
            _driverSummary);

        header.Children.Add(title);

        var status = available
            ? Widgets.Pill("Available", JoyNect.Core.Design.ColorRole.SecondaryContainer, JoyNect.Core.Design.ColorRole.OnSecondaryContainer, M3Glyph.Check)
            : Widgets.Pill("Unavailable", JoyNect.Core.Design.ColorRole.ErrorContainer, JoyNect.Core.Design.ColorRole.OnErrorContainer, M3Glyph.Warning);

        status.VerticalAlignment = VerticalAlignment.Top;
        Grid.SetColumn(status, 1);
        header.Children.Add(status);

        var content = M3Layout.Column(14, header);

        if (!available && !string.IsNullOrWhiteSpace(State.Driver.Remediation))
        {
            content.Children.Add(Widgets.Paragraph(State.Driver.Remediation!, M3Typography.BodyMedium));
        }

        content.Children.Add(_driverActions);

        return new M3Card(M3CardVariant.Filled) { Child = content };
    }

    private UIElement BuildControllerList()
    {
        _statistics.Clear();
        _rates.Clear();

        var list = new StackPanel { Orientation = Orientation.Vertical };
        var controllers = State.Controllers;

        if (controllers.Count == 0)
        {
            list.Children.Add(Widgets.Empty(
                M3Glyph.Bluetooth,
                "No controller connected",
                "Connect a controller and its report cadence, handshakes and unrecognised reports appear here."));
            return list;
        }

        foreach (var controller in controllers)
        {
            var name = M3Typography.Create(controller.Model, M3Typography.LabelLarge);
            var stats = M3Typography.Create(controller.Statistics, M3Typography.BodySmall, JoyNect.Core.Design.ColorRole.OnSurfaceVariant);
            var rate = M3Typography.Create("waiting", M3Typography.LabelMedium, JoyNect.Core.Design.ColorRole.Primary);

            _statistics[controller.Id] = stats;
            _rates[controller.Id] = rate;

            var grid = new Grid { Margin = new Thickness(0, 0, 0, 12) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.Children.Add(M3Layout.Column(4, name, stats));

            rate.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(rate, 1);
            grid.Children.Add(rate);

            list.Children.Add(grid);
        }

        return new M3Card(M3CardVariant.Filled) { Child = list };
    }

    private UIElement BuildHidSection()
    {
        var content = new StackPanel();
        content.Children.Add(_hidList);

        var refresh = new M3Button("Enumerate again", M3ButtonVariant.Text, M3Glyph.Refresh);
        refresh.Click += (_, _) =>
        {
            _loadedHid = false;
            _ = LoadHidAsync();
        };

        content.Children.Add(refresh);

        if (!_loadedHid)
        {
            _hidList.Children.Add(Widgets.Paragraph("Looking for HID interfaces…", M3Typography.BodySmall));
        }

        return new M3Card(M3CardVariant.Outlined) { Child = content };
    }

    private async Task LoadHidAsync()
    {
        _loading = true;
        _hidList.Children.Clear();
        _hidList.Children.Add(Widgets.Paragraph("Looking for HID interfaces…", M3Typography.BodySmall));

        try
        {
            var candidates = await State.EnumerateHidAsync();
            _hidList.Children.Clear();

            if (candidates.Count == 0)
            {
                _hidList.Children.Add(Widgets.Paragraph(
                    "No Nintendo HID interface is present. If the controller is paired, press a button on it: a sleeping controller does not appear until it wakes.",
                    M3Typography.BodyMedium));
            }

            foreach (var group in candidates.GroupBy(c => c.PhysicalDeviceKey, StringComparer.OrdinalIgnoreCase))
            {
                _hidList.Children.Add(Widgets.Paragraph(group.Key, M3Typography.LabelMedium, JoyNect.Core.Design.ColorRole.OnSurfaceVariant, 900));

                foreach (var candidate in group)
                {
                    _hidList.Children.Add(Widgets.Detail(
                        candidate.Model.DisplayName,
                        $"{candidate.Connection} · {(candidate.DeclaresVendorReports ? "vendor reports" : "no vendor reports")} · {candidate.MaxInputReportLength} byte input",
                        wrap: true));

                    _hidList.Children.Add(Widgets.Detail("Path", candidate.DevicePath, wrap: true));
                }

                _hidList.Children.Add(new Border
                {
                    Height = 1,
                    Background = M3ThemeManager.Current.OutlineVariant,
                    Margin = new Thickness(0, 8, 0, 12),
                });
            }

            _loadedHid = true;
            State.Notify($"{candidates.Count} HID interface{(candidates.Count == 1 ? string.Empty : "s")} found");
        }
        catch (Exception ex)
        {
            _hidList.Children.Clear();
            _hidList.Children.Add(Widgets.Paragraph($"Enumerating HID failed: {ex.Message}", M3Typography.BodyMedium, JoyNect.Core.Design.ColorRole.Error));
        }
        finally
        {
            _loading = false;
        }
    }

    private async Task SaveReportAsync()
    {
        var report = await State.BuildDiagnosticReportAsync();
        var dialog = new SaveFileDialog
        {
            Title = "Save diagnostic report",
            Filter = "Text file (*.txt)|*.txt|All files (*.*)|*.*",
            FileName = $"joynect-diagnostics-{DateTime.Now:yyyyMMdd-HHmm}.txt",
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            await File.WriteAllTextAsync(dialog.FileName, report);
            State.Notify($"Report saved to {Path.GetFileName(dialog.FileName)}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            State.Notify("The report could not be saved", true);
        }
    }
}
