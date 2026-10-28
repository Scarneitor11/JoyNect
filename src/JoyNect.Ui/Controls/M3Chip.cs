using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;
using JoyNect.Core.Design;
using JoyNect.Ui.Theme;

namespace JoyNect.Ui.Controls;

/// <summary>What a chip is for.</summary>
public enum M3ChipVariant
{
    /// <summary>Represents an action or an attribute.</summary>
    Assist,

    /// <summary>Can be selected to filter.</summary>
    Filter,

    /// <summary>Represents a user-entered value and can be removed.</summary>
    Input,
}

/// <summary>A Material 3 chip.</summary>
public sealed class M3Chip : M3Interactive
{
    private readonly StackPanel _layout = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _label;
    private readonly M3Icon _leading;
    private readonly M3IconButton? _remove;
    private readonly Border _outline;
    private M3ChipVariant _variant;

    public M3Chip(string label, M3ChipVariant variant = M3ChipVariant.Assist, M3Glyph? icon = null)
    {
        _variant = variant;
        Height = 32;
        MinHeight = 32;
        Shape = M3ShapeToken.Small;
        Padding = new Thickness(12, 0, 12, 0);
        VerticalAlignment = VerticalAlignment.Center;
        AccessibilityName = label;

        _label = M3Typography.Create(label, M3Typography.LabelLarge);
        _label.VerticalAlignment = VerticalAlignment.Center;
        _label.TextWrapping = TextWrapping.NoWrap;

        _leading = new M3Icon
        {
            Size = 18,
            Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = icon is null ? Visibility.Collapsed : Visibility.Visible,
        };

        if (icon is not null)
        {
            _leading.Glyph = icon.Value;
        }

        _outline = new Border { BorderThickness = new Thickness(1), IsHitTestVisible = false };
        Fill.Child = _outline;

        _layout.Children.Add(_leading);
        _layout.Children.Add(_label);

        if (variant == M3ChipVariant.Input)
        {
            _remove = new M3IconButton(M3Glyph.Close) { Diameter = 24, Margin = new Thickness(4, 0, -6, 0) };
            _remove.Click += (_, _) => Removed?.Invoke(this, EventArgs.Empty);
            _layout.Children.Add(_remove);
        }

        ContentHost.Children.Add(_layout);
        RefreshTheme();
    }

    /// <summary>Raised when an input chip is dismissed.</summary>
    public event EventHandler? Removed;

    /// <summary>The chip's text.</summary>
    public string Label
    {
        get => _label.Text;
        set
        {
            _label.Text = value;
            if (_remove is not null)
            {
                _remove.AccessibilityName = $"Remove {value}";
            }
        }
    }

    /// <summary>Which kind of chip this is.</summary>
    public M3ChipVariant Variant
    {
        get => _variant;
        set
        {
            _variant = value;
            RefreshTheme();
        }
    }

    /// <summary>Shows a leading icon.</summary>
    public M3Glyph? Icon
    {
        get => _leading.Visibility == Visibility.Visible ? _leading.Glyph : null;
        set
        {
            if (value is null)
            {
                _leading.Visibility = Visibility.Collapsed;
                return;
            }

            _leading.Glyph = value.Value;
            _leading.Visibility = Visibility.Visible;
        }
    }

    /// <inheritdoc />
    protected override bool SupportsToggle => _variant == M3ChipVariant.Filter;

    /// <inheritdoc />
    protected override AutomationControlType ControlType => AutomationControlType.Button;

    /// <inheritdoc />
    protected override void OnMouseEnter(MouseEventArgs e)
    {
        base.OnMouseEnter(e);
        UpdateOutline();
    }

    /// <inheritdoc />
    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        UpdateOutline();
    }

    /// <inheritdoc />
    protected override void OnSelectionChanged()
    {
        base.OnSelectionChanged();
        RefreshTheme();
    }

    /// <inheritdoc />
    protected override void RefreshTheme()
    {
        // The base constructor paints before these fields exist.
        if (_label is null || _leading is null || _outline is null)
        {
            return;
        }

        var theme = M3ThemeManager.Current;
        var selected = IsSelected && _variant == M3ChipVariant.Filter;
        ColorRole container;
        ColorRole content;

        if (HasExplicitAppearance)
        {
            container = ContainerRole;
            content = StateLayerRole;
        }
        else
        {
            (container, content) = selected
                ? (ColorRole.SecondaryContainer, ColorRole.OnSecondaryContainer)
                : (ColorRole.Surface, ColorRole.OnSurfaceVariant);

            ContainerRole = container;
            StateLayerRole = content;
        }

        base.RefreshTheme();

        _label.Foreground = theme.Brush(content);
        _leading.Foreground = theme.Brush(content);
        _outline.BorderBrush = theme.Brush(HasExplicitAppearance ? container : selected ? container : ColorRole.OutlineVariant);
        _outline.BorderThickness = new Thickness(HasExplicitAppearance || selected ? 0 : 1);
        UpdateOutline();
        AutomationProperties.SetName(this, _label.Text);
    }

    private void UpdateOutline()
    {
        _outline.CornerRadius = new CornerRadius(M3Shape.Small - 0.5);
        _outline.Margin = new Thickness(0.5);
    }
}
