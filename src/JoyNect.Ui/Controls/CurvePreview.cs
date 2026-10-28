using System.Globalization;
using System.Windows;
using System.Windows.Media;
using JoyNect.Core.Mapping;
using JoyNect.Ui.Theme;

namespace JoyNect.Ui.Controls;

/// <summary>
/// A plot of how stick travel is shaped by the dead zone and response curve.
/// </summary>
/// <remarks>
/// Numbers in two sliders do not tell you how a stick will feel; a curve does. This draws the actual
/// function the mapping engine applies, so moving a slider and watching the line bend is the whole
/// interaction. The grey diagonal is the untouched response, for reference.
/// </remarks>
public sealed class CurvePreview : FrameworkElement
{
    private StickSettings _settings = StickSettings.Default;

    public CurvePreview()
    {
        Height = 132;
        MinWidth = 180;
        Loaded += (_, _) => M3ThemeManager.Changed += OnThemeChanged;
        Unloaded += (_, _) => M3ThemeManager.Changed -= OnThemeChanged;
    }

    /// <summary>The settings to plot.</summary>
    public StickSettings Settings
    {
        get => _settings;
        set
        {
            _settings = value;
            InvalidateVisual();
        }
    }

    protected override void OnRender(DrawingContext dc)
    {
        var theme = M3ThemeManager.Current;
        var width = ActualWidth - 34;
        var height = ActualHeight - 30;
        if (width <= 10 || height <= 10)
        {
            return;
        }

        var origin = new Point(14, 8 + height);

        // Axes and reference diagonal.
        var axisPen = new Pen(theme.OutlineVariant, 1.0);
        dc.DrawLine(axisPen, new Point(origin.X, origin.Y), new Point(origin.X + width, origin.Y));
        dc.DrawLine(axisPen, new Point(origin.X, origin.Y), new Point(origin.X, origin.Y - height));

        var referencePen = new Pen(theme.OutlineVariant, 1.0) { DashStyle = new DashStyle([3, 3], 0) };
        dc.DrawLine(referencePen, new Point(origin.X, origin.Y), new Point(origin.X + width, origin.Y - height));

        var deadzoneX = origin.X + (_settings.Deadzone * width);
        dc.DrawLine(new Pen(theme.Outline, 1.0), new Point(deadzoneX, origin.Y), new Point(deadzoneX, origin.Y - height * 0.16));

        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            const int steps = 100;
            for (var i = 0; i <= steps; i++)
            {
                var input = i / (double)steps;
                var output = Math.Clamp(StickProcessor.Apply(input, 0, _settings).X / (double)StickProcessor.MaxValue, 0, 1);
                var point = new Point(origin.X + (input * width), origin.Y - (output * height));

                if (i == 0)
                {
                    context.BeginFigure(point, isFilled: false, isClosed: false);
                }
                else
                {
                    context.LineTo(point, isStroked: true, isSmoothJoin: true);
                }
            }
        }

        geometry.Freeze();
        dc.DrawGeometry(null, new Pen(theme.Primary, 2.5) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, geometry);

        DrawLabel(dc, theme, "input", new Point(origin.X + width - 30, origin.Y + 8), theme.OnSurfaceVariant);
        DrawLabel(dc, theme, "output", new Point(origin.X + 2, origin.Y - height - 12), theme.OnSurfaceVariant);
        DrawLabel(
            dc,
            theme,
            $"dead zone {Math.Round(_settings.Deadzone * 100):0}% · curve {_settings.Curve.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)}",
            new Point(origin.X, origin.Y + 10),
            theme.OnSurfaceVariant);
    }

    private void DrawLabel(DrawingContext dc, M3Theme theme, string text, Point at, Brush brush)
    {
        var typeface = new Typeface(M3Typography.Text, FontStyles.Normal, FontWeights.Medium, FontStretches.Normal);
        var formatted = new FormattedText(
            text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            typeface,
            9.5,
            brush,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(formatted, at);
    }

    private void OnThemeChanged(object? sender, EventArgs e) => InvalidateVisual();
}
