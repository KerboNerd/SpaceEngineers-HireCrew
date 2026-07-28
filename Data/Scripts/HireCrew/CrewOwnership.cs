namespace HireCrew
{
    /// <summary>
    /// Central roster key: faction id when in a faction, otherwise player identity.
    /// </summary>
    public static class CrewOwnership
    {
        public static void Resolve(long identityId, long factionIdOrZero, out long ownerKey, out bool ownerIsFaction)
        {
            if (factionIdOrZero != 0)
            {
                ownerKey = factionIdOrZero;
                ownerIsFaction = true;
                return;
            }

            ownerKey = identityId;
            ownerIsFaction = false;
        }

        public static bool Matches(CrewRecord crew, long ownerKey, bool ownerIsFaction)
        {
            if (crew == null) return false;
            return crew.OwnerKey == ownerKey && crew.OwnerIsFaction == ownerIsFaction;
        }

        public static bool IsInPool(CrewRecord crew)
        {
            return crew != null && crew.Status == CrewStatus.Unassigned;
        }
    }
}
