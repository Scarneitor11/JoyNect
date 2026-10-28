using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using JoyNect.App.Shell;
using JoyNect.Ui.Theme;

namespace JoyNect.App.Capture;

/// <summary>
/// Renders every screen to PNG without a controller, a driver or a visible window.
/// </summary>
/// <remarks>
/// A WPF window can be laid out and drawn off screen, which makes the visual design reviewable in a
/// pull request instead of only on the machine it was written on. The window is parked far off screen
/// so it never flashes; the render still goes through the real layout, text rendering and effects.
/// </remarks>
internal static class CaptureRunner
{
    private static readonly string[] Pages =
    [
        "home", "devices", "mapping", "motion", "lights", "diagnostics", "about", "settings", "style",
    ];

    /// <summary>Writes one PNG per screen into <paramref name="directory"/>.</summary>
    public static int Run(string directory, bool dark)
    {
        var path = Path.GetFullPath(directory);
        Directory.CreateDirectory(path);

        var state = new DemoState(dark);
        var window = new MainWindow(state)
        {
            ShowInTaskbar = false,
            ShowActivated = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -20000,
            Top = -20000,
            Width = 1240,
            Height = 860,
        };

        window.Show();
        Pump();

        foreach (var page in Pages)
        {
            window.NavigateTo(page, force: true);
            Pump();
            Save(window.CaptureRoot, Path.Combine(path, $"{page}{(dark ? "-dark" : string.Empty)}.png"));
        }

        // The overlay is part of the first-run experience, so it is captured like a screen.
        window.ShowOnboarding();
        Pump();
        Save(window.CaptureRoot, Path.Combine(path, $"onboarding{(dark ? "-dark" : string.Empty)}.png"));

        window.Close();
        state.Dispose();

        Console.WriteLine($"captured {Pages.Length + 1} screens to {path}");
        return 0;
    }

    private static void Save(FrameworkElement root, string file)
    {
        root.UpdateLayout();

        var width = (int)Math.Ceiling(root.ActualWidth);
        var height = (int)Math.Ceiling(root.ActualHeight);
        if (width <= 0 || height <= 0)
        {
            throw new InvalidOperationException($"The window was not laid out ({width}x{height}).");
        }

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(root);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));

        using var stream = File.Create(file);
        encoder.Save(stream);
    }

    /// <summary>Runs the dispatcher until layout, rendering and entrance animations have settled.</summary>
    private static void Pump(int milliseconds = 320)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(milliseconds),
            DispatcherPriority.Background,
            (_, _) => frame.Continue = false,
            Dispatcher.CurrentDispatcher);

        timer.Start();
        Dispatcher.PushFrame(frame);
        timer.Stop();
        _ = M3ThemeManager.Current;
    }
}
