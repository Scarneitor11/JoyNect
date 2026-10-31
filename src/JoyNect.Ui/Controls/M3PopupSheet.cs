using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using JoyNect.Ui.Theme;
using static JoyNect.Ui.Theme.M3Motion;

namespace JoyNect.Ui.Controls;

/// <summary>
/// Opens and closes a popup sheet with the Material slide and fade. <see cref="M3Select"/> and
/// <see cref="M3PopupMenu"/> both call this, so they open and close in the same order.
/// </summary>
public static class M3PopupSheet
{
    /// <summary>The scale a sheet starts from when it opens, and returns to when it closes.</summary>
    private const double StartScale = 0.9;

    /// <summary>
    /// Shows a popup sheet and animates it in: it slides down from <paramref name="slide"/> above its rest
    /// position and fades up from transparent. When <paramref name="scale"/> is given, it also grows from
    /// <see cref="StartScale"/> to full size over the same time. Under reduced motion it snaps open with no
    /// travel, fade or scale.
    /// </summary>
    public static void OpenSheet(Popup popup, TranslateTransform transform, UIElement sheet, double slide, ScaleTransform? scale = null)
    {
        transform.BeginAnimation(TranslateTransform.YProperty, null);
        sheet.BeginAnimation(UIElement.OpacityProperty, null);
        if (scale is not null)
        {
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        }

        if (!ReducedMotion)
        {
            transform.Y = -slide;
            sheet.Opacity = 0.0;
            if (scale is not null)
            {
                scale.ScaleX = StartScale;
                scale.ScaleY = StartScale;
            }
        }

        popup.IsOpen = true;
        SlideIn(transform, TranslateTransform.YProperty, -slide);
        FadeIn(sheet, UIElement.OpacityProperty);
        if (scale is not null)
        {
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, Animate(1.0, M3Duration.Medium2, M3Easing.EmphasizedDecelerate));
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, Animate(1.0, M3Duration.Medium2, M3Easing.EmphasizedDecelerate));
        }
    }

    /// <summary>
    /// Animates an open popup sheet out, sliding it back up and fading it, shrinking it to
    /// <see cref="StartScale"/> when <paramref name="scale"/> is given, then closes the popup. Does nothing
    /// when the popup is already closed.
    /// </summary>
    public static void CloseSheet(Popup popup, TranslateTransform transform, UIElement sheet, double slide, ScaleTransform? scale = null)
    {
        if (!popup.IsOpen)
        {
            return;
        }

        SlideOut(transform, TranslateTransform.YProperty, -slide, () => popup.IsOpen = false);
        FadeOut(sheet, UIElement.OpacityProperty);
        if (scale is not null)
        {
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, Animate(StartScale, M3Duration.Short4, M3Easing.StandardAccelerate));
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, Animate(StartScale, M3Duration.Short4, M3Easing.StandardAccelerate));
        }
    }
}
