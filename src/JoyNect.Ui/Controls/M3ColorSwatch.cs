using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using JoyNect.Core.Design;
using JoyNect.Ui.Theme;

namespace JoyNect.Ui.Controls;

/// <summary>
/// A colour choice: the colour itself, with a selection ring.
/// </summary>
/// <remarks>
/// The seed colour drives the whole palette, so choosing one is the main way to personalise the app.
/// A swatch shows the actual colour rather than its name, which is the only honest way to present a
/// colour choice, and the ring is drawn outside the fill so the colour is never covered by its own
/// selection indicator.
/// </remarks>
public sealed class M3ColorSwatch : M3Interactive
{
    private readonly Border _ring;
    private readonly Border _fillCircle;
    private readonly M3Icon _check;
    private uint _colour;

    public M3ColorSwatch(string name, uint colour)
    {
        Width = 44;
        Height = 44;
        Shape = M3ShapeToken.Full;
        AccessibilityName = name;
        ToolTipService.SetToolTip(this, name);
        _colour = colour;

        _ring = new Border
        {
            Width = 40,
            Height = 40,
            CornerRadius = new CornerRadius(20),
            BorderThickness = new Thickness(2),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
        };

        _fillCircle = new Border
        {
            Width = 30,
            Height = 30,
            CornerRadius = new CornerRadius(15),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
        };

        _check = new M3Icon
        {
            Glyph = M3Glyph.Check,
            Size = 16,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Collapsed,
        };

        var host = new Grid();
        host.Children.Add(_ring);
        host.Children.Add(_fillCircle);
        host.Children.Add(_check);
        ContentHost.Children.Add(host);
        RefreshTheme();
    }

    /// <summary>The seed colour this swatch chooses.</summary>
    public uint Colour
    {
        get => _colour;
        set
        {
            _colour = value;
            RefreshTheme();
        }
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
        if (_ring is null || _fillCircle is null || _check is null)
        {
            return;
        }

        var theme = M3ThemeManager.Current;

        ContainerRole = ColorRole.Surface;
        StateLayerRole = ColorRole.OnSurface;
        base.RefreshTheme();
        Fill.Background = Brushes.Transparent;

        _fillCircle.Background = new SolidColorBrush(ColorFromArgb(_colour));

        // Ring and tick are chosen for contrast against the swatch itself: a purple tick on a purple
        // swatch is the one thing that would make the selection state invisible.
        var dark = ColorUtils.LstarFromArgb(_colour) < 50;
        var onColour = dark ? Colors.White : Colors.Black;

        _ring.BorderBrush = IsSelected ? theme.Primary : theme.OutlineVariant;
        _ring.BorderThickness = new Thickness(IsSelected ? 2 : 1);
        _check.Foreground = new SolidColorBrush(onColour);
        _check.Visibility = IsSelected ? Visibility.Visible : Visibility.Collapsed;
    }

    private static Color ColorFromArgb(uint argb) => Color.FromArgb(
        (byte)(argb >> 24),
        (byte)((argb >> 16) & 0xFF),
        (byte)((argb >> 8) & 0xFF),
        (byte)(argb & 0xFF));
}
