using System.Collections.Generic;

namespace HireCrew
{
    public static class CrewValidation
    {
        public const string ErrorWeaponManned = "Weapon already manned";
        public const string ErrorSeatTaken = "Seat already assigned";
        public const string ErrorWrongGrid = "Seat and weapon must be on the same grid";
        public const string ErrorCrewMissing = "Crew not found";

        public static bool CanManageGrid(long requesterIdentity, long gridOwner, bool sameFaction)
        {
            if (requesterIdentity == 0) return false;
            if (requesterIdentity == gridOwner) return true;
            return sameFaction;
        }

        public static string ValidateAssign(IEnumerable<CrewRecord> existingCrewOnGrid, string crewId, long gridEntityId, long seatEntityId, long weaponEntityId)
        {
            if (string.IsNullOrEmpty(crewId)) return ErrorCrewMissing;
            if (gridEntityId == 0 || seatEntityId == 0 || weaponEntityId == 0) return ErrorWrongGrid;

            foreach (var c in existingCrewOnGrid)
            {
                if (c == null || c.Status != CrewStatus.Seated) continue;
                if (c.WeaponEntityId.HasValue && c.WeaponEntityId.Value == weaponEntityId)
                    return ErrorWeaponManned;
                if (c.SeatEntityId.HasValue && c.SeatEntityId.Value == seatEntityId)
                    return ErrorSeatTaken;
            }

            return null;
        }
    }
}
