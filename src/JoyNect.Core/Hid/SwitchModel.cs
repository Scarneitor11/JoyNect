namespace JoyNect.Core.Hid;

/// <summary>How a controller is attached to the PC.</summary>
public enum ConnectionKind
{
    Unknown,
    Bluetooth,
    Usb,
}

/// <summary>Which kind of Switch controller we are looking at.</summary>
public enum SwitchModel
{
    Unknown,
    ProController,
    JoyConLeft,
    JoyConRight,
    NsoN64,
    NsoSnes,
    NsoNes,
    NsoGenesis,
}

/// <summary>Honest statement of how well we expect a device to work, surfaced verbatim in the UI.</summary>
public enum SupportLevel
{
    /// <summary>Implemented and verified against real hardware.</summary>
    Verified,

    /// <summary>Implemented from the protocol specification; not verified against hardware.</summary>
    Unverified,

    /// <summary>Recognised, driven best-effort, known to be incomplete.</summary>
    BestEffort,
}

/// <summary>A model name plus the support we claim for it.</summary>
public sealed record ModelInfo(SwitchModel Model, string DisplayName, SupportLevel Support, string Note)
{
    public static readonly ModelInfo Unknown = new(
        SwitchModel.Unknown,
        "Unknown Nintendo controller",
        SupportLevel.Unverified,
        "This device reports Nintendo's vendor id but JoyNect does not know the model. " +
        "It will be driven with the standard Pro Controller layout.");
}

/// <summary>Maps Nintendo product ids to models.</summary>
/// <remarks>
/// The Pro Controller, Joy-Con and Switch 2 family ids are long-standing and stable. The NSO retro
/// ids are taken from hid-nintendo.c and are marked unverified: JoyNect never relies on the product
/// id alone, it always confirms the device by asking it for its device info over the protocol.
/// </remarks>
public static class SwitchModels
{
    public const int ProductIdJoyConLeft = 0x2006;
    public const int ProductIdJoyConRight = 0x2007;
    public const int ProductIdProController = 0x2009;
    public const int ProductIdNsoN64 = 0x200E;
    public const int ProductIdNsoSnes = 0x2017;
    public const int ProductIdNsoNes = 0x2019;
    public const int ProductIdNsoGenesis = 0x201E;

    private static readonly Dictionary<int, ModelInfo> ByProductId = new()
    {
        [ProductIdProController] = new ModelInfo(
            SwitchModel.ProController,
            "Switch Pro Controller",
            SupportLevel.Unverified,
            "Full layout: sticks, triggers, D-pad, rumble, HD rumble and gyro."),

        [ProductIdJoyConLeft] = new ModelInfo(
            SwitchModel.JoyConLeft,
            "Joy-Con (L)",
            SupportLevel.Unverified,
            "Usable alone sideways, or paired with a Joy-Con (R) as a single virtual pad."),

        [ProductIdJoyConRight] = new ModelInfo(
            SwitchModel.JoyConRight,
            "Joy-Con (R)",
            SupportLevel.Unverified,
            "Usable alone sideways, or paired with a Joy-Con (L) as a single virtual pad."),

        [ProductIdNsoN64] = new ModelInfo(
            SwitchModel.NsoN64,
            "NSO N64 Controller",
            SupportLevel.BestEffort,
            "Retro layout, no sticks or rumble. NSO controllers report a different input report format."),

        [ProductIdNsoSnes] = new ModelInfo(
            SwitchModel.NsoSnes,
            "NSO Super NES Controller",
            SupportLevel.BestEffort,
            "Retro layout, no sticks or rumble. Mapped onto the Xbox 360 D-pad and face buttons."),

        [ProductIdNsoNes] = new ModelInfo(
            SwitchModel.NsoNes,
            "NSO NES Controller",
            SupportLevel.BestEffort,
            "Retro layout, no sticks or rumble. Mapped onto the Xbox 360 D-pad and face buttons."),

        [ProductIdNsoGenesis] = new ModelInfo(
            SwitchModel.NsoGenesis,
            "NSO SEGA Genesis Controller",
            SupportLevel.BestEffort,
            "Retro layout, no sticks or rumble. Mapped onto the Xbox 360 D-pad and face buttons."),
    };

    /// <summary>Number of distinct product ids JoyNect recognises.</summary>
    public static int KnownProductIdCount => ByProductId.Count;

    public static ModelInfo Describe(int productId) =>
        ByProductId.TryGetValue(productId, out var info) ? info : ModelInfo.Unknown;

    /// <summary>Looks up the model information for a model the controller reported about itself.</summary>
    public static ModelInfo DescribeProduct(SwitchModel model)
    {
        foreach (var info in ByProductId.Values)
        {
            if (info.Model == model)
            {
                return info;
            }
        }

        return ModelInfo.Unknown;
    }

    /// <summary>
    /// True when the product id is one we recognise, used only as a cheap pre-filter: the real
    /// identity check is the protocol handshake.
    /// </summary>
    public static bool IsKnownProductId(int productId) => ByProductId.ContainsKey(productId);

    public static bool IsJoyCon(SwitchModel model) =>
        model is SwitchModel.JoyConLeft or SwitchModel.JoyConRight;

    public static bool IsNsoRetro(SwitchModel model) =>
        model is SwitchModel.NsoN64 or SwitchModel.NsoSnes or SwitchModel.NsoNes or SwitchModel.NsoGenesis;

    /// <summary>Model as reported by the controller itself (device info subcommand reply).</summary>
    public static SwitchModel FromDeviceInfoType(byte controllerType) => controllerType switch
    {
        0x01 => SwitchModel.JoyConLeft,
        0x02 => SwitchModel.JoyConRight,
        0x03 => SwitchModel.ProController,
        _ => SwitchModel.Unknown,
    };

    /// <summary>
    /// Device type byte the controller reports for a given product id.
    /// </summary>
    /// <remarks>
    /// The value lives at SPI offset 0x6012 and only its low three bits are used. Retro
    /// controllers report the Pro Controller value, which is why the protocol never trusts the
    /// product id alone.
    /// </remarks>
    public static byte FromProductIdToDeviceType(int productId) => productId switch
    {
        ProductIdJoyConLeft => 0x01,
        ProductIdJoyConRight => 0x02,
        _ => 0x03,
    };
}
