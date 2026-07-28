using System.Collections.Generic;

namespace HireCrew
{
    public static class CrewValidation
    {
        public const string ErrorWeaponManned = "Weapon already manned";
        public const string ErrorSeatTaken = "Seat already assigned";
        public const string ErrorWrongGrid = "Seat and weapon must be on the same grid";
        public const string ErrorCrewMissing = "Crew not found";
        public const string ErrorNotAssigned = "Crew must be assigned first";
        public const string ErrorAmenityTaken = "Amenity already assigned";
        public const string ErrorAmenityWrongType = "Block is not that amenity type";
        public const string ErrorAmenityMissing = "Amenity block missing";
        public const string ErrorAlreadyTraining = "Already training";
        public const string ErrorMaxStars = "Already max stars";
        public const string ErrorNotTraining = "Not training";

        public static bool CanManageGrid(long requesterIdentity, long gridOwner, bool sameFaction)
        {
            if (requesterIdentity == 0) return false;
            if (requesterIdentity == gridOwner) return true;
            return sameFaction;
        }

        public static string ValidateAssign(
            IEnumerable<CrewRecord> existingCrewOnGrid,
            string crewId,
            long gridEntityId,
            long seatEntityId,
            long weaponEntityId,
            CrewRole role = CrewRole.Gunner)
        {
            if (string.IsNullOrEmpty(crewId)) return ErrorCrewMissing;
            if (gridEntityId == 0 || seatEntityId == 0) return ErrorWrongGrid;
            bool needsWeapon = CrewConfig.NeedsWeapon(role);
            if (needsWeapon && weaponEntityId == 0) return ErrorWrongGrid;

            foreach (var c in existingCrewOnGrid)
            {
                if (c == null || c.Status != CrewStatus.Seated) continue;
                if (needsWeapon &&
                    weaponEntityId != 0 &&
                    c.WeaponEntityId.HasValue &&
                    c.WeaponEntityId.Value == weaponEntityId)
                    return ErrorWeaponManned;
                if (c.SeatEntityId.HasValue && c.SeatEntityId.Value == seatEntityId)
                    return ErrorSeatTaken;
            }

            return null;
        }

        public static string ValidateAmenity(
            IEnumerable<CrewRecord> existingCrewOnGrid,
            CrewRecord crew,
            long gridEntityId,
            AmenityKind kind,
            long blockEntityId)
        {
            if (crew == null || string.IsNullOrEmpty(crew.CrewId)) return ErrorCrewMissing;
            // Grid membership is enforced by the caller (same construct as the managed grid).
            if (crew.Status != CrewStatus.Seated) return ErrorNotAssigned;

            // Clear is always allowed for seated crew.
            if (blockEntityId == 0) return null;

            foreach (var c in existingCrewOnGrid)
            {
                if (c == null || c.Status != CrewStatus.Seated) continue;
                if (string.Equals(c.CrewId, crew.CrewId, System.StringComparison.Ordinal)) continue;
                if (AmenityClaimedBy(c, blockEntityId))
                    return ErrorAmenityTaken;
            }

            return null;
        }

        public static bool AmenityClaimedBy(CrewRecord crew, long blockEntityId)
        {
            if (crew == null || blockEntityId == 0) return false;
            if (crew.BedEntityId.HasValue && crew.BedEntityId.Value == blockEntityId) return true;
            if (crew.ToiletEntityId.HasValue && crew.ToiletEntityId.Value == blockEntityId) return true;
            if (crew.ShowerEntityId.HasValue && crew.ShowerEntityId.Value == blockEntityId) return true;
            return false;
        }

        public static string ValidateTrain(CrewRecord crew)
        {
            if (crew == null || string.IsNullOrEmpty(crew.CrewId)) return ErrorCrewMissing;
            if (CrewConfig.IsTraining(crew)) return ErrorAlreadyTraining;
            if (crew.Stars >= CrewConfig.MaxStars) return ErrorMaxStars;
            return null;
        }

        public static string ValidateCancelTrain(CrewRecord crew)
        {
            if (crew == null || string.IsNullOrEmpty(crew.CrewId)) return ErrorCrewMissing;
            if (!CrewConfig.IsTraining(crew)) return ErrorNotTraining;
            return null;
        }

        public static bool TryCompleteTraining(CrewRecord crew, long utcNowTicks)
        {
            if (crew == null || crew.TrainingEndsUtcTicks <= 0) return false;
            if (utcNowTicks < crew.TrainingEndsUtcTicks) return false;
            crew.TrainingEndsUtcTicks = 0;
            crew.Stars = CrewConfig.ClampStars(crew.Stars + 1);
            return true;
        }
    }
}
