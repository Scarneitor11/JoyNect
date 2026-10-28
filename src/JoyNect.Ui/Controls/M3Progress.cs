using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using JoyNect.Core.Design;
using JoyNect.Ui.Theme;

namespace JoyNect.Ui.Controls;

/// <summary>A Material linear progress indicator.</summary>
/// <remarks>
/// Used for values that move slowly and matter — battery level, connection attempts. The bar animates
/// to a new value rather than jumping, which is what makes a battery draining readable at a glance.
/// </remarks>
public sealed class M3LinearProgress : Grid
{
    private readonly Border _track;
    private readonly Border _indicator;
    private double _value;

    public M3LinearProgress()
    {
        Height = 4;
        MinWidth = 80;

        _track = new Border { CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Stretch };
        _indicator = new Border
        {
            CornerRadius = new CornerRadius(2),
            HorizontalAlignment = HorizontalAlignment.Left,
            Width = 0,
        };

        Children.Add(_track);
        Children.Add(_indicator);
        RefreshTheme();
        SizeChanged += (_, _) => UpdateIndicator();

        Loaded += (_, _) => M3ThemeManager.Changed += OnThemeChanged;
        Unloaded += (_, _) => M3ThemeManager.Changed -= OnThemeChanged;
    }

    /// <summary>The value from 0 to 1.</summary>
    public double Value
    {
        get => _value;
        set
        {
            _value = Math.Clamp(value, 0.0, 1.0);
            UpdateIndicator();
        }
    }

    /// <summary>Uses the error colour, for a battery that is about to fail.</summary>
    public bool IsLow { get; set; }

    /// <summary>Applies the theme.</summary>
    public void RefreshTheme()
    {
        var theme = M3ThemeManager.Current;
        _track.Background = theme.SurfaceVariant;
        _indicator.Background = theme.Brush(IsLow ? ColorRole.Error : ColorRole.Primary);
        UpdateIndicator();
    }

    private void UpdateIndicator()
    {
        var width = Math.Max(ActualWidth * _value, _value > 0 ? 4.0 : 0.0);
        if (M3Motion.ReducedMotion)
        {
            _indicator.Width = width;
            return;
        }

        _indicator.BeginAnimation(
            FrameworkElement.WidthProperty,
            M3Motion.Animate(width, M3Duration.Medium2, M3Easing.Standard));
    }

    private void OnThemeChanged(object? sender, EventArgs e) => RefreshTheme();
}

/// <summary>
/// A circular progress ring, used where the shape itself communicates "working".
/// </summary>
public sealed class M3CircularProgress : FrameworkElement
{
    private double _value;
    private double _angle;
    private bool _indeterminate;

    public M3CircularProgress()
    {
        Width = 32;
        Height = 32;
        Loaded += (_, _) => StartSpin();
        Unloaded += (_, _) => StopSpin();
    }

    /// <summary>The value from 0 to 1, ignored while indeterminate.</summary>
    public double Value
    {
        get => _value;
        set
        {
            _value = Math.Clamp(value, 0.0, 1.0);
            InvalidateVisual();
        }
    }

    /// <summary>Spins forever, for work with no known duration.</summary>
    public bool Indeterminate
    {
        get => _indeterminate;
        set
        {
            _indeterminate = value;
            if (value)
            {
                StartSpin();
            }
            else
            {
                StopSpin();
            }

            InvalidateVisual();
        }
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        var theme = M3ThemeManager.Current;
        var size = Math.Min(ActualWidth, ActualHeight);
        if (size <= 0)
        {
            return;
        }

        var thickness = Math.Max(2.0, size / 12.0);
        var radius = (size - thickness) / 2.0;
        var centre = new Point(ActualWidth / 2.0, ActualHeight / 2.0);

        var trackPen = new Pen(theme.SurfaceVariant, thickness);
        drawingContext.DrawEllipse(null, trackPen, centre, radius, radius);

        var valuePen = new Pen(theme.Primary, thickness)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
        };

        var sweep = _indeterminate ? 100.0 : Math.Max(_value * 360.0, 0.0);
        if (sweep <= 0.0)
        {
            return;
        }

        var start = -90.0 + (_indeterminate ? _angle : 0.0);
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            var startPoint = PointOnCircle(centre, radius, start);
            var endPoint = PointOnCircle(centre, radius, start + sweep);
            context.BeginFigure(startPoint, isFilled: false, isClosed: false);
            context.ArcTo(
                endPoint,
                new Size(radius, radius),
                0,
                sweep > 180.0,
                SweepDirection.Clockwise,
                isStroked: true,
                isSmoothJoin: false);
        }

        geometry.Freeze();
        drawingContext.DrawGeometry(null, valuePen, geometry);
    }

    private static Point PointOnCircle(Point centre, double radius, double degrees)
    {
        var radians = degrees * Math.PI / 180.0;
        return new Point(centre.X + (radius * Math.Cos(radians)), centre.Y + (radius * Math.Sin(radians)));
    }

    private void StartSpin()
    {
        if (!_indeterminate || M3Motion.ReducedMotion)
        {
            return;
        }

        var animation = new DoubleAnimation(0, 360, TimeSpan.FromMilliseconds(1400))
        {
            RepeatBehavior = RepeatBehavior.Forever,
        };

        BeginAnimation(AngleProperty, animation);
    }

    private void StopSpin() => BeginAnimation(AngleProperty, null);

    private static readonly DependencyProperty AngleProperty = DependencyProperty.Register(
        nameof(Angle),
        typeof(double),
        typeof(M3CircularProgress),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender, OnAngleChanged));

    private double Angle
    {
        get => (double)GetValue(AngleProperty);
        set => SetValue(AngleProperty, value);
    }

    private static void OnAngleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var progress = (M3CircularProgress)d;
        progress._angle = (double)e.NewValue;
        progress.InvalidateVisual();
    }
}

/// <summary>A small Material badge, for counts and status dots.</summary>
public sealed class M3Badge : Border
{
    public M3Badge(string? text = null)
    {
        CornerRadius = new CornerRadius(8);
        MinWidth = text is null ? 8 : 16;
        Height = 8;
        Background = M3ThemeManager.Current.Error;
        Visibility = text is null ? Visibility.Visible : Visibility.Collapsed;

        if (text is not null)
        {
            Height = 16;
            Padding = new Thickness(4, 0, 4, 0);
            Child = new TextBlock
            {
                Text = text,
                FontFamily = M3Typography.Text,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = M3ThemeManager.Current.OnError,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
        }

        Loaded += (_, _) => RefreshTheme();
        M3ThemeManager.Changed += (_, _) => RefreshTheme();
    }

    /// <summary>Applies the theme.</summary>
    public void RefreshTheme()
    {
        var theme = M3ThemeManager.Current;
        Background = theme.Error;
        if (Child is TextBlock text)
        {
            text.Foreground = theme.OnError;
        }
    }
}
