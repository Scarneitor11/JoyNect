using System.Windows;
using System.Windows.Controls;
using JoyNect.Ui.Pages;
using JoyNect.Ui.Theme;

namespace JoyNect.App.Pages;

/// <summary>
/// Hosts the design system gallery, which lives in the UI library and scrolls itself.
/// </summary>
/// <remarks>
/// It is a plain host rather than an <see cref="AppPage"/> because the gallery already owns a scroll
/// viewer and a page header; nesting it inside another page would give the window two scrollbars and
/// two titles.
/// </remarks>
internal sealed class StyleGuidePageHost : Grid
{
    private StyleGuidePage? _page;

    public StyleGuidePageHost()
    {
        Background = M3ThemeManager.Current.Surface;
        Load();
    }

    /// <summary>Rebuilds the gallery for the current theme.</summary>
    public void Reload() => Load();

    private void Load()
    {
        Children.Clear();
        Background = M3ThemeManager.Current.Surface;
        _page = new StyleGuidePage();
        Children.Add(_page);
    }
}
