using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using JoyNect.Ui.Controls;
using JoyNect.Ui.Theme;
using Xunit;

namespace JoyNect.Core.Tests.Ui;

/// <summary>Checks reduced-motion snapping and pointer alignment on the slider and switch without a window.</summary>
[Collection("WPF controls")]
public class M3ControlMotionTests
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

    private static void Arrange(FrameworkElement element, double width, double height)
    {
        element.Measure(new Size(width, height));
        element.Arrange(new Rect(0, 0, width, height));
        element.UpdateLayout();
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

    private static T Get<T>(object owner, string name) =>
        (T)owner.GetType().GetField(name, Instance)!.GetValue(owner)!;

    private static void Set(object owner, string name, object value) =>
        owner.GetType().GetField(name, Instance)!.SetValue(owner, value);

    [Fact]
    public void Slider_handle_snaps_to_target_size_when_reduced_motion_turns_on_mid_tween()
    {
        RunOnSta(() =>
        {
            try
            {
                M3Motion.ReducedMotionOverride = false;
                var slider = new M3Slider { Minimum = 0, Maximum = 1 };
                Arrange(slider, 300, 32);

                // Start a drag, which tweens the handle from rest size to dragged size.
                Set(slider, "_dragging", true);
                slider.Value = 0.5;
                PumpFor(TimeSpan.FromMilliseconds(40));

                // Switch reduced motion on mid-tween, then end the drag: the handle must land on rest size.
                M3Motion.ReducedMotionOverride = true;
                Set(slider, "_dragging", false);
                slider.Value = 0.2;
                PumpFor(TimeSpan.FromMilliseconds(400));

                var handle = Get<Border>(slider, "_handle");
                Assert.Equal(20.0, handle.Width, 6);
                Assert.Equal(20.0, handle.Height, 6);
            }
            finally
            {
                M3Motion.ReducedMotionOverride = null;
            }
        });
    }

    [Fact]
    public void Switch_thumb_snaps_translate_and_width_when_reduced_motion_turns_on_mid_tween()
    {
        RunOnSta(() =>
        {
            try
            {
                M3Motion.ReducedMotionOverride = false;
                var toggle = new M3Switch { IsSelected = false };
                Arrange(toggle, 52, 32);
                var thumb = Get<Border>(toggle, "_thumb");
                var offset = Get<TranslateTransform>(toggle, "_thumbOffset");
                var animateWidth = toggle.GetType().GetMethod("AnimateThumbWidth", Instance)!;

                // Start a translate and a width tween, then switch reduced motion on mid-tween.
                toggle.IsSelected = true;
                PumpFor(TimeSpan.FromMilliseconds(40));
                animateWidth.Invoke(toggle, new object[] { 26.0 });
                PumpFor(TimeSpan.FromMilliseconds(40));

                M3Motion.ReducedMotionOverride = true;
                toggle.IsSelected = false;
                PumpFor(TimeSpan.FromMilliseconds(400));
                Assert.Equal(0.0, offset.X, 6);

                animateWidth.Invoke(toggle, new object[] { 24.0 });
                PumpFor(TimeSpan.FromMilliseconds(400));
                Assert.Equal(24.0, thumb.Width, 6);

                toggle.IsSelected = true;
                PumpFor(TimeSpan.FromMilliseconds(400));
                Assert.Equal(20.0, offset.X, 6);
            }
            finally
            {
                M3Motion.ReducedMotionOverride = null;
            }
        });
    }

    [Fact]
    public void Slider_handle_centre_and_fill_end_follow_pointer_x_during_drag()
    {
        RunOnSta(() =>
        {
            try
            {
                // Reduced motion keeps sizes exact so the geometry is read without tween timing.
                M3Motion.ReducedMotionOverride = true;
                const double width = 300;
                var slider = new M3Slider { Minimum = 0, Maximum = 1 };
                Arrange(slider, width, 32);
                var layout = Get<Grid>(slider, "_layout");
                Arrange(layout, width, 32);
                Set(slider, "_dragging", true);
                var handle = Get<Border>(slider, "_handle");
                var active = Get<Border>(slider, "_active");
                var setFromPointer = slider.GetType().GetMethod("SetFromPointer", Instance)!;

                foreach (var x in new[] { 10.0, 60.0, 150.0, 221.5, 290.0 })
                {
                    setFromPointer.Invoke(slider, new object[] { x });
                    slider.UpdateLayout();

                    var handleCentre = handle.Margin.Left + (handle.Width / 2.0);
                    Assert.Equal(x, handleCentre, 6);
                    Assert.Equal(x, active.Width, 6);
                }
            }
            finally
            {
                M3Motion.ReducedMotionOverride = null;
            }
        });
    }

    [Fact]
    public void Switch_thumb_reaches_track_end_when_on()
    {
        RunOnSta(() =>
        {
            try
            {
                M3Motion.ReducedMotionOverride = true;
                var toggle = new M3Switch { IsSelected = true };
                Arrange(toggle, 52, 32);
                var thumb = Get<Border>(toggle, "_thumb");
                var offset = Get<TranslateTransform>(toggle, "_thumbOffset");

                // The thumb's right edge sits 4px from the track's right edge when on.
                var rightEdge = thumb.Margin.Left + offset.X + thumb.Width;
                Assert.Equal(52.0 - 4.0, rightEdge, 6);
            }
            finally
            {
                M3Motion.ReducedMotionOverride = null;
            }
        });
    }
}
