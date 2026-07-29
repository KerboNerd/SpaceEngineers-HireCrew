using System;

namespace HireCrew
{
    /// <summary>
    /// Pure harvest SpawnBot position math (unit-tested).
    /// Prefer offset from a loaded player/grid; deep space is last resort.
    /// </summary>
    public static class CrewHarvestSpawnRules
    {
        public const double OffsetMinMeters = 2000.0;
        public const double OffsetMaxMeters = 5000.0;
        public const double DeepSpaceMinMeters = 8000000.0;
        public const double DeepSpaceSpanMeters = 4000000.0;

        public const string AnchorNone = "none";
        public const string AnchorPlayer = "player";
        public const string AnchorGrid = "grid";
        public const string AnchorDeepSpace = "deepspace";

        /// <summary>Deterministic offset from anchor for harvest dummy (variant seeds RNG).</summary>
        public static void OffsetFromAnchor(
            double ax, double ay, double az,
            int variant,
            out double x, out double y, out double z)
        {
            var rng = new Random(unchecked(variant * 9973 + 17));
            double span = OffsetMaxMeters - OffsetMinMeters;
            double r = OffsetMinMeters + rng.NextDouble() * span;
            double ang = rng.NextDouble() * Math.PI * 2.0;
            x = ax + Math.Cos(ang) * r;
            y = ay + (rng.NextDouble() - 0.5) * r * 0.2;
            z = az + Math.Sin(ang) * r;
        }

        /// <summary>Absolute deep-space fallback when no loaded anchor exists.</summary>
        public static void DeepSpaceFallback(
            int variant,
            out double x, out double y, out double z)
        {
            var rng = new Random(unchecked(variant * 9973 + 17));
            double r = DeepSpaceMinMeters + rng.NextDouble() * DeepSpaceSpanMeters;
            double ang = rng.NextDouble() * Math.PI * 2.0;
            x = Math.Cos(ang) * r;
            y = (rng.NextDouble() - 0.5) * r * 0.2;
            z = Math.Sin(ang) * r;
        }
    }
}
