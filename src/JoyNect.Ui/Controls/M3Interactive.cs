using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using JoyNect.Core.Design;
using JoyNect.Ui.Theme;

namespace JoyNect.Ui.Controls;

/// <summary>
/// The behaviour every Material control shares: a container, a state layer, a focus ring, press
/// motion and accessibility.
/// </summary>
/// <remarks>
/// Built on <see cref="Button"/> on purpose. A Material control needs to be tab-reachable, respond to
/// Space and Enter, be announced as an invokable control by a screen reader, and disable cleanly, and
/// those are exactly the semantics a button already has. What is replaced is the visuals; what is kept
/// is the part that makes the control usable with a keyboard.
/// <para>
/// Derived controls compose their content into <see cref="ContentHost"/> and only have to say which
/// colour role fills the container. Everything else — hover and pressed state layers at Material's
/// opacities, a 2px focus indicator, a small press scale, disabled content at 38% — is handled here,
/// which is what keeps twenty controls feeling like one system.
/// </para>
/// </remarks>
public abstract class M3Interactive : Button
{
    private readonly Grid _root = new();
    private readonly Border _focusRing;
    private readonly Border _fill;
    private readonly Border _stateLayer;
    private readonly Grid _host;
    private readonly ScaleTransform _pressScale = new(1.0, 1.0);
    private readonly ScaleTransform _rippleScale = new(0.0, 0.0);
    private readonly Ellipse _ripple = new()
    {
        IsHitTestVisible = false,
        Opacity = 0.0,
        RenderTransformOrigin = new Point(0.5, 0.5),
        // Fill the control's bounds so the scaled ellipse has a size to paint; a shape with no size renders nothing.
        Stretch = Stretch.Fill,
        HorizontalAlignment = HorizontalAlignment.Stretch,
        VerticalAlignment = VerticalAlignment.Stretch,
    };
    private readonly TranslateTransform _hostOffset = new();

    private ColorRole _containerRole = ColorRole.Primary;
    private ColorRole _stateLayerRole = ColorRole.OnSurface;
    private M3ShapeToken _shape = M3ShapeToken.Full;
    private CornerRadius? _cornerOverride;
    private bool _selected;
    private bool _hovered;

    protected M3Interactive()
    {
        _ripple.RenderTransform = _rippleScale;
        Template = M3Templates.ContentOnly;
        FocusVisualStyle = null;
        Focusable = true;
        IsTabStop = true;
        Background = Brushes.Transparent;
        BorderThickness = new Thickness(0);
        Padding = new Thickness(0);
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;
        Cursor = Cursors.Hand;

        _fill = new Border { IsHitTestVisible = false };
        _stateLayer = new Border { IsHitTestVisible = false, Opacity = 0.0 };
        _focusRing = new Border
        {
            IsHitTestVisible = false,
            BorderThickness = new Thickness(2),
            Margin = new Thickness(-3),
            Visibility = Visibility.Collapsed,
        };

        _host = new Grid
        {
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new TransformGroup
            {
                Children =
                {
                    _pressScale,
                    _hostOffset,
                },
            },
            Margin = Padding,
        };

        _root.Children.Add(_focusRing);
        _root.Children.Add(_fill);
        _root.Children.Add(_stateLayer);
        _root.Children.Add(_ripple);
        _root.Children.Add(_host);
        Content = _root;

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        IsEnabledChanged += (_, _) => RefreshInteraction();

        RefreshTheme();
    }

    /// <summary>Where derived controls put their visuals.</summary>
    protected Grid ContentHost => _host;

    /// <summary>The border that paints the container.</summary>
    protected Border Fill => _fill;

    /// <summary>Whether the control reads as selected, for chips, switches and tabs.</summary>
    public bool IsSelected
    {
        get => _selected;
        set
        {
            if (_selected == value)
            {
                return;
            }

            _selected = value;
            OnSelectionChanged();
            RefreshTheme();
            OnSelectedChanged();
        }
    }

    /// <summary>The shape of the container.</summary>
    public M3ShapeToken Shape
    {
        get => _shape;
        set
        {
            _shape = value;
            UpdateCorners();
        }
    }

