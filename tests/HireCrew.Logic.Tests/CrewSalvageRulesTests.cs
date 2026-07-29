using HireCrew;
using Xunit;

namespace HireCrew.Logic.Tests
{
    public class CrewSalvageRulesTests
    {
        [Fact]
        public void IsLegalTarget_rejects_only_enemy()
        {
            Assert.True(CrewSalvageRules.IsLegalTarget(SalvageTargetRelation.Own));
            Assert.True(CrewSalvageRules.IsLegalTarget(SalvageTargetRelation.Faction));
            Assert.True(CrewSalvageRules.IsLegalTarget(SalvageTargetRelation.Unowned));
            Assert.False(CrewSalvageRules.IsLegalTarget(SalvageTargetRelation.Enemy));
        }

        [Fact]
        public void ClassifyTarget_maps_owners()
        {
            Assert.Equal(SalvageTargetRelation.Unowned,
                CrewSalvageRules.ClassifyTarget(10, 100, 0, 0));
            Assert.Equal(SalvageTargetRelation.Own,
                CrewSalvageRules.ClassifyTarget(10, 100, 10, 0));
            Assert.Equal(SalvageTargetRelation.Faction,
                CrewSalvageRules.ClassifyTarget(10, 100, 20, 100));
            Assert.Equal(SalvageTargetRelation.Enemy,
                CrewSalvageRules.ClassifyTarget(10, 100, 20, 200));
            Assert.Equal(SalvageTargetRelation.Enemy,
                CrewSalvageRules.ClassifyTarget(10, 0, 20, 0));
        }

        [Fact]
        public void PreferGrindCandidate_leaf_beats_nearer_interior()
        {
            // Far tip (1 neighbor) vs near bridge (4 neighbors) — tip wins.
            Assert.True(CrewSalvageRules.PreferGrindCandidate(
                neighborCountA: 1, distanceSqA: 10_000,
                neighborCountB: 4, distanceSqB: 25));
        }

        [Fact]
        public void PreferGrindCandidate_same_leafness_nearer_wins()
        {
            Assert.True(CrewSalvageRules.PreferGrindCandidate(
                neighborCountA: 2, distanceSqA: 100,
                neighborCountB: 2, distanceSqB: 400));
            Assert.False(CrewSalvageRules.PreferGrindCandidate(
                neighborCountA: 2, distanceSqA: 400,
                neighborCountB: 2, distanceSqB: 100));
        }

        [Fact]
        public void NeedsEvaAfterRetarget_only_when_outside_grind_range()
        {
            const double grindR = 6.5;
            Assert.False(CrewSalvageRules.NeedsEvaAfterRetarget(grindR * grindR, grindR));
            Assert.False(CrewSalvageRules.NeedsEvaAfterRetarget(4.0, grindR));
            Assert.True(CrewSalvageRules.NeedsEvaAfterRetarget((grindR + 0.1) * (grindR + 0.1), grindR));
        }

        [Fact]
        public void BuildPaddedZone_inflates_all_sides()
        {
            double minX, minY, minZ, maxX, maxY, maxZ;
            CrewSalvageRules.BuildPaddedZone(
                0, 0, 0, 10, 20, 30, 15,
                out minX, out minY, out minZ, out maxX, out maxY, out maxZ);
            Assert.Equal(-15, minX);
            Assert.Equal(-15, minY);
            Assert.Equal(-15, minZ);
            Assert.Equal(25, maxX);
            Assert.Equal(35, maxY);
            Assert.Equal(45, maxZ);
        }

        [Fact]
        public void IsInsideZone_includes_boundary_excludes_outside()
        {
            Assert.True(CrewSalvageRules.IsInsideZone(0, 0, 0, -1, -1, -1, 1, 1, 1));
            Assert.True(CrewSalvageRules.IsInsideZone(1, 0, 0, -1, -1, -1, 1, 1, 1));
            Assert.False(CrewSalvageRules.IsInsideZone(1.01, 0, 0, -1, -1, -1, 1, 1, 1));
        }
    }
}
