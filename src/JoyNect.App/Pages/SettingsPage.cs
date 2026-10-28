using System.IO;
using System.Windows;
using System.Windows.Controls;
using JoyNect.App.Services;
using JoyNect.Core.Design;
using JoyNect.Core.Settings;
using JoyNect.Ui.Controls;
using JoyNect.Ui.Theme;

namespace JoyNect.App.Pages;

/// <summary>
/// Appearance, behaviour and data.
/// </summary>
/// <remarks>
/// Every control writes through <see cref="IAppState.Save"/>, which is what keeps the settings file
/// and the running app in step. Nothing here needs a Save button: the change is the save, and the
/// snackbar confirms it when the change had a visible effect.
/// </remarks>
internal sealed class SettingsPage : AppPage
{
    private static readonly (string Name, uint Colour)[] Seeds =
    [
        ("Mauve", 0xFF6750A4),
        ("Violet", 0xFF6650A4),
        ("Indigo", 0xFF3F5F90),
        ("Blue", 0xFF00658F),
        ("Teal", 0xFF006B5A),
        ("Green", 0xFF1B6C4A),
        ("Olive", 0xFF5B5F00),
        ("Amber", 0xFF8B4A00),
        ("Rose", 0xFF8C4A60),
        ("Clay", 0xFF7D5260),
    ];

    public SettingsPage()
        : base("settings", "Settings", "How JoyNect looks, behaves and where it keeps your data.", M3Glyph.Sliders)
    {
    }

    protected override void Build()
    {
        Body.Children.Add(Hero(null));
        Body.Children.Add(new Border { Height = 20 });
        Body.Children.Add(BuildAppearanceCard());
        Body.Children.Add(new Border { Height = 24 });
        Body.Children.Add(M3Layout.Section("Behaviour", "These take effect immediately.", BuildBehaviour(), 12));
        Body.Children.Add(new Border { Height = 24 });
        Body.Children.Add(M3Layout.Section("Data", "JoyNect keeps a small JSON file and a rolling log; nothing leaves this machine.", BuildDataCard(), 12));
        Body.Children.Add(new Border { Height = 28 });
    }

    private UIElement BuildAppearanceCard()
    {
        var theme = new M3SegmentedButtons { Width = 360 };
        theme.SetSegments("Light", "Dark", "System");
        theme.SelectedIndex = State.Settings.Appearance switch
        {
            AppearanceMode.Light => 0,
            AppearanceMode.Dark => 1,
            _ => 2,
        };

        var variant = new M3SegmentedButtons { Width = 360 };
        variant.SetSegments("Expressive", "Tonal spot");
        variant.SelectedIndex = State.Settings.Variant == SchemeVariant.Expressive ? 0 : 1;

        var wallpaper = new M3Switch { IsSelected = State.Settings.FollowWallpaper };
        wallpaper.AccessibilityName = "Match wallpaper colour";

        var contrast = new M3Switch { IsSelected = State.Settings.Contrast == ContrastLevel.High };
        contrast.AccessibilityName = "High contrast";

        theme.SelectionChanged += (_, _) =>
        {
            var settings = State.Settings.Clone();
            settings.Appearance = theme.SelectedIndex switch
            {
                0 => AppearanceMode.Light,
                1 => AppearanceMode.Dark,
                _ => AppearanceMode.System,
            };

            State.Save(settings);
            RebuildPage();
        };

        variant.SelectionChanged += (_, _) =>
        {
            var settings = State.Settings.Clone();
            settings.Variant = variant.SelectedIndex == 0 ? SchemeVariant.Expressive : SchemeVariant.TonalSpot;
            State.Save(settings);
            RebuildPage();
        };

        wallpaper.Click += (_, _) =>
        {
            var settings = State.Settings.Clone();
            settings.FollowWallpaper = wallpaper.IsSelected;
            State.Save(settings);
            RebuildPage();
        };

        contrast.Click += (_, _) =>
        {
            var settings = State.Settings.Clone();
            settings.Contrast = contrast.IsSelected ? ContrastLevel.High : ContrastLevel.Standard;
            State.Save(settings);
            RebuildPage();
        };

        var swatches = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var (name, colour) in Seeds)
        {
            var swatch = new M3ColorSwatch(name, colour)
            {
                IsSelected = State.Settings.SeedColour == colour && !State.Settings.FollowWallpaper,
                Margin = new Thickness(0, 0, 6, 6),
            };

            var value = colour;
            swatch.Click += (_, _) =>
            {
                var settings = State.Settings.Clone();
                settings.SeedColour = value;
                settings.FollowWallpaper = false;
                State.Save(settings);
                State.Notify($"Palette generated from {name.ToLowerInvariant()}");
                RebuildPage();
            };

            swatches.Children.Add(swatch);
        }

        var hex = State.Settings.SeedColour;
        var seedNote = M3Typography.Create(
            State.Settings.FollowWallpaper
                ? $"Using the wallpaper colour{(M3ThemeManager.WallpaperSeed is { } seed ? $" ({ColorUtils.ToHex(seed)})" : " (not available, using the chosen colour)")}."
                : $"Seed {ColorUtils.ToHex(hex)}.",
            M3Typography.BodySmall,
            ColorRole.OnSurfaceVariant);

