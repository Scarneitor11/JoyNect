using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using JoyNect.Core.Design;
using JoyNect.Ui.Theme;

namespace JoyNect.Ui.Controls;

/// <summary>
/// A row of Material segmented buttons with single selection.
/// </summary>
/// <remarks>
/// Used wherever a small, mutually exclusive choice has to be visible at once — light or dark, one
/// Joy-Con or two. Selection is managed here rather than by the caller so the group can never end up
/// with two selected segments. The selected segment is marked by a single indicator that slides from
/// the old segment to the new one, so the segments themselves stay transparent over it.
/// </remarks>
public sealed class M3SegmentedButtons : Border
{
    private readonly Grid _grid = new();
    private readonly List<Segment> _segments = [];
    private readonly Border _indicator = new()
    {
        IsHitTestVisible = false,
        HorizontalAlignment = HorizontalAlignment.Left,
        VerticalAlignment = VerticalAlignment.Stretch,
        Visibility = Visibility.Collapsed,
    };

    private readonly TranslateTransform _indicatorOffset = new();
    private int _selectedIndex = -1;

    public M3SegmentedButtons()
    {
        CornerRadius = new CornerRadius(20);
        Height = 40;
        BorderThickness = new Thickness(1);
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;
        Child = _grid;
        HorizontalAlignment = HorizontalAlignment.Left;
        _indicator.RenderTransform = _indicatorOffset;
        _grid.SizeChanged += (_, _) => MoveIndicator(animate: false);
        RefreshTheme();
        Loaded += (_, _) => M3ThemeManager.Changed += OnThemeChanged;
        Unloaded += (_, _) => M3ThemeManager.Changed -= OnThemeChanged;
    }

    /// <summary>Raised when the selection changes.</summary>
    public event EventHandler? SelectionChanged;

    /// <summary>The index of the selected segment, or -1.</summary>
    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            var clamped = value < 0 || value >= _segments.Count ? -1 : value;
            if (clamped == _selectedIndex)
            {
                return;
            }

            _selectedIndex = clamped;
            for (var i = 0; i < _segments.Count; i++)
            {
                _segments[i].Button.IsSelected = i == _selectedIndex;
            }

            // Each button has an explicit appearance, so the fill has to be pushed to them here: the
            // selected-state colours are not something a button can derive from IsSelected alone.
            UpdateSelectionVisuals();
            MoveIndicator(animate: true);
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>The text of the selected segment, or null.</summary>
    public string? SelectedLabel =>
        _selectedIndex >= 0 && _selectedIndex < _segments.Count ? _segments[_selectedIndex].Label : null;

    /// <summary>Sets the segments, replacing any existing ones.</summary>
    public void SetSegments(params string[] labels)
    {
        _grid.Children.Clear();
        _grid.ColumnDefinitions.Clear();
        _segments.Clear();
        _grid.Children.Add(_indicator);

        for (var i = 0; i < labels.Length; i++)
        {
            var index = i;
            var button = new M3Button(labels[i], M3ButtonVariant.Text)
            {
                MinHeight = 38,
                Padding = new Thickness(16, 0, 16, 0),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                Shape = M3ShapeToken.Small,
                TransparentContainer = true,
            };

            button.Click += (_, _) => SelectedIndex = index;
            _grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(button, i);
            _grid.Children.Add(button);
            _segments.Add(new Segment(labels[i], button));
        }

        // Clamp the stale selection to the new segment count so a shrunk segment list never
        // leaves _selectedIndex pointing at a segment that no longer exists.
        if (_selectedIndex >= _segments.Count)
        {
            _selectedIndex = -1;
        }

        UpdateSelectionVisuals();
        RefreshTheme();
        MoveIndicator(animate: false);
    }

    /// <summary>Applies the theme and the selected/unselected appearance.</summary>
    public void RefreshTheme()
    {
        var theme = M3ThemeManager.Current;
        BorderBrush = theme.OutlineVariant;
        Background = theme.Surface;
        CornerRadius = new CornerRadius(Height / 2.0);
        _indicator.Background = theme.Brush(ColorRole.SecondaryContainer);

        // A pill made of segments: the outer corners are round, the inner ones are square, so the
        // group reads as one control instead of a row of buttons.
        for (var i = 0; i < _segments.Count; i++)
        {
            var button = _segments[i].Button;
            button.Shape = M3ShapeToken.None;
            button.SetCornerOverride(SegmentRadius(i));
        }

        UpdateSelectionVisuals();
    }

    private CornerRadius SegmentRadius(int index)
    {
        var end = (Height / 2.0) - 1;
        var last = _segments.Count - 1;
        return new CornerRadius(
            index == 0 ? end : 0,
            index == last ? end : 0,
            index == last ? end : 0,
            index == 0 ? end : 0);
    }

    // Moves the indicator to the selected column. Segments are equal star columns, so the offset is one
    // column width per index. The slide only runs between two visible positions, and never under
    // reduced motion, where the indicator jumps straight to its target.
    private void MoveIndicator(bool animate)
    {
        if (_selectedIndex < 0 || _segments.Count == 0)
        {
            _indicator.Visibility = Visibility.Collapsed;
            return;
        }

        var wasVisible = _indicator.Visibility == Visibility.Visible;
        _indicator.Visibility = Visibility.Visible;
        _indicator.CornerRadius = SegmentRadius(_selectedIndex);

        var columnWidth = _grid.ActualWidth / _segments.Count;
        _indicator.Width = columnWidth;
        var target = _selectedIndex * columnWidth;
        if (animate && wasVisible && !M3Motion.ReducedMotion)
        {
            M3Motion.SlideSelection(_indicatorOffset, TranslateTransform.XProperty, target);
            return;
        }

        _indicatorOffset.BeginAnimation(TranslateTransform.XProperty, null);
        _indicatorOffset.X = target;
    }

    private void UpdateSelectionVisuals()
    {
        for (var i = 0; i < _segments.Count; i++)
        {
            var segment = _segments[i];
            var selected = i == _selectedIndex;

            segment.Button.SetAppearance(
                selected ? ColorRole.SecondaryContainer : ColorRole.Surface,
                selected ? ColorRole.OnSecondaryContainer : ColorRole.OnSurface);

            segment.Button.AccessibilityName = segment.Label;
        }
    }

    private void OnThemeChanged(object? sender, EventArgs e) => RefreshTheme();

    private sealed record Segment(string Label, M3Button Button);
}
