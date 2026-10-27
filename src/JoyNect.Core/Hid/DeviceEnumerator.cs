using HidSharp;
using JoyNect.Core.Protocol;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace JoyNect.Core.Hid;

/// <summary>
/// Finds the HID interfaces that could be Switch controllers.
/// </summary>
/// <remarks>
/// This type never opens a device for I/O; it only reports what the operating system advertises, so
/// it is safe to call at any time (including while another program is using the controller).
/// </remarks>
public sealed class DeviceEnumerator
{
    private readonly ILogger<DeviceEnumerator> _logger;

    public DeviceEnumerator(ILogger<DeviceEnumerator>? logger = null)
    {
        _logger = logger ?? NullLogger<DeviceEnumerator>.Instance;
    }

    /// <summary>Enumerates every Nintendo-vendor HID interface currently present.</summary>
    public IReadOnlyList<HidCandidate> EnumerateCandidates()
    {
        var results = new List<HidCandidate>();

        foreach (var device in EnumerateHidDevices())
        {
            // Nintendo's vendor id is the only pre-filter. Product ids are compared against the
            // known table later, but an unknown product id is still returned here: newer
            // controllers reuse the vendor id, and the protocol handshake is the real identity check.
            var candidate = Describe(device, requireNintendoVendorId: true);
            if (candidate is not null)
            {
                results.Add(candidate);
            }
        }

        return results;
    }

    /// <summary>
    /// Enumerates every HID interface on the machine, not just Nintendo's.
    /// </summary>
    /// <remarks>Used by the diagnostics page to prove that HID enumeration itself works.</remarks>
    public IReadOnlyList<HidCandidate> EnumerateAll()
    {
        var results = new List<HidCandidate>();
        foreach (var device in EnumerateHidDevices())
        {
            var candidate = Describe(device, requireNintendoVendorId: false);
            if (candidate is not null)
            {
                results.Add(candidate);
            }
        }

        return results;
    }

    /// <summary>Raw HID enumeration; separated so failures are reported once and clearly.</summary>
    public IReadOnlyList<HidDevice> EnumerateHidDevices()
    {
        try
        {
            return DeviceList.Local.GetHidDevices().ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "HID enumeration failed");
            return [];
        }
    }

    /// <summary>Groups the candidates that belong to the same physical controller.</summary>
    public static IReadOnlyList<IGrouping<string, HidCandidate>> GroupByPhysicalDevice(
        IEnumerable<HidCandidate> candidates) =>
        candidates.GroupBy(c => c.PhysicalDeviceKey, StringComparer.OrdinalIgnoreCase).ToArray();

    private HidCandidate? Describe(HidDevice device, bool requireNintendoVendorId)
    {
        int vendorId;
        int productId;
        try
        {
            vendorId = device.VendorID;
            productId = device.ProductID;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not read vendor/product id for a HID device");
            return null;
        }

        if (requireNintendoVendorId && vendorId != ProtocolConstants.NintendoVendorId)
        {
            return null;
        }

        var path = TryGet(() => device.DevicePath) ?? string.Empty;
        var candidate = new HidCandidate
        {
            DevicePath = path,
            VendorId = vendorId,
            ProductId = productId,
            ProductName = TryGet(() => device.GetProductName()) ?? string.Empty,
            Manufacturer = TryGet(() => device.GetManufacturer()) ?? string.Empty,
            ReleaseNumberBcd = TryGet(() => device.ReleaseNumberBcd),
            Connection = HidDevicePath.Classify(path),
            MaxInputReportLength = TryGet(() => device.GetMaxInputReportLength()),
            MaxOutputReportLength = TryGet(() => device.GetMaxOutputReportLength()),
            PhysicalDeviceKey = HidDevicePath.PhysicalDeviceKey(path, vendorId, productId),
        };

        try
        {
            var descriptor = device.GetReportDescriptor();
            var input = new List<ReportInfo>();
            var output = new List<ReportInfo>();

            foreach (var report in descriptor.InputReports)
            {
                input.Add(new ReportInfo(report.ReportID, report.Length, "in"));
            }

            foreach (var report in descriptor.OutputReports)
            {
                output.Add(new ReportInfo(report.ReportID, report.Length, "out"));
            }

            return candidate with
            {
                InputReports = input.OrderBy(r => r.ReportId).ToArray(),
                OutputReports = output.OrderBy(r => r.ReportId).ToArray(),
            };
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Report descriptor for {Path} could not be read", path);
            return candidate with { DescriptorError = ex.Message };
        }
    }

    private static T? TryGet<T>(Func<T> read)
    {
        try
        {
            return read();
        }
        catch
        {
            return default;
        }
    }
}
