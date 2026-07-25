namespace HireCrew
{
    public enum CrewTier
    {
        Recruit = 0,
        Regular = 1,
        Elite = 2
    }

    public enum CrewStatus
    {
        Unassigned = 0,
        Seated = 1
    }

    public sealed class CrewRecord
    {
        public string CrewId;
        public CrewTier Tier;
        public long GridEntityId;
        public long? SeatEntityId;
        public long? WeaponEntityId;
        public long? CharacterEntityId;
        public long OwnerIdentityId;
        public CrewStatus Status;
        public string DisplayName;
    }
}
