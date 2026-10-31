using System.Threading;
using System.Windows;
using System.Windows.Threading;
using JoyNect.Ui.Controls;
using JoyNect.Ui.Theme;
using Xunit;

namespace JoyNect.Core.Tests.Ui;

/// <summary>Checks that the tray's M3 popup menu opens, shows its items, runs their commands and closes.</summary>
[Collection("WPF controls")]
public class M3PopupMenuTests
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

    // Polls the dispatcher until the condition holds. The timeout is generous so slow CI machines still pass.
    private static void WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            PumpFor(TimeSpan.FromMilliseconds(20));
        }

        Assert.True(condition(), "The condition was not met within 5 seconds.");
    }

    // The popup is anchored to screen coordinates, but its animation clocks only tick while a live window
    // is presenting. The owner window is closed by the caller.
    private static Window ShowHost()
    {
        var window = new Window
        {
            Width = 300,
            Height = 300,
            WindowStyle = WindowStyle.None,
            ShowInTaskbar = false,
            ShowActivated = false,
        };
        window.Show();
        return window;
    }

    // The popup owns its own top-level window. Disposing it and then pumping the dispatcher lets that window
    // be destroyed on this thread, so no message reaches it after the thread is gone.
    private static void Teardown(M3PopupMenu menu, Window window)
    {
        menu.Dispose();
        window.Close();
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    }

    [Fact]
    public void Menu_OpensAndShowsItems()
    {
        RunOnSta(() =>
        {
            var menu = new M3PopupMenu();
            menu.SetItems(
            [
                new M3MenuItem("Open JoyNect", () => { }),
                null,
                new M3MenuItem("Exit JoyNect", () => { }),
            ]);
            var window = ShowHost();
            try
            {
                M3Motion.ReducedMotionOverride = false;
                menu.OpenAt(100, 100);

                Assert.True(menu.IsOpen);
                // The divider is not an item, so only the two commands are listed.
                Assert.Equal(new[] { "Open JoyNect", "Exit JoyNect" }, menu.ItemLabels);
                Assert.NotNull(menu.FindItem("Exit JoyNect"));
            }
            finally
            {
                M3Motion.ReducedMotionOverride = null;
                Teardown(menu, window);
            }
        });
    }

    [Fact]
    public void Menu_SlidesAndFadesIn_OnOpen()
    {
        RunOnSta(() =>
        {
            var menu = new M3PopupMenu();
            menu.SetItems([new M3MenuItem("Open JoyNect", () => { })]);
            var window = ShowHost();
            try
            {
                M3Motion.ReducedMotionOverride = false;
                menu.OpenAt(100, 100);

                // Right after opening the menu is still offset and transparent, so the animation has work to do.
                Assert.True(menu.MenuTransform.Y < 0.0);
                Assert.True(menu.MenuOpacity < 1.0);

                // It then settles at rest and fully opaque.
                WaitUntil(() => Math.Abs(menu.MenuTransform.Y) < 0.001 && menu.MenuOpacity > 0.999);
            }
            finally
            {
                M3Motion.ReducedMotionOverride = null;
                Teardown(menu, window);
            }
        });
    }

    [Fact]
    public void Item_RunsItsCommand_AndMenuCloses()
    {
        RunOnSta(() =>
        {
            var runs = 0;
            var menu = new M3PopupMenu();
            menu.SetItems([new M3MenuItem("Open JoyNect", () => runs++)]);
            var window = ShowHost();
            try
            {
                M3Motion.ReducedMotionOverride = false;
                menu.OpenAt(100, 100);

                menu.FindItem("Open JoyNect")!.Activate();

                Assert.Equal(1, runs);
                WaitUntil(() => !menu.IsOpen);
            }
            finally
            {
                M3Motion.ReducedMotionOverride = null;
                Teardown(menu, window);
            }
        });
    }

    [Fact]
    public void ExitItem_RunsExitCommand_AndMenuCloses()
    {
        RunOnSta(() =>
        {
            var exits = 0;
            var menu = new M3PopupMenu();
            menu.SetItems([new M3MenuItem("Exit JoyNect", () => exits++)]);
            var window = ShowHost();
            try
            {
                M3Motion.ReducedMotionOverride = false;
                menu.OpenAt(100, 100);

                menu.FindItem("Exit JoyNect")!.Activate();

                Assert.Equal(1, exits);
                WaitUntil(() => !menu.IsOpen);
            }
            finally
            {
                M3Motion.ReducedMotionOverride = null;
                Teardown(menu, window);
            }
        });
    }

    [Fact]
    public void Menu_Closes_WhenCloseIsRequested()
    {
        RunOnSta(() =>
        {
            var menu = new M3PopupMenu();
            menu.SetItems([new M3MenuItem("Open JoyNect", () => { })]);
            var window = ShowHost();
            try
            {
                M3Motion.ReducedMotionOverride = false;
                menu.OpenAt(100, 100);
                Assert.True(menu.IsOpen);

                menu.CloseMenu();

                WaitUntil(() => !menu.IsOpen);
            }
            finally
            {
                M3Motion.ReducedMotionOverride = null;
                Teardown(menu, window);
            }
        });
    }

    [Fact]
    public void Menu_SnapsOpenAndShut_UnderReducedMotion()
    {
        RunOnSta(() =>
        {
            var menu = new M3PopupMenu();
            menu.SetItems([new M3MenuItem("Open JoyNect", () => { })]);
            var window = ShowHost();
            try
            {
                M3Motion.ReducedMotionOverride = true;
                menu.OpenAt(100, 100);

                // No slide and no fade: the menu is at rest and opaque straight away.
                Assert.True(menu.IsOpen);
                Assert.Equal(0.0, menu.MenuTransform.Y, 6);
                Assert.Equal(1.0, menu.MenuOpacity, 6);

                menu.CloseMenu();
                WaitUntil(() => !menu.IsOpen);
            }
            finally
            {
                M3Motion.ReducedMotionOverride = null;
                Teardown(menu, window);
            }
        });
    }
}
