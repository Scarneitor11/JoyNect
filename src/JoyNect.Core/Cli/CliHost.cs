using System.Globalization;
using JoyNect.Core.Diagnostics;
using JoyNect.Core.Hid;
using JoyNect.Core.Output;
using Microsoft.Extensions.Logging;

namespace JoyNect.Core.Cli;

/// <summary>
/// Command line surface shared by <c>JoyNect.exe</c> and <c>JoyNect.Cli.exe</c>.
/// </summary>
/// <remarks>
/// Everything here is deliberately free of <see cref="Console"/> so it can be unit tested and so the
/// WPF app can reuse it when it is launched with a command line flag.
/// </remarks>
public sealed class CliHost
{
    private readonly TextWriter _output;
    private readonly TextWriter _error;
    private readonly ILogger<CliHost>? _logger;

    public CliHost(TextWriter output, TextWriter error, ILogger<CliHost>? logger = null)
    {
        _output = output;
        _error = error;
        _logger = logger;
    }

    public const string Version = "1.0.0";

    public static string HelpText =>
        """
        JoyNect - use Nintendo Switch controllers as virtual Xbox 360 controllers.

        Usage: JoyNect <command> [options]

        Commands
          ui                    Open the JoyNect window (default when no command is given)
          diagnose              Print a full diagnostic report (driver, HID interfaces)
          driver                Check whether the ViGEmBus virtual pad driver is usable
          enumerate             List Nintendo HID interfaces with their report descriptors
          api-dump              Print the public API of the bundled ViGEm client library
          help                  Show this text

        Options
          --json                Machine readable output where supported
          --out <path>          Also write the output to a file
          --test-pad            diagnose: also create and remove one virtual pad
          --verbose             Include debug logging on stderr

        Notes
          - JoyNect never changes a controller's settings unless you ask it to.
          - A paired-but-disconnected controller appears when you press any button on it, or when
            you connect it over USB.
        """;

    public async Task<int> RunAsync(string[] args, CancellationToken cancellationToken = default)
    {
        var command = args.Length == 0 ? "ui" : args[0].TrimStart('-').ToLowerInvariant();
        var options = CliOptions.Parse(args.Skip(1));

        try
        {
            switch (command)
            {
                case "ui":
                    _output.WriteLine("The user interface is only available in the desktop app (JoyNect.exe).");
                    _output.WriteLine();
                    _output.WriteLine(HelpText);
                    return 0;

                case "help":
                    _output.WriteLine(HelpText);
                    return 0;

                case "driver":
                    return await WriteAsync(DescribeDriver(), options).ConfigureAwait(false);

                case "enumerate":
                    return await WriteAsync(DescribeDevices(options.AllHidDevices), options).ConfigureAwait(false);

                case "diagnose":
                    return await RunDiagnoseAsync(options, cancellationToken).ConfigureAwait(false);

                case "api-dump":
                    return await WriteAsync(ApiSurfaceDumper.Dump(), options).ConfigureAwait(false);

                default:
                    _error.WriteLine(string.Create(CultureInfo.InvariantCulture, $"Unknown command: {args[0]}"));
                    _error.WriteLine();
                    _error.WriteLine(HelpText);
                    return 2;
            }
        }
        catch (OperationCanceledException)
        {
            _error.WriteLine("Cancelled.");
            return 130;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Command {Command} failed", command);
            _error.WriteLine(string.Create(CultureInfo.InvariantCulture, $"error: {ex.Message}"));
            return 1;
        }
    }

    private async Task<int> RunDiagnoseAsync(CliOptions options, CancellationToken cancellationToken)
    {
        var driver = DriverProbe.Probe(_logger, createTestPad: options.TestPad);
        var report = DiagnosticReport.Collect(driver: driver);

        var text = options.Json ? report.ToJson() : report.ToText();
        return await WriteAsync(text, options, cancellationToken).ConfigureAwait(false);
    }

    private string DescribeDriver()
    {
        var status = DriverProbe.Probe(_logger);
        var builder = new System.Text.StringBuilder();
        builder.AppendLine(string.Create(CultureInfo.InvariantCulture, $"ViGEmBus driver: {(status.Available ? "available" : "unavailable")}"));
        builder.AppendLine(string.Create(CultureInfo.InvariantCulture, $"  {status.Summary}"));
        if (!string.IsNullOrWhiteSpace(status.Remediation))
        {
            builder.AppendLine(string.Create(CultureInfo.InvariantCulture, $"  fix: {status.Remediation}"));
            builder.AppendLine(string.Create(CultureInfo.InvariantCulture, $"  installer: {DriverProbe.OfficialInstallerUri}"));
        }

        return builder.ToString();
    }

    private string DescribeDevices(bool includeEveryHidDevice)
    {
        var enumerator = new DeviceEnumerator();
        var candidates = includeEveryHidDevice ? enumerator.EnumerateAll() : enumerator.EnumerateCandidates();

        if (candidates.Count == 0)
        {
            return includeEveryHidDevice
                ? "No HID interfaces found at all, which means the HID stack or the enumeration layer is broken."
                : "No Nintendo HID interfaces found. Pair a controller (hold SYNC for 2 seconds) or connect it over USB.";
        }

        var builder = new System.Text.StringBuilder();
        builder.AppendLine(string.Create(CultureInfo.InvariantCulture, $"{candidates.Count} HID interface(s):"));

        foreach (var group in DeviceEnumerator.GroupByPhysicalDevice(candidates))
        {
            builder.AppendLine(group.Key);
            foreach (var candidate in group)
            {
                builder.AppendLine(string.Create(CultureInfo.InvariantCulture, $"  {candidate.Describe()}"));
                builder.AppendLine(string.Create(CultureInfo.InvariantCulture, $"  vendor-defined-report {candidate.DeclaresVendorReports}"));
                builder.AppendLine(string.Create(CultureInfo.InvariantCulture, $"  path     {candidate.DevicePath}"));
            }
        }

        return builder.ToString();
    }

    private async Task<int> WriteAsync(string text, CliOptions options, CancellationToken cancellationToken = default)
    {
        _output.Write(text);
        if (!text.EndsWith('\n'))
        {
            _output.WriteLine();
        }

        if (!string.IsNullOrWhiteSpace(options.OutputPath))
        {
            var fullPath = Path.GetFullPath(options.OutputPath);
            var directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await File.WriteAllTextAsync(fullPath, text, cancellationToken).ConfigureAwait(false);
            _error.WriteLine(string.Create(CultureInfo.InvariantCulture, $"written to {fullPath}"));
        }

        return 0;
    }
}
