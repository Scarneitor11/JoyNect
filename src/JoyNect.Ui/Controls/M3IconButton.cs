using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using JoyNect.Core.Design;
using JoyNect.Ui.Theme;

namespace JoyNect.Ui.Controls;

/// <summary>The emphasis of an icon button.</summary>
public enum M3IconButtonVariant
{
    /// <summary>Icon only, coloured with the content colour.</summary>
    Standard,

    /// <summary>Icon only, coloured with the primary colour.</summary>
    FilledTonal,

    /// <summary>Filled with the primary colour, one per surface.</summary>
    Filled,

    /// <summary>An outlined circle around the icon.</summary>
    Outlined,
}

/// <summary>A Material icon button.</summary>
public sealed class M3IconButton : M3Interactive
{
    private readonly M3Icon _icon;
    private readonly Border _outline;
    private M3IconButtonVariant _variant;
    private ColorRole _iconRole = ColorRole.OnSurfaceVariant;

    public M3IconButton(M3Glyph glyph, M3IconButtonVariant variant = M3IconButtonVariant.Standard)
    {
        _icon = new M3Icon
        {
            Glyph = glyph,
            Size = 20.0,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

        _outline = new Border { BorderThickness = new Thickness(1), IsHitTestVisible = false };
        Fill.Child = _outline;

        ContentHost.Children.Add(_icon);
        Width = 40;
        Height = 40;
        MinWidth = 40;
        MinHeight = 40;
        Variant = variant;
        AccessibilityName = glyph.ToString();
    }

    /// <summary>Which emphasis the button uses.</summary>
    public M3IconButtonVariant Variant
    {
        get => _variant;
        set
        {
            _variant = value;
            RefreshTheme();
        }
    }

    /// <summary>The icon shown.</summary>
    public M3Glyph Glyph
    {
        get => _icon.Glyph;
        set
        {
            // Keep an explicit accessible name. Only an unnamed button tracks its glyph, otherwise
            // changing a themed icon (Moon to Sun) silently renames the control in the UI Automation
            // tree and makes it unfindable to screen readers and assistive tools.
            var previous = _icon.Glyph.ToString();
            _icon.Glyph = value;
            if (string.Equals(AccessibilityName, previous, StringComparison.Ordinal))
            {
                AccessibilityName = value.ToString();
            }
        }
    }

    /// <summary>The size of the button; 48 makes a comfortable touch target.</summary>
    public double Diameter
    {
        get => Width;
        set
        {
            Width = value;
            Height = value;
            _icon.Size = Math.Round(value * 0.5);
            UpdateOutline();
        }
    }

    /// <summary>Turns the button into a toggle, reporting its state to assistive technology.</summary>
    public bool IsToggle { get; set; }

    /// <inheritdoc />
    protected override bool SupportsToggle => IsToggle;

    /// <inheritdoc />
    protected override AutomationControlType ControlType => AutomationControlType.Button;

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
        if (_icon is null || _outline is null)
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
                M3IconButtonVariant.FilledTonal => (ColorRole.SecondaryContainer, ColorRole.OnSecondaryContainer),
                M3IconButtonVariant.Filled => (ColorRole.Primary, ColorRole.OnPrimary),
                M3IconButtonVariant.Outlined => (ColorRole.Surface, ColorRole.OnSurfaceVariant),
                _ => (ColorRole.Surface, ColorRole.OnSurfaceVariant),
            };

            if (IsToggle)
            {
                (container, content) = IsSelected
                    ? (ColorRole.SecondaryContainer, ColorRole.OnSecondaryContainer)
                    : (ColorRole.Surface, ColorRole.OnSurfaceVariant);
            }

            ContainerRole = container;
            StateLayerRole = content;
        }

        base.RefreshTheme();

        _iconRole = content;
        _icon.Foreground = theme.Brush(content);

        _outline.BorderThickness = new Thickness(_variant == M3IconButtonVariant.Outlined && !IsSelected ? 1 : 0);
        _outline.BorderBrush = theme.Brush(ColorRole.OutlineVariant);
        UpdateOutline();
        AccessibilityName = string.IsNullOrWhiteSpace(AccessibilityName) ? _icon.Glyph.ToString() : AccessibilityName;
    }

    private void UpdateOutline()
    {
        var diameter = Width > 0 ? Width : 40.0;
        _outline.CornerRadius = new CornerRadius(diameter / 2.0);
        _outline.Margin = new Thickness(0.5);
    }
}
