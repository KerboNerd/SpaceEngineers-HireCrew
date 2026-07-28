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
        Quartermaster = 4,
        DamageControl = 5,
        SalvageOps = 6
    }

    public enum StarBias
    {
        Low = 0,
        Balanced = 1,
        High = 2
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
        [ProtoMember(7)] public int MinCandidates;
        [ProtoMember(8)] public int MaxCandidates;
        /// <summary>Bitmask of CrewRole; 0 = unset (resolve to world mask).</summary>
        [ProtoMember(9)] public int AllowedRoles;
        /// <summary>StarBias ordinal.</summary>
        [ProtoMember(10)] public int StarBias;
        [ProtoMember(11)] public bool RefillOnHire;
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
        [ProtoMember(4)] public int MinCandidates;
        [ProtoMember(5)] public int MaxCandidates;
        [ProtoMember(6)] public int AllowedRoles;
        [ProtoMember(7)] public int StarBias;
        [ProtoMember(8)] public bool RefillOnHire;
        [ProtoMember(9)] public bool ForceReroll;
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

    [ProtoContract]
    public sealed class AdminCommandRequest
    {
        [ProtoMember(1)] public string Verb;
        [ProtoMember(2)] public List<string> Args = new List<string>();
    }

    [ProtoContract]
    public sealed class RepairWaypoint
    {
        [ProtoMember(1)] public long BlockEntityId;
        /// <summary>Position in grid local space (fallback if block id is gone).</summary>
        [ProtoMember(2)] public double LocalX;
        [ProtoMember(3)] public double LocalY;
        [ProtoMember(4)] public double LocalZ;
    }

    [ProtoContract]
    public sealed class RepairGridPath
    {
        [ProtoMember(1)] public long GridEntityId;
        [ProtoMember(2)] public List<RepairWaypoint> Waypoints = new List<RepairWaypoint>();
        /// <summary>True when player finished path; last waypoint is the Exit.</summary>
        [ProtoMember(3)] public bool HasExit;
    }

    [ProtoContract]
    public sealed class PathEditRequest
    {
        [ProtoMember(1)] public long GridEntityId;
        /// <summary>0=Append, 1=Undo, 2=FinishExit, 3=Clear</summary>
        [ProtoMember(2)] public int Op;
        [ProtoMember(3)] public long BlockEntityId;
        [ProtoMember(4)] public double LocalX;
        [ProtoMember(5)] public double LocalY;
        [ProtoMember(6)] public double LocalZ;
    }

    public enum RepairMissionState
    {
        Idle = 0,
        WalkOut = 1,
        AtExit = 2,
        EvaTransit = 3,
        Welding = 4,
        ReturnExit = 5,
        WalkHome = 6
    }

    public static class RepairMissionHintFlags
    {
        public const int None = 0;
        public const int OutOfComps = 1;
        public const int ProjectedTarget = 2;
    }

    [ProtoContract]
    public sealed class RepairMissionSnapshotEntry
    {
        [ProtoMember(1)] public string CrewId;
        [ProtoMember(2)] public string DisplayName;
        [ProtoMember(3)] public long GridEntityId;
        [ProtoMember(4)] public int State;
        [ProtoMember(5)] public int Hints;
    }

    [ProtoContract]
    public sealed class RepairMissionSync
    {
        [ProtoMember(1)] public List<RepairMissionSnapshotEntry> Entries = new List<RepairMissionSnapshotEntry>();
    }

    [ProtoContract]
    public sealed class RepairDispatchRequest
    {
        [ProtoMember(1)] public string CrewId;
        /// <summary>false = Send this crew, true = Recall this crew.</summary>
        [ProtoMember(2)] public bool Recall;
    }

    public enum SalvageMissionState
    {
        Idle = 0,
        EvaTransit = 1,
        Grinding = 2
    }

    public static class SalvageMissionHintFlags
    {
        public const int None = 0;
        public const int CargoFull = 1;
    }

    [ProtoContract]
    public sealed class SalvageMissionSnapshotEntry
    {
        [ProtoMember(1)] public string CrewId;
        [ProtoMember(2)] public string DisplayName;
        [ProtoMember(3)] public long GridEntityId;
        [ProtoMember(4)] public int State;
        [ProtoMember(5)] public int Hints;
    }

    [ProtoContract]
    public sealed class SalvageMissionSync
    {
        [ProtoMember(1)] public List<SalvageMissionSnapshotEntry> Entries = new List<SalvageMissionSnapshotEntry>();
    }

    [ProtoContract]
    public sealed class SalvageDispatchRequest
    {
        [ProtoMember(1)] public string CrewId;
        /// <summary>false = Salvage this crew, true = Recall this crew.</summary>
        [ProtoMember(2)] public bool Recall;
        /// <summary>Target wreck/home grid. Ignored when Recall is true.</summary>
        [ProtoMember(3)] public long TargetGridEntityId;
    }
}
