using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using JoyNect.Core.Design;
using JoyNect.Ui.Theme;

namespace JoyNect.Ui.Controls;

/// <summary>A Material 3 slider.</summary>
/// <remarks>
/// The pointer drags the handle directly rather than jumping to the click position, which is what
/// makes a slider feel like an object rather than a scrollbar. Arrow keys step by one percent, Shift
/// with an arrow steps by ten, and Home/End go to the ends.
/// </remarks>
public sealed class M3Slider : M3Interactive
{
    private readonly Border _track;
    private readonly Border _active;
    private readonly Border _handle;
    private readonly Border _handleState;

    // The handle's size at rest and while dragged. The pointer and the fill both measure from its
    // centre, so a dragged handle (which grows) still lines up with the fill; the centre travels
    // HandleRest/2 in from each end.
    private const double HandleRest = 20.0;
    private const double HandleDragged = 24.0;
    private readonly Grid _layout = new();

    private double _minimum;
    private double _maximum = 1.0;
    private double _value;
    private bool _dragging;
    private double _handleStateTargetOpacity;
    // NaN until the fill has a width, so its first layout snaps instead of tweening from Auto.
    private double _activeTargetWidth = double.NaN;

    public M3Slider()
    {
        Height = 32;
        MinWidth = 120;
        Shape = M3ShapeToken.Full;
        AccessibilityName = "Slider";
        _layout.ClipToBounds = false;

        _track = new Border
        {
            Height = 4,
            CornerRadius = new CornerRadius(2),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            IsHitTestVisible = false,
        };

        _active = new Border
        {
            Height = 4,
            CornerRadius = new CornerRadius(2),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left,
            IsHitTestVisible = false,
        };

        _handleState = new Border
        {
            Width = 36,
            Height = 36,
            CornerRadius = new CornerRadius(18),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left,
            IsHitTestVisible = false,
            Opacity = 0,
        };

        _handle = new Border
        {
            Width = 20,
            Height = 20,
            CornerRadius = new CornerRadius(10),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left,
            IsHitTestVisible = false,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = _handleLayoutTransform,
        };

        _handle.SnapsToDevicePixels = false;

        _layout.Children.Add(_track);
        _layout.Children.Add(_active);
        _layout.Children.Add(_handleState);
        _layout.Children.Add(_handle);
        ContentHost.Children.Add(_layout);

        Fill.Background = Brushes.Transparent;
        SizeChanged += (_, _) => UpdateVisuals();
        RefreshTheme();
    }

    /// <summary>The lowest value.</summary>
    public double Minimum
    {
        get => _minimum;
        set
        {
            _minimum = value;
            if (_value < value)
            {
                Value = value;
            }

            UpdateVisuals();
        }
    }

    /// <summary>The highest value.</summary>
    public double Maximum
    {
        get => _maximum;
        set
        {
            _maximum = value;
            if (_value > value)
            {
                Value = value;
            }

            UpdateVisuals();
        }
    }

