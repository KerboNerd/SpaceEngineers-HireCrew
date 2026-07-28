using System.Collections.Generic;
using HireCrew;
using Xunit;

public class CrewConfigTests
{
    [Fact]
    public void Prices_Increase_By_Stars()
    {
        Assert.True(CrewConfig.GetPrice(0) < CrewConfig.GetPrice(1));
        Assert.True(CrewConfig.GetPrice(1) < CrewConfig.GetPrice(3));
        Assert.True(CrewConfig.GetPrice(3) < CrewConfig.GetPrice(5));
    }

    [Fact]
    public void TrackingRanges_Increase_By_Stars()
    {
        Assert.True(CrewConfig.GetTrackingRange(0) < CrewConfig.GetTrackingRange(2));
        Assert.True(CrewConfig.GetTrackingRange(2) < CrewConfig.GetTrackingRange(5));
    }

    [Fact]
    public void PowerBonus_Increases_By_Stars()
    {
        Assert.True(CrewConfig.GetPowerBonus(0) < CrewConfig.GetPowerBonus(2));
        Assert.True(CrewConfig.GetPowerBonus(2) < CrewConfig.GetPowerBonus(5));
    }

    [Fact]
    public void LegacyTier_Maps_To_Stars()
    {
        Assert.Equal(1, CrewConfig.StarsFromLegacyTier(0));
        Assert.Equal(3, CrewConfig.StarsFromLegacyTier(1));
        Assert.Equal(5, CrewConfig.StarsFromLegacyTier(2));
    }

    [Fact]
    public void FormatStars_Shows_Filled_And_Empty()
    {
        Assert.Equal("***--", CrewConfig.FormatStars(3));
        Assert.Equal("-----", CrewConfig.FormatStars(0));
        Assert.Equal("*****", CrewConfig.FormatStars(5));
    }

    [Fact]
    public void SeatedEngineerPowerMultiplier_Stacks_And_Ignores_Gunners()
    {
        var crew = new[]
        {
            new CrewRecord { Role = CrewRole.Engineer, Stars = 1, Status = CrewStatus.Seated },
            new CrewRecord { Role = CrewRole.Engineer, Stars = 3, Status = CrewStatus.Seated },
            new CrewRecord { Role = CrewRole.Gunner, Stars = 5, Status = CrewStatus.Seated },
            new CrewRecord { Role = CrewRole.Engineer, Stars = 5, Status = CrewStatus.Unassigned },
        };
        float expected = 1f + CrewConfig.GetPowerBonus(1) + CrewConfig.GetPowerBonus(3);
        Assert.Equal(expected, CrewConfig.GetSeatedEngineerPowerMultiplier(crew));
    }

    [Fact]
    public void TrainCost_Increases_By_Step()
    {
        Assert.True(CrewConfig.GetTrainCost(0) < CrewConfig.GetTrainCost(2));
        Assert.True(CrewConfig.GetTrainCost(2) < CrewConfig.GetTrainCost(4));
        Assert.Equal(8000, CrewConfig.GetTrainCost(0));
        Assert.Equal(130000, CrewConfig.GetTrainCost(4));
    }

    [Fact]
    public void TrainMinutes_Increases_By_Step()
    {
        Assert.True(CrewConfig.GetTrainMinutes(0) < CrewConfig.GetTrainMinutes(2));
        Assert.Equal(5, CrewConfig.GetTrainMinutes(0));
        Assert.Equal(60, CrewConfig.GetTrainMinutes(4));
    }

    [Fact]
    public void GetTrainCost_Returns_Zero_At_MaxStars()
    {
        Assert.Equal(0, CrewConfig.GetTrainCost(5));
        Assert.Equal(0, CrewConfig.GetTrainMinutes(5));
    }

    [Fact]
    public void IsTraining_True_When_EndTicks_Set()
    {
        Assert.False(CrewConfig.IsTraining(null));
        Assert.False(CrewConfig.IsTraining(new CrewRecord { TrainingEndsUtcTicks = 0 }));
        Assert.True(CrewConfig.IsTraining(new CrewRecord { TrainingEndsUtcTicks = 1 }));
    }

    [Fact]
    public void RoleLabel_uses_short_names()
    {
        Assert.Equal("Reactor Tech", CrewConfig.RoleLabel(CrewRole.Engineer));
        Assert.Equal("Propulsion Tech", CrewConfig.RoleLabel(CrewRole.Propulsion));
        Assert.Equal("Quartermaster", CrewConfig.RoleLabel(CrewRole.Quartermaster));
    }

    [Fact]
    public void NeedsWeapon_only_gunner()
    {
        Assert.True(CrewConfig.NeedsWeapon(CrewRole.Gunner));
        Assert.False(CrewConfig.NeedsWeapon(CrewRole.Engineer));
        Assert.False(CrewConfig.NeedsWeapon(CrewRole.Helmsman));
        Assert.False(CrewConfig.NeedsWeapon(CrewRole.Quartermaster));
    }

