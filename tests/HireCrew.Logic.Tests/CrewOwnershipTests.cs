using HireCrew;
using Xunit;

public class CrewOwnershipTests
{
    [Fact]
    public void Resolve_Uses_Faction_When_Present()
    {
        long key;
        bool isFaction;
        CrewOwnership.Resolve(identityId: 10, factionIdOrZero: 99, out key, out isFaction);
        Assert.Equal(99, key);
        Assert.True(isFaction);
    }

    [Fact]
    public void Resolve_Falls_Back_To_Identity()
    {
        long key;
        bool isFaction;
        CrewOwnership.Resolve(identityId: 10, factionIdOrZero: 0, out key, out isFaction);
        Assert.Equal(10, key);
        Assert.False(isFaction);
    }

    [Fact]
    public void GetForOwner_Filters_Pool()
    {
        var store = new CrewStore();
        store.Upsert(new CrewRecord
        {
            CrewId = "a",
            OwnerKey = 5,
            OwnerIsFaction = true,
            Status = CrewStatus.Unassigned,
            GridEntityId = 0,
            Stars = 2
        });
        store.Upsert(new CrewRecord
        {
            CrewId = "b",
            OwnerKey = 5,
            OwnerIsFaction = false,
            Status = CrewStatus.Unassigned,
            GridEntityId = 0,
            Stars = 1
        });
        var list = store.GetForOwner(5, true);
        Assert.Single(list);
        Assert.Equal("a", list[0].CrewId);
    }

    [Fact]
    public void RoundTrip_Preserves_OwnerKey()
    {
        var store = new CrewStore();
        store.Upsert(new CrewRecord
        {
            CrewId = "c1",
            Stars = 4,
            GridEntityId = 0,
            OwnerIdentityId = 7,
            OwnerKey = 42,
            OwnerIsFaction = true,
            Status = CrewStatus.Unassigned,
            Role = CrewRole.Gunner,
            DisplayName = "Test Pilot"
        });
        var loaded = CrewStore.FromBytes(store.ToBytes()).Get("c1");
        Assert.NotNull(loaded);
        Assert.Equal(42, loaded.OwnerKey);
        Assert.True(loaded.OwnerIsFaction);
        Assert.Equal(0, loaded.GridEntityId);
    }
}
