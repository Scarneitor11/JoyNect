using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using JoyNect.Core.Design;
using JoyNect.Ui.Theme;

namespace JoyNect.Ui.Controls;

/// <summary>One row of an <see cref="M3PopupMenu"/>. A null entry passed to SetItems is a divider.</summary>
public sealed record M3MenuItem(string Label, Action Run, bool IsEnabled = true, M3Glyph? Icon = null);

/// <summary>
/// A Material popup menu that opens at the pointer or at a screen point, such as a tray icon.
/// </summary>
/// <remarks>
/// Built on the same popup, slide and fade as <see cref="M3Select"/>, so motion is owned by
/// <see cref="M3Motion"/>. It closes when an item runs or when the user clicks away, and under reduced
/// motion it snaps open and shut.
/// </remarks>
public sealed class M3PopupMenu : IDisposable
{
    private readonly Popup _popup;
    private readonly Border _menu;
    private readonly StackPanel _items = new();
    private readonly List<(string Label, M3Button Button)> _rows = [];
    private readonly TranslateTransform _menuTransform = new();
    private readonly ScaleTransform _menuScale = new();

    /// <summary>How far above its rest position the menu starts when it slides in, in device-independent pixels.</summary>
    private const double MenuSlide = 12.0;

    public M3PopupMenu()
    {
        _menu = new Border
        {
            Padding = new Thickness(6),
            CornerRadius = new CornerRadius(M3Shape.Medium),
            Child = _items,
            MinWidth = 180,
            RenderTransform = new TransformGroup { Children = { _menuScale, _menuTransform } },
            RenderTransformOrigin = new Point(0.5, 0.5),
        };

        _popup = new Popup
        {
            AllowsTransparency = true,
            StaysOpen = false,
            PopupAnimation = PopupAnimation.None,
            Child = _menu,
        };

        _popup.Opened += (_, _) => UpdateMenuTheme();
    }

    /// <summary>The menu's slide transform, exposed so tests can check that it moves.</summary>
    public TranslateTransform MenuTransform => _menuTransform;

    /// <summary>The menu's opacity, exposed so tests can check that it fades.</summary>
    public double MenuOpacity => _menu.Opacity;

    /// <summary>Whether the menu is currently shown.</summary>
    public bool IsOpen => _popup.IsOpen;

    /// <summary>The labels of the menu's items, in order, excluding dividers.</summary>
    public IReadOnlyList<string> ItemLabels => _rows.Select(r => r.Label).ToList();

    /// <summary>Finds an item's button by its label, or null when it is not in the menu.</summary>
    public M3Button? FindItem(string label) =>
        _rows.FirstOrDefault(r => string.Equals(r.Label, label, StringComparison.Ordinal)).Button;

    /// <summary>Replaces the menu's items. A null entry adds a divider.</summary>
    public void SetItems(IEnumerable<M3MenuItem?> items)
    {
        _items.Children.Clear();
        _rows.Clear();
        foreach (var entry in items)
        {
            if (entry is null)
            {
                _items.Children.Add(new Border
                {
                    Height = 1,
                    Margin = new Thickness(6, 4, 6, 4),
                    Background = M3ThemeManager.Current.OutlineVariant,
                });
                continue;
            }

            var button = new M3Button(entry.Label, M3ButtonVariant.Text, entry.Icon)
            {
                IsEnabled = entry.IsEnabled,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Margin = new Thickness(0, 1, 0, 1),
            };

            button.Click += (_, _) =>
            {
                // Close first so the menu leaves before the command runs, as a context menu item does.
                CloseMenu();
                entry.Run();
            };
            _items.Children.Add(button);
            _rows.Add((entry.Label, button));
        }
    }

    /// <summary>Opens the menu at the current mouse position.</summary>
    public void OpenAtPointer()
    {
        _popup.Placement = PlacementMode.MousePoint;
        _popup.HorizontalOffset = 0;
        _popup.VerticalOffset = 0;
        Open();
    }

/// <summary>Opens the menu at a screen point, in device-independent pixels.</summary>
    public void OpenAt(double x, double y)
    {
        _popup.Placement = PlacementMode.AbsolutePoint;
        _popup.HorizontalOffset = x;
        _popup.VerticalOffset = y;
        Open();
    }

    private void Open()
    {
        if (_popup.IsOpen)
        {
            _popup.IsOpen = false;
        }

        M3PopupSheet.OpenSheet(_popup, _menuTransform, _menu, MenuSlide, _menuScale);
    }

    /// <summary>Closes the menu with the exit motion.</summary>
    public void CloseMenu() => M3PopupSheet.CloseSheet(_popup, _menuTransform, _menu, MenuSlide, _menuScale);

    /// <summary>Closes the menu at once, without the exit motion, for owners that are going away.</summary>
    public void Dispose()
    {
        _menuTransform.BeginAnimation(TranslateTransform.YProperty, null);
        _menuScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        _menuScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        _menu.BeginAnimation(UIElement.OpacityProperty, null);
        _popup.IsOpen = false;
    }

    private void UpdateMenuTheme()
    {
        var theme = M3ThemeManager.Current;
        _menu.Background = theme.SurfaceContainerHigh;
        _menu.Effect = M3Elevation.For(3);
        _menu.BorderBrush = theme.OutlineVariant;
        _menu.BorderThickness = new Thickness(1);
    }
}
