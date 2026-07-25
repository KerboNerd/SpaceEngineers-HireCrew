namespace HireCrew
{
    public static class CrewConfig
    {
        public const long PriceRecruit = 25000;
        public const long PriceRegular = 75000;
        public const long PriceElite = 200000;

        public const float RangeRecruit = 600f;
        public const float RangeRegular = 1200f;
        public const float RangeElite = 2500f;

        public static long GetPrice(CrewTier tier)
        {
            switch (tier)
            {
                case CrewTier.Recruit: return PriceRecruit;
                case CrewTier.Regular: return PriceRegular;
                case CrewTier.Elite: return PriceElite;
                default: return PriceRegular;
            }
        }

        public static float GetTrackingRange(CrewTier tier)
        {
            switch (tier)
            {
                case CrewTier.Recruit: return RangeRecruit;
                case CrewTier.Regular: return RangeRegular;
                case CrewTier.Elite: return RangeElite;
                default: return RangeRegular;
            }
        }
    }
}
