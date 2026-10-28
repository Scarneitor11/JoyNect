using System.Globalization;
using System.Windows;
using System.Windows.Media;
using JoyNect.Core.Motion;
using JoyNect.Ui.Theme;

namespace JoyNect.Ui.Controls;

/// <summary>
/// A live motion read-out: a bubble level for tilt and three bars for angular velocity.
/// </summary>
/// <remarks>
/// The bubble level is the honest half of this control: it shows which way gravity is pulling, which
/// the accelerometer can measure reliably. The gyro bars show turn rate. Both are labelled with their
/// units, because a motion display without units is a decoration.
/// </remarks>
public sealed class MotionScope : FrameworkElement
{
    private const double DesignWidth = 380;
    private const double DesignHeight = 170;

    /// <summary>The widest tilt the level can show, in degrees.</summary>
    public const double TiltRange = 45.0;

    /// <summary>The angular velocity that fills a bar, in degrees per second.</summary>
    public const double GyroRange = 300.0;

    private MotionSample _sample = MotionSample.Zero;
    private bool _live;

    public MotionScope()
    {
        MinHeight = 150;
        MinWidth = 300;
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;
        Loaded += (_, _) => M3ThemeManager.Changed += OnThemeChanged;
        Unloaded += (_, _) => M3ThemeManager.Changed -= OnThemeChanged;
    }

    /// <summary>Shows a sample.</summary>
    public void SetSample(in MotionSample sample)
    {
        _sample = sample;
        _live = true;
        InvalidateVisual();
    }

    /// <summary>Shows the scope as waiting for a controller with motion.</summary>
    public void SetIdle()
    {
        _sample = MotionSample.Zero;
        _live = false;
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize) => new(
        double.IsInfinity(availableSize.Width) ? DesignWidth : availableSize.Width,
        double.IsInfinity(availableSize.Height) ? DesignHeight : availableSize.Height);

    protected override void OnRender(DrawingContext dc)
    {
        var theme = M3ThemeManager.Current;

        // Scale by height and let the width flow: the level keeps its proportions while the gyro bars
        // take whatever room the card has left, so a wide card does not leave a dead margin.
        var scale = ActualHeight / DesignHeight;
        if (scale <= 0)
        {
            return;
        }

        var viewWidth = Math.Max(ActualWidth / scale, DesignWidth);
        dc.PushTransform(new TransformGroup
        {
            Children =
            {
                new ScaleTransform(scale, scale),
                new TranslateTransform((ActualWidth - (viewWidth * scale)) / 2.0, 0),
            },
        });

        DrawLevel(dc, theme);
        DrawBars(dc, theme, viewWidth);
        dc.Pop();
    }

    private void DrawLevel(DrawingContext dc, M3Theme theme)
    {
        var centre = new Point(76, 84);
        const double radius = 54;

        dc.DrawEllipse(theme.SurfaceContainerLowest, new Pen(theme.OutlineVariant, 1.5), centre, radius, radius);
        dc.DrawEllipse(null, new Pen(theme.OutlineVariant, 1.0), centre, radius * 0.62, radius * 0.62);

        var crosshair = new Pen(theme.OutlineVariant, 1.0) { DashStyle = new DashStyle([2, 3], 0) };
        dc.DrawLine(crosshair, new Point(centre.X - radius, centre.Y), new Point(centre.X + radius, centre.Y));
        dc.DrawLine(crosshair, new Point(centre.X, centre.Y - radius), new Point(centre.X, centre.Y + radius));

        if (_live)
        {
            var (pitch, roll) = _sample.TiltDegrees;
            var travel = (radius - 12) / TiltRange;
            var bubble = new Point(
                centre.X + Math.Clamp(roll, -TiltRange, TiltRange) * travel,
                centre.Y - Math.Clamp(pitch, -TiltRange, TiltRange) * travel);

            // A soft halo behind the bubble makes the position readable at a glance without an arrow.
            dc.DrawEllipse(theme.Overlay(JoyNect.Core.Design.ColorRole.Primary, 0.22), null, bubble, 16, 16);
            dc.DrawEllipse(theme.Primary, null, bubble, 10, 10);

            DrawText(dc, theme, $"pitch {pitch:0}°", new Point(centre.X - 44, centre.Y + radius + 8), 10, theme.OnSurfaceVariant);
            DrawText(dc, theme, $"roll {roll:0}°", new Point(centre.X - 38, centre.Y + radius + 24), 10, theme.OnSurfaceVariant);
            return;
        }

        DrawText(dc, theme, "no motion data", new Point(centre.X - 40, centre.Y - 7), 10, theme.OnSurfaceVariant);
    }

    private void DrawBars(DrawingContext dc, M3Theme theme, double viewWidth)
    {
        var axes = new (string Label, double Value)[]
        {
            ("X", _live ? _sample.AngularVelocityDps.X : 0),
            ("Y", _live ? _sample.AngularVelocityDps.Y : 0),
            ("Z", _live ? _sample.AngularVelocityDps.Z : 0),
        };

        const double left = 164;
        var trackWidth = Math.Max(viewWidth - left - 44, 90);
        var zero = trackWidth / 2.0;

        for (var i = 0; i < axes.Length; i++)
        {
            var (label, value) = axes[i];
            var y = 34 + (i * 42);

            DrawText(dc, theme, label, new Point(left - 18, y - 7), 11, theme.OnSurfaceVariant);

            dc.DrawRoundedRectangle(
                theme.SurfaceContainerLowest,
                null,
                new Rect(left, y - 4, trackWidth, 8),
                4,
                4);

            // A centre tick is what turns the bar into a signed value rather than a magnitude.
            dc.DrawRectangle(theme.OutlineVariant, null, new Rect(left + zero - 0.5, y - 9, 1, 18));

            var clamped = Math.Clamp(value, -GyroRange, GyroRange);
            var width = Math.Abs(clamped) / GyroRange * zero;
            if (width > 1)
            {
                var x = clamped >= 0 ? left + zero : left + zero - width;
                dc.DrawRoundedRectangle(theme.Primary, null, new Rect(x, y - 4, width, 8), 4, 4);
            }

            // Rounding can land on negative zero, which reads as a measurement when it is not one.
            var rounded = _live ? Math.Round(value) : 0;
            var readout = !_live ? "–" : rounded == 0 ? "0" : rounded.ToString("0", CultureInfo.InvariantCulture);
            DrawText(dc, theme, readout, new Point(left + trackWidth + 10, y - 7), 11, theme.OnSurface);
        }

        DrawText(dc, theme, "turn rate, ±300 °/s", new Point(left, 156), 10, theme.OnSurfaceVariant);
    }

    private void DrawText(DrawingContext dc, M3Theme theme, string text, Point at, double size, Brush brush)
    {
        var typeface = new Typeface(M3Typography.Text, FontStyles.Normal, FontWeights.Medium, FontStretches.Normal);
        var formatted = new FormattedText(
            text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            typeface,
            size,
            brush,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);

        dc.DrawText(formatted, at);
    }

    private void OnThemeChanged(object? sender, EventArgs e) => InvalidateVisual();
}
