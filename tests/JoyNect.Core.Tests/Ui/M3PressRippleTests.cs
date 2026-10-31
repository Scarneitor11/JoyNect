using System.Collections;
using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using JoyNect.Ui.Controls;
using JoyNect.Ui.Shell;
using JoyNect.Ui.Theme;
using Xunit;

namespace JoyNect.Core.Tests.Ui;

/// <summary>Checks that a press on a select option, a segment and a rail button starts the shared M3 press ripple.</summary>
[Collection("WPF controls")]
public class M3PressRippleTests
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

    // Mouse capture needs a live presentation source, so each control is hosted in a shown window.
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

    // WPF sets ButtonBase.IsPressed from the physical button state, which a synthetic event cannot change.
    // This sets the same dependency property through WPF's own read-only key, as a real press would.
    private static void Press(ButtonBase button)
    {
        var key = (DependencyPropertyKey)typeof(ButtonBase)
            .GetField("IsPressedPropertyKey", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)!
            .GetValue(null)!;
        button.SetValue(key, true);
        button.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
        {
            RoutedEvent = UIElement.MouseLeftButtonDownEvent,
        });
        Assert.True(button.IsPressed);
    }

    // Pumps the dispatcher until the condition holds or the timeout passes, so animations can progress.
    private static bool PumpUntil(Func<bool> condition, TimeSpan timeout)
    {
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed < timeout)
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

    // The shared ripple is the Ellipse M3Interactive owns. A press has started it once it is visible and has a size.
    private static void AssertRippleStarts(ButtonBase button, string label)
    {
        var ripple = Get<Ellipse>(button, "_ripple");
        var scale = (ScaleTransform)ripple.RenderTransform!;

        Assert.True(
            PumpUntil(() => ripple.Opacity > 0 && scale.ScaleX > 0 && ripple.ActualWidth > 0 && ripple.ActualHeight > 0, TimeSpan.FromSeconds(3)),
            $"{label}: no ripple started (opacity {ripple.Opacity}, scale {scale.ScaleX}, size {ripple.ActualWidth}x{ripple.ActualHeight})");
    }

    [Theory]
    [InlineData("segment")]
    [InlineData("select option")]
    [InlineData("rail button")]
    public void Press_StartsRipple(string kind)
    {
        RunOnSta(() =>
        {
            try
            {
                M3Motion.ReducedMotionOverride = false;
                ButtonBase button;
                Window host;
                switch (kind)
                {
                    case "segment":
                    {
                        var segments = new M3SegmentedButtons();
                        segments.SetSegments("One", "Two");
                        host = ShowHosted(segments);
                        var items = (IEnumerable)segments.GetType().GetField("_segments", Instance)!.GetValue(segments)!;
                        var first = items.Cast<object>().First();
                        button = (ButtonBase)first.GetType().GetProperty("Button")!.GetValue(first)!;
                        break;
                    }
                    case "select option":
                    {
                        var select = new M3Select("Test");
                        select.SetOptions(["One", "Two"], "One");
                        host = ShowHosted(select);

                        // The options live in a popup, so the menu has to be open for them to be laid out and pressed.
                        select.OpenMenu();
                        var options = Get<List<M3Button>>(select, "_buttons");
                        Assert.True(PumpUntil(() => options[0].IsLoaded && options[0].ActualWidth > 0, TimeSpan.FromSeconds(3)));
                        button = options[0];
                        break;
                    }
                    case "rail button":
                    {
                        var rail = new NavRail();
                        rail.SetDestinations(
                        [
                            new NavDestination("a", "Alpha", M3Glyph.Gamepad),
                            new NavDestination("b", "Beta", M3Glyph.Sliders),
                        ], bottomFrom: 2);
                        host = ShowHosted(rail);
                        var items = (IEnumerable)rail.GetType().GetField("_buttons", Instance)!.GetValue(rail)!;
                        button = (ButtonBase)items.Cast<object>().First();
                        break;
                    }
                    default:
                        throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
                }

                using var _ = new WindowScope(host);
                Press(button);
                AssertRippleStarts(button, kind);
            }
            finally
            {
                M3Motion.ReducedMotionOverride = null;
            }
        });
    }

    private sealed class WindowScope(Window window) : IDisposable
    {
        public void Dispose() => window.Close();
    }
}
