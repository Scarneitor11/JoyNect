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
    private readonly Grid _layout = new();

    private double _minimum;
    private double _maximum = 1.0;
    private double _value;
    private bool _dragging;

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
        };

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
            if (Math.Abs(clamped - _value) < double.Epsilon)
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
        var width = Math.Max(_layout.ActualWidth, 1.0);
        var fraction = Math.Clamp(x / width, 0.0, 1.0);
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
        var handleSize = _handle.Width;
        var centre = fraction * (width - handleSize);

        _active.Width = Math.Max(fraction * width, 4.0);
        _handle.Margin = new Thickness(centre, 0, 0, 0);

        var stateSize = _handleState.Width;
        _handleState.Margin = new Thickness(centre - ((stateSize - handleSize) / 2.0), 0, 0, 0);

        var interactive = IsMouseOver || _dragging || IsKeyboardFocused;
        _handleState.Opacity = interactive ? 1.0 : 0.0;
        _handle.Width = _dragging ? 24.0 : 20.0;
        _handle.Height = _handle.Width;
        _handle.CornerRadius = new CornerRadius(_handle.Width / 2.0);
        _active.Height = _dragging ? 8.0 : 4.0;
        _active.CornerRadius = new CornerRadius(_active.Height / 2.0);
        _track.Height = _active.Height;

        // Screen readers announce the help text with the control, which is the closest thing WPF
        // offers to a value string on a custom slider.
        System.Windows.Automation.AutomationProperties.SetHelpText(
            this,
            ValueFormatter?.Invoke(_value) ?? _value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));
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
