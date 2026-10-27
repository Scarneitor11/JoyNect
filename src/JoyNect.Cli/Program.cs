using System.Text;
using JoyNect.Core.Cli;
using Microsoft.Extensions.Logging;

namespace JoyNect.Cli;

/// <summary>
/// Headless entry point. The desktop app exposes the same commands; this executable exists so the
/// commands can be scripted and run in environments that have no window.
/// </summary>
internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        try
        {
            Console.OutputEncoding = Encoding.UTF8;
        }
        catch (IOException)
        {
            // No console attached (for example when output is redirected); nothing to configure.
        }

        var options = CliOptions.Parse(args.Skip(1));
        using var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.SetMinimumLevel(options.Verbose ? LogLevel.Debug : LogLevel.Warning);
            builder.AddSimpleConsole(console =>
            {
                console.SingleLine = true;
                console.TimestampFormat = "HH:mm:ss ";
            });
        });

        var host = new CliHost(Console.Out, Console.Error, loggerFactory.CreateLogger<CliHost>());
        return await host.RunAsync(args).ConfigureAwait(false);
    }
}
