using JoyNect.Core.Mapping;
using Xunit;

namespace JoyNect.Core.Tests.Mapping;

public sealed class StickProcessorTests
{
    private static readonly StickSettings Linear = new() { Deadzone = 0.0 };
    private static readonly StickSettings Default = new();

    [Fact]
    public void CentredStickIsExactlyZero()
    {
        Assert.Equal((short)0, StickProcessor.Apply(0, 0, Linear).X);
        Assert.Equal((short)0, StickProcessor.Apply(0, 0, Linear).Y);
    }

    [Fact]
    public void FullDeflectionReachesTheAxisLimit()
    {
        Assert.Equal(StickProcessor.MaxValue, StickProcessor.Apply(1, 0, Linear).X);
        Assert.Equal(-StickProcessor.MaxValue, StickProcessor.Apply(-1, 0, Linear).X);
        Assert.Equal(StickProcessor.MaxValue, StickProcessor.Apply(0, 1, Linear).Y);
    }

    [Fact]
    public void ValuesBeyondTheLimitAreClamped()
    {
        Assert.Equal(StickProcessor.MaxValue, StickProcessor.Apply(3, 0, Linear).X);
        Assert.Equal(-StickProcessor.MaxValue, StickProcessor.Apply(0, -3, Linear).Y);
    }

    [Fact]
    public void InsideTheDeadZoneTheStickReadsAsCentred()
    {
        Assert.Equal((short)0, StickProcessor.Apply(Default.Deadzone * 0.9, 0, Default).X);
        Assert.Equal((short)0, StickProcessor.Apply(0, -Default.Deadzone, Default).Y);

        // The dead zone is radial: what counts is the distance from the centre, so a diagonal whose
        // two axes are each below the dead zone can still be inside or outside depending on how far
        // the stick was actually pushed.
        var justInside = Default.Deadzone * 0.6;
        Assert.Equal((short)0, StickProcessor.Apply(justInside, justInside, Default).X);

        var justOutside = Default.Deadzone;
        Assert.NotEqual((short)0, StickProcessor.Apply(justOutside, justOutside, Default).X);
    }

    [Fact]
    public void LeavingTheDeadZoneStartsFromZeroInsteadOfJumping()
    {
        // Just past the dead zone the output must be small; a naive implementation would jump
        // straight to the dead zone's magnitude, which feels like a step in the middle of the stick.
        var justOutside = Default.Deadzone + 0.001;
        var result = StickProcessor.Apply(justOutside, 0, Default);

        Assert.InRange(result.X, 1, 1000);
    }

    [Fact]
    public void RescalesTheRemainingTravelSoFullDeflectionStillReachesTheLimit()
    {
        var result = StickProcessor.Apply(1, 0, Default);
        Assert.Equal(StickProcessor.MaxValue, result.X);
    }

    [Fact]
    public void KeepsTheDirectionOfADiagonalInput()
    {
        var result = StickProcessor.Apply(0.5, 0.5, Linear);

        Assert.Equal(result.X, result.Y);

        // The magnitude of the push is preserved, so a diagonal at 0.5 on each axis stays at 71%
        // of the travel rather than being pushed out to the limit.
        Assert.InRange(result.X, 16300, 16400);
    }

    [Fact]
    public void InvertsEachAxisOnItsOwn()
    {
        var horizontal = StickProcessor.Apply(1, 0, new StickSettings { InvertX = true });
        Assert.Equal(-StickProcessor.MaxValue, horizontal.X);
        Assert.Equal(0, horizontal.Y);

        var vertical = StickProcessor.Apply(0, 1, new StickSettings { InvertY = true });
        Assert.Equal(0, vertical.X);
        Assert.Equal(-StickProcessor.MaxValue, vertical.Y);
    }

    [Fact]
    public void CurveCompressesTheMiddleOfTheTravel()
    {
        var soft = new StickSettings { Deadzone = 0, Curve = 2.0 };
        var linear = StickProcessor.Apply(0.5, 0, Linear).X;
        var curved = StickProcessor.Apply(0.5, 0, soft).X;

        Assert.True(curved < linear);
        Assert.Equal(StickProcessor.MaxValue, StickProcessor.Apply(1, 0, soft).X);
    }

    [Fact]
    public void ExtremeDeadZonesAreClampedInsteadOfBreaking()
    {
        var absurd = new StickSettings { Deadzone = 5.0 };

        // Clamped to the largest usable dead zone, which still leaves the outer travel reachable.
        Assert.Equal((short)0, StickProcessor.Apply(0.5, 0.5, absurd).X);
        Assert.Equal(StickProcessor.MaxValue, StickProcessor.Apply(1, 0, absurd).X);

        var negative = new StickSettings { Deadzone = -1.0 };
        Assert.Equal(StickProcessor.MaxValue, StickProcessor.Apply(1, 0, negative).X);
    }
}
