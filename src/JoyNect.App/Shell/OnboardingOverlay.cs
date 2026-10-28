using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using JoyNect.App.Pages;
using JoyNect.App.Services;
using JoyNect.Core.Design;
using JoyNect.Ui.Controls;
using JoyNect.Ui.Theme;

namespace JoyNect.App.Shell;

/// <summary>
/// The first-run guide: what the app is, whether the driver is there, and how to pair a controller.
/// </summary>
/// <remarks>
/// Three steps, each with one job. It is skippable and never blocks the app, because the same
/// instructions live on the Home and Diagnostics pages for anyone who dismisses it.
/// </remarks>
internal sealed class OnboardingOverlay : Grid
{
    private readonly IAppState _state;
    private readonly StackPanel _content = new() { Orientation = Orientation.Vertical };
    private readonly Border _card;
    private M3Button? _primaryAction;
    private int _step;
    private bool _finished;
    private bool _transitionPending;
    private bool _reloadPending;

    public OnboardingOverlay(IAppState state)
    {
        _state = state;
        Focusable = true;
        Background = M3ThemeManager.Current.Overlay(ColorRole.Scrim, 0.55);

        _card = new Border
        {
            Width = 620,
            Padding = new Thickness(36),
            CornerRadius = new CornerRadius(M3Shape.ExtraLarge),
            Background = M3ThemeManager.Current.SurfaceContainerHigh,
            Effect = M3Elevation.For(3),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Child = _content,
        };

        Children.Add(_card);
        PreviewKeyDown += OnKeyDown;
        Build();
    }

    public event EventHandler? Completed;

    /// <summary>Repaints the guide for a theme change, keeping the current step.</summary>
    public void Reload()
    {
        if (_finished)
        {
            return;
        }

        Background = M3ThemeManager.Current.Overlay(ColorRole.Scrim, 0.55);
        _card.Background = M3ThemeManager.Current.SurfaceContainerHigh;
        // The overlays own handler, not the window's. Painting happens after the current event
        // has unwound so a button click that changed the theme does not rebuild the panel raising
        // the event it is still inside. A queued but stale request is dropped once the next one has
        // started, so a theme switch during the first-run guide never touches the guide while it is
        // mid-build.
        if (!_reloadPending)
        {
            _reloadPending = true;
            Dispatcher.BeginInvoke(
                new Action(() =>
                {
                    _reloadPending = false;
                    if (!_finished)
                    {
                        Build();
                    }
                }),
                DispatcherPriority.Background);
        }
    }

    private void Build()
    {
        _content.Children.Clear();

        var skip = new M3IconButton(M3Glyph.Close) { Diameter = 36, HorizontalAlignment = HorizontalAlignment.Right, ToolTip = "Skip the guide" };
        skip.AccessibilityName = "Skip the guide";
        skip.Click += (_, _) => Finish();

        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var brand = M3Layout.Column(
            4,
            M3Typography.Create(_step switch
            {
                0 => "Meet JoyNect",
                1 => "One driver, one time",
                _ => "Pair a controller",
            }, M3Typography.HeadlineMedium),
            M3Typography.Create(_step switch
            {
                0 => "Switch controllers, played as Xbox 360 pads.",
                1 => "Windows needs the ViGEmBus driver before a game can see a pad.",
                _ => "Hold SYNC for two seconds, then add it in Windows Bluetooth settings.",
            }, M3Typography.BodyMedium, ColorRole.OnSurfaceVariant));

        header.Children.Add(brand);
        Grid.SetColumn(skip, 1);
        header.Children.Add(skip);

        _content.Children.Add(header);
        _content.Children.Add(new Border { Height = 22 });

        switch (_step)
        {
            case 0:
                _content.Children.Add(BuildWelcome());
                break;
            case 1:
                _content.Children.Add(BuildDriver());
                break;
            default:
                _content.Children.Add(BuildPairing());
                break;
        }

        _content.Children.Add(new Border { Height = 28 });
        _content.Children.Add(BuildFooter());
    }

    private UIElement BuildWelcome()
    {
        var theme = M3ThemeManager.Current;
        var rows = new StackPanel { Orientation = Orientation.Vertical };

        rows.Children.Add(Point(M3Glyph.Bluetooth, "Connect", "Bluetooth or USB. JoyNect reads the controller's calibration and keeps it connected."));
        rows.Children.Add(Point(M3Glyph.Swap, "Translate", "Buttons and sticks are mapped by physical position, so A stays where your thumb expects it."));
        rows.Children.Add(Point(M3Glyph.Gamepad, "Play", "Games see a standard Xbox 360 pad through XInput. Nothing to configure per game."));

        var note = Widgets.Paragraph(
            "JoyNect runs as a normal user. It never installs a driver by itself and never changes a controller's settings without being asked.",
            M3Typography.BodySmall);

        note.Margin = new Thickness(0, 18, 0, 0);

        return new StackPanel { Children = { rows, note } };
    }

    private UIElement BuildDriver()
    {
        var available = _state.Driver.Available;
        var banner = Widgets.Banner(
            available ? "The driver is ready" : "The driver is not installed",
            available
                ? _state.Driver.Summary
                : _state.Driver.Remediation ?? "Install ViGEmBus, then choose Check again.",
            available ? M3Glyph.Check : M3Glyph.Warning,
            available ? null : Widgets.LinkButton("Get ViGEmBus", JoyNect.Core.Output.DriverProbe.OfficialInstallerUri),
            error: !available);

        var check = new M3Button("Check again", M3ButtonVariant.Outlined, M3Glyph.Refresh);
        check.Click += (_, _) =>
        {
            _state.RefreshDriver();
            RebuildSoon();
        };

        var explain = Widgets.Paragraph(
            "ViGEmBus is a signed kernel driver that creates virtual Xbox 360 pads. It is no longer maintained, but it is still the most reliable way to do this on Windows, and JoyNect links to its last official release rather than shipping an installer.",
            M3Typography.BodySmall);

        explain.Margin = new Thickness(0, 16, 0, 16);

        return new StackPanel { Children = { banner, explain, check } };
    }