    [Fact]
    public void Ambient_presence_defaults_are_sane()
    {
        Assert.True(CrewConfig.AmbientEnabled);
        Assert.Equal("HireCrew_Crew", CrewConfig.AmbientBotSubtype);
        Assert.Equal("NPC_Astronaut", CrewConfig.AmbientCharacterSubtype);
        Assert.True(CrewConfig.AmbientProximityMeters > CrewConfig.AmbientFarFromSeatMeters);
        Assert.True(CrewConfig.AmbientMaxLiveBotsPerGrid > 0);
        Assert.True(CrewConfig.AmbientMaxLiveBotsGlobal >= CrewConfig.AmbientMaxLiveBotsPerGrid);
        Assert.True(CrewConfig.AmbientRecoverTimeoutSeconds > 0f);
        Assert.True(CrewConfig.AmbientSitSecondsMax >= CrewConfig.AmbientSitSecondsMin);
        Assert.True(CrewConfig.AmbientWanderSecondsMax >= CrewConfig.AmbientWanderSecondsMin);
        Assert.True(CrewConfig.AmbientSitSecondsMin > 0f);
        Assert.True(CrewConfig.AmbientWanderSecondsMin > 0f);
        Assert.True(CrewConfig.AmbientWanderRadiusMeters > 1f);
    }

    [Fact]
    public void Body_loss_on_mission_is_not_permanent_hire_loss()
    {
        Assert.False(CrewConfig.PermanentLossOnUnexpectedBodyGone(onMission: true));
        Assert.True(CrewConfig.PermanentLossOnUnexpectedBodyGone(onMission: false));
    }

    [Fact]
    public void ClampRole_accepts_new_roles()
    {
        Assert.Equal(CrewRole.Propulsion, CrewConfig.ClampRole((int)CrewRole.Propulsion));
        Assert.Equal(CrewRole.Gunner, CrewConfig.ClampRole(-1));
        Assert.Equal(CrewRole.SalvageOps, CrewConfig.ClampRole(999));
        Assert.Equal("Construction", CrewConfig.RoleLabel(CrewRole.DamageControl));
        Assert.Equal("Salvage Ops", CrewConfig.RoleLabel(CrewRole.SalvageOps));
        Assert.False(CrewConfig.NeedsWeapon(CrewRole.DamageControl));
        Assert.False(CrewConfig.NeedsWeapon(CrewRole.SalvageOps));
    }

    [Fact]
    public void Salvage_rate_helpers_scale_with_stars()
    {
        Assert.True(CrewConfig.GetSalvageGrindMountPerSecond(0)
            < CrewConfig.GetSalvageGrindMountPerSecond(5));
        Assert.True(CrewConfig.GetSalvageEvaSpeedMeters(0)
            < CrewConfig.GetSalvageEvaSpeedMeters(5));
        Assert.Equal(2000f, CrewConfig.SalvageScanRadiusMeters);
    }

    [Fact]
    public void TrainDiscount_soft_stacks_and_caps()
    {
        var roster = new List<CrewRecord>
        {
            new CrewRecord
            {
                Role = CrewRole.Quartermaster,
                Status = CrewStatus.Seated,
                Stars = 5,
                GridEntityId = 1,
                OwnerKey = 42,
                OwnerIsFaction = false
            },
            new CrewRecord
            {
                Role = CrewRole.Quartermaster,
                Status = CrewStatus.Seated,
                Stars = 5,
                GridEntityId = 1,
                OwnerKey = 42,
                OwnerIsFaction = false
            },
            new CrewRecord
            {
                Role = CrewRole.Gunner,
                Status = CrewStatus.Seated,
                Stars = 5,
                GridEntityId = 1,
                OwnerKey = 42,
                OwnerIsFaction = false
            },
        };
        float d = CrewConfig.GetTrainDiscountFraction(roster, 42, false);
        Assert.InRange(d, 0.20f, CrewConfig.TrainDiscountCap + 0.0001f);
        Assert.True(d <= CrewConfig.TrainDiscountCap + 0.0001f);

        long full = CrewConfig.GetTrainCost(1);
        long discounted = CrewConfig.GetTrainCost(1, d);
        Assert.True(discounted < full);
        Assert.True(discounted >= 1);
    }

    [Fact]
    public void Propulsion_multiplier_stacks_and_caps()
    {
        var crew = new List<CrewRecord>
        {
            new CrewRecord { Role = CrewRole.Propulsion, Status = CrewStatus.Seated, Stars = 5, GridEntityId = 1 },
            new CrewRecord { Role = CrewRole.Propulsion, Status = CrewStatus.Seated, Stars = 5, GridEntityId = 1 },
        };
        float mult = CrewConfig.GetSeatedRoleMultiplier(crew, CrewRole.Propulsion);
        Assert.InRange(mult, 1f, CrewConfig.ThrustMultiplierCap + 0.0001f);
    }

    [Fact]
    public void RepairEvaSpeed_Increases_By_Stars()
    {
        Assert.True(CrewConfig.GetRepairEvaSpeedMeters(0) < CrewConfig.GetRepairEvaSpeedMeters(2));
        Assert.True(CrewConfig.GetRepairEvaSpeedMeters(2) < CrewConfig.GetRepairEvaSpeedMeters(5));
        Assert.Equal(CrewConfig.RepairEvaSpeedMeters * 0.75f, CrewConfig.GetRepairEvaSpeedMeters(0), 3);
        Assert.Equal(CrewConfig.RepairEvaSpeedMeters * 1.25f, CrewConfig.GetRepairEvaSpeedMeters(5), 3);
    }
}
