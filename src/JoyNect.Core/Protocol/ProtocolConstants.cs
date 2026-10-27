namespace JoyNect.Core.Protocol;

/// <summary>
/// Wire-level constants for the Nintendo Switch controller HID protocol.
/// </summary>
/// <remarks>
/// Sources: dekuNukem/Nintendo_Switch_Reverse_Engineering (bluetooth_hid_notes.md,
/// USB-HID-Notes.md, bluetooth_hid_subcommands_notes.md) and the Linux kernel driver
/// drivers/hid/hid-nintendo.c. See docs/PROTOCOL.md for the full mapping of claim -> source.
/// </remarks>
public static class ProtocolConstants
{
    /// <summary>Nintendo's USB vendor id; every Switch controller uses it.</summary>
    public const int NintendoVendorId = 0x057E;

    // ---------------------------------------------------------------- output report ids

    /// <summary>Bluetooth (and post-bootstrap USB) frame carrying rumble data and an optional subcommand.</summary>
    public const byte OutputRumbleAndSubcommand = 0x01;

    /// <summary>Rumble-only frame. JoyNect deliberately never sends this: see OutputFrameBuilder.</summary>
    public const byte OutputRumbleOnly = 0x10;

    /// <summary>USB-only bootstrap frame (connection status, handshake, baud rate, timeout disable).</summary>
    public const byte OutputUsbBootstrap = 0x80;

    // ---------------------------------------------------------------- input report ids

    /// <summary>Full input state: buttons, sticks, battery and three IMU samples (49 bytes).</summary>
    public const byte InputFullState = 0x30;

    /// <summary>Reply to an output subcommand (ACK at offset 13, echoed subcommand id at offset 14).</summary>
    public const byte InputSubcommandReply = 0x21;

    /// <summary>
    /// Full state for NSO retro controllers (NES/SNES/N64/Genesis). UNVERIFIED: derived from
    /// hid-nintendo.c rather than hardware, so JoyNect treats NSO devices as best-effort support.
    /// </summary>
    public const byte InputNsoFullState = 0x3F;

    /// <summary>Subcommand reply for NSO retro controllers. UNVERIFIED, see above.</summary>
    public const byte InputNsoSubcommandReply = 0x23;

    // ---------------------------------------------------------------- subcommands

    public const byte SubcommandBluetoothPairing = 0x01;
    public const byte SubcommandRequestDeviceInfo = 0x02;
    public const byte SubcommandSetInputReportMode = 0x03;
    public const byte SubcommandTriggerButtonPress = 0x04;
    public const byte SubcommandSetShipmentState = 0x08;
    public const byte SubcommandSpiFlashRead = 0x10;
    public const byte SubcommandSpiFlashReadFragment = 0x11;
    public const byte SubcommandSetPlayerLights = 0x30;
    public const byte SubcommandSetHomeLight = 0x38;
    public const byte SubcommandEnableImu = 0x40;
    public const byte SubcommandSetImuSensitivity = 0x41;
    public const byte SubcommandEnableVibration = 0x48;

    /// <summary>Argument for <see cref="SubcommandSetInputReportMode"/>: standard full reports.</summary>
    public const byte ReportModeFull = 0x30;

    /// <summary>Argument for <see cref="SubcommandSetInputReportMode"/>: minimal reports.</summary>
    public const byte ReportModeMinimal = 0x3F;

    // ---------------------------------------------------------------- USB bootstrap

    public const byte UsbBootstrapRequestConnectionStatus = 0x01;
    public const byte UsbBootstrapHandshake = 0x02;
    public const byte UsbBootstrapSetBaudRate = 0x03;
    public const byte UsbBootstrapDisableTimeout = 0x04;

    // ---------------------------------------------------------------- SPI flash addresses

    /// <summary>Factory stick calibration block (18 bytes).</summary>
    public const int SpiAddressStickCalibration = 0x603D;

    /// <summary>Factory motion sensor calibration block (24 bytes).</summary>
    public const int SpiAddressImuCalibration = 0x6020;

    /// <summary>Length of a Bluetooth subcommand frame, including the report id.</summary>
    public const int BluetoothOutputFrameLength = 49;

    /// <summary>Length of a full input state report including the report id byte.</summary>
    public const int FullStateReportLength = 49;

    /// <summary>Number of motion samples packed into one full input report.</summary>
    public const int ImuSamplesPerReport = 3;

    /// <summary>Rumble output frames the controller expects before it stops vibrating on its own.</summary>
    public const int MaxRumbleWriteRateHz = 30;

    /// <summary>Neutral (idle) rumble block, as documented for the linear resonant actuators.</summary>
    public static ReadOnlySpan<byte> NeutralRumble => [0x00, 0x01, 0x40, 0x40];
}
