using System.Windows;
using System.Windows.Media;
using JoyNect.Core.Output;
using JoyNect.Ui.Theme;

namespace JoyNect.Ui.Controls;

/// <summary>
/// A drawing of an Xbox 360 pad that mirrors the state it is given.
/// </summary>
/// <remarks>
/// Drawn rather than illustrated so it can be live: every control that lights up here is a bit the
/// virtual pad is actually reporting, which makes it useful for checking a mapping rather than
/// decorative. The shapes are laid out on a fixed 420 x 260 grid and scaled as one unit, so the
/// drawing never distorts.
/// </remarks>
public sealed class XboxPadCanvas : FrameworkElement
{
    private const double DesignWidth = 420;
    private const double DesignHeight = 260;

    // The drawing's ink does not fill the whole design grid: the triggers start at y=44 and the shell
    // ends at y=216. Scaling the ink, not the grid, is what stops a tall card from being mostly margin.
    private const double InkLeft = 64;
    private const double InkTop = 40;
    private const double InkRight = 356;
    private const double InkBottom = 250;

    private Xbox360PadState _state = Xbox360PadState.Neutral;
    private bool _hasState;

    public XboxPadCanvas()
    {
        MinWidth = 240;
        MinHeight = 160;
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;
        Loaded += (_, _) => M3ThemeManager.Changed += OnThemeChanged;
        Unloaded += (_, _) => M3ThemeManager.Changed -= OnThemeChanged;
    }

    /// <summary>The pad state to draw.</summary>
    public void SetState(in Xbox360PadState state)
    {
        _state = state;
        _hasState = true;

        // A pad that has never reported anything shows dim hardware rather than a controller that
        // looks switched on.
        if (Opacity < 0.99)
        {
            BeginAnimation(OpacityProperty, M3Motion.Animate(1.0, M3Duration.Medium2));
        }

        InvalidateVisual();
    }

    /// <summary>Shows the pad as present but not receiving input.</summary>
    public void SetIdle()
    {
        _state = Xbox360PadState.Neutral;
        _hasState = false;
        BeginAnimation(OpacityProperty, M3Motion.Animate(0.55, M3Duration.Medium2));
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize) => new(
        double.IsInfinity(availableSize.Width) ? DesignWidth : availableSize.Width,
        double.IsInfinity(availableSize.Height) ? DesignHeight : availableSize.Height);

    protected override void OnRender(DrawingContext drawingContext)
    {
        var theme = M3ThemeManager.Current;
        var inkWidth = InkRight - InkLeft;
        var inkHeight = InkBottom - InkTop;
        var scale = Math.Min(ActualWidth / inkWidth, ActualHeight / inkHeight);
        if (scale <= 0)
        {
            return;
        }

        var offsetX = ((ActualWidth - (inkWidth * scale)) / 2.0) - (InkLeft * scale);
        var offsetY = ((ActualHeight - (inkHeight * scale)) / 2.0) - (InkTop * scale);

        drawingContext.PushTransform(new TransformGroup
        {
            Children =
            {
                new ScaleTransform(scale, scale),
                new TranslateTransform(offsetX, offsetY),
            },
        });

        var shellFill = theme.SurfaceContainerHighest;
        var shellStroke = new Pen(theme.OutlineVariant, 2.0);
        var hardware = theme.SurfaceContainerHigh;
        var hardwareStroke = new Pen(theme.Outline, 1.5);
        var pressed = theme.Primary;
        var accent = theme.OnPrimary;
        var label = theme.OnSurfaceVariant;
        var idle = theme.SurfaceContainer;

        DrawShell(drawingContext, shellFill, shellStroke);
        DrawTriggers(drawingContext, theme, hardware, hardwareStroke);
        DrawBumpers(drawingContext, theme, hardware, hardwareStroke);
        DrawStick(drawingContext, new Point(130, 132), _state.LeftThumbX, _state.LeftThumbY, theme, hardware, hardwareStroke, theme.Primary, _state.Buttons.HasFlag(Xbox360Buttons.LeftThumb));
        DrawStick(drawingContext, new Point(294, 194), _state.RightThumbX, _state.RightThumbY, theme, hardware, hardwareStroke, theme.Primary, _state.Buttons.HasFlag(Xbox360Buttons.RightThumb));
        DrawDpad(drawingContext, theme, hardware, hardwareStroke);
        DrawFaceButtons(drawingContext, theme, hardware, hardwareStroke);
        DrawCentreButtons(drawingContext, theme, hardware, hardwareStroke);
        DrawGuide(drawingContext, theme, hardware, hardwareStroke, idle);

        drawingContext.Pop();
    }

