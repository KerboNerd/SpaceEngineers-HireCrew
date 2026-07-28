using HireCrew;
using Xunit;

public class CrewValidationTests
{
    [Fact]
    public void CanManageGrid_True_When_Owner()
    {
        Assert.True(CrewValidation.CanManageGrid(requesterIdentity: 10, gridOwner: 10, sameFaction: false));
    }

    [Fact]
    public void CanManageGrid_True_When_SameFaction()
    {
        Assert.True(CrewValidation.CanManageGrid(requesterIdentity: 11, gridOwner: 10, sameFaction: true));
    }

    [Fact]
    public void CanManageGrid_False_When_Stranger()
    {
        Assert.False(CrewValidation.CanManageGrid(requesterIdentity: 11, gridOwner: 10, sameFaction: false));
    }

    [Fact]
    public void CanAssign_Fails_When_WeaponAlreadyManned()
    {
        var existing = new CrewRecord
        {
            CrewId = "a",
            GridEntityId = 1,
            SeatEntityId = 2,
            WeaponEntityId = 3,
            Status = CrewStatus.Seated
        };
        var err = CrewValidation.ValidateAssign(
            existingCrewOnGrid: new[] { existing },
            crewId: "b",
            gridEntityId: 1,
            seatEntityId: 4,
            weaponEntityId: 3);
        Assert.Equal(CrewValidation.ErrorWeaponManned, err);
    }

    [Fact]
    public void CanAssign_Fails_When_SeatTaken()
    {
        var existing = new CrewRecord
        {
            CrewId = "a",
            GridEntityId = 1,
            SeatEntityId = 2,
            WeaponEntityId = 3,
            Status = CrewStatus.Seated
        };
        var err = CrewValidation.ValidateAssign(
            existingCrewOnGrid: new[] { existing },
            crewId: "b",
            gridEntityId: 1,
            seatEntityId: 2,
            weaponEntityId: 9);
        Assert.Equal(CrewValidation.ErrorSeatTaken, err);
    }

    [Fact]
    public void CanAssign_Ok_When_Free()
    {
        var err = CrewValidation.ValidateAssign(
            existingCrewOnGrid: new CrewRecord[0],
            crewId: "b",
            gridEntityId: 1,
            seatEntityId: 2,
            weaponEntityId: 3);
        Assert.Null(err);
    }

    [Fact]
    public void CanAssign_Engineer_Ok_Without_Weapon()
    {
        var err = CrewValidation.ValidateAssign(
            existingCrewOnGrid: new CrewRecord[0],
            crewId: "e1",
            gridEntityId: 1,
            seatEntityId: 2,
            weaponEntityId: 0,
            role: CrewRole.Engineer);
        Assert.Null(err);
    }

    [Fact]
    public void CanAssign_Engineer_Fails_When_SeatTaken()
    {
        var existing = new CrewRecord
        {
            CrewId = "a",
            GridEntityId = 1,
            SeatEntityId = 2,
            Role = CrewRole.Engineer,
            Status = CrewStatus.Seated
        };
        var err = CrewValidation.ValidateAssign(
            existingCrewOnGrid: new[] { existing },
            crewId: "b",
            gridEntityId: 1,
            seatEntityId: 2,
            weaponEntityId: 0,
            role: CrewRole.Engineer);
        Assert.Equal(CrewValidation.ErrorSeatTaken, err);
    }

    [Fact]
    public void ValidateTrain_Fails_When_Missing()
    {
        Assert.Equal(CrewValidation.ErrorCrewMissing, CrewValidation.ValidateTrain(null));
    }

    [Fact]
    public void ValidateTrain_Fails_When_AlreadyTraining()
    {
        var c = new CrewRecord { CrewId = "x", Stars = 1, TrainingEndsUtcTicks = 99 };
        Assert.Equal(CrewValidation.ErrorAlreadyTraining, CrewValidation.ValidateTrain(c));
    }

    [Fact]
    public void ValidateTrain_Fails_When_MaxStars()
    {
        var c = new CrewRecord { CrewId = "x", Stars = 5, TrainingEndsUtcTicks = 0 };
        Assert.Equal(CrewValidation.ErrorMaxStars, CrewValidation.ValidateTrain(c));
    }

    [Fact]
    public void ValidateTrain_Ok()
    {
        var c = new CrewRecord { CrewId = "x", Stars = 2, TrainingEndsUtcTicks = 0 };
        Assert.Null(CrewValidation.ValidateTrain(c));
    }

    [Fact]
    public void ValidateCancelTrain_Fails_When_NotTraining()
    {
        var c = new CrewRecord { CrewId = "x", TrainingEndsUtcTicks = 0 };
        Assert.Equal(CrewValidation.ErrorNotTraining, CrewValidation.ValidateCancelTrain(c));
    }

    [Fact]
    public void TryCompleteTraining_Promotes_When_Due()
    {
        var c = new CrewRecord { Stars = 2, TrainingEndsUtcTicks = 100 };
        Assert.True(CrewValidation.TryCompleteTraining(c, 100));
        Assert.Equal(3, c.Stars);
        Assert.Equal(0, c.TrainingEndsUtcTicks);
    }

    [Fact]
    public void TryCompleteTraining_NoOp_When_NotDue()
    {
        var c = new CrewRecord { Stars = 2, TrainingEndsUtcTicks = 200 };
        Assert.False(CrewValidation.TryCompleteTraining(c, 100));
        Assert.Equal(2, c.Stars);
        Assert.Equal(200, c.TrainingEndsUtcTicks);
    }
}
