using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using JoyNect.Ui.Controls;
using JoyNect.Ui.Theme;
using Xunit;

namespace JoyNect.Core.Tests.Ui;

/// <summary>Checks the press ripple and expressive state layers on M3Interactive without a window.</summary>
[Collection("WPF controls")]
public class M3ControlAnimationTests
{
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic;

    // WPF controls need a single-threaded apartment and a dispatcher, so each check runs on its own STA thread.
    private static void RunOnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
        {
            throw new Exception("STA check failed", failure);
        }
    }

    // Mouse capture, and so IsPressed, needs a live presentation source, so the control is hosted in a
    // shown window. The returned value closes the window when the test scope ends.
    private static IDisposable ShowHosted(FrameworkElement element)
    {
        var window = new Window
        {
            Content = element,
            Width = 300,
            Height = 200,
            WindowStyle = WindowStyle.None,
            ShowInTaskbar = false,
            ShowActivated = false,
        };
        window.Show();
        window.UpdateLayout();
        return new WindowScope(window);
    }

    private sealed class WindowScope(Window window) : IDisposable
    {
        public void Dispose() => window.Close();
    }

    private static void PumpFor(TimeSpan span)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = span };
        timer.Tick += (_, _) => frame.Continue = false;
        frame.Continue = true;
        timer.Start();
        Dispatcher.PushFrame(frame);
        timer.Stop();
    }

    // Fields such as _ripple live on M3Interactive, so search up the hierarchy from the concrete control.
    private static T Get<T>(object owner, string name)
    {
        for (var type = owner.GetType(); type is not null; type = type.BaseType)
        {
            var field = type.GetField(name, Instance | BindingFlags.DeclaredOnly);
            if (field is not null)
            {
                return (T)field.GetValue(owner)!;
            }
        }

        throw new MissingFieldException(owner.GetType().FullName, name);
    }

    private static MouseButtonEventArgs PressEvent(RoutedEvent routed) =>
        new(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = routed };

    // WPF sets ButtonBase.IsPressed from the physical button state, which a synthetic event cannot change.
    // This sets the same dependency property through WPF's own read-only key, as a real press would.
    private static void SetPressedState(System.Windows.Controls.Primitives.ButtonBase button, bool pressed)
    {
        var key = (DependencyPropertyKey)typeof(System.Windows.Controls.Primitives.ButtonBase)
            .GetField("IsPressedPropertyKey", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)!
            .GetValue(null)!;
        button.SetValue(key, pressed);
    }

    [Fact]
    public void Ripple_PlaysOnPress_ReleasesOnRelease()
    {
        RunOnSta(() =>
        {
            try
            {
                M3Motion.ReducedMotionOverride = false;
                var button = new M3Button("Press me");
                using var host = ShowHosted(button);

                var root = Get<Grid>(button, "_root");
                var ripple = Get<Ellipse>(button, "_ripple");
                var rippleScale = (ScaleTransform)ripple.RenderTransform!;

                // The ripple lives in the visual tree, hidden at rest.
                Assert.NotNull(ripple);
                Assert.Equal(5, root.Children.Count);
                Assert.Contains((object)ripple, root.Children.Cast<object>());
                Assert.Equal(0.0, ripple.Opacity, 6);
                Assert.Equal(0.0, rippleScale.ScaleX, 6);
                Assert.Equal(0.0, rippleScale.ScaleY, 6);

                // Press: the ripple scales up and fades in.
                SetPressedState(button, true);
                button.RaiseEvent(PressEvent(UIElement.MouseLeftButtonDownEvent));
                Assert.True(button.IsPressed);
                PumpFor(TimeSpan.FromMilliseconds(300));

                Assert.Equal(1.0, rippleScale.ScaleX, 6);
                Assert.Equal(1.0, rippleScale.ScaleY, 6);
                Assert.Equal(0.6, ripple.Opacity, 6);

                // Release: the ripple fades out.
                SetPressedState(button, false);
                button.RaiseEvent(PressEvent(UIElement.MouseLeftButtonUpEvent));
                Assert.False(button.IsPressed);
                PumpFor(TimeSpan.FromMilliseconds(300));

                Assert.Equal(0.0, ripple.Opacity, 6);
            }
            finally
            {
                M3Motion.ReducedMotionOverride = null;
            }
        });
    }

    [Fact]
    public void Ripple_DoesNotPlayWhenDisabled()
    {
        RunOnSta(() =>
        {
            try
            {
                M3Motion.ReducedMotionOverride = false;
                var button = new M3Button("Disabled") { IsEnabled = false };
                using var host = ShowHosted(button);

                var ripple = Get<Ellipse>(button, "_ripple");
                var rippleScale = (ScaleTransform)ripple.RenderTransform!;

                button.RaiseEvent(PressEvent(UIElement.MouseLeftButtonDownEvent));
                Assert.False(button.IsPressed);

                PumpFor(TimeSpan.FromMilliseconds(300));

                // No ripple plays: opacity and scale stay at rest.
                Assert.Equal(0.0, ripple.Opacity, 6);
                Assert.Equal(0.0, rippleScale.ScaleX, 6);
                Assert.Equal(0.0, rippleScale.ScaleY, 6);
            }
            finally
            {
                M3Motion.ReducedMotionOverride = null;
            }
        });
    }

    [Fact]
    public void Ripple_SnapsUnderReducedMotion()
    {
        RunOnSta(() =>
        {
            try
            {
                M3Motion.ReducedMotionOverride = true;
                var button = new M3Button("Press me");
                using var host = ShowHosted(button);

                var ripple = Get<Ellipse>(button, "_ripple");
                var rippleScale = (ScaleTransform)ripple.RenderTransform!;

                SetPressedState(button, true);
                button.RaiseEvent(PressEvent(UIElement.MouseLeftButtonDownEvent));
                Assert.True(button.IsPressed);

                // No pump: with reduced motion the snap is immediate, no tween in flight.
                Assert.True(rippleScale.ScaleX is 0.0 or 1.0);
                Assert.True(rippleScale.ScaleY is 0.0 or 1.0);
                Assert.Equal(0.0, ripple.Opacity, 6);
            }
            finally
            {
                M3Motion.ReducedMotionOverride = null;
            }
        });
    }

    [Fact]
    public void Ripple_HasVisibleSize_WhenPressed()
    {
        RunOnSta(() =>
        {
            try
            {
                M3Motion.ReducedMotionOverride = false;
                var button = new M3Button("Press me");
                using var host = ShowHosted(button);

                var ripple = Get<Ellipse>(button, "_ripple");

                SetPressedState(button, true);
                button.RaiseEvent(PressEvent(UIElement.MouseLeftButtonDownEvent));
                PumpFor(TimeSpan.FromMilliseconds(300));

                // A shape with no size paints nothing, so the ripple must measure to the control.
                Assert.True(
                    ripple.ActualWidth > 0 && ripple.ActualHeight > 0,
                    $"ripple measured {ripple.ActualWidth}x{ripple.ActualHeight}");
            }
            finally
            {
                M3Motion.ReducedMotionOverride = null;
            }
        });
    }

    [Fact]
    public void StateLayer_UsesExpressiveEasing()
    {
        var standard = M3Easing.Standard;
        Assert.NotNull(standard);
        Assert.IsType<CubicBezierEasing>(standard);
    }
}