    /// <summary>
    /// The colour role that fills the container.
    /// </summary>
    /// <remarks>
    /// Assigning a role does not repaint by itself. Every derived control sets both roles and then
    /// calls <see cref="RefreshTheme" /> once, which keeps a two-colour change to one repaint; a setter
    /// that repainted would also recurse, because <see cref="RefreshTheme" /> assigns these roles.
    /// </remarks>
    protected ColorRole ContainerRole
    {
        get => _containerRole;
        set => _containerRole = value;
    }

    /// <summary>The colour role of the state layer, usually the content colour.</summary>
    protected ColorRole StateLayerRole
    {
        get => _stateLayerRole;
        set => _stateLayerRole = value;
    }

    /// <summary>The automation control type this control reports.</summary>
    protected virtual AutomationControlType ControlType => AutomationControlType.Button;

    /// <summary>Whether the control exposes a toggle pattern rather than only invoke.</summary>
    protected virtual bool SupportsToggle => false;

    /// <summary>A name for screen readers when the visible label is not enough.</summary>
    public string AccessibilityName
    {
        get => AutomationProperties.GetName(this);
        set => AutomationProperties.SetName(this, value);
    }

    /// <summary>
    /// True when the colour roles were set from outside, for example by a segmented button group.
    /// </summary>
    /// <remarks>
    /// A control that normally derives its colours from its variant has to know whether someone else
    /// owns them now; otherwise the next repaint would silently undo a selected-state fill.
    /// </remarks>
    protected bool HasExplicitAppearance { get; private set; }

    private bool _transparentContainer;

    /// <summary>
    /// When true the container paints nothing, so a control behind it shows through. A segmented group
    /// uses this so its sliding selection indicator sits under the segments.
    /// </summary>
    public bool TransparentContainer
    {
        get => _transparentContainer;
        set
        {
            _transparentContainer = value;
            RefreshTheme();
        }
    }

    /// <summary>
    /// Sets the container and content roles together.
    /// </summary>
    /// <remarks>
    /// Controls that compose other controls need to restyle them, and doing that through two
    /// protected setters would either expose the internals or require a subclass. One call is a
    /// smaller surface than either.
    /// </remarks>
    public void SetAppearance(ColorRole container, ColorRole content)
    {
        _containerRole = container;
        _stateLayerRole = content;
        HasExplicitAppearance = true;
        RefreshTheme();
    }

    /// <summary>
    /// Overrides the corner radius, for controls that share a shape with their neighbours.
    /// </summary>
    public void SetCornerOverride(CornerRadius radius)
    {
        _cornerOverride = radius;
        UpdateCorners();
    }

    /// <summary>Invokes the control as a screen reader or access key would.</summary>
    public void Activate()
    {
        if (IsEnabled)
        {
            OnClick();
        }
    }

    /// <summary>Called when the theme changes; derived controls refresh their colours here.</summary>
    protected virtual void RefreshTheme()
    {
        var theme = M3ThemeManager.Current;
        _fill.Background = _transparentContainer ? Brushes.Transparent : theme.Brush(_containerRole);
        _stateLayer.Background = theme.Overlay(_stateLayerRole, 1.0);
        _focusRing.BorderBrush = theme.Primary;
        Opacity = IsEnabled ? 1.0 : 1.0;
        _host.Opacity = IsEnabled ? 1.0 : M3Theme.DisabledOpacity;
        _ripple.Fill = _stateLayer.Background;
        UpdateCorners();
    }

    /// <summary>Called after <see cref="IsSelected"/> changes.</summary>
    protected virtual void OnSelectionChanged()
    {
    }

    /// <summary>Called after the selected visuals have been applied.</summary>
    protected virtual void OnSelectedChanged()
    {
    }

    /// <inheritdoc />
    protected override void OnClick()
    {
        if (SupportsToggle)
        {
            IsSelected = !IsSelected;
        }

        base.OnClick();
    }

