using HireCrew;
using Xunit;

public class CrewConfigTests
{
    [Fact]
    public void Prices_Increase_By_Tier()
    {
        Assert.True(CrewConfig.GetPrice(CrewTier.Recruit) < CrewConfig.GetPrice(CrewTier.Regular));
        Assert.True(CrewConfig.GetPrice(CrewTier.Regular) < CrewConfig.GetPrice(CrewTier.Elite));
    }

    [Fact]
    public void TrackingRanges_Increase_By_Tier()
    {
        Assert.True(CrewConfig.GetTrackingRange(CrewTier.Recruit) < CrewConfig.GetTrackingRange(CrewTier.Regular));
        Assert.True(CrewConfig.GetTrackingRange(CrewTier.Regular) < CrewConfig.GetTrackingRange(CrewTier.Elite));
    }
}
