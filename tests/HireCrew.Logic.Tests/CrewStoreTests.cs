using HireCrew;
using Xunit;

public class CrewStoreTests
{
    [Fact]
    public void RoundTrip_Serialize_Preserves_Records()
    {
        var store = new CrewStore();
        store.Upsert(new CrewRecord
        {
            CrewId = "c1",
            Tier = CrewTier.Regular,
            GridEntityId = 100,
            SeatEntityId = null,
            WeaponEntityId = null,
            CharacterEntityId = null,
            OwnerIdentityId = 7,
            Status = CrewStatus.Unassigned
        });

        var bytes = store.ToBytes();
        var loaded = CrewStore.FromBytes(bytes);
        var got = loaded.Get("c1");
        Assert.NotNull(got);
        Assert.Equal(CrewTier.Regular, got.Tier);
        Assert.Equal(100L, got.GridEntityId);
        Assert.Equal(CrewStatus.Unassigned, got.Status);
    }

    [Fact]
    public void Remove_Deletes_Record()
    {
        var store = new CrewStore();
        store.Upsert(new CrewRecord { CrewId = "x", GridEntityId = 1, Tier = CrewTier.Recruit, OwnerIdentityId = 1, Status = CrewStatus.Unassigned });
        Assert.True(store.Remove("x"));
        Assert.Null(store.Get("x"));
    }
}
