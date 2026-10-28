using System.Windows;
using System.Windows.Media;

namespace JoyNect.Ui.Controls;

/// <summary>The icons JoyNect uses.</summary>
public enum M3Glyph
{
    Gamepad,
    Sliders,
    Swap,
    Motion,
    Vibration,
    Pulse,
    Info,
    Bluetooth,
    Battery,
    Refresh,
    Check,
    Close,
    ChevronDown,
    ChevronRight,
    ArrowBack,
    ArrowForward,
    Play,
    Power,
    Palette,
    Person,
    Keyboard,
    Sparkles,
    Warning,
    Error,
    Copy,
    Save,
    Search,
    MoreVertical,
    ExternalLink,
    Folder,
    Monitor,
    Accessibility,
    Help,
    Lock,
    Drag,
    Circle,
    Add,
    Remove,
    Trash,
    Bolt,
    Sun,
    Moon,
    Contrast,
    Usb,
}

/// <summary>
/// Hand-drawn vector icons on a 24 unit grid, stroked like Material Symbols outlined.
/// </summary>
/// <remarks>
/// Drawn rather than borrowed so the set is consistent, has no licence questions, and scales cleanly
/// at any size. Every icon uses the same 24 unit viewport, 2 unit stroke, round caps and joins, which
/// is what makes a row of them look like one family. Dots are drawn as very short segments: with round
/// caps they render as clean circles at any scale.
/// </remarks>
public static class M3Icons
{
    private static readonly Dictionary<M3Glyph, Geometry> Geometries = Build();

    /// <summary>The geometry for a glyph, frozen and cached.</summary>
    public static Geometry Get(M3Glyph glyph) => Geometries[glyph];

    /// <summary>Creates an icon element.</summary>
    public static M3Icon Create(M3Glyph glyph, double size = 24.0, Brush? brush = null) => new()
    {
        Glyph = glyph,
        Size = size,
        Foreground = brush ?? Brushes.Black,
    };

