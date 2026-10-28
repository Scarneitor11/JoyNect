using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using JoyNect.Ui.Controls;
using JoyNect.Ui.Theme;

namespace JoyNect.App.Pages;

/// <summary>
/// What this is, what it deliberately is not, and who wrote the parts it stands on.
/// </summary>
/// <remarks>
/// The limitations are listed as plainly as the features. A controller bridge is the kind of program
/// where an unstated limitation becomes an hour of someone's evening lost to a bug that is not a bug.
/// </remarks>
internal sealed class AboutPage : AppPage
{
    public AboutPage()
        : base("about", "About", "A small bridge between Nintendo's controllers and Windows' pads.", M3Glyph.Info)
    {
    }

    protected override void Build()
    {
        var gallery = new M3Button("Design system", M3ButtonVariant.Outlined, M3Glyph.Palette);
        gallery.Click += (_, _) => Navigate("style");

        Body.Children.Add(Hero(null, gallery));
        Body.Children.Add(new Border { Height = 20 });
        Body.Children.Add(BuildIdentityCard());
        Body.Children.Add(new Border { Height = 20 });

        var columns = new Grid();
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var left = M3Layout.Column(20, BuildDoesCard(), BuildShortcutsCard());
        left.VerticalAlignment = VerticalAlignment.Top;
        columns.Children.Add(left);

        var right = M3Layout.Column(20, BuildLimitationsCard(), BuildCreditsCard());
        right.VerticalAlignment = VerticalAlignment.Top;
        Grid.SetColumn(right, 2);
        columns.Children.Add(right);

        Body.Children.Add(columns);
        Body.Children.Add(new Border { Height = 24 });
        Body.Children.Add(Widgets.Paragraph(
            "JoyNect is free software under the MIT licence. It contains no telemetry and makes no internet requests. The optional motion server only talks to emulators on this machine, over loopback.",
            M3Typography.BodySmall));
    }

    private UIElement BuildIdentityCard()
    {
        var icon = new Image
        {
            Source = new BitmapImage(new Uri("pack://application:,,,/assets/app.png")),
            Width = 56,
            Height = 56,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var name = M3Typography.Create("JoyNect", M3Typography.HeadlineMedium);
        var version = M3Typography.Create(
            $"Version {VersionText()} · Windows 10 and 11 · no administrator rights needed",
            M3Typography.BodySmall,
            JoyNect.Core.Design.ColorRole.OnSurfaceVariant);

        var text = M3Layout.Column(3, name, version);
        text.VerticalAlignment = VerticalAlignment.Center;

        var title = M3Layout.Row(16, icon, text);

        var tagline = Widgets.Paragraph(
            "Switch controllers speak a protocol Windows has never heard of. JoyNect translates it, live, into an Xbox 360 pad that every game already supports — with a Material 3 interface instead of a settings dialog from 2009.",
            M3Typography.BodyLarge,
            JoyNect.Core.Design.ColorRole.OnSurface,
            880);

        tagline.Margin = new Thickness(0, 18, 0, 0);

        var content = M3Layout.Column(0, title, tagline);

        var chips = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 16, 0, 0) };
        chips.Children.Add(Widgets.Pill(".NET 8", JoyNect.Core.Design.ColorRole.SecondaryContainer, JoyNect.Core.Design.ColorRole.OnSecondaryContainer));
        chips.Children.Add(new Border { Width = 8 });
        chips.Children.Add(Widgets.Pill("XInput", JoyNect.Core.Design.ColorRole.TertiaryContainer, JoyNect.Core.Design.ColorRole.OnTertiaryContainer));
        chips.Children.Add(new Border { Width = 8 });
        chips.Children.Add(Widgets.Pill("HID over Bluetooth and USB", JoyNect.Core.Design.ColorRole.SurfaceContainerHighest, JoyNect.Core.Design.ColorRole.OnSurfaceVariant));

        content.Children.Add(chips);

