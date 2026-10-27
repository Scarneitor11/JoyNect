using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using JoyNect.Core.Hid;
using JoyNect.Core.Output;

namespace JoyNect.Core.Diagnostics;

/// <summary>Everything JoyNect can report about this machine, ready to paste into a support request.</summary>
public sealed class DiagnosticReport
{
    public required string ProductVersion { get; init; }

    public required string OsDescription { get; init; }

    public required string FrameworkDescription { get; init; }

    public required string ProcessArchitecture { get; init; }

    public required bool IsElevated { get; init; }

    public required DateTimeOffset GeneratedAt { get; init; }

    public required DriverStatus Driver { get; init; }

    public required IReadOnlyList<HidCandidate> Candidates { get; init; }

    public required IReadOnlyList<string> Notes { get; init; }

    /// <summary>Collects a report without creating virtual pads or touching a controller's state.</summary>
    public static DiagnosticReport Collect(
        DriverStatus? driver = null,
        IReadOnlyList<string>? notes = null,
        DeviceEnumerator? enumerator = null)
    {
        enumerator ??= new DeviceEnumerator();
        var assembly = typeof(DiagnosticReport).Assembly;

        return new DiagnosticReport
        {
            ProductVersion = assembly.GetName().Version?.ToString() ?? "unknown",
            OsDescription = RuntimeInformation.OSDescription,
            FrameworkDescription = RuntimeInformation.FrameworkDescription,
            ProcessArchitecture = RuntimeInformation.ProcessArchitecture.ToString(),
            IsElevated = IsRunningElevated(),
            GeneratedAt = DateTimeOffset.Now,
            Driver = driver ?? DriverProbe.Probe(),
            Candidates = enumerator.EnumerateCandidates(),
            Notes = notes ?? [],
        };
    }

    public string ToText()
    {
        var builder = new StringBuilder();

        builder.AppendLine("JoyNect diagnostic report");
        builder.AppendLine(string.Create(CultureInfo.InvariantCulture, $"generated : {GeneratedAt:yyyy-MM-dd HH:mm:ss zzz}"));
        builder.AppendLine(string.Create(CultureInfo.InvariantCulture, $"version   : {ProductVersion}"));
        builder.AppendLine(string.Create(CultureInfo.InvariantCulture, $"os        : {OsDescription}"));
        builder.AppendLine(string.Create(CultureInfo.InvariantCulture, $"runtime   : {FrameworkDescription} ({ProcessArchitecture})"));
        builder.AppendLine(string.Create(CultureInfo.InvariantCulture, $"elevated  : {(IsElevated ? "yes" : "no")}"));
        builder.AppendLine();

        builder.AppendLine("-- virtual gamepad driver --");
        builder.AppendLine(string.Create(CultureInfo.InvariantCulture, $"status    : {(Driver.Available ? "available" : "unavailable")}"));
        builder.AppendLine(string.Create(CultureInfo.InvariantCulture, $"detail    : {Driver.Summary}"));
        if (!string.IsNullOrWhiteSpace(Driver.Error))
        {
            builder.AppendLine(string.Create(CultureInfo.InvariantCulture, $"error     : {Driver.Error}"));
        }

        if (!string.IsNullOrWhiteSpace(Driver.Remediation))
        {
            builder.AppendLine(string.Create(CultureInfo.InvariantCulture, $"fix       : {Driver.Remediation}"));
        }

        builder.AppendLine();
        builder.AppendLine("-- nintendo HID interfaces present --");

        if (Candidates.Count == 0)
        {
            builder.AppendLine("none. Pair a controller (hold SYNC for 2 seconds) or connect it over USB.");
        }
        else
        {
            var index = 1;
            foreach (var group in DeviceEnumerator.GroupByPhysicalDevice(Candidates))
            {
                builder.AppendLine(string.Create(CultureInfo.InvariantCulture, $"[{index++}] {group.Key}"));
                foreach (var candidate in group)
                {
                    builder.AppendLine(string.Create(CultureInfo.InvariantCulture, $"     {candidate.Describe()}"));
                    builder.AppendLine(string.Create(CultureInfo.InvariantCulture, $"     path      {candidate.DevicePath}"));
                    if (!string.IsNullOrWhiteSpace(candidate.DescriptorError))
                    {
                        builder.AppendLine(string.Create(CultureInfo.InvariantCulture, $"     descriptor unreadable: {candidate.DescriptorError}"));
                    }
                }
            }
        }

        if (Notes.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("-- notes --");
            foreach (var note in Notes)
            {
                builder.AppendLine(string.Create(CultureInfo.InvariantCulture, $"- {note}"));
            }
        }

        return builder.ToString();
    }

    public string ToJson()
    {
        var payload = new
        {
            product = "JoyNect",
            version = ProductVersion,
            generatedAt = GeneratedAt,
            os = OsDescription,
            runtime = FrameworkDescription,
            architecture = ProcessArchitecture,
            elevated = IsElevated,
            driver = new
            {
                available = Driver.Available,
                summary = Driver.Summary,
                remediation = Driver.Remediation,
                error = Driver.Error,
            },
            hidCandidates = Candidates.Select(c => new
            {
                c.VendorId,
                c.ProductId,
                model = c.Model.Model.ToString(),
                displayName = c.Model.DisplayName,
                support = c.Model.Support.ToString(),
                connection = c.Connection.ToString(),
                c.MaxInputReportLength,
                c.MaxOutputReportLength,
                c.ProductName,
                c.DevicePath,
                c.PhysicalDeviceKey,
                declaresVendorReports = c.DeclaresVendorReports,
                inputReports = c.InputReports.Select(r => new { reportId = $"0x{r.ReportId:x2}", r.Length }),
                outputReports = c.OutputReports.Select(r => new { reportId = $"0x{r.ReportId:x2}", r.Length }),
                c.DescriptorError,
            }),
            notes = Notes,
        };

        return System.Text.Json.JsonSerializer.Serialize(
            payload,
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
    }

    public static bool IsRunningElevated()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch (Exception)
        {
            return false;
        }
    }
}
