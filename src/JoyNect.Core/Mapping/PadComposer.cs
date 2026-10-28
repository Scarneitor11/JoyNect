using JoyNect.Core.Output;
using JoyNect.Core.Protocol;

namespace JoyNect.Core.Mapping;

/// <summary>Raw input of one controller, as decoded from the wire.</summary>
public readonly record struct SwitchPadInput(SwitchButtons Buttons, StickRaw LeftStick, StickRaw RightStick)
{
    public static SwitchPadInput Neutral { get; } = new(
        SwitchButtons.None,
        new StickRaw(StickRaw.Center, StickRaw.Center),
        new StickRaw(StickRaw.Center, StickRaw.Center));
}

/// <summary>
/// Where a controller's stick data lives and where it should end up on the virtual pad.
/// </summary>
/// <remarks>
/// Only three shapes exist in practice, but they are easy to get wrong:
/// a Pro Controller fills both stick fields; a Joy-Con (L) fills the left field; a Joy-Con (R)
/// fills the right field. When two Joy-Con are combined the right one's stick becomes the virtual
/// pad's right stick, but used alone sideways it has to drive the left stick instead.
/// </remarks>
public enum StickLayout
{
    /// <summary>Both fields are real sticks: left field to left stick, right field to right stick.</summary>
    BothSticks,

    /// <summary>The stick arrives in the left field and drives the virtual pad's left stick.</summary>
    LeftFieldToLeft,

    /// <summary>The stick arrives in the right field and drives the virtual pad's left stick.</summary>
    RightFieldToLeft,

    /// <summary>The stick arrives in the right field and drives the virtual pad's right stick.</summary>
    RightFieldToRight,

    /// <summary>The stick arrives in the left field and drives the virtual pad's right stick.</summary>
    LeftFieldToRight,
}

/// <summary>One contributor to a virtual pad: a Pro Controller, or half of a Joy-Con pair.</summary>
/// <param name="Active">
/// Whether the controller is currently delivering reports. An inactive source contributes nothing
/// at all, so a controller that has gone quiet cannot leave an input latched in a game.
/// </param>
public sealed record ComposeSource(
    SwitchPadInput Input,
    StickLayout Layout,
    StickCalibration LeftCalibration,
    StickCalibration RightCalibration,
    bool Active = true);

/// <summary>
/// Applies stick shaping and the button map, and merges the halves of a combined pad.
/// </summary>
public sealed class PadComposer
{
    public PadComposer(MappingProfile profile)
    {
        Profile = profile;
    }

    public MappingProfile Profile { get; set; }

    /// <summary>Composes the complete state of one virtual pad from its sources.</summary>
    public Xbox360PadState Compose(ReadOnlySpan<ComposeSource> sources)
    {
        var buttons = Xbox360Target.None;
        short leftX = 0, leftY = 0, rightX = 0, rightY = 0;
        var leftAssigned = false;
        var rightAssigned = false;

        foreach (var source in sources)
        {
            buttons |= MapButtons(source.Input.Buttons);

            foreach (var (layout, x, y) in ReadSticks(source))
            {
                switch (layout)
                {
                    case PadStick.Left:
                        leftX = x;
                        leftY = y;
                        leftAssigned = true;
                        break;
                    case PadStick.Right:
                        rightX = x;
                        rightY = y;
                        rightAssigned = true;
                        break;
                }
            }
        }

        if (!leftAssigned && !rightAssigned && buttons == Xbox360Target.None)
        {
            return Xbox360PadState.Neutral;
        }

        return new Xbox360PadState(
            (Xbox360Buttons)(ushort)(buttons & Xbox360Target.ButtonMask),
            leftX,
            leftY,
            rightX,
            rightY,
            (buttons & Xbox360Target.LeftTrigger) != 0 ? TriggerValues.Pressed : TriggerValues.Released,
            (buttons & Xbox360Target.RightTrigger) != 0 ? TriggerValues.Pressed : TriggerValues.Released);
    }

    /// <summary>Applies the button map to a raw button mask.</summary>
    public Xbox360Target MapButtons(SwitchButtons pressed)
    {
        var result = Xbox360Target.None;
        foreach (var (source, target) in Profile.Buttons)
        {
            if ((pressed & source) == source)
            {
                result |= target;
            }
        }

        return result;
    }

    /// <summary>Human readable name of what a Switch button currently maps to.</summary>
    public string DescribeMapping(SwitchButtons button) =>
        Profile.Buttons.TryGetValue(button, out var target) && target != Xbox360Target.None
            ? Xbox360TargetNames.Describe(target)
            : "not mapped";

    private IEnumerable<(PadStick Stick, short X, short Y)> ReadSticks(ComposeSource source)
    {
        // An inactive source reads as exactly centred rather than as "raw centre processed through
        // calibration": the calibration's centre is a property of the hardware, and feeding a neutral
        // reading through it can still yield an offset.
        var left = source.Active
            ? StickProcessor.ToPadStick(source.Input.LeftStick, source.LeftCalibration, Profile.LeftStick)
            : default;

        var right = source.Active
            ? StickProcessor.ToPadStick(source.Input.RightStick, source.RightCalibration, Profile.RightStick)
            : default;

        switch (source.Layout)
        {
            case StickLayout.BothSticks:
                yield return (PadStick.Left, left.X, left.Y);
                yield return (PadStick.Right, right.X, right.Y);
                break;

            case StickLayout.LeftFieldToLeft:
                yield return (PadStick.Left, left.X, left.Y);
                break;

            case StickLayout.RightFieldToLeft:
                yield return (PadStick.Left, right.X, right.Y);
                break;

            case StickLayout.RightFieldToRight:
                yield return (PadStick.Right, right.X, right.Y);
                break;

            case StickLayout.LeftFieldToRight:
                yield return (PadStick.Right, left.X, left.Y);
                break;
        }
    }

    private enum PadStick
    {
        Left,
        Right,
    }
}

/// <summary>Turns a mapping target into text for the user interface.</summary>
public static class Xbox360TargetNames
{
    public static string Describe(Xbox360Target target)
    {
        if (target == Xbox360Target.None)
        {
            return "not mapped";
        }

        var names = new List<string>();
        foreach (var candidate in SelectableTargets)
        {
            if ((target & candidate.Target) == candidate.Target)
            {
                names.Add(candidate.Name);
            }
        }

        return names.Count > 0 ? string.Join(" + ", names) : "not mapped";
    }

    /// <summary>All targets a user can pick, in the order the remap list should show them.</summary>
    public static IReadOnlyList<(Xbox360Target Target, string Name)> SelectableTargets { get; } =
    [
        (Xbox360Target.A, "A"),
        (Xbox360Target.B, "B"),
        (Xbox360Target.X, "X"),
        (Xbox360Target.Y, "Y"),
        (Xbox360Target.LeftShoulder, "Left bumper"),
        (Xbox360Target.RightShoulder, "Right bumper"),
        (Xbox360Target.LeftTrigger, "Left trigger"),
        (Xbox360Target.RightTrigger, "Right trigger"),
        (Xbox360Target.DpadUp, "D-pad up"),
        (Xbox360Target.DpadDown, "D-pad down"),
        (Xbox360Target.DpadLeft, "D-pad left"),
        (Xbox360Target.DpadRight, "D-pad right"),
        (Xbox360Target.Start, "Start"),
        (Xbox360Target.Back, "Back"),
        (Xbox360Target.Guide, "Guide"),
        (Xbox360Target.LeftThumb, "Left stick click"),
        (Xbox360Target.RightThumb, "Right stick click"),
    ];
}
