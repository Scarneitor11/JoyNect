using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using JoyNect.Core.Design;
using JoyNect.Ui.Controls;
using JoyNect.Ui.Theme;

namespace JoyNect.Ui.Shell;

/// <summary>One place the app can go.</summary>
public sealed record NavDestination(string Key, string Label, M3Glyph Glyph);

/// <summary>A Material 3 navigation rail.</summary>
/// <remarks>
/// A rail rather than a drawer because this app has a handful of destinations that are all worth
/// seeing at once, and because a desktop window has the vertical space for it. Labels stay visible:
/// an icon-only rail looks tidier in a screenshot and is worse for anyone who does not already know
/// what the icons mean.
/// </remarks>
public sealed class NavRail : Border
{
    private readonly StackPanel _top = new() { Orientation = Orientation.Vertical };
    private readonly StackPanel _bottom = new() { Orientation = Orientation.Vertical };
    private readonly List<NavRailButton> _buttons = [];
    private string _selectedKey = string.Empty;
    private bool _entered;

    public NavRail()
    {
        Width = 88;
        BorderThickness = new Thickness(0, 0, 1, 0);
        Child = new Grid
        {
            Margin = new Thickness(0, 12, 0, 12),
            Children = { _top, _bottom },
        };

        _bottom.VerticalAlignment = VerticalAlignment.Bottom;
        RefreshTheme();
        Loaded += (_, _) => M3ThemeManager.Changed += OnThemeChanged;
        Unloaded += (_, _) => M3ThemeManager.Changed -= OnThemeChanged;
        Loaded += (_, _) =>
        {
            if (!_entered && _buttons.Count > 0)
            {
                PlayEntrance();
            }
        };
    }

    /// <summary>Raised when the user picks a destination.</summary>
    public event EventHandler<NavDestination>? DestinationSelected;

    /// <summary>The destination currently shown.</summary>
    public string SelectedKey => _selectedKey;

    /// <summary>
    /// Fills the rail. Destinations after <paramref name="bottomFrom"/> are pinned to the bottom.
    /// </summary>
    public void SetDestinations(IEnumerable<NavDestination> destinations, int bottomFrom)
    {
        _top.Children.Clear();
        _bottom.Children.Clear();
        _buttons.Clear();

        var index = 0;
        foreach (var destination in destinations)
        {
            var button = new NavRailButton(destination);
            button.Click += (_, _) => Select(destination.Key, raise: true);

            if (index >= bottomFrom && bottomFrom > 0)
            {
                _bottom.Children.Add(button);
            }
            else
            {
                _top.Children.Add(button);
            }

            _buttons.Add(button);
            index++;
        }

        if (_buttons.Count > 0 && string.IsNullOrEmpty(_selectedKey))
        {
            Select(_buttons[0].Destination.Key, raise: false);
        }

        if (IsLoaded && !_entered && _buttons.Count > 0)
        {
            PlayEntrance();
        }
    }

    /// <summary>
    /// Staggers the items in: each slides up and fades in on <see cref="M3Motion"/>'s entrance timing.
    /// Runs once, when the items first appear.
    /// </summary>
    private void PlayEntrance()
    {
        _entered = true;
        for (var i = 0; i < _buttons.Count; i++)
        {
            var button = _buttons[i];
            button.Body.Opacity = 0.0;
            M3Motion.Entrance(button.Slide, TranslateTransform.YProperty, 0.0, i);
            M3Motion.EntranceFade(button.Body, i);
        }
    }

    /// <summary>Shows a destination as selected without raising the event.</summary>
    public void Select(string key, bool raise)
    {
        _selectedKey = key;
        NavDestination? selected = null;

        foreach (var button in _buttons)
        {
            var isSelected = string.Equals(button.Destination.Key, key, StringComparison.Ordinal);
            button.IsSelected = isSelected;
            if (isSelected)
            {
                selected = button.Destination;
            }
        }

        if (raise && selected is not null)
        {
            DestinationSelected?.Invoke(this, selected);
        }
    }

    /// <summary>Applies the theme.</summary>
    public void RefreshTheme()
    {
        var theme = M3ThemeManager.Current;
        Background = theme.Surface;
        BorderBrush = theme.OutlineVariant;

        foreach (var button in _buttons)
        {
            button.ApplyTheme();
        }
    }

    private void OnThemeChanged(object? sender, EventArgs e) => RefreshTheme();
}

/// <summary>One rail item: an icon in a selection pill with its label underneath.</summary>
internal sealed class NavRailButton : M3Interactive
{
    private readonly Border _pill;
    private readonly M3Icon _icon;
    private readonly TextBlock _label;

    public NavRailButton(NavDestination destination)
    {
        Destination = destination;
        RenderTransform = Slide;

        Width = 72;
        Height = 56;
        Shape = M3ShapeToken.Large;
        Margin = new Thickness(8, 0, 8, 4);
        AccessibilityName = destination.Label;

        _pill = new Border
        {
            Width = 52,
            Height = 30,
            CornerRadius = new CornerRadius(15),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 8),
            Opacity = 0,
            IsHitTestVisible = false,
        };

        _icon = new M3Icon
        {
            Glyph = destination.Glyph,
            Size = 22,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

        _label = M3Typography.Create(destination.Label, M3Typography.LabelSmall);
        _label.HorizontalAlignment = HorizontalAlignment.Center;
        _label.TextAlignment = TextAlignment.Center;
        _label.FontSize = 10.5;

        var layout = new Grid();
        Body = layout;
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var iconHost = new Grid();
        iconHost.Children.Add(_pill);
        iconHost.Children.Add(_icon);
        layout.Children.Add(iconHost);

        Grid.SetRow(_label, 1);
        layout.Children.Add(_label);

        ContentHost.Children.Add(layout);
        ToolTipService.SetToolTip(this, destination.Label);
        RefreshTheme();
    }

    /// <summary>What this item opens.</summary>
    public NavDestination Destination { get; }

    /// <summary>The entrance slide; starts 12 DIPs below rest.</summary>
    public TranslateTransform Slide { get; } = new(0, 12);

    /// <summary>
    /// The icon and label layout. The entrance fade goes here, because the base control writes its own
    /// opacity on every theme refresh.
    /// </summary>
    public Grid Body { get; }

    /// <inheritdoc />
    public void ApplyTheme() => RefreshTheme();

    /// <inheritdoc />
    protected override void OnSelectionChanged()
    {
        base.OnSelectionChanged();
        RefreshTheme();
    }

    /// <inheritdoc />
    protected override void RefreshTheme()
    {
        if (_icon is null || _pill is null || _label is null)
        {
            return;
        }

        var theme = M3ThemeManager.Current;

        ContainerRole = ColorRole.Surface;
        StateLayerRole = ColorRole.OnSurface;
        base.RefreshTheme();
        Fill.Background = Brushes.Transparent;

        var selected = IsSelected;
        _pill.Background = theme.SecondaryContainer;
        _icon.Foreground = selected ? theme.OnSecondaryContainer : theme.OnSurfaceVariant;
        _label.Foreground = selected ? theme.OnSurface : theme.OnSurfaceVariant;
        _label.FontWeight = selected ? FontWeights.SemiBold : FontWeights.Medium;

        var target = selected ? 1.0 : 0.0;
        if (Math.Abs(_pill.Opacity - target) < 0.01)
        {
            return;
        }

        M3Motion.FadeSelection(_pill, target);
    }
}
