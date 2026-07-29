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

        /// <summary>
        /// True if candidate A should be ground before B.
        /// Leaf-first (fewer face neighbors), then nearer distance as tie-break.
        /// </summary>
        public static bool PreferGrindCandidate(
            int neighborCountA,
            double distanceSqA,
            int neighborCountB,
            double distanceSqB)
        {
            if (neighborCountA < 0) neighborCountA = 0;
            if (neighborCountB < 0) neighborCountB = 0;
            if (distanceSqA < 0) distanceSqA = 0;
            if (distanceSqB < 0) distanceSqB = 0;

            if (neighborCountA != neighborCountB)
                return neighborCountA < neighborCountB;
            return distanceSqA < distanceSqB;
        }

        /// <summary>
        /// After switching grind cells, only flip to EVA when the new block is out of range.
        /// Same-cell drift must soft-reapproach in Grinding (no EvaTransit bounce).
        /// </summary>
        public static bool NeedsEvaAfterRetarget(double distanceSq, double grindRangeMeters)
        {
            if (grindRangeMeters < 0) grindRangeMeters = 0;
            if (distanceSq < 0) distanceSq = 0;
            return distanceSq > grindRangeMeters * grindRangeMeters;
        }

        /// <summary>Inflate an axis-aligned box by <paramref name="padMeters"/> on every side.</summary>
        public static void BuildPaddedZone(
            double minX, double minY, double minZ,
            double maxX, double maxY, double maxZ,
            double padMeters,
            out double outMinX, out double outMinY, out double outMinZ,
            out double outMaxX, out double outMaxY, out double outMaxZ)
        {
            if (padMeters < 0) padMeters = 0;
            // Normalize inverted inputs.
            if (minX > maxX) { double t = minX; minX = maxX; maxX = t; }
            if (minY > maxY) { double t = minY; minY = maxY; maxY = t; }
            if (minZ > maxZ) { double t = minZ; minZ = maxZ; maxZ = t; }
            outMinX = minX - padMeters;
            outMinY = minY - padMeters;
            outMinZ = minZ - padMeters;
            outMaxX = maxX + padMeters;
            outMaxY = maxY + padMeters;
            outMaxZ = maxZ + padMeters;
        }

        public static bool IsInsideZone(
            double x, double y, double z,
            double minX, double minY, double minZ,
            double maxX, double maxY, double maxZ)
        {
            return x >= minX && x <= maxX
                && y >= minY && y <= maxY
                && z >= minZ && z <= maxZ;
        }

        public static bool IsValidZone(
            double minX, double minY, double minZ,
            double maxX, double maxY, double maxZ)
        {
            return minX <= maxX && minY <= maxY && minZ <= maxZ;
        }
    }
}
