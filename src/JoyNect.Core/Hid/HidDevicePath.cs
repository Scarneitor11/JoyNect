using System.Globalization;

namespace JoyNect.Core.Hid;

/// <summary>
/// Parses Windows HID device interface paths.
/// </summary>
/// <remarks>
/// Windows exposes the same physical controller as several HID top-level collections. A Switch Pro
/// Controller connected over USB, for example, produces both a vendor-defined collection (carrying
/// report 0x30) and a generic "HID-compliant game controller" collection. Opening the wrong one
/// yields a device that never produces usable reports, so the path is the first thing we classify.
///
/// Typical shapes:
///   USB : \\?\hid#vid_057e&amp;pid_2009#7&amp;35c399e5&amp;0&amp;0000#{4d1e55b2-f16f-11cf-88cb-001111000030}
///   BT  : \\?\hid#{00001124-0000-1000-8000-00805f9b34fb}_vid&amp;0002057e_pid&amp;2009#a&amp;1f2e3d4c&amp;0&amp;0000#{...}
/// </remarks>
public static class HidDevicePath
{
    private const string BluetoothServiceGuidFragment = "00001124";

    public static ConnectionKind Classify(string? devicePath)
    {
        if (string.IsNullOrWhiteSpace(devicePath))
        {
            return ConnectionKind.Unknown;
        }

        var path = devicePath.ToLowerInvariant();

        // Bluetooth HID devices are enumerated behind the HID-over-Bluetooth service GUID, or (on
        // some stacks) behind an explicit bthhfenum/bth prefix.
        if (path.Contains(BluetoothServiceGuidFragment, StringComparison.Ordinal) ||
            path.Contains("bthhfenum", StringComparison.Ordinal) ||
            path.Contains("bth", StringComparison.Ordinal))
        {
            return ConnectionKind.Bluetooth;
        }

        if (path.Contains("usb", StringComparison.Ordinal) || path.StartsWith(@"\\?\hid#vid_", StringComparison.Ordinal))
        {
            return ConnectionKind.Usb;
        }

        return ConnectionKind.Unknown;
    }

    /// <summary>
    /// Key grouping the top-level collections that belong to one physical device.
    /// </summary>
    /// <remarks>
    /// Windows gives every collection its own device node whose instance ends in the collection
    /// index, for example <c>7&amp;5907fb6&amp;0&amp;0001</c> and <c>7&amp;5907fb6&amp;0&amp;0002</c>. Dropping that
    /// trailing index leaves the parent instance, which is what identifies the hardware.
    ///
    /// This is only a pre-filter: the authoritative identity is the controller's own MAC address,
    /// which is read over the protocol during the handshake.
    /// </remarks>
    public static string PhysicalDeviceKey(string? devicePath, int vendorId, int productId)
    {
        var instance = InstanceToken(devicePath);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"0x{vendorId:x4}:0x{productId:x4}:{instance}");
    }

    /// <summary>Extracts the parent device instance from a device path.</summary>
    public static string InstanceToken(string? devicePath)
    {
        if (string.IsNullOrWhiteSpace(devicePath))
        {
            return string.Empty;
        }

        var segments = devicePath.Split('#', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 3)
        {
            return string.Empty;
        }

        // segments: [ "\\?\hid", "vid_057e&pid_2009", "7&35c399e5&0&0000", "{interface-guid}" ]
        var instance = segments[2];
        var parts = instance.Split('&', StringSplitOptions.RemoveEmptyEntries);

        // "...&0&0001" is the top-level collection index, not part of the hardware identity.
        var length = parts.Length;
        if (length >= 2 && parts[length - 2] == "0")
        {
            length -= 2;
        }

        return string.Join('&', parts.Take(Math.Max(1, length))).ToLowerInvariant();
    }

    /// <summary>
    /// Extracts the lower 32 bits of the Bluetooth address that Windows embeds in a device path.
    /// </summary>
    /// <remarks>
    /// Bluetooth HID paths look like <c>..._pid&amp;2009#a&amp;2f9e0b1d&amp;0&amp;0000#{...}</c>: the
    /// <c>a&amp;</c> prefix marks an address derived instance and the eight hex digits are that address
    /// split into two halves. Only half the address is present, so this is a hint for diagnostics
    /// and never a unique key: the protocol handshake supplies the real address.
    /// </remarks>
    public static ulong? TryBluetoothAddress(string? devicePath)
    {
        if (string.IsNullOrWhiteSpace(devicePath) || Classify(devicePath) != ConnectionKind.Bluetooth)
        {
            return null;
        }

        var segments = devicePath.Split('#', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 3)
        {
            return null;
        }

        var parts = segments[2].Split('&', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2 &&
            parts[0].Equals("a", StringComparison.OrdinalIgnoreCase) &&
            parts[1].Length == 8 &&
            ulong.TryParse(parts[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var half))
        {
            return half;
        }

        return null;
    }
}
