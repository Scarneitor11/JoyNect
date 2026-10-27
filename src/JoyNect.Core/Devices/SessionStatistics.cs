using System.Globalization;
using JoyNect.Core.Diagnostics;

namespace JoyNect.Core.Devices;

/// <summary>
/// Live measurements for one controller link.
/// </summary>
/// <remarks>
/// This is how the app can honestly report link quality: report cadence percentiles, how many
/// reports were not understood, and what the first unexpected report looked like. That last field
/// exists so a user can hand over the exact bytes when something is wrong.
/// </remarks>
public sealed class SessionStatistics
{
    /// <summary>Sliding window of the intervals between full state reports.</summary>
    public IntervalHistogram ReportIntervals { get; } = new();

    public long ReportsReceived { get; private set; }

    public long UnrecognisedReports { get; private set; }

    public int Rehandshakes { get; private set; }

    public int HandshakeFailures { get; private set; }

    public long FramesWritten { get; private set; }

    public string? FirstUnrecognisedReportHex { get; private set; }

    public DateTimeOffset? LastReportAt { get; private set; }

    public double? ReportsPerSecond
    {
        get
        {
            var average = ReportIntervals.Average;
            return average is > 0 ? 1000.0 / average.Value : null;
        }
    }

    public void RecordReport(TimeSpan? intervalSincePrevious, DateTimeOffset now)
    {
        ReportsReceived++;
        LastReportAt = now;
        if (intervalSincePrevious is { } interval && interval > TimeSpan.Zero)
        {
            ReportIntervals.Add(interval);
        }
    }

    public void RecordUnrecognised(ReadOnlySpan<byte> report)
    {
        UnrecognisedReports++;
        FirstUnrecognisedReportHex ??= Convert.ToHexString(report[..Math.Min(report.Length, 24)]);
    }

    public void RecordRehandshake() => Rehandshakes++;

    public void RecordHandshakeFailure() => HandshakeFailures++;

    public void RecordWrite() => FramesWritten++;

    public string DescribeCadence() => ReportIntervals.Describe();

    public string DescribeSummary()
    {
        var builder = new System.Text.StringBuilder();
        builder.Append(string.Create(CultureInfo.InvariantCulture, $"{ReportsReceived} reports received"));
        if (ReportsReceived > 0)
        {
            builder.Append(string.Create(CultureInfo.InvariantCulture, $" ({DescribeCadence()})"));
        }

        if (UnrecognisedReports > 0)
        {
            builder.Append(string.Create(CultureInfo.InvariantCulture, $"; {UnrecognisedReports} unrecognised, first: {FirstUnrecognisedReportHex}"));
        }

        if (Rehandshakes > 0)
        {
            builder.Append(string.Create(CultureInfo.InvariantCulture, $"; {Rehandshakes} re-handshake(s) after a stall"));
        }

        if (HandshakeFailures > 0)
        {
            builder.Append(string.Create(CultureInfo.InvariantCulture, $"; {HandshakeFailures} handshake failure(s)"));
        }

        return builder.ToString();
    }
}
