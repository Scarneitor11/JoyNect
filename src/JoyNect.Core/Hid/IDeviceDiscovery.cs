using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace JoyNect.Core.Hid;

/// <summary>
/// A device that is currently attached, plus the way to open it.
/// </summary>
/// <remarks>
/// The open step is a delegate rather than a direct method so that discovery can be replaced with a
/// scripted source in tests: the rest of the pipeline (handshake, mapping, pad output) is identical
/// whether the bytes come from a real controller or a simulator.
/// </remarks>
public sealed record DiscoveredDevice(HidCandidate Candidate, Func<IControllerTransport?> Open);

/// <summary>Finds controllers that are attached to this machine.</summary>
public interface IDeviceDiscovery
{
    IReadOnlyList<DiscoveredDevice> Discover();
}

/// <summary>Discovery backed by the Windows HID stack.</summary>
public sealed class HidSharpDeviceDiscovery : IDeviceDiscovery
{
    private readonly DeviceEnumerator _enumerator;
    private readonly ILogger _logger;

    public HidSharpDeviceDiscovery(DeviceEnumerator? enumerator = null, ILogger? logger = null)
    {
        _enumerator = enumerator ?? new DeviceEnumerator();
        _logger = logger ?? NullLogger.Instance;
    }

    public IReadOnlyList<DiscoveredDevice> Discover() =>
        _enumerator.EnumerateMatches()
            .Select(match => new DiscoveredDevice(match.Candidate, () => Open(match)))
            .ToArray();

    private IControllerTransport? Open(HidDeviceMatch match)
    {
        if (!HidControllerTransport.TryOpen(match.Device, match.Candidate, out var transport, out var error, _logger))
        {
            _logger.LogInformation("Could not open {Path}: {Error}", match.Candidate.DevicePath, error);
            return null;
        }

        return transport;
    }
}