        return new M3Card(M3CardVariant.Elevated) { Child = content };
    }

    private UIElement BuildDoesCard()
    {
        var content = M3Layout.Column(
            10,
            M3Typography.Create("What it does", M3Typography.TitleMedium),
            Bullet("Connects Switch Pro Controllers, Joy-Con and the NSO retro pads over Bluetooth or USB."),
            Bullet("Reads factory stick and motion calibration from the controller's own flash."),
            Bullet("Presents each controller as a standard Xbox 360 pad through the ViGEmBus driver."),
            Bullet("Publishes live gyro and accelerometer data over the Cemuhook DSU protocol, so emulators such as Cemu and Dolphin can use motion controls."),
            Bullet("Shapes sticks, remaps buttons, sets player lights and scales rumble."),
            Bullet("Reconnects by itself, and recovers a controller that drops off."));

        return new M3Card(M3CardVariant.Filled) { Child = content };
    }

    private UIElement BuildLimitationsCard()
    {
        var content = M3Layout.Column(
            10,
            M3Typography.Create("What it does not do", M3Typography.TitleMedium),
            Bullet("Switch 2 controllers are not claimed as supported. An unknown 0x057E device is labelled untested instead of guessed at."),
            Bullet("Four pads is the XInput limit; more controllers can connect but cannot all play at once."),
            Bullet("Motion reaches emulators through DSU, not through XInput. A game needs its own DSU client or motion support; the virtual Xbox pad itself has no gyroscope."),
            Bullet("HD rumble cannot reproduce an Xbox motor; the strength setting is a compromise."),
            Bullet("It does not ship ViGEmBus. The driver is no longer maintained, so JoyNect links to the last signed release instead of bundling an installer."),
            Bullet("No mouse, keyboard or macro emulation, and no anti-cheat tricks."));

        return new M3Card(M3CardVariant.Outlined) { Child = content };
    }

    private UIElement BuildCreditsCard()
    {
        var content = M3Layout.Column(
            8,
            M3Typography.Create("Built on", M3Typography.TitleMedium),
            Credit("HidSharp", "MIT · managed HID access over Bluetooth and USB", "https://www.zer7.com/software/hidsharp"),
            Credit("Nefarius.ViGEm.Client", "MIT · virtual Xbox 360 pads", "https://github.com/nefarius/ViGEm.NET"),
            Credit("ViGEmBus", "BSD-3-Clause · the kernel driver", "https://github.com/nefarius/ViGEmBus"),
            Credit("Material Color Utilities", "Apache-2.0 · reference for the HCT colour tests", "https://github.com/material-foundation/material-color-utilities"),
            Credit("Segoe UI Variable", "the Windows typeface the Material scale is mapped onto", "https://learn.microsoft.com/windows/apps/design/style/typography"));

        return new M3Card(M3CardVariant.Filled) { Child = content };
    }

    private UIElement BuildShortcutsCard()
    {
        var content = M3Layout.Column(
            6,
            M3Typography.Create("Keyboard", M3Typography.TitleMedium),
            Shortcut("Ctrl + 1 … 8", "switch pages"),
            Shortcut("Ctrl + R", "scan for controllers"),
            Shortcut("Ctrl + ,", "settings"),
            Shortcut("F1", "about"),
            Shortcut("Tab / arrows", "move between controls; every control has a focus ring"),
            Shortcut("Esc", "close the first-run guide"));

        return new M3Card(M3CardVariant.Filled) { Child = content };
    }

    private static UIElement Bullet(string text)
    {
        var marker = new Border
        {
            Width = 6,
            Height = 6,
            CornerRadius = new CornerRadius(3),
            Background = M3ThemeManager.Current.Primary,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(4, 8, 0, 0),
        };

        var paragraph = M3Typography.Create(text, M3Typography.BodySmall, JoyNect.Core.Design.ColorRole.OnSurfaceVariant);
        paragraph.MaxWidth = 520;

        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.Children.Add(marker);

        var body = new Border { Margin = new Thickness(14, 0, 0, 0), Child = paragraph };
        Grid.SetColumn(body, 1);
        row.Children.Add(body);
        return row;
    }

    private static UIElement Credit(string name, string detail, string uri)
    {
        var title = Widgets.WithLink(string.Empty, name, uri);
        title.FontWeight = FontWeights.SemiBold;
        title.FontSize = 13;

        var note = M3Typography.Create(detail, M3Typography.BodySmall, JoyNect.Core.Design.ColorRole.OnSurfaceVariant);
        return M3Layout.Column(2, title, note);
    }

    private static UIElement Shortcut(string keys, string description)
    {
        var key = Widgets.Pill(keys, JoyNect.Core.Design.ColorRole.SurfaceContainerHighest, JoyNect.Core.Design.ColorRole.OnSurface);
        key.MinWidth = 96;

        var text = M3Typography.Create(description, M3Typography.BodySmall, JoyNect.Core.Design.ColorRole.OnSurfaceVariant);
        return M3Layout.Row(10, key, text);
    }

    private static string VersionText()
    {
        var version = Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(1, 0, 0);
        return version.Revision > 0
            ? version.ToString()
            : $"{version.Major}.{version.Minor}.{version.Build}";
    }
}
