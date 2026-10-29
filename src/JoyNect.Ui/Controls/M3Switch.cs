using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using JoyNect.Core.Design;
using JoyNect.Ui.Theme;

namespace JoyNect.Ui.Controls;

/// <summary>A Material 3 switch.</summary>
/// <remarks>
/// The thumb grows while it is held, which is the one piece of expressive motion on this control: it
/// makes the target feel physical without adding a ripple that would be wrong on a desktop pointer.
/// The whole track is the hit area, so the control is comfortably targetable at 32 pixels tall.
/// </remarks>
public sealed class M3Switch : M3Interactive
{
    private readonly Border _track;
    private readonly Border _thumb;
    private readonly TranslateTransform _thumbOffset = new();
    private readonly Grid _layout = new();
    private const double RestingThumbSize = 24.0;

    // Unfrozen so each switch can tween its own colours; the shared theme brushes are frozen.
    private readonly SolidColorBrush _trackFill = new();
    private readonly SolidColorBrush _thumbFill = new();

    // Until the constructor finishes, the first theme colours are set directly so the control does not
    // flash from the brushes' default colour on its first paint.
    private bool _built;

    public M3Switch()
    {
        Width = 52;
        Height = 32;
        Shape = M3ShapeToken.Full;
        AccessibilityName = "Switch";

        _track = new Border
        {
            Width = 52,
            Height = 32,
            CornerRadius = new CornerRadius(16),
            Background = _trackFill,
            IsHitTestVisible = false,
        };

        _thumb = new Border
        {
            Width = RestingThumbSize,
            Height = RestingThumbSize,
            CornerRadius = new CornerRadius(RestingThumbSize / 2.0),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0, 0, 0),
            Background = _thumbFill,
            IsHitTestVisible = false,
            RenderTransform = _thumbOffset,
        };

        _layout.Children.Add(_track);
        _layout.Children.Add(_thumb);
        ContentHost.Children.Add(_layout);
        Fill.Background = Brushes.Transparent;

        RefreshTheme();
        RefreshThumb();
        _built = true;
    }

    /// <inheritdoc />
    protected override bool SupportsToggle => true;

    /// <inheritdoc />
    protected override AutomationControlType ControlType => AutomationControlType.Button;

    /// <inheritdoc />
    protected override void OnSelectionChanged()
    {
        base.OnSelectionChanged();
        RefreshThumb();
    }

    /// <inheritdoc />
    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        RefreshThumb();
    }

    /// <inheritdoc />
    protected override void OnMouseLeftButtonDown(System.Windows.Input.MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        AnimateThumbWidth(IsSelected ? 26 : 28);
    }

    /// <inheritdoc />
    protected override void OnMouseLeftButtonUp(System.Windows.Input.MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        AnimateThumbWidth(RestingThumbSize);
        RefreshThumb();
    }

    /// <inheritdoc />
    protected override void OnKeyUp(System.Windows.Input.KeyEventArgs e)
    {
        base.OnKeyUp(e);
        AnimateThumbWidth(RestingThumbSize);
        RefreshThumb();
    }

    /// <inheritdoc />
    protected override void RefreshTheme()
    {
        // The base constructor paints before these fields exist.
        if (_track is null || _thumb is null)
        {
            return;
        }

        var theme = M3ThemeManager.Current;
        ContainerRole = ColorRole.Surface;
        StateLayerRole = IsSelected ? ColorRole.Primary : ColorRole.OnSurface;
        base.RefreshTheme();

        Fill.Background = Brushes.Transparent;
        SetBrushColour(_trackFill, IsSelected ? theme.Primary.Color : theme.SurfaceContainerHighest.Color);

        // An unselected thumb is outlined rather than filled: that is what distinguishes "off" from
        // "disabled" at a glance.
        SetBrushColour(_thumbFill, IsSelected ? theme.OnPrimary.Color : theme.OnSurfaceVariant.Color);
        _thumb.BorderThickness = new Thickness(0);
        RefreshThumb();
    }

    private void SetBrushColour(SolidColorBrush brush, Color colour)
    {
        if (!_built || M3Motion.ReducedMotion)
        {
            // A running animation overrides the local value, so clear it before snapping.
            brush.BeginAnimation(SolidColorBrush.ColorProperty, null);
            brush.Color = colour;
            return;
        }

        // Starting from the current colour, so a toggle mid-fade reverses smoothly.
        brush.BeginAnimation(
            SolidColorBrush.ColorProperty,
            new ColorAnimation
            {
                To = colour,
                Duration = new Duration(TimeSpan.FromMilliseconds(M3Duration.Short4)),
                EasingFunction = M3Easing.Standard,
            });
    }

    private void RefreshThumb()
    {
        if (_thumb is null || _thumbOffset is null)
        {
            return;
        }

        // Travel uses the resting thumb size, not _thumb.Width: that value is mid-animation during a
        // press, so reading it here would stop the thumb short of the track end.
        var travel = Math.Max(Width - RestingThumbSize - 8.0, 0.0);
        var target = IsSelected ? travel : 0.0;

        _thumb.CornerRadius = new CornerRadius(_thumb.Width / 2.0);

        if (M3Motion.ReducedMotion)
        {
            // A running animation overrides the local value, so clear it before snapping.
            _thumbOffset.BeginAnimation(TranslateTransform.XProperty, null);
            _thumbOffset.X = target;
            return;
        }

        // The thumb slides on a transform rather than on its margin: a margin animation would
        // re-layout the whole control on every frame, and it cannot be animated with a double curve.
        _thumbOffset.BeginAnimation(
            TranslateTransform.XProperty,
            M3Motion.Animate(target, M3Duration.Medium2, M3Easing.Emphasized));
    }

    private void AnimateThumbWidth(double width)
    {
        _thumb.CornerRadius = new CornerRadius(width / 2.0);

        if (M3Motion.ReducedMotion)
        {
            _thumb.BeginAnimation(FrameworkElement.WidthProperty, null);
            _thumb.Width = width;
            RefreshThumb();
            return;
        }

        _thumb.BeginAnimation(
            FrameworkElement.WidthProperty,
            M3Motion.Animate(width, M3Duration.Short4, M3Easing.Emphasized));

        RefreshThumb();
    }
}
