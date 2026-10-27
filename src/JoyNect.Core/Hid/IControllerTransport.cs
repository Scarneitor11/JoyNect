using JoyNect.Core.Hid;

namespace JoyNect.Core.Hid;

/// <summary>
/// A bidirectional HID link to one controller.
/// </summary>
/// <remarks>
/// Deliberately narrow: the session state machine owns all protocol logic, and the transport only
/// moves bytes. That is what lets the whole pipeline run against a scripted simulator in tests.
/// </remarks>
public interface IControllerTransport : IDisposable
{
    /// <summary>Human readable identification, used in logs and the diagnostics view.</summary>
    string Description { get; }

    ConnectionKind Connection { get; }

    /// <summary>Longest input report the device can deliver.</summary>
    int MaxInputReportLength { get; }

    /// <summary>Length an output report must have, including the report id byte.</summary>
    int OutputReportLength { get; }

    /// <summary>
    /// Reads one input report.
    /// </summary>
    /// <returns>Number of bytes read, or 0 when <paramref name="timeoutMilliseconds"/> elapsed.</returns>
    int Read(Span<byte> buffer, int timeoutMilliseconds);

    /// <summary>
    /// Writes one output report.
    /// </summary>
    /// <remarks>Implementations must not silently swallow failures other than a closed device.</remarks>
    void Write(ReadOnlySpan<byte> report);
}
