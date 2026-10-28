using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using JoyNect.App.Capture;
using JoyNect.Core.Cli;

namespace JoyNect.App;

/// <summary>Entry point for the desktop app.</summary>
/// <remarks>
/// The same executable is the GUI, the command line surface and the screenshot renderer, because a
/// second binary for diagnostics is one more thing to explain. A WPF app has no console, so the
/// command line commands attach to the parent console when there is one.
/// </remarks>
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length > 0 && args[0].Equals("--capture", StringComparison.OrdinalIgnoreCase))
        {
            var directory = args.Length > 1 && !args[1].StartsWith('-') ? args[1] : "captures";
            return Capture(directory, args.Any(a => a.Equals("--dark", StringComparison.OrdinalIgnoreCase)));
        }

        if (args.Length > 0 && IsCommand(args[0]))
        {
            return RunCommand(args);
        }

        using var mutex = new Mutex(initiallyOwned: true, @"Local\JoyNect.Desktop", out var isFirstInstance);
        if (!isFirstInstance)
        {
            MessageBox.Show(
                "JoyNect is already running. Look for its icon in the notification area.",
                "JoyNect",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return 0;
        }

        var startHidden = args.Any(a => a.Equals("--tray", StringComparison.OrdinalIgnoreCase));
        var application = new JoyNectApplication(startHidden);
        return application.Run();
    }

    private static int Capture(string directory, bool dark)
    {
        var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        JoyNect.Ui.Theme.M3Scrollbars.Install(application);

        try
        {
            return CaptureRunner.Run(directory, dark);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
        finally
        {
            application.Shutdown();
        }
    }

    private static bool IsCommand(string argument) =>
        argument.TrimStart('-').ToLowerInvariant() is "help" or "diagnose" or "driver" or "enumerate" or "api-dump";

    private static int RunCommand(string[] args)
    {
        NativeConsole.Attach();
        var host = new CliHost(Console.Out, Console.Error);
        return host.RunAsync(args).GetAwaiter().GetResult();
    }

    /// <summary>Attaches to the console that launched the process, if there is one.</summary>
    private static class NativeConsole
    {
        private const int AttachParentProcess = -1;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AttachConsole(int processId);

        public static void Attach()
        {
            if (!AttachConsole(AttachParentProcess))
            {
                return;
            }

            var stdout = Console.OpenStandardOutput();
            Console.SetOut(new StreamWriter(stdout) { AutoFlush = true });
        }
    }
}
