namespace JoyNect.Core.Protocol;

/// <summary>
/// Builds the frames JoyNect writes to a controller.
/// </summary>
/// <remarks>
/// Two transports exist and they differ only at the start:
/// <list type="bullet">
/// <item>Bluetooth: every command is output report 0x01 (counter, 8 rumble bytes, subcommand, args).</item>
/// <item>USB: the controller first needs the 0x80 bootstrap (connection status, handshake, baud rate,
/// handshake again, disable timeout), after which it accepts the same 0x01 frames.</item>
/// </list>
/// The report id is byte 0 of the buffer given to the HID layer, and frames are zero padded to the
/// length the device's report descriptor declares for that report id.
/// </remarks>
public static class OutputFrames
{
    /// <summary>Length of a Bluetooth subcommand frame, including the report id.</summary>
    public const int FrameLength = ProtocolConstants.BluetoothOutputFrameLength;

    /// <summary>Offset of the subcommand byte inside a 0x01 frame.</summary>
    public const int SubcommandOffset = 10;

    /// <summary>Offset of the first subcommand argument.</summary>
    public const int SubcommandArgumentOffset = 11;

    /// <summary>Offset of the packet counter.</summary>
    public const int CounterOffset = 1;

    /// <summary>Offset of the left actuator's four rumble bytes.</summary>
    public const int LeftRumbleOffset = 2;

    /// <summary>Offset of the right actuator's four rumble bytes.</summary>
    public const int RightRumbleOffset = 6;

    /// <summary>
    /// The minimum interval between two output writes.
    /// </summary>
    /// <remarks>
    /// The Linux driver documents that writing too quickly can disconnect a Bluetooth controller,
    /// so all writes go through one serialized path with this floor. Sending at 30 Hz is enough to
    /// keep rumble alive and to answer subcommands promptly.
    /// </remarks>
    public static readonly TimeSpan MinimumWriteInterval = TimeSpan.FromMilliseconds(15);

    /// <summary>
    /// Builds a frame that carries rumble data and optionally a subcommand.
    /// </summary>
    /// <param name="destination">Buffer of at least <see cref="FrameLength"/> bytes.</param>
    /// <param name="counter">Packet counter, incremented by the caller and wrapping at 0x0F.</param>
    /// <param name="rumbleLeft">Four encoded bytes for the left actuator.</param>
    /// <param name="rumbleRight">Four encoded bytes for the right actuator.</param>
    /// <param name="subcommand">Subcommand id, or null for a rumble-only frame.</param>
    /// <param name="arguments">Subcommand arguments, ignored when <paramref name="subcommand"/> is null.</param>
    /// <returns>Number of bytes written.</returns>
    public static int BuildSubcommand(
        Span<byte> destination,
        byte counter,
        ReadOnlySpan<byte> rumbleLeft,
        ReadOnlySpan<byte> rumbleRight,
        byte? subcommand = null,
        ReadOnlySpan<byte> arguments = default)
    {
        if (destination.Length < FrameLength)
        {
            throw new ArgumentException($"A frame needs {FrameLength} bytes.", nameof(destination));
        }

        if (rumbleLeft.Length < 4 || rumbleRight.Length < 4)
        {
            throw new ArgumentException("Rumble data must be four bytes per actuator.", nameof(rumbleLeft));
        }

        destination[..FrameLength].Clear();
        destination[0] = ProtocolConstants.OutputRumbleAndSubcommand;
        destination[CounterOffset] = counter;
        rumbleLeft[..4].CopyTo(destination[LeftRumbleOffset..]);
        rumbleRight[..4].CopyTo(destination[RightRumbleOffset..]);

        if (subcommand is { } id)
        {
            destination[SubcommandOffset] = id;
            var count = Math.Min(arguments.Length, FrameLength - SubcommandArgumentOffset);
            arguments[..count].CopyTo(destination[SubcommandArgumentOffset..]);
        }

        return FrameLength;
    }

    /// <summary>
    /// Builds a rumble-only frame.
    /// </summary>
    /// <remarks>
    /// JoyNect deliberately uses report 0x01 with subcommand 0x00 instead of the dedicated rumble
    /// report 0x10: one report id means one code path and one write queue, and 0x01 is the form every
    /// stack accepts.
    /// </remarks>
    public static int BuildRumbleOnly(
        Span<byte> destination,
        byte counter,
        ReadOnlySpan<byte> rumbleLeft,
        ReadOnlySpan<byte> rumbleRight) =>
        BuildSubcommand(destination, counter, rumbleLeft, rumbleRight);

    /// <summary>Builds one of the four USB bootstrap commands.</summary>
    /// <remarks>
    /// Order matters: 0x01 (connection status), 0x02 (handshake), 0x03 (3 Mbit baud rate),
    /// 0x02 (handshake again at the new rate), 0x04 (disable the USB timeout so the controller stays
    /// in USB mode).
    /// </remarks>
    public static int BuildUsbBootstrap(Span<byte> destination, byte usbCommand)
    {
        if (destination.Length < 2)
        {
            throw new ArgumentException("A USB bootstrap frame needs two bytes.", nameof(destination));
        }

        destination[0] = ProtocolConstants.OutputUsbBootstrap;
        destination[1] = usbCommand;
        return 2;
    }

    /// <summary>Builds the arguments for subcommand 0x10 (SPI flash read): 5 zero bytes + address + length.</summary>
    public static int BuildSpiReadArguments(Span<byte> destination, int address, byte length)
    {
        if (destination.Length < 6)
        {
            throw new ArgumentException("SPI read arguments need six bytes.", nameof(destination));
        }

        destination[0] = 0;
        destination[1] = 0;
        destination[2] = 0;
        destination[3] = 0;
        destination[4] = (byte)(address & 0xFF);
        destination[5] = (byte)((address >> 8) & 0xFF);
        destination[6] = (byte)((address >> 16) & 0xFF);
        destination[7] = (byte)((address >> 24) & 0xFF);
        destination[8] = length;
        return 9;
    }

    /// <summary>Builds the arguments for subcommand 0x30 (player lights).</summary>
    public static int BuildPlayerLightArguments(Span<byte> destination, byte lightState)
    {
        destination[0] = lightState;
        return 1;
    }

    /// <summary>
    /// Turns a 1-based player slot into the player light value.
    /// </summary>
    /// <remarks>
    /// The four lights are independent bits, so a slot lights one lamp: slots 1 to 4 map to
    /// 0x01, 0x02, 0x04, 0x08. Slots above four are invalid (the virtual pad limit is four).
    /// </remarks>
    public static byte PlayerLightForSlot(int slot) => slot switch
    {
        1 => 0x01,
        2 => 0x02,
        3 => 0x04,
        4 => 0x08,
        _ => 0x00,
    };
}