    private static void DrawShell(DrawingContext dc, Brush fill, Pen stroke)
    {
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(new Point(112, 84), isFilled: true, isClosed: true);
            context.BezierTo(new Point(150, 56), new Point(270, 56), new Point(308, 84), true, true);
            context.BezierTo(new Point(344, 104), new Point(352, 168), new Point(338, 212), true, true);
            context.BezierTo(new Point(330, 240), new Point(300, 244), new Point(284, 218), true, true);
            context.LineTo(new Point(270, 188), true, true);
            context.LineTo(new Point(150, 188), true, true);
            context.LineTo(new Point(136, 218), true, true);
            context.BezierTo(new Point(120, 244), new Point(90, 244), new Point(82, 212), true, true);
            context.BezierTo(new Point(68, 168), new Point(76, 104), new Point(112, 84), true, true);
        }

        geometry.Freeze();
        dc.DrawGeometry(fill, stroke, geometry);
    }

    private void DrawTriggers(DrawingContext dc, M3Theme theme, Brush fill, Pen stroke)
    {
        var left = new Rect(104, 40, 88, 18);
        var right = new Rect(228, 40, 88, 18);
        DrawTrigger(dc, left, _state.LeftTrigger, theme, fill, stroke);
        DrawTrigger(dc, right, _state.RightTrigger, theme, fill, stroke);
    }

    private static void DrawTrigger(DrawingContext dc, Rect bounds, byte value, M3Theme theme, Brush fill, Pen stroke)
    {
        var radius = new CornerRadius(8);
        dc.DrawRoundedRectangle(fill, stroke, bounds, radius.TopLeft, radius.TopLeft);

        if (value > 0)
        {
            // The fill grows from the bottom, so the drawing shows how far the trigger is pulled.
            var height = Math.Max(2.0, bounds.Height * (value / 255.0));
            var filled = new Rect(bounds.X + 2, bounds.Bottom - height - 2, bounds.Width - 4, height);
            dc.DrawRoundedRectangle(theme.Primary, null, filled, 6, 6);
        }
    }

    private void DrawBumpers(DrawingContext dc, M3Theme theme, Brush fill, Pen stroke)
    {
        var left = new Rect(100, 62, 96, 18);
        var right = new Rect(224, 62, 96, 18);
        dc.DrawRoundedRectangle(
            _state.Buttons.HasFlag(Xbox360Buttons.LeftShoulder) ? theme.Primary : fill,
            stroke,
            left,
            9,
            9);
        dc.DrawRoundedRectangle(
            _state.Buttons.HasFlag(Xbox360Buttons.RightShoulder) ? theme.Primary : fill,
            stroke,
            right,
            9,
            9);
    }

    private static void DrawStick(
        DrawingContext dc,
        Point centre,
        short x,
        short y,
        M3Theme theme,
        Brush hardware,
        Pen stroke,
        Brush pressed,
        bool clicked)
    {
        const double well = 28;
        const double knob = 16;

        dc.DrawEllipse(theme.SurfaceContainerLowest, stroke, centre, well, well);
        dc.DrawEllipse(hardware, null, centre, well - 3, well - 3);

        // XInput's Y axis points up; screen Y points down.
        var travel = well - knob - 4;
        var offset = new Vector(x / 32767.0, -y / 32767.0) * travel;
        var knobCentre = centre + offset;

        dc.DrawEllipse(clicked ? pressed : theme.SurfaceContainerHigh, stroke, knobCentre, knob, knob);
        dc.DrawEllipse(null, new Pen(theme.OutlineVariant, 1.0), knobCentre, knob - 5, knob - 5);
    }

    private void DrawDpad(DrawingContext dc, M3Theme theme, Brush fill, Pen stroke)
    {
        var centre = new Point(126, 202);
        const double arm = 9;
        const double length = 22;

        var parts = new (Rect Bounds, Xbox360Buttons Button)[]
        {
            (new Rect(centre.X - arm, centre.Y - length - 6, arm * 2, length), Xbox360Buttons.DpadUp),
            (new Rect(centre.X - arm, centre.Y + 6, arm * 2, length), Xbox360Buttons.DpadDown),
            (new Rect(centre.X - length - 6, centre.Y - arm, length, arm * 2), Xbox360Buttons.DpadLeft),
            (new Rect(centre.X + 6, centre.Y - arm, length, arm * 2), Xbox360Buttons.DpadRight),
        };

        foreach (var (bounds, button) in parts)
        {
            dc.DrawRoundedRectangle(
                _state.Buttons.HasFlag(button) ? theme.Primary : fill,
                stroke,
                bounds,
                4,
                4);
        }
    }

    private void DrawFaceButtons(DrawingContext dc, M3Theme theme, Brush fill, Pen stroke)
    {
        var centre = new Point(272, 126);
        var positions = new (string Text, Point At, Xbox360Buttons Button)[]
        {
            ("Y", new Point(centre.X, centre.Y - 22), Xbox360Buttons.Y),
            ("B", new Point(centre.X, centre.Y + 22), Xbox360Buttons.B),
            ("X", new Point(centre.X - 22, centre.Y), Xbox360Buttons.X),
            ("A", new Point(centre.X + 22, centre.Y), Xbox360Buttons.A),
        };

        var typeface = new Typeface(M3Typography.Text, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

        foreach (var (text, at, button) in positions)
        {
            var isPressed = _state.Buttons.HasFlag(button);
            dc.DrawEllipse(isPressed ? theme.Primary : fill, stroke, at, 14, 14);

            var formatted = new FormattedText(
                text,
                System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                typeface,
                12,
                isPressed ? theme.OnPrimary : theme.OnSurfaceVariant,
                VisualTreeHelper.GetDpi(this).PixelsPerDip);

            dc.DrawText(formatted, new Point(at.X - (formatted.Width / 2), at.Y - (formatted.Height / 2)));
        }
    }

    private void DrawCentreButtons(DrawingContext dc, M3Theme theme, Brush fill, Pen stroke)
    {
        var back = new Point(188, 120);
        var start = new Point(232, 120);

        dc.DrawRoundedRectangle(
            _state.Buttons.HasFlag(Xbox360Buttons.Back) ? theme.Primary : fill,
            stroke,
            new Rect(back.X - 9, back.Y - 7, 18, 14),
            7,
            7);

        dc.DrawRoundedRectangle(
            _state.Buttons.HasFlag(Xbox360Buttons.Start) ? theme.Primary : fill,
            stroke,
            new Rect(start.X - 9, start.Y - 7, 18, 14),
            7,
            7);
    }

    private void DrawGuide(DrawingContext dc, M3Theme theme, Brush fill, Pen stroke, Brush idle)
    {
        var centre = new Point(210, 152);
        var pressed = _state.Buttons.HasFlag(Xbox360Buttons.Guide);

        dc.DrawEllipse(pressed ? theme.Primary : idle, stroke, centre, 14, 14);
        dc.DrawEllipse(null, new Pen(theme.Outline, 1.0), centre, 9, 9);
    }

    private void OnThemeChanged(object? sender, EventArgs e) => InvalidateVisual();

    /// <summary>True once a real state has been drawn, for pages that want to label the drawing.</summary>
    public bool HasLiveState => _hasState;
}
