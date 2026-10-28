using System.Windows;
using System.Windows.Controls;
using JoyNect.Core.Design;
using JoyNect.Ui.Theme;

namespace JoyNect.Ui.Controls;

/// <summary>The five Material button emphases.</summary>
public enum M3ButtonVariant
{
    /// <summary>Highest emphasis: one per screen.</summary>
    Filled,

    /// <summary>Medium emphasis, tinted with the primary container colour.</summary>
    Tonal,

    /// <summary>Medium emphasis without a fill.</summary>
    Outlined,

    /// <summary>Low emphasis, for secondary actions in dense places.</summary>
    Text,

    /// <summary>A filled button that floats above the surface.</summary>
    Elevated,
}

/// <summary>A Material 3 button.</summary>
public class M3Button : M3Interactive
{
    private readonly StackPanel _layout;
    private readonly M3Icon _icon;
    private readonly TextBlock _label;
    private readonly Border _outline;
    private M3ButtonVariant _variant = M3ButtonVariant.Filled;

    public M3Button(string label, M3ButtonVariant variant = M3ButtonVariant.Filled, M3Glyph? icon = null)
    {
        _label = M3Typography.Create(label, M3Typography.LabelLarge);

        // A button is a single line. Wrapping a label inside a pill produces a taller pill whose text
        // is clipped at the ends, which is worse than trimming it.
        _label.TextWrapping = TextWrapping.NoWrap;
        _label.TextTrimming = TextTrimming.CharacterEllipsis;
        _label.VerticalAlignment = VerticalAlignment.Center;
        _label.HorizontalAlignment = HorizontalAlignment.Left;

        _icon = new M3Icon
        {
            Glyph = icon ?? M3Glyph.Circle,
            Size = 18.0,
            Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = icon is null ? Visibility.Collapsed : Visibility.Visible,
        };

        _outline = new Border
        {
            BorderThickness = new Thickness(1),
            IsHitTestVisible = false,
        };

        _layout = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

        _layout.Children.Add(_icon);
        _layout.Children.Add(_label);

        // Fill, then the outline, then the content: the outline has to sit above the fill and below
        // the label so the label's antialiasing does not fight the border.
        Fill.Child = _outline;
        ContentHost.Children.Add(_layout);

        Variant = variant;
        Padding = new Thickness(24, 0, 24, 0);
        MinHeight = 40;
        HorizontalAlignment = HorizontalAlignment.Left;
        VerticalAlignment = VerticalAlignment.Center;

        if (label.Length > 0)
        {
            AccessibilityName = label;
        }
    }

    /// <summary>Which emphasis the button uses.</summary>
    public M3ButtonVariant Variant
    {
        get => _variant;
        set
        {
            _variant = value;
            MinHeight = value == M3ButtonVariant.Text ? 36 : 40;
            Padding = new Thickness(value == M3ButtonVariant.Text ? 12 : 24, 0, value == M3ButtonVariant.Text ? 12 : 24, 0);
            RefreshTheme();
        }
    }

    /// <summary>The button's label.</summary>
    public string Label
    {
        get => _label.Text;
        set
        {
            _label.Text = value;
            if (string.IsNullOrWhiteSpace(AccessibilityName))
            {
                AccessibilityName = value;
            }
        }
    }

    /// <summary>Shows or hides the leading icon.</summary>
    public M3Glyph? Icon
    {
        get => _icon.Visibility == Visibility.Visible ? _icon.Glyph : null;
        set
        {
            if (value is null)
            {
                _icon.Visibility = Visibility.Collapsed;
                return;
            }

            _icon.Glyph = value.Value;
            _icon.Visibility = Visibility.Visible;
            RefreshTheme();
        }
    }

    /// <summary>Stretches the button to the width it is given.</summary>
    public bool Stretch
    {
        get => HorizontalAlignment == HorizontalAlignment.Stretch;
        set => HorizontalAlignment = value ? HorizontalAlignment.Stretch : HorizontalAlignment.Left;
    }

    /// <inheritdoc />
    protected override void RefreshTheme()
    {
        // The base constructor paints before these fields exist.
        if (_label is null || _icon is null || _outline is null)
        {
            return;
        }

        var theme = M3ThemeManager.Current;
        ColorRole container;
        ColorRole content;

        if (HasExplicitAppearance)
        {
            container = ContainerRole;
            content = StateLayerRole;
        }
        else
        {
            (container, content) = _variant switch
            {
                M3ButtonVariant.Tonal => (ColorRole.SecondaryContainer, ColorRole.OnSecondaryContainer),
                M3ButtonVariant.Outlined or M3ButtonVariant.Text => (ColorRole.Surface, ColorRole.Primary),
                M3ButtonVariant.Elevated => (ColorRole.SurfaceContainerLow, ColorRole.Primary),
                _ => (ColorRole.Primary, ColorRole.OnPrimary),
            };

            ContainerRole = container;
            StateLayerRole = content;
        }

        base.RefreshTheme();

        _label.Foreground = theme.Brush(content);
        _icon.Foreground = theme.Brush(content);
        _icon.Margin = _icon.Visibility == Visibility.Visible ? new Thickness(0, 0, 8, 0) : new Thickness(0);

        var outlined = !HasExplicitAppearance && _variant == M3ButtonVariant.Outlined;
        _outline.BorderBrush = theme.Brush(outlined ? ColorRole.Outline : container);
        _outline.BorderThickness = new Thickness(outlined ? 1 : 0);

        // The outline border needs to follow the pill shape, and a 1px border inside a 20 radius makes
        // the radius read as 19, so the outline is inset by the border width.
        var radius = MinHeight / 2.0;
        _outline.CornerRadius = new CornerRadius(Math.Max(radius - 0.5, 0));
        _outline.Margin = new Thickness(0.5);

        M3Elevation.Apply(this, !HasExplicitAppearance && _variant == M3ButtonVariant.Elevated ? 1 : 0);
    }
}
