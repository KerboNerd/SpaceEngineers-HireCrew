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
        /// Apply as: hover += sideNorm * lateral + outwardNorm * outwardBias.
        /// </summary>
        public static void SharedHoverOffsets(int slotIndex, out double lateral, out double outwardBias)
        {
            int slot = Math.Abs(slotIndex) % 7;
            lateral = (slot - 3) * 1.35;
            outwardBias = 0.15;
        }
    }
}
