using JoyNect.Core.Hid;
using JoyNect.Core.Protocol;

namespace JoyNect.Core.Mapping;

/// <summary>How two Joy-Con are treated.</summary>
public enum JoyConMode
{
    /// <summary>
    /// Each Joy-Con is its own pad and is used held sideways, like Nintendo's own two player mode.
    /// </summary>
    Sideways,

    /// <summary>
    /// A left and a right Joy-Con are merged into a single pad with both sticks and the full button
    /// set, which is what games with two sticks and motion need.
    /// </summary>
    Combined,
}

/// <summary>Analog stick shaping.</summary>
public sealed record StickSettings
{
    /// <summary>Radial dead zone in [0, 1] of the stick's travel.</summary>
    public double Deadzone { get; init; } = 0.08;

    /// <summary>Response exponent: 1 is linear, higher values are gentler around the centre.</summary>
    public double Curve { get; init; } = 1.0;

    public bool InvertX { get; init; }

    /// <summary>
    /// Flips the vertical axis.
    /// </summary>
    /// <remarks>
    /// XInput reports a positive Y as "up", while the raw stick value increases downwards, so the
    /// default flips it. It is a visible setting rather than a hidden constant because getting it
    /// wrong makes vertical aiming feel reversed in every game.
    /// </remarks>
    public bool InvertY { get; init; }

    public static StickSettings Default { get; } = new();
}

/// <summary>
/// Per-controller configuration: button mapping, stick shaping and rumble behaviour.
/// </summary>
public sealed class MappingProfile
{
    public MappingProfile(
        string name,
        IReadOnlyDictionary<SwitchButtons, Xbox360Target> buttons,
        StickSettings? leftStick = null,
        StickSettings? rightStick = null)
    {
        Name = name;
        Buttons = new Dictionary<SwitchButtons, Xbox360Target>(buttons);
        LeftStick = leftStick ?? StickSettings.Default;
        RightStick = rightStick ?? StickSettings.Default;
    }

    public string Name { get; set; }

    public Dictionary<SwitchButtons, Xbox360Target> Buttons { get; }

    public StickSettings LeftStick { get; set; } = StickSettings.Default;

    public StickSettings RightStick { get; set; } = StickSettings.Default;

    public bool RumbleEnabled { get; set; } = true;

    /// <summary>Rumble strength multiplier in [0, 1].</summary>
    public double RumbleStrength { get; set; } = 0.6;

    public JoyConMode JoyConMode { get; set; } = JoyConMode.Sideways;

    /// <summary>Maps one Switch button, replacing whatever it mapped to before.</summary>
    public void Remap(SwitchButtons source, Xbox360Target target)
    {
        if (target == Xbox360Target.None)
        {
            Buttons.Remove(source);
            return;
        }

        Buttons[source] = target;
    }

    public Xbox360Target TargetFor(SwitchButtons source) =>
        Buttons.TryGetValue(source, out var target) ? target : Xbox360Target.None;

    public MappingProfile Clone() => new(Name, Buttons, LeftStick, RightStick)
    {
        RumbleEnabled = RumbleEnabled,
        RumbleStrength = RumbleStrength,
        JoyConMode = JoyConMode,
    };

    /// <summary>
    /// The default mapping, which preserves each button's physical position.
    /// </summary>
    /// <remarks>
    /// Nintendo labels the right hand face button "A" and the bottom one "B", the opposite of Xbox.
    /// Games act on position rather than label, so A maps to B and B maps to A: the button on the
    /// right always confirms, whichever controller is in your hands. Likewise the shoulder buttons
    /// map by position and ZL/ZR, which sit behind the controller, become the analog triggers.
    /// </remarks>
    public static MappingProfile CreateDefault(SwitchModel model, JoyConMode joyConMode = JoyConMode.Sideways)
    {
        var buttons = new Dictionary<SwitchButtons, Xbox360Target>
        {
            [SwitchButtons.A] = Xbox360Target.B,
            [SwitchButtons.B] = Xbox360Target.A,
            [SwitchButtons.X] = Xbox360Target.Y,
            [SwitchButtons.Y] = Xbox360Target.X,

            [SwitchButtons.Down] = Xbox360Target.DpadDown,
            [SwitchButtons.Up] = Xbox360Target.DpadUp,
            [SwitchButtons.Right] = Xbox360Target.DpadRight,
            [SwitchButtons.Left] = Xbox360Target.DpadLeft,

            [SwitchButtons.L] = Xbox360Target.LeftShoulder,
            [SwitchButtons.R] = Xbox360Target.RightShoulder,
            [SwitchButtons.Zl] = Xbox360Target.LeftTrigger,
            [SwitchButtons.Zr] = Xbox360Target.RightTrigger,

            [SwitchButtons.Minus] = Xbox360Target.Back,
            [SwitchButtons.Plus] = Xbox360Target.Start,
            [SwitchButtons.Home] = Xbox360Target.Guide,

            [SwitchButtons.LeftStickClick] = Xbox360Target.LeftThumb,
            [SwitchButtons.RightStickClick] = Xbox360Target.RightThumb,
        };

        var profile = new MappingProfile($"{SwitchModels.DescribeProduct(model).DisplayName} default", buttons)
        {
            JoyConMode = joyConMode,
            RumbleStrength = SwitchModels.IsJoyCon(model) ? 0.5 : 0.6,
        };

        if (SwitchModels.IsJoyCon(model))
        {
            // A single Joy-Con held sideways uses its rail buttons as the shoulders and has no
            // second stick, so the rail pair becomes both bumpers. Deliberately simple and fully
            // remappable: how a sideways Joy-Con should behave is a matter of taste.
            profile.Buttons[SwitchButtons.LeftStickSr] = Xbox360Target.LeftShoulder;
            profile.Buttons[SwitchButtons.LeftStickSl] = Xbox360Target.RightShoulder;
            profile.Buttons[SwitchButtons.RightStickSr] = Xbox360Target.LeftShoulder;
            profile.Buttons[SwitchButtons.RightStickSl] = Xbox360Target.RightShoulder;
            profile.Buttons[SwitchButtons.Capture] = Xbox360Target.Back;
        }
        else if (SwitchModels.IsNsoRetro(model))
        {
            // Retro controllers have no sticks and a reduced button set, so the profile aims at
            // "playable" rather than at reproducing a modern layout.
            profile.Buttons[SwitchButtons.Zl] = Xbox360Target.LeftShoulder;
            profile.Buttons[SwitchButtons.Zr] = Xbox360Target.RightShoulder;
            profile.Buttons[SwitchButtons.Capture] = Xbox360Target.Back;
        }

        return profile;
    }
}
