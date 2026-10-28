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
/// with two selected segments.
/// </remarks>
public sealed class M3SegmentedButtons : Border
{
    private readonly Grid _grid = new();
    private readonly List<Segment> _segments = [];
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
            };

            button.Click += (_, _) => SelectedIndex = index;
            _grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(button, i);
            _grid.Children.Add(button);
            _segments.Add(new Segment(labels[i], button));
        }

        UpdateSelectionVisuals();
        RefreshTheme();
    }

    /// <summary>Applies the theme and the selected/unselected appearance.</summary>
    public void RefreshTheme()
    {
        var theme = M3ThemeManager.Current;
        BorderBrush = theme.OutlineVariant;
        Background = theme.Surface;
        CornerRadius = new CornerRadius(Height / 2.0);

        // A pill made of segments: the outer corners are round, the inner ones are square, so the
        // group reads as one control instead of a row of buttons.
        for (var i = 0; i < _segments.Count; i++)
        {
            var button = _segments[i].Button;
            var radius = new CornerRadius(
                i == 0 ? (Height / 2.0) - 1 : 0,
                i == _segments.Count - 1 ? (Height / 2.0) - 1 : 0,
                i == _segments.Count - 1 ? (Height / 2.0) - 1 : 0,
                i == 0 ? (Height / 2.0) - 1 : 0);

            button.Shape = M3ShapeToken.None;
            button.SetCornerOverride(radius);
        }

        UpdateSelectionVisuals();
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
