using Dolap.Core;
using Xunit;

namespace Dolap.Core.Tests;

public class LayoutTests
{
    [Fact]
    public void SnapsToWorkAreaOnNegativeCoordinateMonitor()
    {
        var result = LayoutSnap.Apply(new(-1910, 8, 300, 500), new(-1920, 0, 1920, 1040), []);
        Assert.Equal(-1920, result.X); Assert.Equal(0, result.Y);
        Assert.Equal(300, result.Width); Assert.Equal(500, result.Height);
    }

    [Fact]
    public void AdjacentCabinetsUseGapAndAlignBottoms()
    {
        var result = LayoutSnap.Apply(new(518, 305, 200, 300), new(0, 0, 1920, 1040), [new(200, 100, 300, 500)]);
        Assert.Equal(512, result.X); Assert.Equal(300, result.Y);
    }

    [Fact]
    public void DistantCabinetDoesNotAttract()
    {
        var moving = new LayoutRect(518, 700, 200, 200);
        Assert.Equal(moving, LayoutSnap.Apply(moving, new(0, 0, 1920, 1040), [new(200, 100, 300, 200)]));
    }

    [Fact]
    public void DpiScaledThresholdAndGapAreRespected()
    {
        var result = LayoutSnap.Apply(new(540, 300, 200, 300), new(0, 0, 1920, 1040), [new(200, 100, 300, 500)], 36, 24);
        Assert.Equal(524, result.X);
    }

    [Fact]
    public void SettingsPersistGloballyWithoutChangingCabinetPositions()
    {
        using var store = new CollectionStore(":memory:");
        var first = store.FirstCabinetId(); var second = store.AddCabinet();
        var before = store.GetCabinets();
        store.SaveEffective(first, store.LoadEffective(first) with { LayoutLocked = true, MagneticSnap = false });
        Assert.True(store.LoadEffective(second.Id).LayoutLocked);
        Assert.False(store.LoadEffective(second.Id).MagneticSnap);
        Assert.Equal(before, store.GetCabinets());
    }
}
