using System;

namespace HireCrew
{
    /// <summary>
    /// World-level hire desk defaults and hard limits (HireCrewConfig.xml).
    /// Pure data + normalize; session code handles world-storage I/O via ModAPI XML helpers.
    /// </summary>
    public sealed class HireWorldConfig
    {
        public const string FileName = "HireCrewConfig.xml";

        public static HireWorldConfig Current { get; private set; }

        public int RefreshMinutesMin = 1;
        public int RefreshMinutesMax = 300;
        public int RefreshMinutesDefault = 15;

        public int PriceMultiplierPercentMin = 25;
        public int PriceMultiplierPercentMax = 500;
        public int PriceMultiplierPercentDefault = 100;

        public int MinCandidates = 1;
        public int MaxCandidates = 8;

        public long[] PriceByStars = { 10000, 25000, 50000, 90000, 150000, 250000 };
        public float PriceVarianceFraction = 0.15f;
        public int[] StarWeights = { 25, 25, 20, 15, 10, 5 };

        /// <summary>Bit (1 &lt;&lt; (int)CrewRole). Default = all roles.</summary>
        public int AllowedRolesMask = AllRolesMask;

        public bool RefillOnHireDefault = false;

        public static int AllRolesMask
        {
            get
            {
                int m = 0;
                for (int i = 0; i <= CrewConfig.MaxRole; i++)
                    m |= (1 << i);
                return m;
            }
        }

        public static HireWorldConfig CreateDefaults()
        {
            var cfg = new HireWorldConfig();
            cfg.Normalize();
            return cfg;
        }

        public static void SetCurrent(HireWorldConfig cfg)
        {
            Current = cfg ?? CreateDefaults();
            Current.Normalize();
        }

        public static void ClearCurrent()
        {
            Current = null;
        }

        public void Normalize()
        {
            if (RefreshMinutesMin < 1) RefreshMinutesMin = 1;
            if (RefreshMinutesMax < RefreshMinutesMin) RefreshMinutesMax = RefreshMinutesMin;
            if (RefreshMinutesDefault < RefreshMinutesMin) RefreshMinutesDefault = RefreshMinutesMin;
            if (RefreshMinutesDefault > RefreshMinutesMax) RefreshMinutesDefault = RefreshMinutesMax;

            if (PriceMultiplierPercentMin < 1) PriceMultiplierPercentMin = 1;
            if (PriceMultiplierPercentMax < PriceMultiplierPercentMin)
                PriceMultiplierPercentMax = PriceMultiplierPercentMin;
            if (PriceMultiplierPercentDefault < PriceMultiplierPercentMin)
                PriceMultiplierPercentDefault = PriceMultiplierPercentMin;
            if (PriceMultiplierPercentDefault > PriceMultiplierPercentMax)
                PriceMultiplierPercentDefault = PriceMultiplierPercentMax;

            if (MinCandidates < 1) MinCandidates = 1;
            if (MaxCandidates < MinCandidates) MaxCandidates = MinCandidates;

            PriceByStars = FixLongArray(PriceByStars, new long[] { 10000, 25000, 50000, 90000, 150000, 250000 });
            StarWeights = FixIntArray(StarWeights, new int[] { 25, 25, 20, 15, 10, 5 });
            int weightSum = 0;
            for (int i = 0; i < StarWeights.Length; i++)
            {
                if (StarWeights[i] < 0) StarWeights[i] = 0;
                weightSum += StarWeights[i];
            }
            if (weightSum <= 0)
                StarWeights = new int[] { 25, 25, 20, 15, 10, 5 };

            if (PriceVarianceFraction < 0f) PriceVarianceFraction = 0f;
            if (PriceVarianceFraction > 0.9f) PriceVarianceFraction = 0.9f;

            AllowedRolesMask &= AllRolesMask;
            if (AllowedRolesMask == 0)
                AllowedRolesMask = AllRolesMask;
        }

        public int[] GetStarWeights(StarBias bias)
        {
            var src = StarWeights ?? new int[] { 25, 25, 20, 15, 10, 5 };
            var w = new int[6];
            for (int i = 0; i < 6; i++)
            {
                int baseW = i < src.Length ? src[i] : 0;
                if (baseW < 0) baseW = 0;
                if (bias == StarBias.Low)
                    w[i] = baseW * (6 - i);
                else if (bias == StarBias.High)
                    w[i] = baseW * (i + 1);
                else
                    w[i] = baseW;
            }
            int sum = 0;
            for (int i = 0; i < w.Length; i++) sum += w[i];
            if (sum <= 0)
            {
                for (int i = 0; i < 6; i++) w[i] = 1;
            }
            return w;
        }

        public static int FirstAllowedRole(int mask)
        {
            mask &= AllRolesMask;
            if (mask == 0) mask = AllRolesMask;
            for (int i = 0; i <= CrewConfig.MaxRole; i++)
            {
                if ((mask & (1 << i)) != 0)
                    return i;
            }
            return (int)CrewRole.Gunner;
        }

        public static bool RoleAllowed(int mask, int role)
        {
            if (role < 0 || role > CrewConfig.MaxRole) return false;
            return (mask & (1 << role)) != 0;
        }

        private static long[] FixLongArray(long[] arr, long[] fallback)
        {
            if (arr == null || arr.Length != 6) return (long[])fallback.Clone();
            for (int i = 0; i < 6; i++)
                if (arr[i] < 1) arr[i] = fallback[i];
            return arr;
        }

        private static int[] FixIntArray(int[] arr, int[] fallback)
        {
            if (arr == null || arr.Length != 6) return (int[])fallback.Clone();
            return arr;
        }
    }
}
