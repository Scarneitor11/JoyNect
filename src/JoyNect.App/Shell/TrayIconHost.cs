using System.Drawing;
using System.Windows;
using System.Windows.Controls;
using Hardcodet.Wpf.TaskbarNotification;
using JoyNect.App.Services;
using JoyNect.Ui.Theme;

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
    private readonly ContextMenu _menu;
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
        _menu = new ContextMenu
        {
            Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint,
        };

        _menu.Opened += (_, _) => BuildMenu();

        _icon = new TaskbarIcon
        {
            Icon = _appIcon,
            ContextMenu = _menu,
            ToolTipText = "JoyNect",
            Visibility = Visibility.Visible,
        };

        _icon.TrayMouseDoubleClick += (_, _) => _open();
        _icon.TrayLeftMouseUp += (_, _) => _open();
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
        _icon.Dispose();
        _appIcon.Dispose();
    }

    private void OnStateChanged() => Refresh();

    private void BuildMenu()
    {
        var theme = M3ThemeManager.Current;

        _menu.Background = theme.SurfaceContainerHigh;
        _menu.Foreground = theme.OnSurface;
        _menu.BorderBrush = theme.OutlineVariant;
        _menu.Padding = new Thickness(4);
        _menu.Items.Clear();

        _menu.Items.Add(Item("Open JoyNect", _open));
        _menu.Items.Add(Item(_state.IsRunning ? "Pause connecting" : "Resume connecting", () =>
        {
            _state.SetRunning(!_state.IsRunning);
            Refresh();
        }));

        _menu.Items.Add(new Separator());

        var scan = Item("Scan for controllers", _state.Rescan);
        scan.IsEnabled = _state.IsRunning;
        _menu.Items.Add(scan);

        _menu.Items.Add(new Separator());
        _menu.Items.Add(Item("Exit JoyNect", _exit));
    }

    private MenuItem Item(string header, Action action)
    {
        var theme = M3ThemeManager.Current;
        var item = new MenuItem
        {
            Header = header,
            FontFamily = M3Typography.Text,
            FontSize = 13,
            Padding = new Thickness(12, 7, 12, 7),
            Background = theme.SurfaceContainerHigh,
            Foreground = theme.OnSurface,
        };

        item.Click += (_, _) => action();
        return item;
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
