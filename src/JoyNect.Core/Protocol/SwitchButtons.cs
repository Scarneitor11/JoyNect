namespace JoyNect.Core.Protocol;

/// <summary>
/// Buttons of a Switch controller, in the bit layout used by the full state report.
/// </summary>
/// <remarks>
/// Report 0x30 carries the buttons in three bytes:
/// byte 3 (right side, bits 0-7), byte 4 (shared, bits 8-15), byte 5 (left side, bits 16-23).
/// Bit 14 is unused. The two SR/SL pairs belong to the two halves of a Joy-Con.
/// </remarks>
[Flags]
public enum SwitchButtons : uint
{
    None = 0,

    // ---- byte 3: right hand side
    Y = 1u << 0,
    X = 1u << 1,
    B = 1u << 2,
    A = 1u << 3,
    RightStickSr = 1u << 4,
    RightStickSl = 1u << 5,
    R = 1u << 6,
    Zr = 1u << 7,

    // ---- byte 4: shared
    Minus = 1u << 8,
    Plus = 1u << 9,
    RightStickClick = 1u << 10,
    LeftStickClick = 1u << 11,
    Home = 1u << 12,
    Capture = 1u << 13,
    // bit 14 is unused
    ChargingGrip = 1u << 15,

    // ---- byte 5: left hand side
    Down = 1u << 16,
    Up = 1u << 17,
    Right = 1u << 18,
    Left = 1u << 19,
    LeftStickSr = 1u << 20,
    LeftStickSl = 1u << 21,
    L = 1u << 22,
    Zl = 1u << 23,
}

public static class SwitchButtonExtensions
{
    /// <summary>Buttons that only exist on a Joy-Con held sideways.</summary>
    public const SwitchButtons ShoulderRailButtons = SwitchButtons.LeftStickSr | SwitchButtons.LeftStickSl
        | SwitchButtons.RightStickSr | SwitchButtons.RightStickSl;

    public static bool Has(this SwitchButtons value, SwitchButtons flag) => (value & flag) == flag;

    /// <summary>Human readable name, used by the input test view and by logging.</summary>
    public static string ToDisplayName(this SwitchButtons button) => button switch
    {
        SwitchButtons.A => "A",
        SwitchButtons.B => "B",
        SwitchButtons.X => "X",
        SwitchButtons.Y => "Y",
        SwitchButtons.L => "L",
        SwitchButtons.R => "R",
        SwitchButtons.Zl => "ZL",
        SwitchButtons.Zr => "ZR",
        SwitchButtons.Plus => "+",
        SwitchButtons.Minus => "-",
        SwitchButtons.Home => "Home",
        SwitchButtons.Capture => "Capture",
        SwitchButtons.LeftStickClick => "Left stick",
        SwitchButtons.RightStickClick => "Right stick",
        SwitchButtons.Up => "Up",
        SwitchButtons.Down => "Down",
        SwitchButtons.Left => "Left",
        SwitchButtons.Right => "Right",
        SwitchButtons.LeftStickSr => "Left rail SR",
        SwitchButtons.LeftStickSl => "Left rail SL",
        SwitchButtons.RightStickSr => "Right rail SR",
        SwitchButtons.RightStickSl => "Right rail SL",
        SwitchButtons.ChargingGrip => "Charging grip",
        _ => button.ToString(),
    };
}
