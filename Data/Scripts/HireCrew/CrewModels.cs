using System.Collections.Generic;
using ProtoBuf;

namespace HireCrew
{
    public enum CrewStatus
    {
        Unassigned = 0,
        Seated = 1
    }

    public enum CrewRole
    {
        Gunner = 0,
        Engineer = 1,
        Helmsman = 2,
        Propulsion = 3,
        Quartermaster = 4
    }

    [ProtoContract]
    public sealed class CrewRecord
    {
        [ProtoMember(1)] public string CrewId;
        /// <summary>0–5 star rating (replaces old Recruit/Regular/Elite tiers).</summary>
        [ProtoMember(2)] public int Stars;
        /// <summary>0 while in the owner pool (unassigned); set when stationed on a grid.</summary>
        [ProtoMember(3)] public long GridEntityId;
        [ProtoMember(4)] public long? SeatEntityId;
        [ProtoMember(5)] public long? WeaponEntityId;
        [ProtoMember(6)] public long? CharacterEntityId;
        [ProtoMember(7)] public long OwnerIdentityId;
        [ProtoMember(8)] public CrewStatus Status;
        [ProtoMember(9)] public string DisplayName;
        [ProtoMember(10)] public long? BedEntityId;
        [ProtoMember(11)] public long? ToiletEntityId;
        [ProtoMember(12)] public long? ShowerEntityId;
        [ProtoMember(13)] public CrewRole Role;
        /// <summary>Faction id or player identity — shared roster key.</summary>
        [ProtoMember(14)] public long OwnerKey;
        [ProtoMember(15)] public bool OwnerIsFaction;
        /// <summary>UTC end ticks while training; 0 = not training.</summary>
        [ProtoMember(16)] public long TrainingEndsUtcTicks;
    }

    [ProtoContract]
    public sealed class HireCandidate
    {
        [ProtoMember(1)] public string CandidateId;
        [ProtoMember(2)] public string FirstName;
        [ProtoMember(3)] public string LastName;
        [ProtoMember(4)] public int Stars;
        [ProtoMember(5)] public int Role;
        [ProtoMember(6)] public long Price;

        public string FullName
        {
            get { return ((FirstName ?? "") + " " + (LastName ?? "")).Trim(); }
        }
    }

    [ProtoContract]
    public sealed class HireBlockPool
    {
        [ProtoMember(1)] public long BlockEntityId;
        [ProtoMember(2)] public long GridEntityId;
        /// <summary>Refresh interval in minutes (1–300).</summary>
        [ProtoMember(3)] public int RefreshMinutes;
        [ProtoMember(4)] public long NextRefreshUtcTicks;
        [ProtoMember(5)] public List<HireCandidate> Candidates = new List<HireCandidate>();
        /// <summary>Price multiplier percent (25–500, 100 = 1.00x).</summary>
        [ProtoMember(6)] public int PriceMultiplierPercent = CrewConfig.DefaultPriceMultiplierPercent;
    }

    [ProtoContract]
    public sealed class AssignAmenityRequest
    {
        [ProtoMember(1)] public string CrewId;
        [ProtoMember(2)] public long GridEntityId;
        [ProtoMember(3)] public int Kind;
        /// <summary>0 clears the amenity slot.</summary>
        [ProtoMember(4)] public long BlockEntityId;
    }

    /// <summary>Debug slash-command hire (free when SkipCharge).</summary>
    [ProtoContract]
    public sealed class HireRequest
    {
        [ProtoMember(1)] public long GridEntityId;
        [ProtoMember(2)] public int Stars;
        [ProtoMember(3)] public bool SkipCharge;
        [ProtoMember(4)] public int Role;
    }

    [ProtoContract]
    public sealed class HireFromPoolRequest
    {
        [ProtoMember(1)] public long BlockEntityId;
        [ProtoMember(2)] public string CandidateId;
    }

    [ProtoContract]
    public sealed class HireRefreshRequest
    {
        [ProtoMember(1)] public long BlockEntityId;
        [ProtoMember(2)] public int RefreshMinutes;
        /// <summary>Price multiplier percent (25–500). Always sent with refresh settings.</summary>
        [ProtoMember(3)] public int PriceMultiplierPercent;
    }

    [ProtoContract]
    public sealed class HirePoolRequest
    {
        [ProtoMember(1)] public long BlockEntityId;
    }

    [ProtoContract]
    public sealed class HirePoolSync
    {
        [ProtoMember(1)] public long BlockEntityId;
        [ProtoMember(2)] public byte[] PoolBytes;
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
    public sealed class BulkAssignEntry
    {
        [ProtoMember(1)] public string CrewId;
        [ProtoMember(2)] public long SeatEntityId;
        [ProtoMember(3)] public long WeaponEntityId;
    }

    [ProtoContract]
    public sealed class BulkAssignRequest
    {
        [ProtoMember(1)] public long GridEntityId;
        [ProtoMember(2)] public List<BulkAssignEntry> Entries;
    }

    [ProtoContract]
    public sealed class DismissRequest
    {
        [ProtoMember(1)] public string CrewId;
        [ProtoMember(2)] public long GridEntityId;
    }

    [ProtoContract]
    public sealed class UnassignRequest
    {
        [ProtoMember(1)] public string CrewId;
    }

    [ProtoContract]
    public sealed class TrainRequest
    {
        [ProtoMember(1)] public string CrewId;
    }

    [ProtoContract]
    public sealed class CancelTrainRequest
    {
        [ProtoMember(1)] public string CrewId;
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
