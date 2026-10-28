using System.Windows;
using System.Windows.Threading;
using JoyNect.App.Services;
using JoyNect.App.Shell;
using JoyNect.Core.Settings;
using JoyNect.Ui.Theme;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace JoyNect.App;

/// <summary>
/// The WPF application: owns the state, the window and the tray icon.
/// </summary>
/// <remarks>
/// Everything is constructed here rather than through a container, because the object graph is three
/// objects deep and a container would hide the one thing worth seeing: the window is the only thing
/// that keeps the app alive, and quitting is explicit.
/// </remarks>
internal sealed class JoyNectApplication : Application
{
    private readonly bool _startHidden;
    private ILoggerFactory? _loggers;
    private ILogger<JoyNectApplication>? _logger;
    private AppState? _state;
    private MainWindow? _window;
    private TrayIconHost? _tray;

    public JoyNectApplication(bool startHidden)
    {
        _startHidden = startHidden;
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        DispatcherUnhandledException += OnUnhandledException;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _loggers = LoggingSetup.Create();
        _logger = _loggers.CreateLogger<JoyNectApplication>();
        M3Scrollbars.Install(this);
        _logger.LogInformation("JoyNect starting (hidden: {Hidden})", _startHidden);

        try
        {
            _state = new AppState(
                new SettingsStore(SettingsStore.DefaultPath, _loggers.CreateLogger<SettingsStore>()),
                _loggers);
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Could not start");
            MessageBox.Show(
                $"JoyNect could not start.\n\n{ex.Message}",
                "JoyNect",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        M3ThemeManager.Changed += OnThemeChanged;

        _window = new MainWindow(_state);
        MainWindow = _window;
        _window.Closed += (_, _) => Shutdown();

        try
        {
            _tray = new TrayIconHost(_state, ShowWindow, ExitApplication);
            _window.Tray = _tray;
        }
        catch (Exception ex)
        {
            // The window is still usable; only close-to-tray has to be abandoned in this case.
            _logger.LogError(ex, "The notification area icon could not be created");
        }

        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        _state.Changed += OnStateChanged;

        if (_startHidden)
        {
            _logger.LogInformation("Started in the notification area");
            return;
        }

        ShowWindow();

        if (!_state.Settings.OnboardingComplete)
        {
            _window.ShowOnboarding();
        }

        if (!_state.Driver.Available)
        {
            _window.ShowSnackbar(
                "ViGEmBus is not installed: controllers will connect, but games will not see a pad.",
                error: true);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _logger?.LogInformation("JoyNect exiting");

        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        M3ThemeManager.Changed -= OnThemeChanged;

        if (_state is not null)
        {
            _state.Changed -= OnStateChanged;
        }

        _tray?.Dispose();
        _state?.Dispose();
        _loggers?.Dispose();

        base.OnExit(e);
    }

    private void ShowWindow()
    {
        if (_window is null)
        {
            return;
        }

        if (!_window.IsVisible)
        {
            _window.Show();
        }

        if (_window.WindowState == WindowState.Minimized)
        {
            _window.WindowState = WindowState.Normal;
        }

        _window.Activate();
        _window.Topmost = true;
        _window.Topmost = false;
    }

    private void ExitApplication()
    {
        if (_window is null)
        {
            Shutdown();
            return;
        }

        _window.RequestExit();
    }

    private void OnStateChanged() => _tray?.Refresh();

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is not (UserPreferenceCategory.General or UserPreferenceCategory.Color or UserPreferenceCategory.VisualStyle))
        {
            return;
        }

        // The theme manager decides whether anything actually changed; a repaint follows its event.
        M3ThemeManager.Refresh();
    }

    private void OnThemeChanged(object? sender, EventArgs e) =>
        _logger?.LogDebug("Theme changed (dark: {Dark})", M3ThemeManager.Current.IsDark);

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _logger?.LogError(e.Exception, "Unhandled exception on the UI thread");

        // A controller app that dies because one HID event was malformed is worse than one that reports
        // the problem and keeps the remaining controllers working.
        e.Handled = true;
        _window?.ShowSnackbar($"Something went wrong: {e.Exception.Message}", error: true);
    }
}
