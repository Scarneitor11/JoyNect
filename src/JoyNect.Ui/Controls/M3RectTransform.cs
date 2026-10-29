using System.Windows;
using System.Windows.Media;

namespace JoyNect.Ui.Controls;

/// <summary>
/// Attached properties that store the animated corner radius for a <see cref="Border"/> that is being
/// scale-animated. The <see cref="CornerRadius"/> on <see cref="Border"/> is not animatable through
/// <see cref="DoubleAnimation"/>, so callers mirror the animated radius here onto a transform and then
/// re-apply it to the <see cref="Border.CornerRadius"/> each frame.
/// </summary>
public static class M3RectTransform
{
    public static readonly DependencyProperty RadiusXProperty =
        DependencyProperty.RegisterAttached("RadiusX", typeof(double), typeof(M3RectTransform), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.None));

    public static readonly DependencyProperty RadiusYProperty =
        DependencyProperty.RegisterAttached("RadiusY", typeof(double), typeof(M3RectTransform), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.None));

    public static double GetRadiusX(DependencyObject element) => (double)element.GetValue(RadiusXProperty);
    public static void SetRadiusX(DependencyObject element, double value) => element.SetValue(RadiusXProperty, value);
    public static double GetRadiusY(DependencyObject element) => (double)element.GetValue(RadiusYProperty);
    public static void SetRadiusY(DependencyObject element, double value) => element.SetValue(RadiusYProperty, value);

    /// <summary>Builds a <see cref="CornerRadius"/> from the animated radii on a transform.</summary>
    public static CornerRadius ToCornerRadius(DependencyObject transform) =>
        new CornerRadius((double)transform.GetValue(RadiusXProperty), (double)transform.GetValue(RadiusYProperty), (double)transform.GetValue(RadiusYProperty), (double)transform.GetValue(RadiusYProperty));
}
