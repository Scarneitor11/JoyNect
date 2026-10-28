using JoyNect.Core.Output;

namespace JoyNect.Core.Mapping;

/// <summary>
/// Anything a Switch button can be mapped onto.
/// </summary>
/// <remarks>
/// The low 16 bits are exactly the XUSB button bits (see <see cref="Xbox360Buttons"/>). The two
/// upper bits are pseudo targets for the analog triggers, which are not buttons in XInput: a game
/// reads them as a byte, so they cannot live in the button mask but they still need to be
/// remappable. The composer strips the pseudo bits before the state reaches the driver.
/// </remarks>
[Flags]
public enum Xbox360Target : uint
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

    /// <summary>Pseudo target: drives the analog left trigger to fully pressed.</summary>
    LeftTrigger = 0x1_0000,

    /// <summary>Pseudo target: drives the analog right trigger to fully pressed.</summary>
    RightTrigger = 0x2_0000,

    /// <summary>Every real button bit.</summary>
    ButtonMask = 0xFFFF,

    /// <summary>Every pseudo (non button) target.</summary>
    TriggerMask = LeftTrigger | RightTrigger,
}

/// <summary>Trigger values used for a fully pressed digital trigger.</summary>
public static class TriggerValues
{
    /// <summary>
    /// Fully pressed.
    /// </summary>
    /// <remarks>
    /// Switch controllers only report ZL/ZR as pressed or not, so a digital press becomes full
    /// travel. Games treat roughly a third of the travel as "pressed", so anything lower risks a
    /// digital press being ignored.
    /// </remarks>
    public const byte Pressed = 255;

    public const byte Released = 0;
}
