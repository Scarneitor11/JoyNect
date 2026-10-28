using System.Windows;
using System.Windows.Controls;
using JoyNect.App.Services;
using JoyNect.Ui.Controls;
using JoyNect.Ui.Theme;

namespace JoyNect.App.Pages;

/// <summary>
/// A page: a scrolling column with a large title, rebuilt when the theme changes.
/// </summary>
/// <remarks>
/// Pages build their content in code and rebuild it when the theme changes. That sounds wasteful, but
/// it is what keeps a theme switch honest: no page can forget to update a colour, because every
/// colour is read again. Rebuilding only happens for the page on screen, a few times a session at
/// most, and never on a data refresh.
/// </remarks>
public abstract class AppPage : Grid
{
    private readonly StackPanel _body = new() { Orientation = Orientation.Vertical };
    private readonly ScrollViewer _scroll;
    private M3Theme? _builtWith;
    private bool _built;

    protected AppPage(string key, string title, string subtitle, M3Glyph glyph)
    {
        Key = key;
        Title = title;
        Subtitle = subtitle;
        Glyph = glyph;

        Background = M3ThemeManager.Current.Surface;

        _scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Focusable = false,
            Padding = new Thickness(40, 28, 40, 40),
            Content = _body,
        };

        _body.MaxWidth = 1180;

        // Stretch, not Left: a page whose content is narrow would otherwise shrink its whole column
        // and leave cards ragged against the right edge of a wide window.
        _body.HorizontalAlignment = HorizontalAlignment.Stretch;
        Children.Add(_scroll);
    }

    /// <summary>The destination key.</summary>
    public string Key { get; }

    /// <summary>The page's name, used in window titles and the accessibility tree.</summary>
    public string Title { get; }

    /// <summary>The one line explanation under the title.</summary>
    public string Subtitle { get; }

    /// <summary>The icon shown in the navigation rail.</summary>
    public M3Glyph Glyph { get; }

    /// <summary>Raised when a page wants another page opened.</summary>
    public event EventHandler<string>? NavigateRequested;

    /// <summary>The page's running state, available while it is built.</summary>
    protected IAppState State { get; private set; } = null!;

    /// <summary>The scrolling column pages fill.</summary>
    protected StackPanel Body => _body;

    /// <summary>Builds the page for the given state.</summary>
    public void Attach(IAppState state)
    {
        State = state;
        _built = false;
    }

    /// <summary>Draws the page if it has never been drawn, or the theme changed since it was.</summary>
    public void EnsureBuilt()
    {
        if (_built && ReferenceEquals(_builtWith, M3ThemeManager.Current))
        {
            Refresh();
            return;
        }

        _built = true;
        _builtWith = M3ThemeManager.Current;
        Background = M3ThemeManager.Current.Surface;
        _body.Children.Clear();
        Build();
        Refresh();
    }

    /// <summary>
    /// Rebuilds the page if it has been built before.
    /// </summary>
    /// <remarks>
    /// A hidden page that was never built needs no repaint: it will read the current theme when it is
    /// first shown. Rebuilding only what exists keeps a theme change from constructing eight pages.
    /// </remarks>
    public void Reload()
    {
        if (_built)
        {
            RebuildPage();
        }
    }

    /// <summary>Forces a rebuild on the next paint, for a page whose data changed shape.</summary>
    protected void RebuildPage()
    {
        _built = false;
        EnsureBuilt();
    }

    /// <summary>Called when the page becomes visible.</summary>
    public virtual void OnShown()
    {
    }

    /// <summary>Called when another page replaces this one.</summary>
    public virtual void OnHidden()
    {
    }

    /// <summary>Copies live values into the controls that show them. Called a few times a second.</summary>
    public virtual void Refresh()
    {
    }

    /// <summary>Builds the page content into <see cref="Body"/>.</summary>
    protected abstract void Build();

    /// <summary>Opens another page.</summary>
    protected void Navigate(string key) => NavigateRequested?.Invoke(this, key);

    /// <summary>A standard page header.</summary>
    protected UIElement Hero(string? subtitle = null, params UIElement[] actions) =>
        M3Layout.Hero(Title, subtitle ?? Subtitle, actions);

    /// <summary>A section with a heading and supporting text.</summary>
    protected static StackPanel Section(string title, string? supporting, UIElement content, double spacing = 16) =>
        M3Layout.Section(title, supporting, content, spacing);
}
