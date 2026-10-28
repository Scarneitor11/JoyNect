using JoyNect.Core.Hid;
using JoyNect.Core.Mapping;
using JoyNect.Core.Output;
using JoyNect.Core.Protocol;
using Xunit;

namespace JoyNect.Core.Tests.Mapping;

/// <summary>
/// Button map and composer tests.
/// </summary>
/// <remarks>
/// The first test is the promise the whole app makes: press the button on the right of the
/// controller and the game sees the button on the right of an Xbox pad. Nintendo swaps the labels,
/// so mapping them by position means A maps to B — this is where that is pinned down.
/// </remarks>
public sealed class PadComposerTests
{
    private static ComposeSource ProSource(SwitchButtons buttons = SwitchButtons.None, StickRaw? left = null, StickRaw? right = null) =>
        new(
            new SwitchPadInput(buttons, left ?? Centred, right ?? Centred),
            StickLayout.BothSticks,
            StickCalibration.Default,
            StickCalibration.Default);

    /// <summary>
    /// A stick at rest, expressed with the calibration's own centre.
    /// </summary>
    /// <remarks>
    /// Deliberately not <see cref="StickRaw.Center"/>: a real controller's calibrated centre is not the
    /// middle of the raw range, so "centred" means "at the calibrated centre" everywhere in this file.
    /// </remarks>
    private static readonly StickRaw Centred = new(StickCalibration.Default.CenterX, StickCalibration.Default.CenterY);

    private static readonly int CalibratedCentreY = StickCalibration.Default.CenterY;

    private static PadComposer CreateProComposer() =>
        new(MappingProfile.CreateDefault(SwitchModel.ProController));

    [Theory]
    [InlineData(SwitchButtons.A, Xbox360Buttons.B)]
    [InlineData(SwitchButtons.B, Xbox360Buttons.A)]
    [InlineData(SwitchButtons.X, Xbox360Buttons.Y)]
    [InlineData(SwitchButtons.Y, Xbox360Buttons.X)]
    [InlineData(SwitchButtons.Up, Xbox360Buttons.DpadUp)]
    [InlineData(SwitchButtons.Down, Xbox360Buttons.DpadDown)]
    [InlineData(SwitchButtons.Left, Xbox360Buttons.DpadLeft)]
    [InlineData(SwitchButtons.Right, Xbox360Buttons.DpadRight)]
    [InlineData(SwitchButtons.L, Xbox360Buttons.LeftShoulder)]
    [InlineData(SwitchButtons.R, Xbox360Buttons.RightShoulder)]
    [InlineData(SwitchButtons.Minus, Xbox360Buttons.Back)]
    [InlineData(SwitchButtons.Plus, Xbox360Buttons.Start)]
    [InlineData(SwitchButtons.Home, Xbox360Buttons.Guide)]
    [InlineData(SwitchButtons.LeftStickClick, Xbox360Buttons.LeftThumb)]
    [InlineData(SwitchButtons.RightStickClick, Xbox360Buttons.RightThumb)]
    public void MapsFaceAndShoulderButtonsByPhysicalPosition(SwitchButtons source, Xbox360Buttons expected)
    {
        var state = CreateProComposer().Compose([ProSource(source)]);
        Assert.Equal(expected, state.Buttons);
    }

    [Fact]
    public void ZlAndZrBecomeTheAnalogTriggers()
    {
        var composer = CreateProComposer();

        var released = composer.Compose([ProSource()]);
        Assert.Equal(0, released.LeftTrigger);
        Assert.Equal(0, released.RightTrigger);

        var pressed = composer.Compose([ProSource(SwitchButtons.Zl | SwitchButtons.Zr)]);
        Assert.Equal(TriggerValues.Pressed, pressed.LeftTrigger);
        Assert.Equal(TriggerValues.Pressed, pressed.RightTrigger);

        // The triggers are not buttons: the pseudo bits must never reach the driver.
        Assert.Equal(Xbox360Buttons.None, pressed.Buttons);
    }

    [Fact]
    public void NothingPressedProducesTheNeutralState()
    {
        var state = CreateProComposer().Compose([ProSource()]);
        Assert.True(state.IsNeutral);
    }

    [Fact]
    public void AnEmptySourceListIsNeutral()
    {
        Assert.True(CreateProComposer().Compose([]).IsNeutral);
    }

    [Fact]
    public void RemappingChangesTheResult()
    {
        var composer = CreateProComposer();
        Assert.Equal(Xbox360Buttons.B, composer.Compose([ProSource(SwitchButtons.A)]).Buttons);

        composer.Profile.Remap(SwitchButtons.A, Xbox360Target.X);
        Assert.Equal(Xbox360Buttons.X, composer.Compose([ProSource(SwitchButtons.A)]).Buttons);
    }

