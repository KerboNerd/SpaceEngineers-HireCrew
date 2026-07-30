using HireCrew;
using Xunit;

public class CrewRepairShareRulesTests
{
    [Fact]
    public void MaxClaimSlots_Projected_AlwaysOne()
    {
        Assert.Equal(1, CrewRepairShareRules.MaxClaimSlots(
            projected: true,
            maxIntegrity: 99999f,
            shareMaxWelders: 3,
            shareMaxIntegrity: 5000f,
            onlyRemainingWork: true));
    }

    [Fact]
    public void MaxClaimSlots_SmallReal_One()
    {
        Assert.Equal(1, CrewRepairShareRules.MaxClaimSlots(
            projected: false,
            maxIntegrity: 100f,
            shareMaxWelders: 3,
            shareMaxIntegrity: 5000f,
            onlyRemainingWork: false));
    }

    [Fact]
    public void MaxClaimSlots_LargeWithOtherWork_ShareCap()
    {
        Assert.Equal(3, CrewRepairShareRules.MaxClaimSlots(
            projected: false,
            maxIntegrity: 5000f,
            shareMaxWelders: 3,
            shareMaxIntegrity: 5000f,
            onlyRemainingWork: false));
    }

    [Fact]
    public void MaxClaimSlots_LargeOnlyRemaining_Unlimited()
    {
        Assert.Equal(int.MaxValue, CrewRepairShareRules.MaxClaimSlots(
            projected: false,
            maxIntegrity: 8000f,
            shareMaxWelders: 3,
            shareMaxIntegrity: 5000f,
            onlyRemainingWork: true));
    }

    [Fact]
    public void IsClaimFull_AtCap()
    {
        Assert.False(CrewRepairShareRules.IsClaimFull(2, 3));
        Assert.True(CrewRepairShareRules.IsClaimFull(3, 3));
        Assert.False(CrewRepairShareRules.IsClaimFull(99, int.MaxValue));
    }

    [Fact]
    public void SharedHoverOffsets_SpreadsSlots()
    {
        double lat0, out0, lat1, out1;
        CrewRepairShareRules.SharedHoverOffsets(0, out lat0, out out0);
        CrewRepairShareRules.SharedHoverOffsets(1, out lat1, out out1);
        Assert.NotEqual(lat0, lat1);
        Assert.Equal(0.15, out0);
    }
}
