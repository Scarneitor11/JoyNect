using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using JoyNect.Ui.Controls;
using Xunit;

namespace JoyNect.Core.Tests.Ui;

// Both UI classes share the static M3ThemeManager.Current, whose brush caches are not thread-safe,
// so they run one at a time.
[CollectionDefinition("WPF controls", DisableParallelization = true)]
public sealed class WpfControlsCollection
{
}

/// <summary>Checks the slider and switch geometry without a visible window.</summary>
[Collection("WPF controls")]
public class M3ControlGeometryTests
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
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = span };
        timer.Tick += (_, _) => frame.Continue = false;
        frame.Continue = true;
        timer.Start();
        Dispatcher.PushFrame(frame);
        timer.Stop();
    }

    private static Border Field(object owner, string name) =>
        (Border)owner.GetType().GetField(name, Instance)!.GetValue(owner)!;

    [Fact]
    public void Slider_value_maps_to_handle_centre_and_fill_width()
    {
        RunOnSta(() =>
        {
            const double width = 300;
            var slider = new M3Slider { Minimum = 0, Maximum = 0.35 };
            Arrange(slider, width, 32);
            var layout = (Grid)slider.GetType().GetField("_layout", Instance)!.GetValue(slider)!;
            Arrange(layout, width, 32);
            slider.Value = 0.35 / 2;
            slider.UpdateLayout();
            // The fill tweens to its target width, so let the animation finish before reading it.
            PumpFor(TimeSpan.FromSeconds(1));

            var handle = Field(slider, "_handle");
            var active = Field(slider, "_active");

            const double rest = 20.0;
            var span = width - rest;
            // Halfway through the range the centre sits at half the span plus half a handle.
            var expectedCentre = 0.5 * span + rest / 2.0;
            var handleCentre = handle.Margin.Left + handle.Width / 2.0;
            Assert.Equal(expectedCentre, handleCentre, 6);
            Assert.Equal(expectedCentre, active.Width, 6);
        });
    }

    [Fact]
    public void Slider_fill_end_stays_under_handle_centre_while_dragging_and_after_tween()
    {
        RunOnSta(() =>
        {
            const double width = 300;
            var slider = new M3Slider { Minimum = 0, Maximum = 1 };
            Arrange(slider, width, 32);
            var layout = (Grid)slider.GetType().GetField("_layout", Instance)!.GetValue(slider)!;
            Arrange(layout, width, 32);
            slider.Value = 0.25;
            PumpFor(TimeSpan.FromSeconds(1));

            var handle = Field(slider, "_handle");
            var active = Field(slider, "_active");
            var dragging = slider.GetType().GetField("_dragging", Instance)!;

            double HandleCentre() => handle.Margin.Left + handle.Width / 2.0;

            // During a drag the fill snaps to the handle, so there is no frame where they differ.
            dragging.SetValue(slider, true);
            slider.Value = 0.75;
            slider.UpdateLayout();
            Assert.Equal(HandleCentre(), active.Width, 6);

            // After the drag ends, the fill tweens and still settles on the handle centre.
            dragging.SetValue(slider, false);
            slider.Value = 0.5;
            slider.UpdateLayout();
            PumpFor(TimeSpan.FromSeconds(1));
            Assert.Equal(HandleCentre(), active.Width, 6);
        });
    }

    [Fact]
    public void Slider_pointer_x_maps_to_value_across_the_handle_span()
    {
        RunOnSta(() =>
        {
            const double width = 300;
            var slider = new M3Slider { Minimum = 0, Maximum = 1 };
            Arrange(slider, width, 32);
            var layout = (Grid)slider.GetType().GetField("_layout", Instance)!.GetValue(slider)!;
            Arrange(layout, width, 32);

            var setFromPointer = slider.GetType().GetMethod("SetFromPointer", Instance)!;

            // The pointer maps from half a handle in, so x = 10 is the minimum and x = 290 the maximum.
            setFromPointer.Invoke(slider, new object[] { 10.0 });
            Assert.Equal(0.0, slider.Value, 6);

            setFromPointer.Invoke(slider, new object[] { 290.0 });
            Assert.Equal(1.0, slider.Value, 6);

            setFromPointer.Invoke(slider, new object[] { 150.0 });
            Assert.Equal(0.5, slider.Value, 2);
        });
    }

    [Fact]
    public void Switch_thumb_has_4px_inset_and_20px_travel_in_a_52px_track()
    {
        RunOnSta(() =>
        {
            var toggle = new M3Switch { IsSelected = false };
            Arrange(toggle, 52, 32);

            var thumb = Field(toggle, "_thumb");
            Assert.Equal(4, thumb.Margin.Left);
            Assert.Equal(24, thumb.Width);

            var offset = (TranslateTransform)thumb.RenderTransform;
            var refresh = toggle.GetType().GetMethod("RefreshThumb", Instance)!;

            // Off rests at the inset, so the thumb's offset is zero.
            PumpFor(TimeSpan.FromSeconds(1));
            Assert.Equal(0.0, offset.X, 6);

            // On travels 52 - 24 - 8 = 20px, ending 4px from the right edge.
            toggle.IsSelected = true;
            refresh.Invoke(toggle, Array.Empty<object>());
            PumpFor(TimeSpan.FromSeconds(1));
            Assert.Equal(20.0, offset.X, 6);
        });
    }
}
