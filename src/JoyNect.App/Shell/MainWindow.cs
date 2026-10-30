using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using System.Windows.Threading;
using JoyNect.App.Pages;
using JoyNect.App.Services;
using JoyNect.Core.Design;
using JoyNect.Core.Devices;
using JoyNect.Ui.Controls;
using JoyNect.Ui.Shell;
using JoyNect.Ui.Theme;

namespace JoyNect.App.Shell;

/// <summary>
/// The window: a top app bar, a navigation rail and one page at a time.
/// </summary>
/// <remarks>
/// The window chrome is drawn rather than borrowed. Windows' own title bar cannot be tinted with the
/// Material scheme on Windows 10, and a grey title bar above a tinted surface is exactly the seam that
/// makes a designed app look themed rather than designed. The trade is that dragging, double-click to
/// maximise and the system menu have to be wired up by hand, which is what <see cref="WindowChrome"/>
/// and <see cref="OnTopBarMouseDown"/> do.
/// </remarks>
public sealed class MainWindow : Window
{
    private static readonly NavDestination[] Destinations =
    [
        new("home", "Home", M3Glyph.Gamepad),
        new("devices", "Devices", M3Glyph.Bluetooth),
        new("mapping", "Mapping", M3Glyph.Swap),
        new("motion", "Motion", M3Glyph.Motion),
        new("lights", "Lights", M3Glyph.Vibration),
        new("diagnostics", "Diagnostics", M3Glyph.Pulse),
        new("about", "About", M3Glyph.Info),
        new("settings", "Settings", M3Glyph.Sliders),
    ];

    private readonly IAppState _state;
    private readonly Grid _root = new();
    private readonly Grid _pageHost = new() { ClipToBounds = true };
    private readonly NavRail _rail = new();
    private readonly Border _topBar;
    private readonly StackPanel _pills = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    private readonly Border _snackbar;
    private readonly TextBlock _snackbarText;
    private readonly M3Button _snackbarAction;
    private readonly DispatcherTimer _tick;
    private readonly DispatcherTimer _snackbarTimer;
    private readonly Dictionary<string, FrameworkElement> _pages = [];
    private readonly Dictionary<string, AppPage> _appPages = [];
    private string _pillSignature = string.Empty;
    private FrameworkElement? _currentPage;
    private AppPage? _currentAppPage;
    private OnboardingOverlay? _onboarding;
    private bool _exiting;