    private UIElement BuildPairing()
    {
        var controllers = _state.Controllers;
        var ready = controllers.Count(c => c.IsReady);

        var banner = Widgets.Banner(
            ready > 0 ? $"{ready} controller{(ready == 1 ? " is" : "s are")} ready" : "Waiting for a controller",
            ready > 0
                ? "You can close this guide and start playing; JoyNect keeps watching for more."
                : "Press a button on a paired controller to wake it, or hold SYNC to pair a new one.",
            ready > 0 ? M3Glyph.Check : M3Glyph.Search,
            action: null,
            error: false);

        var scan = new M3Button("Scan now", M3ButtonVariant.Tonal, M3Glyph.Refresh);
        scan.Click += (_, _) =>
        {
            _state.Rescan();
            RebuildSoon();
        };

        var steps = new StackPanel { Orientation = Orientation.Vertical };
        steps.Children.Add(Point(M3Glyph.Refresh, "1 · Hold SYNC", "The small round button on top of the controller, until the lights chase each other."));
        steps.Children.Add(Point(M3Glyph.Bluetooth, "2 · Add it in Windows", "Settings → Bluetooth & devices → Add device → Bluetooth."));
        steps.Children.Add(Point(M3Glyph.Check, "3 · Come back here", "JoyNect picks the controller up within a couple of seconds."));

        var openSettings = Widgets.LinkButton("Open Windows Bluetooth", "ms-settings:bluetooth");

        var bottom = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 18, 0, 0),
            Children = { openSettings, scan },
        };

        scan.Margin = new Thickness(8, 0, 0, 0);

        return new StackPanel { Children = { banner, new Border { Height = 18 }, steps, bottom } };
    }

    private UIElement BuildFooter()
    {
        // Every rebuild owns its own elements. The dots used to be a field that was re-parented
        // into each new footer, which throws once the old footer still logically owns it — and it
        // left the guide dead after the first Continue click.
        var dots = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
        };

        for (var i = 0; i < 3; i++)
        {
            dots.Children.Add(new Border
            {
                Width = i == _step ? 22 : 8,
                Height = 8,
                CornerRadius = new CornerRadius(4),
                Background = i == _step ? M3ThemeManager.Current.Primary : M3ThemeManager.Current.OutlineVariant,
                Margin = new Thickness(0, 0, 6, 0),
            });
        }

        var back = new M3Button("Back", M3ButtonVariant.Text, M3Glyph.ArrowBack)
        {
            Visibility = _step == 0 ? Visibility.Collapsed : Visibility.Visible,
        };

        back.Click += (_, _) => GoToStep(_step - 1);

        var next = new M3Button(_step == 2 ? "Start using JoyNect" : "Continue", M3ButtonVariant.Filled);
        next.Click += (_, _) =>
        {
            if (_step == 2)
            {
                Finish();
                return;
            }

            GoToStep(_step + 1);
        };

        _primaryAction = next;

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(dots);

        var actions = M3Layout.Row(8, back, next);
        Grid.SetColumn(actions, 1);
        grid.Children.Add(actions);
        return grid;
    }

    /// <summary>
    /// Moves to another step after the current input event has unwound. Rebuilding a panel from
    /// inside one of its own buttons is legal in WPF but makes focus and event routing needlessly
    /// fragile, so the guide's own transitions are queued like any other state change.
    /// </summary>
    private void GoToStep(int step)
    {
        if (_finished || _transitionPending)
        {
            return;
        }

        _transitionPending = true;
        _step = Math.Clamp(step, 0, 2);
        Dispatcher.BeginInvoke(
            new Action(() =>
            {
                _transitionPending = false;
                if (_finished)
                {
                    return;
                }

                Build();
                _primaryAction?.Focus();
            }),
            DispatcherPriority.Input);
    }

    private void RebuildSoon()
    {
        if (_finished)
        {
            return;
        }

        Dispatcher.BeginInvoke(
            new Action(() =>
            {
                if (!_finished)
                {
                    Build();
                }
            }),
            DispatcherPriority.Input);
    }

    private static UIElement Point(M3Glyph glyph, string title, string body)
    {
        var theme = M3ThemeManager.Current;
        var icon = new Border
        {
            Width = 42,
            Height = 42,
            CornerRadius = new CornerRadius(14),
            Background = theme.SecondaryContainer,
            Child = M3Icons.Create(glyph, 21, theme.OnSecondaryContainer),
            VerticalAlignment = VerticalAlignment.Top,
        };

        var text = M3Layout.Column(
            3,
            M3Typography.Create(title, M3Typography.TitleSmall),
            M3Typography.Create(body, M3Typography.BodySmall, ColorRole.OnSurfaceVariant));

        ((TextBlock)text.Children[1]).MaxWidth = 470;

        var row = M3Layout.Row(14, icon, text);
        row.Margin = new Thickness(0, 0, 0, 14);
        return row;
    }

    private void Finish()
    {
        if (_finished)
        {
            return;
        }

        _finished = true;

        // The window removes this overlay when the event fires. Let the click finish first so the
        // guide is never detached from inside its own handler.
        Dispatcher.BeginInvoke(
            new Action(() => Completed?.Invoke(this, EventArgs.Empty)),
            DispatcherPriority.Normal);
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Finish();
            e.Handled = true;
        }
    }
}