    /// <summary>The current value.</summary>
    public double Value
    {
        get => _value;
        set
        {
            var clamped = Math.Clamp(value, _minimum, Math.Max(_maximum, _minimum));
            if (Math.Abs(clamped - _value) < 1e-6)
            {
                return;
            }

            _value = clamped;
            UpdateVisuals();
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>The value as a fraction of the range.</summary>
    public double Fraction => _maximum - _minimum <= 0 ? 0 : (_value - _minimum) / (_maximum - _minimum);

    /// <summary>Raised when the value changes.</summary>
    public event EventHandler? ValueChanged;

    /// <summary>Formats the value for assistive technology.</summary>
    public Func<double, string>? ValueFormatter { get; set; }

    /// <inheritdoc />
    protected override AutomationControlType ControlType => AutomationControlType.Slider;

    /// <inheritdoc />
    protected override AutomationPeer OnCreateAutomationPeer() => new M3SliderPeer(this);

    /// <inheritdoc />
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        _dragging = true;
        CaptureMouse();
        SetFromPointer(e.GetPosition(_layout).X);
    }

    /// <inheritdoc />
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragging)
        {
            SetFromPointer(e.GetPosition(_layout).X);
        }
    }

    /// <inheritdoc />
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        _dragging = false;
        ReleaseMouseCapture();
        UpdateVisuals();
    }

    /// <inheritdoc />
    protected override void OnMouseEnter(MouseEventArgs e)
    {
        base.OnMouseEnter(e);
        UpdateVisuals();
    }

    /// <inheritdoc />
    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        UpdateVisuals();
    }

    /// <inheritdoc />
    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnGotKeyboardFocus(e);
        UpdateVisuals();
    }

    /// <inheritdoc />
    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnLostKeyboardFocus(e);
        UpdateVisuals();
    }

    /// <inheritdoc />
    protected override void OnKeyDown(KeyEventArgs e)
    {
        var step = (Maximum - Minimum) / 100.0;
        if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0)
        {
            step *= 10.0;
        }

        switch (e.Key)
        {
            case Key.Left or Key.Down:
                Value -= step;
                e.Handled = true;
                break;
            case Key.Right or Key.Up:
                Value += step;
                e.Handled = true;
                break;
            case Key.Home:
                Value = Minimum;
                e.Handled = true;
                break;
            case Key.End:
                Value = Maximum;
                e.Handled = true;
                break;
        }

        base.OnKeyDown(e);
    }

    /// <inheritdoc />
    protected override void RefreshTheme()
    {
        // The base constructor paints before these fields exist.
        if (_track is null || _active is null || _handle is null || _handleState is null)
        {
            return;
        }

        var theme = M3ThemeManager.Current;
        ContainerRole = ColorRole.Surface;
        StateLayerRole = ColorRole.Primary;
        base.RefreshTheme();

        Fill.Background = Brushes.Transparent;
        _track.Background = theme.SurfaceVariant;
        _active.Background = theme.Primary;
        _handle.Background = theme.Primary;
        _handleState.Background = theme.Overlay(ColorRole.Primary, 0.12);
        UpdateVisuals();
    }

    private void SetFromPointer(double x)
    {
        // The handle's centre travels from half a handle in from each end, so the pointer maps onto the
        // same span the handle moves across. Mapping over the full width drifts the thumb away from the cursor.
        var handle = HandleRest;
        var span = Math.Max(_layout.ActualWidth - handle, 1.0);
        var fraction = Math.Clamp((x - (handle / 2.0)) / span, 0.0, 1.0);
        Value = _minimum + (fraction * (_maximum - _minimum));
    }

    private void UpdateVisuals()
    {
        if (_layout is null)
        {
            return;
        }

        var width = _layout.ActualWidth;
        if (width <= 0)
        {
            return;
        }

        var fraction = Math.Clamp(Fraction, 0.0, 1.0);

        // The handle centre travels HandleRest/2 in from each end; the fill and the handle are both
        // measured from that same centre, so the fill ends exactly under the handle.
        var centre = fraction * (width - HandleRest);
        var handleCentre = centre + (HandleRest / 2.0);

        AnimateFillWidth(Math.Max(handleCentre, 4.0));

        var interactive = IsMouseOver || _dragging || IsKeyboardFocused;
        AnimateHandleStateOpacity(interactive ? 1.0 : 0.0);
        // During a drag the fill snaps to stay under the handle, so the handle can tween its size.
        // Outside a drag the handle also snaps: its grow animation would otherwise follow the fill visually.
        var handleSize = _dragging ? HandleDragged : HandleRest;

        HandleSizeSnapOrScale(_handle, _handleLayoutTransform, handleSize, WidthProperty, HeightProperty, out _);
        var renderedSize = _handle.Width * _handleLayoutTransform.ScaleX;
        _handleLayoutTransform.SetValue(M3RectTransform.RadiusXProperty, renderedSize / 2.0);
        _handleLayoutTransform.SetValue(M3RectTransform.RadiusYProperty, renderedSize / 2.0);
        _handle.CornerRadius = M3RectTransform.ToCornerRadius(_handleLayoutTransform);
        _handle.Margin = new Thickness(centre + ((HandleRest - renderedSize) / 2.0), 0, 0, 0);

        var stateSize = _handleState.Width;
        _handleState.Margin = new Thickness(handleCentre - (stateSize / 2.0), 0, 0, 0);

        // The track and fill thicken while dragged. Height is not animated on the fill because its width
        // already follows the pointer; a height tween would only lag behind it.
        var thickness = _dragging ? 8.0 : 4.0;
        _active.Height = thickness;
        _active.CornerRadius = new CornerRadius(thickness / 2.0);
        _track.Height = thickness;

        // Screen readers announce the help text with the control, which is the closest thing WPF
        // offers to a value string on a custom slider.
        System.Windows.Automation.AutomationProperties.SetHelpText(
            this,
            ValueFormatter?.Invoke(_value) ?? _value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));
    }

    private readonly ScaleTransform _handleLayoutTransform = new();

    private void AnimateHandleStateOpacity(double opacity)
    {
        if (M3Motion.ReducedMotion)
        {
            // A running animation overrides the local value, so clear it before snapping.
            _handleState.BeginAnimation(UIElement.OpacityProperty, null);
            _handleState.Opacity = opacity;
            _handleStateTargetOpacity = opacity;
            return;
        }

        if (Math.Abs(_handleStateTargetOpacity - opacity) < 0.001)
        {
            return;
        }

        _handleStateTargetOpacity = opacity;
        _handleState.BeginAnimation(
            UIElement.OpacityProperty,
            M3Motion.Animate(opacity, M3Duration.Short3, M3Easing.Standard));
    }

    private void AnimateFillWidth(double width)
    {
        // A drag snaps so the fill stays under the handle, which moves without a tween.
        if (double.IsNaN(_activeTargetWidth) || _dragging || M3Motion.ReducedMotion)
        {
            // A running animation overrides the local value, so clear it before snapping.
            _active.BeginAnimation(WidthProperty, null);
            _active.Width = width;
            _activeTargetWidth = width;
            return;
        }

        if (Math.Abs(_activeTargetWidth - width) < 0.001)
        {
            return;
        }

        // Interruptible: a new target starts from the current width, so a fast drag never queues up a backlog.
        _activeTargetWidth = width;
        _active.BeginAnimation(
            WidthProperty,
            M3Motion.Animate(width, M3Duration.Short3, M3Easing.Standard));
    }

    /// <summary>
    /// Returns the target rendered size for a handle scale animation, and starts the scale tween if the
    /// motion is not reduced. The caller uses the returned size for the handle margin so the scale does not
    /// change the handle's layout position. A scale transform, unlike a <see cref="System.Windows.FrameworkElement.Width"/>
    /// animation, preserves the visual centre during the tween, so the fill can stay under the scale-rendered
    /// handle while the animation is running.
    /// </summary>
    private (double Size, System.Windows.Media.Transform Transform) HandleSizeSnapOrScale(
        System.Windows.FrameworkElement handle,
        System.Windows.Media.ScaleTransform layoutTransform,
        double targetSize,
        System.Windows.DependencyProperty widthProperty,
        System.Windows.DependencyProperty heightProperty,
        out double renderedSize)
    {
        renderedSize = targetSize;

        if (M3Motion.ReducedMotion)
        {
            layoutTransform.ScaleX = 1.0;
            layoutTransform.ScaleY = 1.0;
            handle.BeginAnimation(widthProperty, null);
            handle.BeginAnimation(heightProperty, null);
            handle.Width = targetSize;
            handle.Height = targetSize;
            return (targetSize, layoutTransform);
        }

        var currentScaledWidth = handle.Width * layoutTransform.ScaleX;
        if (Math.Abs(currentScaledWidth - targetSize) < 0.001)
        {
            return (targetSize, layoutTransform);
        }

        handle.BeginAnimation(widthProperty, null);
        handle.BeginAnimation(heightProperty, null);
        handle.Width = targetSize;
        handle.Height = targetSize;
        layoutTransform.ScaleX = 1.0;
        layoutTransform.ScaleY = 1.0;
        layoutTransform.BeginAnimation(
            System.Windows.Media.ScaleTransform.ScaleXProperty,
            DoubleAnimation_NotFrozen(targetSize / targetSize, M3Duration.Short3));
        layoutTransform.BeginAnimation(
            System.Windows.Media.ScaleTransform.ScaleYProperty,
            DoubleAnimation_NotFrozen(targetSize / targetSize, M3Duration.Short3));

        renderedSize = targetSize;
        return (targetSize, layoutTransform);
    }    private static System.Windows.Media.Animation.DoubleAnimation DoubleAnimation_NotFrozen(double to, int durationMs)
    {
        return new System.Windows.Media.Animation.DoubleAnimation
        {
            To = to,
            Duration = TimeSpan.FromMilliseconds(durationMs),
            EasingFunction = M3Easing.Standard,
        };
    }


    private sealed class M3SliderPeer : FrameworkElementAutomationPeer, IRangeValueProvider
    {
        private readonly M3Slider _owner;

        public M3SliderPeer(M3Slider owner)
            : base(owner)
        {
            _owner = owner;
        }

        public override object? GetPattern(PatternInterface patternInterface) =>
            patternInterface == PatternInterface.RangeValue ? this : base.GetPattern(patternInterface);

        public double Value => _owner.Value;

        public bool IsReadOnly => !_owner.IsEnabled;

        public double Maximum => _owner.Maximum;

        public double Minimum => _owner.Minimum;

        public double LargeChange => (_owner.Maximum - _owner.Minimum) / 10.0;

        public double SmallChange => (_owner.Maximum - _owner.Minimum) / 100.0;

        public void SetValue(double value) =>
            Dispatcher.BeginInvoke(new Action(() => _owner.Value = value));

        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Slider;

        protected override string GetClassNameCore() => nameof(M3Slider);
    }
}
