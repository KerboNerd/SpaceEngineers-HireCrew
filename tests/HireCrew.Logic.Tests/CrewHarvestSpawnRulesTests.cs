using System;
using HireCrew;
using Xunit;

public class CrewHarvestSpawnRulesTests
{
    [Fact]
    public void OffsetFromAnchor_DistanceInRange()
    {
        double ax = 100, ay = 200, az = -50;
        double x, y, z;
        CrewHarvestSpawnRules.OffsetFromAnchor(ax, ay, az, 1, out x, out y, out z);
        double dx = x - ax, dy = y - ay, dz = z - az;
        double dist = Math.Sqrt(dx * dx + dy * dy + dz * dz);
        Assert.InRange(dist, CrewHarvestSpawnRules.OffsetMinMeters * 0.99, CrewHarvestSpawnRules.OffsetMaxMeters * 1.01);
    }

    [Fact]
    public void OffsetFromAnchor_DeterministicForVariant()
    {
        double x1, y1, z1, x2, y2, z2;
        CrewHarvestSpawnRules.OffsetFromAnchor(0, 0, 0, 42, out x1, out y1, out z1);
        CrewHarvestSpawnRules.OffsetFromAnchor(0, 0, 0, 42, out x2, out y2, out z2);
        Assert.Equal(x1, x2);
        Assert.Equal(y1, y2);
        Assert.Equal(z1, z2);
    }

    [Fact]
    public void DeepSpaceFallback_FarFromOrigin()
    {
        double x, y, z;
        CrewHarvestSpawnRules.DeepSpaceFallback(3, out x, out y, out z);
        double dist = Math.Sqrt(x * x + y * y + z * z);
        Assert.True(dist >= CrewHarvestSpawnRules.DeepSpaceMinMeters * 0.99);
    }

    [Fact]
    public void AnchorKindConstants_Stable()
    {
        Assert.Equal("player", CrewHarvestSpawnRules.AnchorPlayer);
        Assert.Equal("grid", CrewHarvestSpawnRules.AnchorGrid);
        Assert.Equal("deepspace", CrewHarvestSpawnRules.AnchorDeepSpace);
        Assert.Equal("none", CrewHarvestSpawnRules.AnchorNone);
    }
}
