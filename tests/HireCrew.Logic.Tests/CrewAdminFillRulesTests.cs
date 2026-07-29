using HireCrew;
using Xunit;

public class CrewAdminFillRulesTests
{
    [Fact]
    public void IsFillRole_ConstructionAndSalvage_True()
    {
        Assert.True(CrewAdminFillRules.IsFillRole(CrewRole.DamageControl));
        Assert.True(CrewAdminFillRules.IsFillRole(CrewRole.SalvageOps));
    }

    [Fact]
    public void IsFillRole_OtherRoles_False()
    {
        Assert.False(CrewAdminFillRules.IsFillRole(CrewRole.Gunner));
        Assert.False(CrewAdminFillRules.IsFillRole(CrewRole.Engineer));
        Assert.False(CrewAdminFillRules.IsFillRole(CrewRole.Helmsman));
        Assert.False(CrewAdminFillRules.IsFillRole(CrewRole.Propulsion));
        Assert.False(CrewAdminFillRules.IsFillRole(CrewRole.Quartermaster));
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(10, 10)]
    [InlineData(50, 50)]
    [InlineData(0, 1)]
    [InlineData(-3, 1)]
    [InlineData(51, 50)]
    [InlineData(999, 50)]
    public void ClampCount_Bounds(int requested, int expected)
    {
        Assert.Equal(expected, CrewAdminFillRules.ClampCount(requested));
    }

    [Fact]
    public void FormatResult_Partial()
    {
        string msg = CrewAdminFillRules.FormatResult("Construction", 8, 10, 2, "Test Ship");
        Assert.Equal("Filled Construction: assigned 8/10 (2 no seat) on Test Ship", msg);
    }

    [Fact]
    public void FormatResult_Full()
    {
        string msg = CrewAdminFillRules.FormatResult("Salvage Ops", 10, 10, 0, "Grid");
        Assert.Equal("Filled Salvage Ops: assigned 10/10 on Grid", msg);
    }

    [Fact]
    public void Constants()
    {
        Assert.Equal(10, CrewAdminFillRules.DefaultCount);
        Assert.Equal(1, CrewAdminFillRules.MinCount);
        Assert.Equal(50, CrewAdminFillRules.MaxCount);
        Assert.Equal(3, CrewAdminFillRules.FillStars);
    }
}
