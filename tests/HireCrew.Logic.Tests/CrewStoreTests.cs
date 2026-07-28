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
            Stars = 3,
            Role = CrewRole.Engineer,
            GridEntityId = 0,
            SeatEntityId = null,
            WeaponEntityId = null,
            CharacterEntityId = null,
            OwnerIdentityId = 7,
            OwnerKey = 7,
            OwnerIsFaction = false,
            Status = CrewStatus.Unassigned,
            DisplayName = "Alex Brooks"
        });

        var bytes = store.ToBytes();
        var loaded = CrewStore.FromBytes(bytes);
        var got = loaded.Get("c1");
        Assert.NotNull(got);
        Assert.Equal(3, got.Stars);
        Assert.Equal(CrewRole.Engineer, got.Role);
        Assert.Equal(0L, got.GridEntityId);
        Assert.Equal(7L, got.OwnerKey);
        Assert.Equal(CrewStatus.Unassigned, got.Status);
        Assert.Equal("Alex Brooks", got.DisplayName);
        Assert.Equal(0L, got.TrainingEndsUtcTicks);
    }

    [Fact]
    public void RoundTrip_Preserves_TrainingEndsUtcTicks()
    {
        var store = new CrewStore();
        store.Upsert(new CrewRecord
        {
            CrewId = "t1",
            Stars = 2,
            OwnerIdentityId = 1,
            OwnerKey = 1,
            Status = CrewStatus.Unassigned,
            TrainingEndsUtcTicks = 123456789L
        });
        var got = CrewStore.FromBytes(store.ToBytes()).Get("t1");
        Assert.NotNull(got);
        Assert.Equal(123456789L, got.TrainingEndsUtcTicks);
    }

    [Fact]
    public void Remove_Deletes_Record()
    {
        var store = new CrewStore();
        store.Upsert(new CrewRecord { CrewId = "x", GridEntityId = 0, Stars = 1, OwnerIdentityId = 1, OwnerKey = 1, Status = CrewStatus.Unassigned });
        Assert.True(store.Remove("x"));
        Assert.Null(store.Get("x"));
    }
}
