using System.Globalization;

namespace JoyNect.Core.Output;

/// <summary>
/// Buttons of an Xbox 360 pad, in the bit order the XUSB report uses.
/// </summary>
/// <remarks>
/// These are the same bits XInput exposes in <c>XINPUT_GAMEPAD.wButtons</c>. JoyNect keeps its own
/// enum so the core library does not depend on the client library's types, and it verifies the
/// values against that library once at startup (see <c>ViGEmBackend.VerifyButtonMapping</c>) so a
/// wrong constant can never silently remap a whole controller.
/// </remarks>
[Flags]
public enum Xbox360Buttons : ushort
{
    None = 0,

    DpadUp = 0x0001,
    DpadDown = 0x0002,
    DpadLeft = 0x0004,
    DpadRight = 0x0008,

    Start = 0x0010,
    Back = 0x0020,

    LeftThumb = 0x0040,
    RightThumb = 0x0080,

    LeftShoulder = 0x0100,
    RightShoulder = 0x0200,

    Guide = 0x0400,

    A = 0x1000,
    B = 0x2000,
    X = 0x4000,
    Y = 0x8000,
}

/// <summary>Complete state of one virtual Xbox 360 controller.</summary>
public readonly record struct Xbox360PadState(
    Xbox360Buttons Buttons,
    short LeftThumbX,
    short LeftThumbY,
    short RightThumbX,
    short RightThumbY,
    byte LeftTrigger,
    byte RightTrigger)
{
    public static Xbox360PadState Neutral { get; } = new(Xbox360Buttons.None, 0, 0, 0, 0, 0, 0);

    public bool IsNeutral =>
        Buttons == Xbox360Buttons.None
        && LeftThumbX == 0 && LeftThumbY == 0
        && RightThumbX == 0 && RightThumbY == 0
        && LeftTrigger == 0 && RightTrigger == 0;

    public string Describe() => string.Create(
        CultureInfo.InvariantCulture,
        $"buttons={Buttons} left=({LeftThumbX},{LeftThumbY}) right=({RightThumbX},{RightThumbY}) triggers=({LeftTrigger},{RightTrigger})");
}
