using System.Collections;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using JoyNect.Ui.Controls;
using JoyNect.Ui.Shell;
using JoyNect.Ui.Theme;
using Xunit;

namespace JoyNect.Core.Tests.Ui;

/// <summary>Checks the staggered entrance and the selection pill fade on NavRail in a shown window.</summary>
[Collection("WPF controls")]
public class NavRailEntranceTests
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

    private static NavRail BuildRail()
    {
        var rail = new NavRail();
        rail.SetDestinations(
        [
            new NavDestination("a", "Alpha", M3Glyph.Gamepad),
            new NavDestination("b", "Beta", M3Glyph.Sliders),
            new NavDestination("c", "Gamma", M3Glyph.Swap),
        ], bottomFrom: 3);
        return rail;
    }

    // NavRail keeps its items in the private _buttons list, and each item's Slide is a public transform.
    private static List<UIElement> Items(NavRail rail)
    {
        var buttons = (IEnumerable)rail.GetType().GetField("_buttons", Instance)!.GetValue(rail)!;
        return buttons.Cast<object>().Select(b => (UIElement)b).ToList();
    }

    private static TranslateTransform SlideOf(UIElement item) =>
        (TranslateTransform)item.GetType().GetProperty("Slide")!.GetValue(item)!;

    // The fade lives on each item's Body, which the base control never rewrites.
    private static double OpacityOf(UIElement item) =>
        ((UIElement)item.GetType().GetProperty("Body")!.GetValue(item)!).Opacity;

    // The selection pill is the private _pill inside each NavRailButton. It is the element that fades on selection.
    private static double PillOpacityOf(UIElement item) =>
        ((UIElement)item.GetType().GetField("_pill", Instance)!.GetValue(item)!).Opacity;

    private static UIElement ItemFor(NavRail rail, string key) =>
        Items(rail).First(i => ((NavDestination)i.GetType().GetProperty("Destination")!.GetValue(i)!).Key == key);

    private static Window ShowHosted(FrameworkElement element)
    {
        var window = new Window
        {
            Content = element,
            Width = 400,
            Height = 600,
            WindowStyle = WindowStyle.None,
            ShowInTaskbar = false,
            ShowActivated = false,
        };
        window.Show();
        window.UpdateLayout();
        return window;
    }

    [Fact]
    public void Entrance_StartsOffset_AndSettlesAtRest()
    {
        RunOnSta(() =>
        {
            Window? window = null;
            try
            {
                M3Motion.ReducedMotionOverride = false;
                var rail = BuildRail();
                window = ShowHosted(rail);

                var items = Items(rail);
                var start = string.Join(", ", items.Select(i => $"op={OpacityOf(i):0.###} y={SlideOf(i).Y:0.###}"));
                Assert.True(items.All(i => OpacityOf(i) < 1.0 && SlideOf(i).Y > 0.0), $"Not offset at start: {start}");

                var settled = WaitUntil(
                    () => items.All(item => Math.Abs(SlideOf(item).Y) < 0.5 && OpacityOf(item) > 0.99),
                    TimeSpan.FromSeconds(5));
                Assert.True(settled, "Rail items did not settle at rest.");
            }
            finally
            {
                M3Motion.ReducedMotionOverride = null;
                window?.Close();
            }
        });
    }

    [Fact]
    public void Entrance_SnapsToRest_UnderReducedMotion()
    {
        RunOnSta(() =>
        {
            Window? window = null;
            try
            {
                M3Motion.ReducedMotionOverride = true;
                var rail = BuildRail();
                window = ShowHosted(rail);

                var items = Items(rail);
                var settled = WaitUntil(
                    () => items.All(item => Math.Abs(SlideOf(item).Y) < 0.5 && OpacityOf(item) > 0.99),
                    TimeSpan.FromSeconds(5));
                Assert.True(settled, "Rail items did not snap to rest under reduced motion.");
            }
            finally
            {
                M3Motion.ReducedMotionOverride = null;
                window?.Close();
            }
        });
    }

    [Fact]
    public void Selecting_AnotherDestination_FadesNewPillIn_AndOldPillOut()
    {
        RunOnSta(() =>
        {
            Window? window = null;
            try
            {
                M3Motion.ReducedMotionOverride = false;
                var rail = BuildRail();
                window = ShowHosted(rail);

                var alpha = ItemFor(rail, "a");
                var beta = ItemFor(rail, "b");
                var timeout = TimeSpan.FromSeconds(5);
                Assert.True(WaitUntil(() => PillOpacityOf(alpha) > 0.99, timeout), "Initial pill did not fade in.");

                rail.Select("b", raise: false);
                Assert.True(PillOpacityOf(beta) < 0.99, "Newly selected pill jumped to full opacity instead of fading in.");

                var swapped = WaitUntil(() => PillOpacityOf(beta) > 0.99 && PillOpacityOf(alpha) < 0.01, timeout);
                Assert.True(
                    swapped,
                    $"Pills did not settle: new={PillOpacityOf(beta):0.###}, old={PillOpacityOf(alpha):0.###}.");
            }
            finally
            {
                M3Motion.ReducedMotionOverride = null;
                window?.Close();
            }
        });
    }

    [Fact]
    public void Selecting_UnderReducedMotion_SnapsPillsToTarget_WithoutIntermediateOpacity()
    {
        RunOnSta(() =>
        {
            Window? window = null;
            try
            {
                M3Motion.ReducedMotionOverride = true;
                var rail = BuildRail();
                window = ShowHosted(rail);

                var alpha = ItemFor(rail, "a");
                var beta = ItemFor(rail, "b");
                var timeout = TimeSpan.FromSeconds(5);
                Assert.True(WaitUntil(() => PillOpacityOf(alpha) > 0.99, timeout), "Initial pill did not snap in.");

                rail.Select("b", raise: false);

                var observed = new List<double>();
                var swapped = WaitUntil(() =>
                {
                    observed.Add(PillOpacityOf(beta));
                    return PillOpacityOf(beta) > 0.99 && PillOpacityOf(alpha) < 0.01;
                }, timeout);

                Assert.True(swapped, "Pills did not snap to their targets under reduced motion.");
                Assert.DoesNotContain(observed, v => v > 0.01 && v < 0.99);
            }
            finally
            {
                M3Motion.ReducedMotionOverride = null;
                window?.Close();
            }
        });
    }
}
