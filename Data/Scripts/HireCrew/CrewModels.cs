using ProtoBuf;

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

    [ProtoContract]
    public sealed class CrewRecord
    {
        [ProtoMember(1)] public string CrewId;
        [ProtoMember(2)] public CrewTier Tier;
        [ProtoMember(3)] public long GridEntityId;
        [ProtoMember(4)] public long? SeatEntityId;
        [ProtoMember(5)] public long? WeaponEntityId;
        [ProtoMember(6)] public long? CharacterEntityId;
        [ProtoMember(7)] public long OwnerIdentityId;
        [ProtoMember(8)] public CrewStatus Status;
        [ProtoMember(9)] public string DisplayName;
    }

    [ProtoContract]
    public sealed class HireRequest
    {
        [ProtoMember(1)] public long GridEntityId;
        [ProtoMember(2)] public int Tier;
    }

    [ProtoContract]
    public sealed class AssignRequest
    {
        [ProtoMember(1)] public string CrewId;
        [ProtoMember(2)] public long GridEntityId;
        [ProtoMember(3)] public long SeatEntityId;
        [ProtoMember(4)] public long WeaponEntityId;
    }

    [ProtoContract]
    public sealed class DismissRequest
    {
        [ProtoMember(1)] public string CrewId;
        [ProtoMember(2)] public long GridEntityId;
    }

    [ProtoContract]
    public sealed class RosterSync
    {
        [ProtoMember(1)] public long GridEntityId;
        [ProtoMember(2)] public byte[] StoreBytes;
    }

    [ProtoContract]
    public sealed class NotifyMessage
    {
        [ProtoMember(1)] public string Text;
    }
}