    /// <inheritdoc />
    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        UpdateCorners();
    }

    /// <inheritdoc />
    protected override void OnMouseEnter(MouseEventArgs e)
    {
        base.OnMouseEnter(e);
        _hovered = true;
        RefreshInteraction();
    }

    /// <inheritdoc />
    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        _hovered = false;
        RefreshInteraction();
    }

    /// <inheritdoc />
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        RefreshInteraction();
        PlayPressRipple();
    }

    /// <inheritdoc />
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        RefreshInteraction();
        FadePressRipple();
    }

    /// <inheritdoc />
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        RefreshInteraction();
    }

    /// <inheritdoc />
    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        RefreshInteraction();
    }

    /// <inheritdoc />
    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnGotKeyboardFocus(e);
        RefreshInteraction();
    }

    /// <inheritdoc />
    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnLostKeyboardFocus(e);
        RefreshInteraction();
    }

    /// <inheritdoc />
    protected override AutomationPeer OnCreateAutomationPeer() => new M3ControlPeer(this);

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        M3ThemeManager.Changed += OnThemeManagerChanged;
        RefreshTheme();
        RefreshInteraction();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e) =>
        M3ThemeManager.Changed -= OnThemeManagerChanged;

    private void OnThemeManagerChanged(object? sender, EventArgs e) => RefreshTheme();

    private void RefreshInteraction()
    {
        var theme = M3ThemeManager.Current;

        // Material's state layer is additive: pressed wins over hover, and focus is shown by its own
        // indicator rather than by brightening the layer.
        var opacity = IsPressed
            ? M3Theme.PressedOpacity
            : _hovered
                ? M3Theme.HoverOpacity
                : IsKeyboardFocused && !_hovered
                    ? M3Theme.FocusOpacity
                    : 0.0;

        Animate(_stateLayer, UIElement.OpacityProperty, IsEnabled ? opacity : 0.0);

        // A focus indicator is only useful for keyboard users, so it appears with the keyboard and
        // steps out of the way for the pointer.
        var showFocusRing = IsKeyboardFocused && !_hovered && IsEnabled;
        _focusRing.Visibility = showFocusRing ? Visibility.Visible : Visibility.Collapsed;

        if (showFocusRing)
        {
            _focusRing.BorderBrush = theme.Primary;
        }

        // The press scale is the expressive part of a press: small, quick, and gone on release.
        var scale = IsPressed && IsEnabled ? 0.97 : 1.0;
        if (Math.Abs(_pressScale.ScaleX - scale) > 0.001)
        {
            var animation = M3Motion.Animate(scale, M3Duration.Short2, M3Easing.Emphasized);
            _pressScale.BeginAnimation(ScaleTransform.ScaleXProperty, animation);
            _pressScale.BeginAnimation(ScaleTransform.ScaleYProperty, animation);
        }

        Animate(_host, UIElement.OpacityProperty, IsEnabled ? 1.0 : M3Theme.DisabledOpacity);
    }

    private static void Animate(UIElement element, DependencyProperty property, double value)
    {
        var current = (double)element.GetValue(property);
        if (Math.Abs(current - value) < 0.001)
        {
            return;
        }

        element.BeginAnimation(property, M3Motion.Animate(value, M3Duration.Short2, M3Easing.Standard));
    }

    /// <summary>Starts the press ripple: scale grows 0→1 and opacity fades 0→0.6.</summary>
    private void PlayPressRipple()
    {
        if (!IsEnabled || !IsPressed)
        {
            return;
        }

        if (M3Motion.ReducedMotion)
        {
            // No ripple tween: keep it hidden at its start state so no partial tween ever paints.
            Snap(_rippleScale, ScaleTransform.ScaleXProperty, 0.0);
            Snap(_rippleScale, ScaleTransform.ScaleYProperty, 0.0);
            Snap(_ripple, UIElement.OpacityProperty, 0.0);
            return;
        }

        var press = M3Motion.Animate(1.0, M3Duration.Short2, M3Easing.EmphasizedDecelerate);
        _rippleScale.BeginAnimation(ScaleTransform.ScaleXProperty, press);
        _rippleScale.BeginAnimation(ScaleTransform.ScaleYProperty, press);
        _ripple.BeginAnimation(
            UIElement.OpacityProperty,
            M3Motion.Animate(0.6, M3Duration.Short2, M3Easing.EmphasizedDecelerate));
    }

    /// <summary>Fades the press ripple out on release.</summary>
    private void FadePressRipple()
    {
        if (!IsEnabled)
        {
            return;
        }

        _ripple.BeginAnimation(UIElement.OpacityProperty, M3Motion.Animate(0.0, M3Duration.Short2));
    }

    /// <summary>Stops any running animation on the property and sets its value directly.</summary>
    private static void Snap(UIElement element, DependencyProperty property, double value)
    {
        element.BeginAnimation(property, null);
        element.SetValue(property, value);
    }

    /// <summary>Stops any running animation on the property and sets its value directly.</summary>
    private static void Snap(System.Windows.Media.Animation.Animatable target, DependencyProperty property, double value)
    {
        target.BeginAnimation(property, null);
        target.SetValue(property, value);
    }

    /// <summary>
    /// Animates the control into view on its first load: the fill fades in and the content host
    /// slides up from 8px offset to its final place.
    /// </summary>
    /// <remarks>
    /// Derived controls call this from their first <see cref="FrameworkElement.Loaded"/> handling.
    /// When reduced motion is on, both steps snap to their end state instead of tweening.
    /// </remarks>
    protected virtual void AnimateEntrance()
    {
        if (M3Motion.ReducedMotion)
        {
            Snap(_fill, UIElement.OpacityProperty, 1.0);
            Snap(_hostOffset, TranslateTransform.YProperty, 0.0);
            return;
        }

        _fill.Opacity = 0.0;
        _fill.BeginAnimation(
            UIElement.OpacityProperty,
            M3Motion.Animate(1.0, M3Duration.Entrance, M3Easing.EmphasizedDecelerate));

        _hostOffset.Y = 8.0;
        _hostOffset.BeginAnimation(
            TranslateTransform.YProperty,
            M3Motion.Animate(0.0, M3Duration.Entrance, M3Easing.EmphasizedDecelerate, from: 8.0));
    }

    private void UpdateCorners()
    {
        var height = ActualHeight > 0 ? ActualHeight : 40.0;
        var radius = _cornerOverride ?? (Shape == M3ShapeToken.Full
            ? new CornerRadius(height / 2.0)
            : M3Shape.Radius(Shape));

        _fill.CornerRadius = radius;
        _stateLayer.CornerRadius = radius;

        // The focus ring follows the container's shape, slightly outside it.
        var ringRadius = Shape == M3ShapeToken.Full
            ? new CornerRadius((height / 2.0) + 2.0)
            : new CornerRadius(radius.TopLeft + 2.0);

        _focusRing.CornerRadius = ringRadius;
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        // ButtonBase does not apply Padding to anything itself — every template does it by hand.
        // The Material controls inset their content host instead, so the fill keeps the full bounds.
        if (e.Property == PaddingProperty && _host is not null)
        {
            _host.Margin = (Thickness)e.NewValue;
        }
    }

    /// <summary>Reports the control with the semantics a screen reader expects.</summary>
    private sealed class M3ControlPeer : FrameworkElementAutomationPeer, IInvokeProvider, IToggleProvider
    {
        private readonly M3Interactive _owner;

        public M3ControlPeer(M3Interactive owner)
            : base(owner)
        {
            _owner = owner;
        }

        /// <inheritdoc />
        public override object? GetPattern(PatternInterface patternInterface) =>
            patternInterface switch
            {
                PatternInterface.Invoke => this,
                PatternInterface.Toggle when _owner.SupportsToggle => this,
                _ => base.GetPattern(patternInterface),
            };

        public void Invoke() =>
            Dispatcher.BeginInvoke(new Action(() => _owner.Activate()));

        public void Toggle() =>
            Dispatcher.BeginInvoke(new Action(() => _owner.Activate()));

        public ToggleState ToggleState =>
            _owner.IsSelected ? System.Windows.Automation.ToggleState.On : System.Windows.Automation.ToggleState.Off;

        /// <inheritdoc />
        protected override AutomationControlType GetAutomationControlTypeCore() => _owner.ControlType;

        /// <inheritdoc />
        protected override string GetClassNameCore() => _owner.GetType().Name;

        /// <inheritdoc />
        protected override string GetNameCore()
        {
            var name = base.GetNameCore();
            if (!string.IsNullOrWhiteSpace(name))
            {
                return name;
            }

            return _owner.AccessibilityName ?? _owner.Content?.ToString() ?? string.Empty;
        }

        /// <inheritdoc />
        protected override bool IsEnabledCore() => _owner.IsEnabled;
    }
}
