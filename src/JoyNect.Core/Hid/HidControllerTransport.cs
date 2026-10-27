using HidSharp;
using JoyNect.Core.Protocol;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace JoyNect.Core.Hid;

/// <summary>
/// HID transport backed by the Windows HID stack through HidSharp.
/// </summary>
public sealed class HidControllerTransport : IControllerTransport
{
    private readonly HidStream _stream;
    private readonly ILogger _logger;
    private readonly byte[] _outputBuffer;
    private readonly byte[] _readBuffer;

    private HidControllerTransport(HidStream stream, HidCandidate candidate, ILogger logger)
    {
        _stream = stream;
        _logger = logger;
        Description = candidate.DevicePath;
        Connection = candidate.Connection;
        MaxInputReportLength = candidate.MaxInputReportLength > 0
            ? candidate.MaxInputReportLength
            : ProtocolConstants.FullStateReportLength;
        OutputReportLength = ResolveOutputReportLength(candidate);
        _outputBuffer = new byte[OutputReportLength];
        _readBuffer = new byte[Math.Max(MaxInputReportLength, ProtocolConstants.FullStateReportLength)];
    }

    public string Description { get; }

    public ConnectionKind Connection { get; }

    public int MaxInputReportLength { get; }

    public int OutputReportLength { get; }

    /// <summary>Opens a candidate for reading and writing.</summary>
    public static bool TryOpen(
        HidDevice device,
        HidCandidate candidate,
        out HidControllerTransport? transport,
        out string? error,
        ILogger? logger = null)
    {
        transport = null;
        error = null;
        logger ??= NullLogger.Instance;

        try
        {
            if (!device.TryOpen(out var stream) || stream is null)
            {
                error = "the device is in use by another program (close BetterJoy, DS4Windows, Steam " +
                        "or any other controller tool) or access was denied";
                return false;
            }

            // Short timeouts: the reader loop must be able to notice cancellation and staleness.
            stream.ReadTimeout = 250;
            stream.WriteTimeout = 250;

            transport = new HidControllerTransport(stream, candidate, logger);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            logger.LogWarning(ex, "Opening {Path} failed", candidate.DevicePath);
            return false;
        }
    }

    public int Read(Span<byte> buffer, int timeoutMilliseconds)
    {
        try
        {
            _stream.ReadTimeout = timeoutMilliseconds;
            var read = _stream.Read(_readBuffer, 0, _readBuffer.Length);
            if (read <= 0)
            {
                return 0;
            }

            // The report id is byte 0 of what the HID stack returns, matching how every other
            // controller tool on Windows reads these devices.
            var count = Math.Min(read, buffer.Length);
            _readBuffer.AsSpan(0, count).CopyTo(buffer);
            return count;
        }
        catch (TimeoutException)
        {
            // The expected path when nothing arrived: not an error.
            return 0;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Read from {Path} failed", Description);
            throw new ControllerLinkException("Reading from the controller failed.", ex);
        }
    }

    public void Write(ReadOnlySpan<byte> report)
    {
        try
        {
            // Windows requires the buffer to match the declared output report length, so short
            // frames are zero padded rather than rejected.
            _outputBuffer.AsSpan().Clear();
            var count = Math.Min(report.Length, _outputBuffer.Length);
            report[..count].CopyTo(_outputBuffer);

            _stream.Write(_outputBuffer, 0, _outputBuffer.Length);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Write to {Path} failed", Description);
            throw new ControllerLinkException("Writing to the controller failed.", ex);
        }
    }

    public void Dispose()
    {
        try
        {
            _stream.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Disposing the HID stream failed");
        }
    }

    /// <summary>
    /// Decides how long an output report must be.
    /// </summary>
    /// <remarks>
    /// Priority: the length the report descriptor declares for report id 0x01, then the device's
    /// maximum output report length, then the documented 49 byte frame. Bluetooth controllers
    /// commonly declare 49 bytes while the reference notes describe 64 byte padded frames, so the
    /// descriptor wins whenever it has an opinion.
    /// </remarks>
    public static int ResolveOutputReportLength(HidCandidate candidate)
    {
        var declared = candidate.OutputReports
            .FirstOrDefault(r => r.ReportId == ProtocolConstants.OutputRumbleAndSubcommand)?.Length ?? 0;

        var length = declared > 0 ? declared : candidate.MaxOutputReportLength;
        if (length < OutputFrames.FrameLength)
        {
            length = OutputFrames.FrameLength;
        }

        if (candidate.MaxOutputReportLength > 0 && length > candidate.MaxOutputReportLength)
        {
            length = candidate.MaxOutputReportLength;
        }

        return length;
    }
}

/// <summary>Raised when the HID link itself failed, as opposed to a protocol level problem.</summary>
public sealed class ControllerLinkException : Exception
{
    public ControllerLinkException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}