    private static Dictionary<M3Glyph, Geometry> Build()
    {
        var data = new Dictionary<M3Glyph, string>
        {
            [M3Glyph.Gamepad] =
                "M6.5 9h11a3 3 0 0 1 2.9 2.3l1.4 5.2a2.5 2.5 0 0 1-4.4 2.3L16 17H8l-1.4 1.8a2.5 2.5 0 0 1-4.4-2.3l1.4-5.2A3 3 0 0 1 6.5 9Z M8 12v3 M6.5 13.5h3 M16 12.2l.01 0 M18 14.4l.01 0",
            [M3Glyph.Sliders] = "M4 8h16 M4 16h16 M9 5.5v5 M15 13.5v5",
            [M3Glyph.Swap] = "M7 4.5v13 M7 17.5l-3-3 M7 17.5l3-3 M17 19.5v-13 M17 6.5l-3 3 M17 6.5l3 3",
            [M3Glyph.Motion] = "M20.5 12a8.5 8.5 0 1 1-3.4-6.8 M20.5 5v4.5h-4.5 M10 10h4v4h-4z",
            [M3Glyph.Vibration] = "M9 7h6a2 2 0 0 1 2 2v6a2 2 0 0 1-2 2H9a2 2 0 0 1-2-2V9a2 2 0 0 1 2-2Z M4 9.5v5 M2 11v2 M20 9.5v5 M22 11v2",
            [M3Glyph.Pulse] = "M3 12h4l2.5-6 3.5 12 2.5-6H21",
            [M3Glyph.Info] = "M12 21a9 9 0 1 0 0-18 9 9 0 0 0 0 18Z M12 11v5.5 M12 7.8l.01 0",
            [M3Glyph.Bluetooth] = "M7.5 7.5l9 9-4.5 3.5V4l4.5 3.5-9 9",
            [M3Glyph.Usb] = "M12 21V6 M9 8.5L12 5l3 3.5 M12 12l-4 3 M8 15a1.9 1.9 0 1 0 0 .01 M12 12l4 3 M16 18a1.9 1.9 0 1 0 0 .01",
            [M3Glyph.Battery] = "M4 8.5h12a2 2 0 0 1 2 2v3a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2v-3a2 2 0 0 1 2-2Z M21 10.5v3 M6 11v2",
            [M3Glyph.Refresh] = "M20 12a8 8 0 1 1-2.4-5.7 M20 4.5v4.5h-4.5",
            [M3Glyph.Check] = "M5 12.5l4.5 4.5L19 7.5",
            [M3Glyph.Close] = "M6.5 6.5l11 11 M17.5 6.5l-11 11",
            [M3Glyph.ChevronDown] = "M6.5 9.5l5.5 5.5 5.5-5.5",
            [M3Glyph.ChevronRight] = "M9.5 6.5l5.5 5.5-5.5 5.5",
            [M3Glyph.ArrowBack] = "M20 12H4.5 M10 6.5L4.5 12 10 17.5",
            [M3Glyph.ArrowForward] = "M4 12h15.5 M14 6.5l5.5 5.5-5.5 5.5",
            [M3Glyph.Play] = "M8.5 5.5l10 6.5-10 6.5z",
            [M3Glyph.Power] = "M12 3.5v8 M7.2 6.5a7 7 0 1 0 9.6 0",
            [M3Glyph.Palette] = "M12 21a9 9 0 1 1 9-9c0 1.9-1.5 3-3.2 3H16a2 2 0 0 0-1.5 3.3A1.7 1.7 0 0 1 12 21Z M7.5 10.5l.01 0 M9.8 7.2l.01 0 M14 6.8l.01 0 M16.4 10.2l.01 0",
            [M3Glyph.Person] = "M12 12.2a3.9 3.9 0 1 0 0-7.8 3.9 3.9 0 0 0 0 7.8Z M4.5 20.5a7.5 7.5 0 0 1 15 0",
            [M3Glyph.Keyboard] = "M4.5 6.5h15a2 2 0 0 1 2 2v7a2 2 0 0 1-2 2h-15a2 2 0 0 1-2-2v-7a2 2 0 0 1 2-2Z M7 10l.01 0 M11 10l.01 0 M15 10l.01 0 M7 13l.01 0 M11 13l.01 0 M15 13l.01 0 M8.5 16h7",
            [M3Glyph.Sparkles] = "M12 3.5l1.7 4.8 4.8 1.7-4.8 1.7L12 16.5l-1.7-4.8L5.5 10l4.8-1.7L12 3.5Z M18.5 15l.9 2.1 2.1.9-2.1.9-.9 2.1-.9-2.1-2.1-.9 2.1-.9.9-2.1Z",
            [M3Glyph.Warning] = "M12 4.5l8.5 15h-17L12 4.5Z M12 10v4 M12 17l.01 0",
            [M3Glyph.Error] = "M12 21a9 9 0 1 0 0-18 9 9 0 0 0 0 18Z M12 7.8v5.4 M12 16.4l.01 0",
            [M3Glyph.Copy] = "M8.5 8.5h9a2 2 0 0 1 2 2v9a2 2 0 0 1-2 2h-9a2 2 0 0 1-2-2v-9a2 2 0 0 1 2-2Z M5.5 15.5h-.5a2 2 0 0 1-2-2v-9a2 2 0 0 1 2-2h9a2 2 0 0 1 2 2v.5",
            [M3Glyph.Save] = "M5.5 4.5h10l3 3v12h-13z M8.5 4.5v5.5h7V4.5 M8.5 19.5v-6h7v6",
            [M3Glyph.Search] = "M10.8 17.6a6.8 6.8 0 1 0 0-13.6 6.8 6.8 0 0 0 0 13.6Z M15.8 15.8l4.7 4.7",
            [M3Glyph.MoreVertical] = "M12 6.2l.01 0 M12 12l.01 0 M12 17.8l.01 0",
            [M3Glyph.ExternalLink] = "M14 4.5h5.5V10 M19.5 4.5l-8.5 8.5 M18 14v5a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2h5",
            [M3Glyph.Folder] = "M3.5 7.5a2 2 0 0 1 2-2h3.6l2 2h9.4a2 2 0 0 1 2 2v8a2 2 0 0 1-2 2H5.5a2 2 0 0 1-2-2v-10Z",
            [M3Glyph.Monitor] = "M3.5 5.5h17v11h-17z M9 20.5h6 M12 16.5v4",
            [M3Glyph.Accessibility] = "M12 7.4a2.1 2.1 0 1 0 0-4.2 2.1 2.1 0 0 0 0 4.2Z M4.5 9.8l7.5 1.8 7.5-1.8 M12 11.6v4.4 M12 16l-3.2 4.8 M12 16l3.2 4.8",
            [M3Glyph.Help] = "M12 21a9 9 0 1 0 0-18 9 9 0 0 0 0 18Z M9.4 9.6a2.7 2.7 0 1 1 3.9 2.4c-.8.4-1.3 1.1-1.3 2v.5 M12 17.2l.01 0",
            [M3Glyph.Lock] = "M6 10.5h12a1.5 1.5 0 0 1 1.5 1.5v7.5a1.5 1.5 0 0 1-1.5 1.5H6a1.5 1.5 0 0 1-1.5-1.5V12A1.5 1.5 0 0 1 6 10.5Z M8.2 10.5V8a3.8 3.8 0 0 1 7.6 0v2.5",
            [M3Glyph.Drag] = "M9 6.5l.01 0 M15 6.5l.01 0 M9 12l.01 0 M15 12l.01 0 M9 17.5l.01 0 M15 17.5l.01 0",
            [M3Glyph.Circle] = "M12 20.5a8.5 8.5 0 1 0 0-17 8.5 8.5 0 0 0 0 17Z",
            [M3Glyph.Add] = "M12 5.5v13 M5.5 12h13",
            [M3Glyph.Remove] = "M5.5 12h13",
            [M3Glyph.Trash] = "M4.5 7h15 M9.5 7V4.5h5V7 M6.5 7l.9 12.5a1.5 1.5 0 0 0 1.5 1.4h6.2a1.5 1.5 0 0 0 1.5-1.4L17.5 7 M10 11v6 M14 11v6",
            [M3Glyph.Bolt] = "M13 3.5L6 13h4.5L11 20.5 18 11h-4.5L13 3.5Z",
            [M3Glyph.Sun] = "M12 16.5a4.5 4.5 0 1 0 0-9 4.5 4.5 0 0 0 0 9Z M12 2.5v2 M12 19.5v2 M4.5 12h-2 M21.5 12h-2 M6 6l-1.4-1.4 M19.4 19.4L18 18 M6 18l-1.4 1.4 M19.4 4.6L18 6",
            [M3Glyph.Moon] = "M20 14.5a8.5 8.5 0 0 1-10.5-10.5A8.5 8.5 0 1 0 20 14.5Z",
            [M3Glyph.Contrast] = "M12 20.5a8.5 8.5 0 1 0 0-17 8.5 8.5 0 0 0 0 17Z M12 3.5v17",
        };

        var result = new Dictionary<M3Glyph, Geometry>(data.Count);
        foreach (var (glyph, path) in data)
        {
            var geometry = Geometry.Parse(path);
            geometry.Freeze();
            result[glyph] = geometry;
        }

        return result;
    }
}

