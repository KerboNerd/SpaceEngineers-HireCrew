using HireCrew;
using Xunit;

public class CrewBotRelationsTests
{
    [Fact]
    public void FriendlyFaction_Uses_OwnerKey_When_Faction_Roster()
    {
        var crew = new CrewRecord
        {
            OwnerIdentityId = 10,
            OwnerKey = 99,
            OwnerIsFaction = true
        };
        Assert.Equal(99, CrewBotRelations.ResolveFriendlyFactionId(crew, id => 0));
    }

    [Fact]
    public void FriendlyFaction_Looks_Up_Player_Faction_When_Personal_Roster()
    {
        var crew = new CrewRecord
        {
            OwnerIdentityId = 10,
            OwnerKey = 10,
            OwnerIsFaction = false
        };
        Assert.Equal(55, CrewBotRelations.ResolveFriendlyFactionId(crew, id => id == 10 ? 55L : 0L));
    }

    [Fact]
    public void FriendlyFaction_Zero_When_Factionless_Needs_Fallback()
    {
        var crew = new CrewRecord
        {
            OwnerIdentityId = 10,
            OwnerKey = 10,
            OwnerIsFaction = false
        };
        long resolved = CrewBotRelations.ResolveFriendlyFactionId(crew, id => 0);
        Assert.Equal(0, resolved);
        Assert.True(CrewBotRelations.NeedsFallbackFriendlyFaction(resolved));
    }

    [Fact]
    public void Owner_Faction_Does_Not_Need_Fallback()
    {
        Assert.False(CrewBotRelations.NeedsFallbackFriendlyFaction(99));
    }

    [Fact]
    public void CharacterOwner_Prefers_Hiring_Identity()
    {
        var crew = new CrewRecord { OwnerIdentityId = 42 };
        Assert.Equal(42, CrewBotRelations.ResolveCharacterOwnerIdentityId(crew, botIdentityId: 99));
    }

    [Fact]
    public void CharacterOwner_Falls_Back_To_Bot_Identity()
    {
        Assert.Equal(99, CrewBotRelations.ResolveCharacterOwnerIdentityId(null, botIdentityId: 99));
        Assert.Equal(99, CrewBotRelations.ResolveCharacterOwnerIdentityId(
            new CrewRecord { OwnerIdentityId = 0 }, botIdentityId: 99));
    }
}