    public MainWindow(IAppState state)
    {
        _state = state;

        Title = "JoyNect";
        Width = 1240;
        Height = 860;
        MinWidth = 1000;
        MinHeight = 680;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.CanResize;
        Background = M3ThemeManager.Current.Surface;
        Icon = new BitmapImage(new Uri("pack://application:,,,/assets/app.png"));
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;

        WindowChrome.SetWindowChrome(this, new WindowChrome
        {
            CaptionHeight = 0,
            ResizeBorderThickness = new Thickness(6),
            GlassFrameThickness = new Thickness(0),
            CornerRadius = new CornerRadius(0),
            UseAeroCaptionButtons = false,
        });

        _topBar = BuildTopBar();

        _snackbarText = M3Typography.Create(string.Empty, M3Typography.BodyMedium);
        _snackbarAction = new M3Button("Dismiss", M3ButtonVariant.Text) { Visibility = Visibility.Collapsed, Margin = new Thickness(12, 0, 0, 0) };
        _snackbarAction.Click += (_, _) => HideSnackbar();

        var snackbarContent = new Grid();
        snackbarContent.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        snackbarContent.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        snackbarContent.Children.Add(_snackbarText);
        Grid.SetColumn(_snackbarAction, 1);
        snackbarContent.Children.Add(_snackbarAction);

        _snackbar = new Border
        {
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(18, 12, 14, 12),
            Margin = new Thickness(0, 0, 0, 24),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            MaxWidth = 620,
            Visibility = Visibility.Collapsed,
            Child = snackbarContent,
            Effect = M3Elevation.For(3),
            IsHitTestVisible = true,
        };

        _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(60) });
        _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _root.Children.Add(_topBar);

        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(88) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        body.Children.Add(_rail);
        Grid.SetColumn(_pageHost, 1);
        body.Children.Add(_pageHost);
        Grid.SetRow(body, 1);
        _root.Children.Add(body);

        Grid.SetRowSpan(_snackbar, 2);
        _root.Children.Add(_snackbar);

        Content = _root;

        _rail.SetDestinations(Destinations, bottomFrom: 7);
        _rail.DestinationSelected += (_, destination) => NavigateTo(destination.Key);

        _tick = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _tick.Tick += (_, _) =>
        {
            _currentAppPage?.Refresh();
            Tray?.Refresh();
        };

        _snackbarTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4.5) };
        _snackbarTimer.Tick += (_, _) => HideSnackbar();

        _state.Notified += OnNotified;
        M3ThemeManager.Changed += OnThemeChanged;
        Loaded += (_, _) =>
        {
            _tick.Start();
            ApplyChrome();
            NavigateTo(InitialPage(), force: true);
        };

        Closing += OnClosing;
        PreviewKeyDown += OnPreviewKeyDown;
    }

    /// <summary>The tray icon, attached once the application has created it.</summary>
    public TrayIconHost? Tray { get; set; }

    /// <summary>The root visual, exposed so the screenshot renderer can draw the shell off screen.</summary>
    public FrameworkElement CaptureRoot => _root;

    /// <summary>Opens a page by key. Public so the tray and the renderer can drive it.</summary>
    public void NavigateTo(string key, bool force = false)
    {
        if (!force && string.Equals(_currentAppPage?.Key ?? key, key, StringComparison.Ordinal) && _currentPage is not null)
        {
            return;
        }

        if (!_pages.TryGetValue(key, out var page))
        {
            page = CreatePage(key);
            page.Visibility = Visibility.Collapsed;
            _pages[key] = page;
            _pageHost.Children.Add(page);
            Grid.SetRowSpan(page, 1);
        }

        if (_currentPage == page && !force)
        {
            return;
        }

        _currentAppPage?.OnHidden();

        foreach (var child in _pageHost.Children)
        {
            if (child is FrameworkElement element)
            {
                element.Visibility = Visibility.Collapsed;
            }
        }

        _currentPage = page;
        _currentAppPage = page as AppPage;
        _currentAppPage?.Attach(_state);
        _currentAppPage?.EnsureBuilt();
        _currentAppPage?.OnShown();

        page.Visibility = Visibility.Visible;
        page.Opacity = 1.0;
        _rail.Select(key, raise: false);
        Title = $"JoyNect — {Destinations.FirstOrDefault(d => d.Key == key)?.Label ?? "Home"}";

        if (!M3Motion.ReducedMotion && !force)
        {
            page.Opacity = 0.0;
            var translate = new TranslateTransform(18, 0);
            page.RenderTransform = translate;

            page.BeginAnimation(
                OpacityProperty,
                new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(M3Duration.Medium2)) { EasingFunction = M3Easing.EmphasizedDecelerate });

            translate.BeginAnimation(
                TranslateTransform.XProperty,
                new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(M3Duration.Medium3)) { EasingFunction = M3Easing.EmphasizedDecelerate });
        }

        RememberPage(key);
    }

    /// <summary>Shows the first-run guide.</summary>
    public void ShowOnboarding()
    {
        if (_onboarding is not null)
        {
            return;
        }

        _onboarding = new OnboardingOverlay(_state);
        _onboarding.Completed += OnOnboardingCompleted;
        Grid.SetRowSpan(_onboarding, 2);
        _root.Children.Add(_onboarding);
        _onboarding.Focus();
    }

    private void OnOnboardingCompleted(object? sender, EventArgs e) => CloseOnboarding();

    /// <summary>Applies the current theme to the chrome; called when the theme changes.</summary>
    public void ApplyChrome()
    {
        var theme = M3ThemeManager.Current;
        Background = theme.Surface;
        _topBar.Background = theme.Surface;
        _rail.RefreshTheme();
        _pageHost.Background = theme.Surface;
        UpdatePills();

        foreach (var page in _appPages.Values)
        {
            page.Reload();
        }

        if (_pages.TryGetValue("style", out var style) && style is StyleGuidePageHost host)
        {
            host.Reload();
        }

        if (_onboarding is not null)
        {
            _onboarding.Reload();
        }
    }

    /// <summary>Closes the window for real, ignoring the close-to-tray preference.</summary>
    public void RequestExit()
    {
        _exiting = true;
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        _tick.Stop();
        _snackbarTimer.Stop();
        _state.Notified -= OnNotified;
        M3ThemeManager.Changed -= OnThemeChanged;
        base.OnClosed(e);
    }

    private string InitialPage()
    {
        var last = _state.Settings.LastPage;
        return Destinations.Any(d => d.Key == last) ? last : "home";
    }

    private void RememberPage(string key)
    {
        if (string.Equals(_state.Settings.LastPage, key, StringComparison.Ordinal))
        {
            return;
        }

        var settings = _state.Settings.Clone();
        settings.LastPage = key;
        _state.Save(settings);
    }

    private FrameworkElement CreatePage(string key)
    {
        if (string.Equals(key, "style", StringComparison.Ordinal))
        {
            return new StyleGuidePageHost();
        }

        AppPage page = key switch
        {
            "devices" => new DevicesPage(),
            "mapping" => new MappingPage(),
            "motion" => new MotionPage(),
            "lights" => new LightsPage(),
            "diagnostics" => new DiagnosticsPage(),
            "about" => new AboutPage(),
            "settings" => new SettingsPage(),
            _ => new HomePage(),
        };

        page.Attach(_state);
        page.NavigateRequested += (_, destination) => NavigateTo(destination);
        _appPages[page.Key] = page;
        return page;
    }

    private Border BuildTopBar()
    {
        var icon = new Image
        {
            Source = new BitmapImage(new Uri("pack://application:,,,/assets/app.png")),
            Width = 24,
            Height = 24,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var name = M3Typography.Create("JoyNect", M3Typography.TitleSmall);
        name.Margin = new Thickness(10, 0, 0, 0);

        var brand = M3Layout.Row(0, icon, name);
        brand.VerticalAlignment = VerticalAlignment.Center;
        brand.Margin = new Thickness(20, 0, 0, 0);

        _pills.VerticalAlignment = VerticalAlignment.Center;

        var theme = new M3IconButton(M3ThemeManager.Current.IsDark ? M3Glyph.Sun : M3Glyph.Moon)
        {
            Diameter = 36,
            Margin = new Thickness(4, 0, 0, 0),
            ToolTip = "Switch between light and dark",
        };

        theme.AccessibilityName = "Switch between light and dark";
        theme.Click += (_, _) =>
        {
            var settings = _state.Settings.Clone();
            settings.Appearance = M3ThemeManager.Current.IsDark ? JoyNect.Core.Settings.AppearanceMode.Light : JoyNect.Core.Settings.AppearanceMode.Dark;
            _state.Save(settings);
            ApplyChrome();
            theme.Glyph = M3ThemeManager.Current.IsDark ? M3Glyph.Sun : M3Glyph.Moon;
        };

        var diagnostics = new M3IconButton(M3Glyph.Pulse)
        {
            Diameter = 36,
            Margin = new Thickness(4, 0, 0, 0),
            ToolTip = "Diagnostics",
        };

        diagnostics.AccessibilityName = "Open diagnostics";
        diagnostics.Click += (_, _) => NavigateTo("diagnostics");

        var minimize = new M3IconButton(M3Glyph.Remove)
        {
            Diameter = 36,
            Margin = new Thickness(4, 0, 0, 0),
            ToolTip = "Minimise",
        };

        minimize.AccessibilityName = "Minimise";
        minimize.Click += (_, _) => WindowState = WindowState.Minimized;

        var close = new M3IconButton(M3Glyph.Close)
        {
            Diameter = 36,
            Margin = new Thickness(4, 0, 0, 0),
            ToolTip = "Close",
        };

        close.AccessibilityName = "Close";
        close.Click += (_, _) => Close();

        var right = M3Layout.Row(2, _pills, theme, diagnostics, minimize, close);
        right.Margin = new Thickness(0, 0, 14, 0);
        right.VerticalAlignment = VerticalAlignment.Center;

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(brand);
        Grid.SetColumn(right, 1);
        grid.Children.Add(right);

        var bar = new Border
        {
            Background = M3ThemeManager.Current.Surface,
            BorderBrush = M3ThemeManager.Current.OutlineVariant,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = grid,
        };

        bar.MouseLeftButtonDown += OnTopBarMouseDown;
        return bar;
    }

    private void OnTopBarMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        if (e.ClickCount == 2)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            e.Handled = true;
            return;
        }

        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
            // The button was released before the drag started; nothing to do.
        }
    }

    private void UpdatePills()
    {
        var ready = _state.Controllers.Count(c => c.State == ControllerLinkState.Ready);
        var total = _state.Controllers.Count;
        var signature = $"{ready}/{total}|{_state.Driver.Available}|{_state.IsRunning}";

        if (signature == _pillSignature)
        {
            return;
        }

        _pillSignature = signature;
        _pills.Children.Clear();

        var theme = M3ThemeManager.Current;

        if (!_state.IsRunning)
        {
            _pills.Children.Add(Widgets.Pill("Paused", ColorRole.ErrorContainer, ColorRole.OnErrorContainer, M3Glyph.Power));
            _pills.Children.Add(new Border { Width = 8 });
        }

        _pills.Children.Add(_state.Driver.Available
            ? Widgets.Pill("Driver ready", ColorRole.SecondaryContainer, ColorRole.OnSecondaryContainer, M3Glyph.Check)
            : Widgets.Pill("Driver missing", ColorRole.ErrorContainer, ColorRole.OnErrorContainer, M3Glyph.Warning));

        _pills.Children.Add(new Border { Width = 8 });

        _pills.Children.Add(total == 0
            ? Widgets.Pill("No controller", ColorRole.SurfaceContainerHighest, ColorRole.OnSurfaceVariant, M3Glyph.Bluetooth)
            : Widgets.Pill(
                $"{ready}/{total} ready",
                ready > 0 ? ColorRole.TertiaryContainer : ColorRole.SurfaceContainerHighest,
                ready > 0 ? ColorRole.OnTertiaryContainer : ColorRole.OnSurfaceVariant,
                M3Glyph.Gamepad));
    }

    private void OnNotified(string message, bool error)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(() => OnNotified(message, error)));
            return;
        }

        ShowSnackbar(message, error);
    }

    /// <summary>Shows a message at the bottom of the window.</summary>
    public void ShowSnackbar(string message, bool error = false)
    {
        var theme = M3ThemeManager.Current;
        _snackbar.Background = error ? theme.ErrorContainer : theme.InverseSurface;
        _snackbarText.Foreground = error ? theme.OnErrorContainer : theme.InverseOnSurface;
        _snackbarText.Text = message;
        _snackbar.Visibility = Visibility.Visible;
        _snackbarTimer.Stop();
        _snackbarTimer.Start();

        if (M3Motion.ReducedMotion)
        {
            _snackbar.Opacity = 1.0;
            return;
        }

        _snackbar.Opacity = 0.0;
        var translate = new TranslateTransform(0, 14);
        _snackbar.RenderTransform = translate;

        _snackbar.BeginAnimation(
            OpacityProperty,
            new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(M3Duration.Medium1)));

        translate.BeginAnimation(
            TranslateTransform.YProperty,
            new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(M3Duration.Medium2)) { EasingFunction = M3Easing.EmphasizedDecelerate });
    }

    private void HideSnackbar()
    {
        _snackbarTimer.Stop();

        if (_snackbar.Visibility != Visibility.Visible)
        {
            return;
        }

        if (M3Motion.ReducedMotion)
        {
            _snackbar.Visibility = Visibility.Collapsed;
            return;
        }

        var animation = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(M3Duration.Short4))
        {
            EasingFunction = M3Easing.StandardAccelerate,
        };

        animation.Completed += (_, _) => _snackbar.Visibility = Visibility.Collapsed;
        _snackbar.BeginAnimation(OpacityProperty, animation);
    }

    private void CloseOnboarding()
    {
        if (_onboarding is null)
        {
            return;
        }

        _onboarding.Completed -= OnOnboardingCompleted;
        _root.Children.Remove(_onboarding);
        _onboarding = null;

        var settings = _state.Settings.Clone();
        settings.OnboardingComplete = true;
        _state.Save(settings);
        _currentPage?.Focus();
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_exiting || !_state.Settings.CloseToTray)
        {
            return;
        }

        e.Cancel = true;
        Hide();
        Tray?.Notify("JoyNect is still running", "Controllers keep working. Open the tray icon to bring the window back, or choose Exit to stop.");
    }

    private void OnThemeChanged(object? sender, EventArgs e)
    {
        // A page that changed the theme is still inside its own click handler; repainting after it has
        // unwound avoids rebuilding the control that is currently raising the event.
        Dispatcher.BeginInvoke(new Action(ApplyChrome), DispatcherPriority.Background);
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            if (e.Key is >= Key.D1 and <= Key.D8)
            {
                var index = e.Key - Key.D1;
                if (index < Destinations.Length)
                {
                    NavigateTo(Destinations[index].Key);
                    e.Handled = true;
                }
            }
            else if (e.Key == Key.R)
            {
                _state.Rescan();
                ShowSnackbar("Scanning for controllers");
                e.Handled = true;
            }
            else if (e.Key == Key.OemComma)
            {
                NavigateTo("settings");
                e.Handled = true;
            }
        }
        else if (e.Key == Key.F1)
        {
            NavigateTo("about");
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && _onboarding is not null)
        {
            CloseOnboarding();
            e.Handled = true;
        }
    }
}