/// <summary>Draws one icon.</summary>
public sealed class M3Icon : FrameworkElement
{
    private M3Glyph _glyph = M3Glyph.Circle;
    private double _size = 24.0;
    private Brush _foreground = Brushes.Black;
    private double _thickness = 2.0;
    private Pen? _pen;
    private Brush? _penBrush;
    private double _penThickness;

    public M3Glyph Glyph
    {
        get => _glyph;
        set
        {
            _glyph = value;
            InvalidateVisual();
        }
    }

    /// <summary>The size of the icon box; the drawing scales to fit.</summary>
    public double Size
    {
        get => _size;
        set
        {
            _size = value;
            Width = value;
            Height = value;
            InvalidateVisual();
        }
    }

    public Brush Foreground
    {
        get => _foreground;
        set
        {
            _foreground = value;
            InvalidateVisual();
        }
    }

    /// <summary>Stroke thickness in the 24 unit grid, so it scales with the icon.</summary>
    public double Thickness
    {
        get => _thickness;
        set
        {
            _thickness = value;
            InvalidateVisual();
        }
    }

    protected override Size MeasureOverride(Size availableSize) => new(_size, _size);

    protected override void OnRender(DrawingContext drawingContext)
    {
        var geometry = M3Icons.Get(_glyph);
        var scale = _size / 24.0;
        if (scale <= 0.0)
        {
            return;
        }

        drawingContext.PushTransform(new ScaleTransform(scale, scale));
        drawingContext.DrawGeometry(null, GetPen(), geometry);
        drawingContext.Pop();
    }

    /// <summary>The pen for this icon, rebuilt only when its colour or weight changes.</summary>
    private Pen GetPen()
    {
        if (_pen is not null && ReferenceEquals(_penBrush, _foreground) && Math.Abs(_penThickness - _thickness) < 0.001)
        {
            return _pen;
        }

        var pen = new Pen(_foreground, _thickness)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round,
        };

        // Freezing is an optimisation, not a requirement: a brush supplied by a caller may still be
        // mutable, and in that case the pen simply stays unfrozen.
        try
        {
            pen.Freeze();
        }
        catch (InvalidOperationException)
        {
        }

        _pen = pen;
        _penBrush = _foreground;
        _penThickness = _thickness;
        return pen;
    }
}
