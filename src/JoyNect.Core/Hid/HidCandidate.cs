namespace JoyNect.Core.Hid;

/// <summary>One HID report declared by a device's report descriptor.</summary>
public sealed record ReportInfo(byte ReportId, int Length, string Kind);

/// <summary>
/// An enumerable HID interface that might belong to a Switch controller, with everything we can
/// learn about it before opening it.
/// </summary>
public sealed record HidCandidate
{
    public required string DevicePath { get; init; }

    public required int VendorId { get; init; }

    public required int ProductId { get; init; }

    public string ProductName { get; init; } = string.Empty;

    public string Manufacturer { get; init; } = string.Empty;

    /// <summary>Release number in binary-coded decimal form; null when the device does not report one.</summary>
    public int? ReleaseNumberBcd { get; init; }

    public ConnectionKind Connection { get; init; }

    public int MaxInputReportLength { get; init; }

    public int MaxOutputReportLength { get; init; }

    /// <summary>Input reports declared by the descriptor, or empty when the descriptor could not be read.</summary>
    public IReadOnlyList<ReportInfo> InputReports { get; init; } = [];

    /// <summary>Output reports declared by the descriptor, or empty when the descriptor could not be read.</summary>
    public IReadOnlyList<ReportInfo> OutputReports { get; init; } = [];

    /// <summary>Best-effort grouping key for collections of one physical device.</summary>
    public string PhysicalDeviceKey { get; init; } = string.Empty;

    /// <summary>Why the report descriptor could not be read, when that happened.</summary>
    public string? DescriptorError { get; init; }

    public ModelInfo Model => SwitchModels.Describe(ProductId);

    /// <summary>
    /// True when the descriptor declares the vendor-defined full-state report: the marker that this
    /// is the collection we actually want, not the generic game-controller one.
    /// </summary>
    public bool DeclaresVendorReports =>
        InputReports.Any(r => r.ReportId is Protocol.ProtocolConstants.InputFullState
            or Protocol.ProtocolConstants.InputSubcommandReply
            or Protocol.ProtocolConstants.InputNsoFullState);

    /// <summary>Compact one-line description used by the diagnostics UI and CLI.</summary>
    public string Describe()
    {
        var connection = Connection switch
        {
            ConnectionKind.Bluetooth => "Bluetooth",
            ConnectionKind.Usb => "USB",
            _ => "unknown link",
        };

        var reports = InputReports.Count == 0
            ? "descriptor unavailable"
            : string.Join(", ", InputReports.Select(r => $"0x{r.ReportId:x2}({r.Length})"));

        return $"{Model.DisplayName} [{connection}] in<= {MaxInputReportLength} out<= {MaxOutputReportLength} reports: {reports}";
    }
}
