using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using JoyNect.Core.Design;
using JoyNect.Ui.Theme;

namespace JoyNect.Ui.Controls;

/// <summary>
/// A Material dropdown: a field that opens a menu of choices.
/// </summary>
/// <remarks>
/// WPF's ComboBox templates bring their own chrome and a popup list that cannot be styled into a
/// Material menu without rewriting the whole control, so this is a small control built on the same
/// button behaviour as everything else. The menu is a popup so it can leave the window, and it closes
/// on selection, on Escape and on clicking away, which is what makes a dropdown feel finished.
/// </remarks>
public sealed class M3Select : M3Interactive
{
    private readonly TextBlock _label;
    private readonly M3Icon _chevron;
    private readonly Popup _popup;
    private readonly Border _menu;
    private readonly StackPanel _menuItems = new();
    private readonly List<string> _options = [];
    private readonly List<M3Button> _buttons = [];
    private readonly TranslateTransform _menuTransform = new();

    /// <summary>How far above its rest position the sheet starts when it slides in, in device-independent pixels.</summary>
    private const double SheetSlide = 12.0;

    private string _value = string.Empty;
    private int _highlighted = -1;

    public M3Select(string accessibleName = "Choose an option")
    {
        Height = 40;
        MinWidth = 160;
        Shape = M3ShapeToken.Small;
        Padding = new Thickness(16, 0, 12, 0);
        AccessibilityName = accessibleName;

        _label = M3Typography.Create(string.Empty, M3Typography.BodyLarge, ColorRole.OnSurface);
        _label.TextTrimming = TextTrimming.CharacterEllipsis;
        _label.HorizontalAlignment = HorizontalAlignment.Left;

        _chevron = new M3Icon { Glyph = M3Glyph.ChevronDown, Size = 18, Margin = new Thickness(12, 0, 0, 0) };

        var layout = new Grid();
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        layout.Children.Add(_label);
        Grid.SetColumn(_chevron, 1);
        layout.Children.Add(_chevron);
        ContentHost.Children.Add(layout);

        _menu = new Border
        {
            Padding = new Thickness(6),
            CornerRadius = new CornerRadius(M3Shape.Medium),
            Child = _menuItems,
            MinWidth = 180,
            RenderTransform = _menuTransform,
        };

        _popup = new Popup
        {
            Placement = PlacementMode.Bottom,
            AllowsTransparency = true,
            StaysOpen = false,
            PopupAnimation = PopupAnimation.None,
            Child = _menu,
            VerticalOffset = 4,
        };

        _popup.Opened += (_, _) => UpdateMenuTheme();
        Click += (_, _) => ToggleMenu();
    }

    /// <summary>The sheet's slide transform, exposed so tests can check that it moves.</summary>
    public TranslateTransform MenuTransform => _menuTransform;

    /// <summary>The sheet's opacity, exposed so tests can check that it fades.</summary>
    public double MenuOpacity => _menu.Opacity;

    /// <summary>Raised when the chosen option changes.</summary>
    public event EventHandler? SelectionChanged;

