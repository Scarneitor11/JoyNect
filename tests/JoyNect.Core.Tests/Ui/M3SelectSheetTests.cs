using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using JoyNect.Ui.Controls;
using JoyNect.Ui.Theme;
using Xunit;

namespace JoyNect.Core.Tests.Ui;

/// <summary>Checks that the M3Select sheet animates on open and snaps under reduced motion.</summary>
[Collection("WPF controls")]
public class M3SelectSheetTests
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

    // Animation clocks only tick while the control is in a live presentation source, so the select is hosted
    // on a shown window. The window is closed by the caller.
    private static Window ShowHosted(M3Select select)
    {
        var window = new Window
        {
            Content = new StackPanel { Children = { select } },
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
    public void Sheet_SlidesAndFadesIn_OnOpen()
    {
        RunOnSta(() =>
        {
            var select = new M3Select("Test");
            select.SetOptions(["One", "Two", "Three"], "One");
            var window = ShowHosted(select);
            try
            {
                M3Motion.ReducedMotionOverride = false;
                select.OpenMenu();

                // Right after opening the sheet is still offset and transparent, so the animation has work to do.
                Assert.True(select.MenuTransform.Y < 0.0);
                Assert.True(select.MenuOpacity < 1.0);

                // Partway through the open animation the sheet has moved toward rest and is more opaque.
                PumpFor(TimeSpan.FromMilliseconds(60));
                Assert.True(select.MenuTransform.Y > -12.0);
                Assert.True(select.MenuOpacity > 0.0);

                // After the animation the sheet is at rest and fully opaque.
                PumpFor(TimeSpan.FromMilliseconds(400));
                Assert.Equal(0.0, select.MenuTransform.Y, 6);
                Assert.Equal(1.0, select.MenuOpacity, 6);
            }
            finally
            {
                M3Motion.ReducedMotionOverride = null;
                window.Close();
            }
        });
    }

    [Fact]
    public void Sheet_SnapsToRest_UnderReducedMotion()
    {
        RunOnSta(() =>
        {
            var select = new M3Select("Test");
            select.SetOptions(["One", "Two"], "One");
            var window = ShowHosted(select);
            try
            {
                M3Motion.ReducedMotionOverride = true;
                select.OpenMenu();
                PumpFor(TimeSpan.FromMilliseconds(50));

                // No slide and no fade: the sheet is at rest and opaque straight away.
                Assert.Equal(0.0, select.MenuTransform.Y, 6);
                Assert.Equal(1.0, select.MenuOpacity, 6);
            }
            finally
            {
                M3Motion.ReducedMotionOverride = null;
                window.Close();
            }
        });
    }
}