        var content = M3Layout.Column(
            18,
            M3Typography.Create("Appearance", M3Typography.TitleMedium),
            M3Typography.Create("Theme", M3Typography.LabelLarge),
            theme,
            Row("Match wallpaper", "Take the seed colour from the desktop wallpaper. Windows does not tell apps directly, so JoyNect reads the current wallpaper image.", wallpaper),
            Row("High contrast", "Strengthen text and outlines to clear 7:1 contrast; useful in bright rooms.", contrast),
            M3Typography.Create("Colour variant", M3Typography.LabelLarge),
            variant,
            M3Typography.Create("Seed colour", M3Typography.LabelLarge),
            swatches,
            seedNote);

        return new M3Card(M3CardVariant.Filled) { Child = content };
    }

    private UIElement BuildBehaviour()
    {
        var startup = new M3Switch { IsSelected = StartupRegistration.IsEnabled() };
        startup.AccessibilityName = "Start with Windows";
        startup.Click += (_, _) =>
        {
            var enabled = startup.IsSelected;
            if (!StartupRegistration.Set(enabled))
            {
                startup.IsSelected = !enabled;
                State.Notify("Windows would not accept the startup entry", true);
                return;
            }

            var settings = State.Settings.Clone();
            settings.StartWithWindows = enabled;
            State.Save(settings);
            State.Notify(enabled ? "JoyNect will start minimised with Windows" : "JoyNect will not start with Windows");
        };

        var tray = new M3Switch { IsSelected = State.Settings.CloseToTray };
        tray.AccessibilityName = "Close to the notification area";
        tray.Click += (_, _) =>
        {
            var settings = State.Settings.Clone();
            settings.CloseToTray = tray.IsSelected;
            State.Save(settings);
        };

        var notifications = new M3Switch { IsSelected = State.Settings.ShowConnectionNotifications };
        notifications.AccessibilityName = "Connection notifications";
        notifications.Click += (_, _) =>
        {
            var settings = State.Settings.Clone();
            settings.ShowConnectionNotifications = notifications.IsSelected;
            State.Save(settings);
        };

        var pads = new M3Switch { IsSelected = State.Settings.CreateVirtualPads };
        pads.AccessibilityName = "Create virtual pads";
        pads.Click += (_, _) =>
        {
            var settings = State.Settings.Clone();
            settings.CreateVirtualPads = pads.IsSelected;
            State.Save(settings);
            State.Notify(pads.IsSelected ? "Virtual pads will be created" : "Controllers can be read, but no pad is created");
        };

        return M3Layout.Column(
            12,
            M3Layout.SettingCard("Start with Windows", "Starts in the notification area so controllers work before a game is opened.", startup),
            M3Layout.SettingCard("Close to the notification area", "The X button hides the window; quit from the tray menu. Off means closing the window quits.", tray),
            M3Layout.SettingCard("Connection notifications", "Show a snackbar when a controller connects or disconnects.", notifications),
            M3Layout.SettingCard("Create virtual pads", "Off lets you inspect a controller without a pad appearing in games.", pads));
    }

    private UIElement BuildDataCard()
    {
        var path = State.SettingsPath;

        var pathText = new TextBlock
        {
            Text = path,
            FontFamily = M3Typography.Text,
            FontSize = 12,
            Foreground = M3ThemeManager.Current.OnSurfaceVariant,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left,
            MaxWidth = 520,
        };

        var copy = new M3IconButton(M3Glyph.Copy) { Diameter = 36, ToolTip = "Copy the settings path" };
        copy.AccessibilityName = "Copy the settings path";
        copy.Click += (_, _) => Widgets.Copy(path, message => State.Notify(message));

        var openFolder = new M3Button("Open folder", M3ButtonVariant.Outlined, M3Glyph.Folder);
        openFolder.Click += (_, _) => OpenSettingsFolder(path);

        var report = new M3Button("Copy diagnostic report", M3ButtonVariant.Tonal, M3Glyph.Save);
        report.Click += async (_, _) =>
        {
            var text = await State.BuildDiagnosticReportAsync();
            Widgets.Copy(text, message => State.Notify(message));
        };

        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(pathText);
        Grid.SetColumn(copy, 1);
        row.Children.Add(copy);

        return new M3Card(M3CardVariant.Filled)
        {
            Child = M3Layout.Column(
                14,
                M3Typography.Create("Settings file", M3Typography.LabelLarge),
                row,
                M3Layout.Row(8, openFolder, report)),
        };
    }

    private static UIElement Row(string title, string body, M3Switch toggle)
    {
        var text = M3Layout.Column(
            3,
            M3Typography.Create(title, M3Typography.LabelLarge),
            M3Typography.Create(body, M3Typography.BodySmall, ColorRole.OnSurfaceVariant));

        var bodyBlock = (TextBlock)text.Children[1];
        bodyBlock.MaxWidth = 560;

        // Stretch plus a MaxWidth centres in WPF; this row wants both texts flush left.
        bodyBlock.HorizontalAlignment = HorizontalAlignment.Left;

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(text);

        toggle.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(toggle, 1);
        grid.Children.Add(toggle);
        return grid;
    }

    private static void OpenSettingsFolder(string path)
    {
        try
        {
            var directory = System.IO.Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(directory))
            {
                return;
            }

            Directory.CreateDirectory(directory);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"\"{directory}\"")
            {
                UseShellExecute = true,
            });
        }
        catch (Exception)
        {
            // Opening a folder is a convenience; failing to do it must not crash the app.
        }
    }

}