    /// <summary>The current option's text.</summary>
    public string Value
    {
        get => _value;
        set
        {
            var text = value ?? string.Empty;
            if (string.Equals(_value, text, StringComparison.Ordinal))
            {
                return;
            }

            _value = text;
            _label.Text = text;
            AutomationProperties.SetItemStatus(this, text);
            UpdateButtons();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Replaces the options.</summary>
    public void SetOptions(IEnumerable<string> options, string? selected = null)
    {
        _options.Clear();
        _options.AddRange(options);
        _menuItems.Children.Clear();
        _buttons.Clear();

        foreach (var option in _options)
        {
            var text = option;
            var button = new M3Button(text, M3ButtonVariant.Text)
            {
                Stretch = true,
                MinHeight = 36,
                Padding = new Thickness(12, 0, 12, 0),
                Shape = M3ShapeToken.Small,
                HorizontalContentAlignment = HorizontalAlignment.Left,
            };

            button.Click += (_, _) =>
            {
                Value = text;
                CloseMenu();
            };

            _menuItems.Children.Add(button);
            _buttons.Add(button);
        }

        _value = selected ?? string.Empty;
        _label.Text = _value;
        UpdateButtons();
        _menu.Width = Math.Max(ActualWidth, 180);
    }

    /// <summary>Re-applies the theme now, for a page that repaints itself.</summary>
    public void ApplyTheme() => RefreshTheme();

    /// <inheritdoc />
    protected override void RefreshTheme()
    {
        // Called from the base constructor before the fields exist, so it has to be null safe.
        if (_chevron is null || _menu is null)
        {
            return;
        }

        ContainerRole = ColorRole.SurfaceContainerHighest;
        StateLayerRole = ColorRole.OnSurface;
        base.RefreshTheme();

        _chevron.Foreground = M3ThemeManager.Current.OnSurfaceVariant;
        UpdateMenuTheme();
    }

    /// <inheritdoc />
    protected override void OnKeyDown(KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down when !_popup.IsOpen:
            case Key.Enter when !_popup.IsOpen:
            case Key.Space when !_popup.IsOpen:
                OpenMenu();
                e.Handled = true;
                break;
            case Key.Escape when _popup.IsOpen:
                CloseMenu();
                e.Handled = true;
                break;
            case Key.Up or Key.Down when _popup.IsOpen:
                StepThroughOptions(e.Key == Key.Down ? 1 : -1);
                e.Handled = true;
                break;
        }

        base.OnKeyDown(e);
    }

    /// <inheritdoc />
    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        _menu.Width = Math.Max(ActualWidth, 180);
    }

    /// <summary>Opens the menu, for a caller that wants to drive the control.</summary>
    public void OpenMenu()
    {
        _popup.PlacementTarget = this;
        _popup.HorizontalOffset = 0;
        UpdateMenuTheme();
        M3PopupSheet.OpenSheet(_popup, _menuTransform, _menu, SheetSlide);
        var index = _options.IndexOf(_value);
        _highlighted = index >= 0 ? index : 0;
        UpdateHighlight();
    }

    /// <summary>Closes the menu.</summary>
    public void CloseMenu() => M3PopupSheet.CloseSheet(_popup, _menuTransform, _menu, SheetSlide);

    /// <summary>Applies theme colours without recursing through <see cref="RefreshTheme"/>.</summary>
    private void UpdateMenuTheme()
    {
        var theme = M3ThemeManager.Current;
        _menu.Background = theme.SurfaceContainerHigh;
        _menu.Effect = M3Elevation.For(3);
        _menu.BorderBrush = theme.OutlineVariant;
        _menu.BorderThickness = new Thickness(1);
        _chevron.Foreground = theme.OnSurfaceVariant;

        foreach (var button in _buttons)
        {
            button.SetAppearance(ColorRole.SurfaceContainerHigh, ColorRole.OnSurface);
        }
    }

    private void ToggleMenu()
    {
        if (_popup.IsOpen)
        {
            CloseMenu();
        }
        else
        {
            OpenMenu();
        }
    }

    private void StepThroughOptions(int delta)
    {
        if (_options.Count == 0)
        {
            return;
        }

        _highlighted = (_highlighted + delta + _options.Count) % _options.Count;
        Value = _options[_highlighted];
        UpdateHighlight();
    }

    private void UpdateHighlight()
    {
        for (var i = 0; i < _buttons.Count; i++)
        {
            var isHighlighted = i == _highlighted;
            _buttons[i].SetAppearance(
                isHighlighted ? ColorRole.SecondaryContainer : ColorRole.SurfaceContainerHigh,
                isHighlighted ? ColorRole.OnSecondaryContainer : ColorRole.OnSurface);
        }
    }

    private void UpdateButtons()
    {
        for (var i = 0; i < _buttons.Count && i < _options.Count; i++)
        {
            var selected = string.Equals(_options[i], _value, StringComparison.Ordinal);
            _buttons[i].Icon = selected ? M3Glyph.Check : null;
        }
    }
}