    [Fact]
    public void UnmappingAButtonStopsItFromReachingThePad()
    {
        var composer = CreateProComposer();
        composer.Profile.Remap(SwitchButtons.A, Xbox360Target.None);

        Assert.Equal(Xbox360Buttons.None, composer.Compose([ProSource(SwitchButtons.A)]).Buttons);
        Assert.Equal("not mapped", composer.DescribeMapping(SwitchButtons.A));
    }

    [Fact]
    public void AppliesStickCalibrationAndShaping()
    {
        var composer = CreateProComposer();
        var calibration = StickCalibration.Default;

        var pushedRight = new StickRaw(calibration.CenterX + calibration.DeltaAboveX, calibration.CenterY);
        var state = composer.Compose([ProSource(left: pushedRight)]);

        Assert.Equal(StickProcessor.MaxValue, state.LeftThumbX);
        Assert.Equal(0, state.LeftThumbY);
    }

    [Fact]
    public void VerticalAxisIsInvertedByDefaultToMatchXInput()
    {
        var composer = CreateProComposer();
        var calibration = StickCalibration.Default;

        // Raw values increase downwards, XInput wants positive up.
        var rawAboveCentre = new StickRaw(calibration.CenterX, calibration.CenterY + calibration.DeltaAboveY);
        var state = composer.Compose([ProSource(left: rawAboveCentre)]);
        Assert.True(state.LeftThumbY < 0, "raw above the centre should read as down on the pad");

        composer.Profile.LeftStick = composer.Profile.LeftStick with { InvertY = true };
        var flipped = composer.Compose([ProSource(left: rawAboveCentre)]);
        Assert.True(flipped.LeftThumbY > 0);
    }

    [Fact]
    public void RightFieldToLeftMakesASidewaysJoyConUseItsOwnStick()
    {
        // A Joy-Con (R) reports in the right hand field but must drive the pad's left stick when it
        // is used alone.
        var composer = new PadComposer(MappingProfile.CreateDefault(SwitchModel.JoyConRight));
        var source = new ComposeSource(
            new SwitchPadInput(SwitchButtons.None, Centred, new StickRaw(3800, CalibratedCentreY)),
            StickLayout.RightFieldToLeft,
            StickCalibration.Default,
            StickCalibration.Default);

        var state = composer.Compose([source]);

        Assert.True(state.LeftThumbX > 30000);
        Assert.Equal(0, state.RightThumbX);
    }

    [Fact]
    public void TwoSourcesMergeIntoOnePadWithBothSticks()
    {
        // Combined Joy-Con: the left half owns the left stick and the D-pad, the right half owns the
        // right stick and the face buttons.
        var composer = new PadComposer(MappingProfile.CreateDefault(SwitchModel.JoyConLeft, JoyConMode.Combined));

        var left = new ComposeSource(
            new SwitchPadInput(SwitchButtons.Up, new StickRaw(3800, CalibratedCentreY), Centred),
            StickLayout.LeftFieldToLeft,
            StickCalibration.Default,
            StickCalibration.Default);

        var right = new ComposeSource(
            new SwitchPadInput(SwitchButtons.A, Centred, new StickRaw(400, CalibratedCentreY)),
            StickLayout.RightFieldToRight,
            StickCalibration.Default,
            StickCalibration.Default);

        var state = composer.Compose([left, right]);

        Assert.True(state.LeftThumbX > 30000);
        Assert.True(state.RightThumbX < -30000);
        Assert.True(state.Buttons.HasFlag(Xbox360Buttons.DpadUp));
        Assert.True(state.Buttons.HasFlag(Xbox360Buttons.B));
    }

    [Fact]
    public void JoyConProfileKeepsTheRailButtonsReachable()
    {
        var composer = new PadComposer(MappingProfile.CreateDefault(SwitchModel.JoyConLeft));

        var state = composer.Compose([ProSource(SwitchButtons.LeftStickSr | SwitchButtons.LeftStickSl)]);

        Assert.Equal(Xbox360Buttons.LeftShoulder | Xbox360Buttons.RightShoulder, state.Buttons);
    }

    [Theory]
    [InlineData(Xbox360Target.A, "A")]
    [InlineData(Xbox360Target.LeftTrigger, "Left trigger")]
    [InlineData(Xbox360Target.A | Xbox360Target.B, "A + B")]
    [InlineData(Xbox360Target.None, "not mapped")]
    public void DescribesMappingTargetsForTheInterface(Xbox360Target target, string expected)
    {
        Assert.Equal(expected, Xbox360TargetNames.Describe(target));
    }

    [Fact]
    public void CloningAProfileCopiesItsMapping()
    {
        var original = MappingProfile.CreateDefault(SwitchModel.ProController);
        var clone = original.Clone();

        clone.Remap(SwitchButtons.A, Xbox360Target.X);

        Assert.Equal(Xbox360Buttons.B, new PadComposer(original).Compose([ProSource(SwitchButtons.A)]).Buttons);
        Assert.Equal(Xbox360Buttons.X, new PadComposer(clone).Compose([ProSource(SwitchButtons.A)]).Buttons);
    }
}
