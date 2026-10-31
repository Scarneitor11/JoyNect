using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using JoyNect.Ui.Theme;
using Xunit;

namespace JoyNect.Core.Tests.Ui;

/// <summary>Checks M3Motion's staggered animations.</summary>
[Collection("WPF controls")]
public class M3MenuAnimationTests
{
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

    // Animation clocks only tick while the element is in a live presentation source, so each transform
    // is hosted on a shown window. The window is closed by the caller.
    private static Window ShowHosted(params TranslateTransform[] transforms)
    {
        var panel = new StackPanel();
        foreach (var transform in transforms)
        {
            panel.Children.Add(new Border { Height = 10, RenderTransform = transform });
        }

        var window = new Window
        {
            Content = panel,
            Width = 300,
            Height = 300,
            WindowStyle = WindowStyle.None,
            ShowInTaskbar = false,
            ShowActivated = false,
        };
        window.Show();
        return window;
    }

    [Fact]
    public void Stagger_BeginsAfterDelayAndReachesTarget()
    {
        RunOnSta(() =>
        {
            var snapped = new TranslateTransform { X = 0.0 };
            var delayed = new TranslateTransform { X = 0.0 };
            var window = ShowHosted(snapped, delayed);
            try
            {
                // Snapped: reduced motion reaches the target immediately regardless of delay.
                M3Motion.ReducedMotionOverride = true;
                M3Motion.Stagger(snapped, TranslateTransform.XProperty, 100.0, startDelayMs: 50);
                PumpFor(TimeSpan.FromMilliseconds(100));
                Assert.Equal(100.0, snapped.X, 6);

                // Delayed: the animation is registered but does not begin until its delay elapses,
                // so BeginTime > 0 means nothing has changed right after the call.
                M3Motion.ReducedMotionOverride = false;
                M3Motion.Stagger(delayed, TranslateTransform.XProperty, 100.0, startDelayMs: 50);
                Assert.Equal(0.0, delayed.X, 6);

                PumpFor(TimeSpan.FromMilliseconds(300));
                Assert.Equal(100.0, delayed.X, 6);
            }
            finally
            {
                M3Motion.ReducedMotionOverride = null;
                window.Close();
            }
        });
    }

    [Fact]
    public void Entrance_AppliesProgressiveDelays()
    {
        RunOnSta(() =>
        {
            var targets = Enumerable.Range(0, 3).Select(_ => new TranslateTransform { X = 0.0 }).ToArray();
            var window = ShowHosted(targets);
            try
            {
                M3Motion.ReducedMotionOverride = false;

                // Progressive begin times: index 0 (delay 0) begins first, then index 1 (~40ms),
                // then index 2 (~80ms); nothing starts before its delay.
                // The stopwatch starts before registration, so its elapsed time covers the animation clock's
                // window. The dispatcher may overshoot a pump interval, so the "not yet begun" checks only
                // apply while that elapsed time is still inside the element's delay.
                var clock = System.Diagnostics.Stopwatch.StartNew();
                for (var i = 0; i < targets.Length; i++)
                {
                    M3Motion.Entrance(targets[i], TranslateTransform.XProperty, 100.0, i);
                }

                PumpFor(TimeSpan.FromMilliseconds(20));
                var elapsed = clock.Elapsed.TotalMilliseconds;
                Assert.InRange(targets[0].X, 0.0001, 100.0);
                if (elapsed < 40) Assert.Equal(0.0, targets[1].X, 6);
                if (elapsed < 80) Assert.Equal(0.0, targets[2].X, 6);

                PumpFor(TimeSpan.FromMilliseconds(40));
                elapsed = clock.Elapsed.TotalMilliseconds;
                Assert.InRange(targets[1].X, 0.0001, 100.0);
                if (elapsed < 80) Assert.Equal(0.0, targets[2].X, 6);

                PumpFor(TimeSpan.FromMilliseconds(40));
                Assert.InRange(targets[2].X, 0.0001, 100.0);
            }
            finally
            {
                M3Motion.ReducedMotionOverride = null;
                window.Close();
            }
        });
    }

    [Fact]
    public void Stagger_RespectsReducedMotion()
    {
        RunOnSta(() =>
        {
            var target = new TranslateTransform { X = 0.0 };
            var window = ShowHosted(target);
            try
            {
                M3Motion.ReducedMotionOverride = true;
                M3Motion.Stagger(target, TranslateTransform.XProperty, 25.0, startDelayMs: 500);
                PumpFor(TimeSpan.FromMilliseconds(50));
                Assert.Equal(25.0, target.X, 6);
            }
            finally
            {
                M3Motion.ReducedMotionOverride = null;
                window.Close();
            }
        });
    }
}
