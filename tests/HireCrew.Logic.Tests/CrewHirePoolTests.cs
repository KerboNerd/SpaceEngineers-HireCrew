using System;
using HireCrew;
using Xunit;

public class CrewHirePoolTests
{
    [Fact]
    public void RollCandidateCount_Is_Between_1_And_8()
    {
        var rng = new Random(1);
        for (int i = 0; i < 40; i++)
        {
            int n = CrewHireGenerator.RollCandidateCount(rng);
            Assert.InRange(n, 1, 8);
        }
    }

    [Fact]
    public void RollStars_Is_Between_0_And_5()
    {
        var rng = new Random(2);
        for (int i = 0; i < 100; i++)
            Assert.InRange(CrewHireGenerator.RollStars(rng), 0, 5);
    }

    [Fact]
    public void GeneratePool_Creates_Named_Priced_Candidates()
    {
        var list = CrewHireGenerator.GeneratePool(new Random(42));
        Assert.InRange(list.Count, 1, 8);
        foreach (var c in list)
        {
            Assert.False(string.IsNullOrEmpty(c.CandidateId));
            Assert.False(string.IsNullOrEmpty(c.FirstName));
            Assert.False(string.IsNullOrEmpty(c.LastName));
            Assert.InRange(c.Stars, 0, 5);
            Assert.True(c.Price > 0);
        }
    }

    [Fact]
    public void HirePoolStore_TakeCandidate_Removes_From_Pool()
    {
        var store = new HirePoolStore();
        var pool = store.Ensure(10, 20, new Random(3), DateTime.UtcNow);
        Assert.True(pool.Candidates.Count > 0);
        var id = pool.Candidates[0].CandidateId;
        var taken = store.TakeCandidate(10, id);
        Assert.NotNull(taken);
        Assert.Equal(id, taken.CandidateId);
        Assert.Null(store.TakeCandidate(10, id));
    }

    [Fact]
    public void HirePoolStore_RoundTrip_Preserves_Pool()
    {
        var store = new HirePoolStore();
        var pool = store.Ensure(11, 22, new Random(7), new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        pool.RefreshMinutes = 45;
        pool.PriceMultiplierPercent = 150;
        var bytes = store.ToBytes();
        var loaded = HirePoolStore.FromBytes(bytes);
        var got = loaded.Get(11);
        Assert.NotNull(got);
        Assert.Equal(22L, got.GridEntityId);
        Assert.Equal(45, got.RefreshMinutes);
        Assert.Equal(150, got.PriceMultiplierPercent);
        Assert.Equal(pool.Candidates.Count, got.Candidates.Count);
        Assert.Equal(pool.Candidates[0].CandidateId, got.Candidates[0].CandidateId);
    }

    [Fact]
    public void ApplyMultiplierToPool_Rescales_Prices()
    {
        var pool = new HireBlockPool
        {
            BlockEntityId = 1,
            PriceMultiplierPercent = 100,
            Candidates = new System.Collections.Generic.List<HireCandidate>
            {
                new HireCandidate { CandidateId = "a", Price = 10000 }
            }
        };
        CrewHireGenerator.ApplyMultiplierToPool(pool, 200);
        Assert.Equal(200, pool.PriceMultiplierPercent);
        Assert.Equal(20000, pool.Candidates[0].Price);
    }

    [Fact]
    public void TickRefresh_Rerolls_When_Due()
    {
        var store = new HirePoolStore();
        var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var pool = store.Ensure(5, 6, new Random(9), now);
        pool.NextRefreshUtcTicks = now.AddMinutes(-1).Ticks;
        var beforeId = pool.Candidates.Count > 0 ? pool.Candidates[0].CandidateId : null;
        Assert.True(store.TickRefresh(now, new Random(10)));
        var after = store.Get(5);
        Assert.NotNull(after);
        Assert.True(after.NextRefreshUtcTicks > now.Ticks);
        if (beforeId != null && after.Candidates.Count > 0)
            Assert.NotEqual(beforeId, after.Candidates[0].CandidateId);
    }
}
