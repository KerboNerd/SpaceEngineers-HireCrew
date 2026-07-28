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
    }
}
