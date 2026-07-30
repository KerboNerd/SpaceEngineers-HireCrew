using System;

namespace HireCrew
{
    /// <summary>
    /// Pure Construction shared-weld claim/hover helpers (no ModAPI).
    /// </summary>
    public static class CrewRepairShareRules
    {
        public static bool IsLargeBlock(float maxIntegrity, float shareMaxIntegrity)
        {
            return maxIntegrity >= shareMaxIntegrity;
        }

        public static int MaxClaimSlots(
            bool projected,
            float maxIntegrity,
            int shareMaxWelders,
            float shareMaxIntegrity,
            bool onlyRemainingWork)
        {
            if (projected)
                return 1;
            if (!IsLargeBlock(maxIntegrity, shareMaxIntegrity))
                return 1;
            if (onlyRemainingWork)
                return int.MaxValue;
            if (shareMaxWelders < 1)
                return 1;
            return shareMaxWelders;
        }

        public static bool IsClaimFull(int claimantsExcludingSelf, int maxSlots)
        {
            if (maxSlots == int.MaxValue)
                return false;
            return claimantsExcludingSelf >= maxSlots;
        }

        /// <summary>
        /// Lateral + slight outward bias so shared welders do not occupy one hover point.
        /// Keep spacing tight — stand-off + large lateral can exceed weld range (~5 m).
        /// Apply as: hover += sideNorm * lateral + outwardNorm * outwardBias.
        /// </summary>
        public static void SharedHoverOffsets(int slotIndex, out double lateral, out double outwardBias)
        {
            int slot = Math.Abs(slotIndex) % 7;
            lateral = (slot - 3) * 0.55;
            outwardBias = 0.1;
        }

        /// <summary>
        /// Pull a world point onto the sphere of radius <paramref name="maxDist"/> around the block.
        /// </summary>
        public static void ClampOffsetToMaxDistance(
            double blockX, double blockY, double blockZ,
            ref double x, ref double y, ref double z,
            double maxDist)
        {
            if (maxDist < 0.5)
                maxDist = 0.5;
            double dx = x - blockX;
            double dy = y - blockY;
            double dz = z - blockZ;
            double lenSq = dx * dx + dy * dy + dz * dz;
            double maxSq = maxDist * maxDist;
            if (lenSq <= maxSq || lenSq < 1e-12)
                return;
            double scale = maxDist / Math.Sqrt(lenSq);
            x = blockX + dx * scale;
            y = blockY + dy * scale;
            z = blockZ + dz * scale;
        }

        /// <summary>
        /// Squared distance from a point to an AABB (0 if inside). Used for large-block weld range.
        /// </summary>
        public static double DistanceSquaredToAabb(
            double px, double py, double pz,
            double minX, double minY, double minZ,
            double maxX, double maxY, double maxZ)
        {
            double qx = px < minX ? minX : (px > maxX ? maxX : px);
            double qy = py < minY ? minY : (py > maxY ? maxY : py);
            double qz = pz < minZ ? minZ : (pz > maxZ ? maxZ : pz);
            double dx = px - qx;
            double dy = py - qy;
            double dz = pz - qz;
            return dx * dx + dy * dy + dz * dz;
        }

        public static bool IsWithinDistanceOfAabb(
            double px, double py, double pz,
            double minX, double minY, double minZ,
            double maxX, double maxY, double maxZ,
            double maxDist)
        {
            if (maxDist < 0)
                maxDist = 0;
            return DistanceSquaredToAabb(px, py, pz, minX, minY, minZ, maxX, maxY, maxZ)
                <= maxDist * maxDist;
        }
    }
}
