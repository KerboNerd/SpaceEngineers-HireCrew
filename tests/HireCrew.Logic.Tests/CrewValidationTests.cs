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
}
