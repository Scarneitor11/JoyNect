using System.Drawing;
using System.Windows;
using Hardcodet.Wpf.TaskbarNotification;
using JoyNect.App.Services;
using JoyNect.Ui.Controls;

namespace JoyNect.App.Shell;

/// <summary>
/// The notification area icon.
/// </summary>
/// <remarks>
/// Closing the window keeps JoyNect running, so the tray icon is the app's real home: it shows whether
/// connecting is on, how many controllers are live, and gives back the window. The menu is rebuilt
/// each time it opens so it can never show a stale count.
/// </remarks>
public sealed class TrayIconHost : IDisposable
{
    private readonly IAppState _state;
    private readonly TaskbarIcon _icon;
    private readonly M3PopupMenu _menu = new();
    private readonly Action _open;
    private readonly Action _exit;
    private readonly Icon _appIcon;
    private readonly System.Windows.Threading.Dispatcher _uiDispatcher;
    private bool _disposed;

    public TrayIconHost(IAppState state, Action open, Action exit)
    {
        _state = state;
        _open = open;
        _exit = exit;

        _appIcon = LoadIcon();
        // Controller state changes are raised on reader/discovery threads; marshal UI touches to the
        // dispatcher thread that owns the TaskbarIcon so we never touch DependencyObject state off-thread.
        _uiDispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;
        _icon = new TaskbarIcon
        {
            Icon = _appIcon,
            ToolTipText = "JoyNect",
            Visibility = Visibility.Visible,
        };

        _icon.TrayMouseDoubleClick += (_, _) => _open();
        _icon.TrayLeftMouseUp += (_, _) => _open();
        _icon.TrayRightMouseUp += (_, _) =>
        {
            BuildMenu();
            _menu.OpenAtPointer();
        };
        _state.Changed += OnStateChanged;
        Refresh();
    }

    /// <summary>Updates the tooltip; called by the window on a timer as well as on state changes.</summary>
    public void Refresh()
    {
        if (_disposed)
        {
            return;
        }

        // Controllers is thread-safe; only the DependencyObject touch (_icon.ToolTipText) needs the UI thread.
        var ready = _state.Controllers.Count(c => c.IsReady);
        var total = _state.Controllers.Count;
        var text = _state.IsRunning
            ? $"JoyNect — {ready} of {total} controller{(total == 1 ? string.Empty : "s")} ready"
            : "JoyNect — paused";

        if (_uiDispatcher.CheckAccess())
        {
            _icon.ToolTipText = text;
        }
        else
        {
            _uiDispatcher.BeginInvoke(new Action(() => _icon.ToolTipText = text));
        }
    }

    /// <summary>Shows a balloon notification, used once when the window is hidden to the tray.</summary>
    public void Notify(string title, string message)
    {
        try
        {
            _icon.ShowBalloonTip(title, message, BalloonIcon.Info);
        }
        catch (Exception)
        {
            // Notifications can be disabled by policy; the tray icon is still there.
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _state.Changed -= OnStateChanged;
        _menu.Dispose();
        _icon.Dispose();
        _appIcon.Dispose();
    }

    private void OnStateChanged() => Refresh();

    private void BuildMenu()
    {
        _menu.SetItems(
        [
            new M3MenuItem("Open JoyNect", _open, Icon: M3Glyph.Monitor),
            new M3MenuItem(
                _state.IsRunning ? "Pause connecting" : "Resume connecting",
                () =>
                {
                    _state.SetRunning(!_state.IsRunning);
                    Refresh();
                },
                Icon: _state.IsRunning ? M3Glyph.Power : M3Glyph.Play),
            null,
            new M3MenuItem("Scan for controllers", _state.Rescan, _state.IsRunning, M3Glyph.Refresh),
            null,
            new M3MenuItem("Exit JoyNect", _exit, Icon: M3Glyph.Close),
        ]);
    }

    private static Icon LoadIcon()
    {
        // A missing or unreadable icon must never cost us the tray icon: without it, closing the
        // window would leave a running app with no way to get it back.
        try
        {
            var resource = Application.GetResourceStream(new Uri("pack://application:,,,/assets/app.ico"));
            if (resource is not null)
            {
                return new Icon(resource.Stream);
            }
        }
        catch (Exception)
        {
            // Fall through to the executable's own icon.
        }

        try
        {
            return System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? SystemIcons.Application;
        }
        catch (Exception)
        {
            return SystemIcons.Application;
        }
    }
}
