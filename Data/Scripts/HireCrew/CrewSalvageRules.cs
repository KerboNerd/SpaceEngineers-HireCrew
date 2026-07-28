namespace HireCrew
{
    public enum SalvageTargetRelation
    {
        Own = 0,
        Faction = 1,
        Unowned = 2,
        Enemy = 3
    }

    public static class CrewSalvageRules
    {
        public static bool IsLegalTarget(SalvageTargetRelation relation)
        {
            return relation != SalvageTargetRelation.Enemy;
        }

        public static SalvageTargetRelation ClassifyTarget(
            long viewerIdentityId,
            long viewerFactionIdOrZero,
            long gridPrimaryOwnerId,
            long gridOwnerFactionIdOrZero)
        {
            if (gridPrimaryOwnerId == 0)
                return SalvageTargetRelation.Unowned;
            if (viewerIdentityId != 0 && gridPrimaryOwnerId == viewerIdentityId)
                return SalvageTargetRelation.Own;
            if (viewerFactionIdOrZero != 0
                && gridOwnerFactionIdOrZero != 0
                && viewerFactionIdOrZero == gridOwnerFactionIdOrZero)
                return SalvageTargetRelation.Faction;
            return SalvageTargetRelation.Enemy;
        }
    }
}
