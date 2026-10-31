using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using JoyNect.Ui.Controls;
using JoyNect.Ui.Theme;
using Xunit;

namespace JoyNect.Core.Tests.Ui;

/// <summary>Checks the sliding selection indicator on M3SegmentedButtons in a shown window.</summary>
[Collection("WPF controls")]
public class M3SegmentedButtonsMotionTests
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

    // Layout and animation need a live presentation source, so the control is hosted in a shown window.
    private static Window ShowHosted(FrameworkElement element)
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
        return window;
    }

    // Fields such as _indicatorOffset are private to M3SegmentedButtons, so read them by reflection.
    private static T Get<T>(object owner, string name) =>
        (T)owner.GetType().GetField(name, Instance)!.GetValue(owner)!;

    // Polls the dispatcher until the condition holds or the generous timeout runs out. No tight millisecond timing.
    private static bool WaitUntil(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return true;
            }

            var frame = new DispatcherFrame();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            timer.Tick += (_, _) => frame.Continue = false;
            frame.Continue = true;
            timer.Start();
            Dispatcher.PushFrame(frame);
            timer.Stop();
        }

        return condition();
    }

    [Fact]
    public void Indicator_SlidesToNewSegment_AndSettles()
    {
        RunOnSta(() =>
        {
            Window? window = null;
            try
            {
                M3Motion.ReducedMotionOverride = false;
                var group = new M3SegmentedButtons { Width = 300 };
                group.SetSegments("One", "Two", "Three");
                window = ShowHosted(group);

                var offset = Get<TranslateTransform>(group, "_indicatorOffset");
                var grid = Get<System.Windows.Controls.Grid>(group, "_grid");
                group.SelectedIndex = 0;
                window.UpdateLayout();
                var columnWidth = grid.ActualWidth / 3;
                Assert.True(columnWidth > 0);

                group.SelectedIndex = 2;
                Assert.True(offset.HasAnimatedProperties);

                // Settles on the target column once the slide finishes.
                var settled = WaitUntil(() => Math.Abs(offset.X - 2 * columnWidth) < 0.5, TimeSpan.FromSeconds(5));
                Assert.True(settled, $"Indicator stopped at {offset.X}, expected {2 * columnWidth}.");
            }
            finally
            {
                M3Motion.ReducedMotionOverride = null;
                window?.Close();
            }
        });
    }

    [Fact]
    public void Indicator_SnapsWithoutTravel_UnderReducedMotion()
    {
        RunOnSta(() =>
        {
            Window? window = null;
            try
            {
                M3Motion.ReducedMotionOverride = true;
                var group = new M3SegmentedButtons { Width = 300 };
                group.SetSegments("One", "Two", "Three");
                window = ShowHosted(group);

                var offset = Get<TranslateTransform>(group, "_indicatorOffset");
                var grid = Get<System.Windows.Controls.Grid>(group, "_grid");
                group.SelectedIndex = 0;
                window.UpdateLayout();
                group.SelectedIndex = 2;

                // No pump: the indicator is already at its target with no tween in flight.
                Assert.False(offset.HasAnimatedProperties);
                Assert.Equal(2 * grid.ActualWidth / 3, offset.X, 6);
            }
            finally
            {
                M3Motion.ReducedMotionOverride = null;
                window?.Close();
            }
        });
    }
}
