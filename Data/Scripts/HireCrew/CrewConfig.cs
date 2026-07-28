using System;
using System.Collections.Generic;
using System.Text;

namespace HireCrew
{
    public static class CrewConfig
    {
        public const int MinStars = 0;
        public const int MaxStars = 5;

        private const int FallbackMinCandidates = 1;
        private const int FallbackMaxCandidates = 8;
        private const int FallbackMinRefreshMinutes = 1;
        private const int FallbackMaxRefreshMinutes = 300;
        private const int FallbackDefaultRefreshMinutes = 15;
        private const int FallbackMinPriceMultiplierPercent = 25;
        private const int FallbackMaxPriceMultiplierPercent = 500;
        private const int FallbackDefaultPriceMultiplierPercent = 100;
        private const float FallbackPriceVarianceFraction = 0.15f;

        private static readonly long[] FallbackPriceByStars = { 10000, 25000, 50000, 90000, 150000, 250000 };
        private static readonly int[] FallbackStarWeights = { 25, 25, 20, 15, 10, 5 };

        public static int MinCandidates
        {
            get
            {
                return HireWorldConfig.Current != null
                    ? HireWorldConfig.Current.MinCandidates
                    : FallbackMinCandidates;
            }
        }

        public static int MaxCandidates
        {
            get
            {
                return HireWorldConfig.Current != null
                    ? HireWorldConfig.Current.MaxCandidates
                    : FallbackMaxCandidates;
            }
        }

        public static int MinRefreshMinutes
        {
            get
            {
                return HireWorldConfig.Current != null
                    ? HireWorldConfig.Current.RefreshMinutesMin
                    : FallbackMinRefreshMinutes;
            }
        }

        public static int MaxRefreshMinutes
        {
            get
            {
                return HireWorldConfig.Current != null
                    ? HireWorldConfig.Current.RefreshMinutesMax
                    : FallbackMaxRefreshMinutes;
            }
        }

        public static int DefaultRefreshMinutes
        {
            get
            {
                return HireWorldConfig.Current != null
                    ? HireWorldConfig.Current.RefreshMinutesDefault
                    : FallbackDefaultRefreshMinutes;
            }
        }

        /// <summary>Hire desk price multiplier as percent (100 = 1.00x).</summary>
        public static int MinPriceMultiplierPercent
        {
            get
            {
                return HireWorldConfig.Current != null
                    ? HireWorldConfig.Current.PriceMultiplierPercentMin
                    : FallbackMinPriceMultiplierPercent;
            }
        }

        public static int MaxPriceMultiplierPercent
        {
            get
            {
                return HireWorldConfig.Current != null
                    ? HireWorldConfig.Current.PriceMultiplierPercentMax
                    : FallbackMaxPriceMultiplierPercent;
            }
        }

        public static int DefaultPriceMultiplierPercent
        {
            get
            {
                return HireWorldConfig.Current != null
                    ? HireWorldConfig.Current.PriceMultiplierPercentDefault
                    : FallbackDefaultPriceMultiplierPercent;
            }
        }

        public const int MaxRole = (int)CrewRole.DamageControl;

        /// <summary>Base hire prices by star rating (before per-candidate variance).</summary>
        public static long[] PriceByStars
        {
            get
            {
                return HireWorldConfig.Current != null && HireWorldConfig.Current.PriceByStars != null
                    ? HireWorldConfig.Current.PriceByStars
                    : FallbackPriceByStars;
            }
        }

        public static readonly float[] RangeByStars = { 400f, 600f, 900f, 1300f, 1800f, 2500f };

        public static readonly float[] PowerBonusByStars = { 0.01f, 0.02f, 0.03f, 0.04f, 0.05f, 0.07f };
        public static readonly float[] GyroBonusByStars = { 0.02f, 0.04f, 0.06f, 0.08f, 0.10f, 0.12f };
        public static readonly float[] ThrustBonusByStars = { 0.02f, 0.04f, 0.06f, 0.08f, 0.10f, 0.12f };
        public static readonly float[] TrainDiscountByStars = { 0.05f, 0.08f, 0.11f, 0.14f, 0.17f, 0.20f };

        /// <summary>Weighted star rolls (Balanced / world table). Sum typically 100.</summary>
        public static int[] StarWeights
        {
            get
            {
                return HireWorldConfig.Current != null && HireWorldConfig.Current.StarWeights != null
                    ? HireWorldConfig.Current.StarWeights
                    : FallbackStarWeights;
            }
        }

        /// <summary>Credit cost to train from this star to star+1 (index 0..4).</summary>
        public static readonly long[] TrainCostByStars = { 8000, 20000, 40000, 75000, 130000 };

        /// <summary>Minutes to train from this star to star+1 (index 0..4).</summary>
        public static readonly int[] TrainMinutesByStars = { 5, 10, 20, 40, 60 };

        public static float PriceVarianceFraction
        {
            get
            {
                return HireWorldConfig.Current != null
                    ? HireWorldConfig.Current.PriceVarianceFraction
                    : FallbackPriceVarianceFraction;
            }
        }

        public const float PowerMultiplierCap = 2.5f;
        public const float GyroMultiplierCap = 2.0f;
        public const float ThrustMultiplierCap = 2.0f;
        public const float TrainDiscountCap = 0.40f;
        public const float AmenityEfficiencyBonus = 0.10f;

        // Ambient walking NPCs (vanilla AstronautBehavior; nearby-only presentation layer).
        public const bool AmbientEnabled = true;
        /// <summary>Player must be within this many meters of the seat to keep a live bot.</summary>
        public const float AmbientProximityMeters = 90f;
        /// <summary>Grid linear speed above this (m/s) counts as moving — ambient bots despawn / do not spawn.</summary>
        public const float AmbientGridIdleLinearSpeedMeters = 0.35f;
        /// <summary>Grid angular speed above this (rad/s) counts as moving.</summary>
        public const float AmbientGridIdleAngularSpeedRad = 0.04f;
        /// <summary>Soft seat-neighborhood radius; beyond this, recover timer starts.</summary>
        public const float AmbientFarFromSeatMeters = 25f;
        public const int AmbientMaxLiveBotsPerGrid = 8;
        public const int AmbientMaxLiveBotsGlobal = 32;
        /// <summary>Seconds far from seat (or wrong seat) before despawn/re-seat recovery.</summary>
        public const float AmbientRecoverTimeoutSeconds = 45f;
        /// <summary>Custom survival-enabled clone; falls back to vanilla Astronaut if missing.</summary>
        public const string AmbientBotSubtype = "HireCrew_Crew";
        public const string AmbientBotSubtypeFallback = "Astronaut";
        /// <summary>Character definition used when SpawnBot fails (common in space).</summary>
        public const string AmbientCharacterSubtype = "NPC_Astronaut";
        /// <summary>
        /// Scripted AttachPilot bypasses vanilla SitInSeat(duration), so HireCrew drives
        /// sit/stand cycles. Seconds seated before RemovePilot (min/max inclusive).
        /// </summary>
        public const float AmbientSitSecondsMin = 8f;
        public const float AmbientSitSecondsMax = 16f;
        /// <summary>Seconds standing/wandering before re-seat attempt (cockpit seats).</summary>
        public const float AmbientWanderSecondsMin = 25f;
        public const float AmbientWanderSecondsMax = 55f;
        /// <summary>Seat-neighborhood walk radius (meters) for scripted wander targets.</summary>
        public const float AmbientWanderRadiusMeters = 9f;

        // Damage Control EVA repair (Phase 1).
        /// <summary>
        /// Passed into IncreaseMountLevel as welder seconds/sec (Keen multiplies by IntegrityPointsPerSec).
        /// </summary>
        /// <summary>Base Keen welder-seconds applied per real second at 0★ (scaled by GetRepairWeldMountPerSecond).</summary>
        public const float RepairWeldSecondsPerSecond = 1.5f;

        /// <summary>0★ → 0.75× base, 5★ → 1.25× base.</summary>
        public static float GetRepairWeldMountPerSecond(int stars)
        {
            return RepairWeldSecondsPerSecond * (0.75f + 0.1f * ClampStars(stars));
        }
        /// <summary>How often to scan grids for new Damage Control sorties.</summary>
        public const float RepairMissionScanSeconds = 0.35f;
        public const float RepairEvaStandOffMeters = 4.0f;
        public const float RepairEvaSpeedMeters = 9f;
        /// <summary>How quickly EVA speed can change (m/s²) — lower = softer turns.</summary>
        public const float RepairEvaAccelMeters = 6f;
        /// <summary>How quickly body yaw follows flight heading (blend rate).</summary>
        public const float RepairEvaTurnRate = 3.5f;
        public const float RepairEvaArriveMeters = 1.5f;
        /// <summary>Can weld any damaged block within this range (no need to enter the block).</summary>
        public const float RepairWeldRangeMeters = 5f;
        public const float RepairStuckSeconds = 2.2f;
        public const float RepairStuckMoveMeters = 0.2f;
        public const float RepairWaypointArriveMeters = 1.25f;
        /// <summary>Cooldown after a sortie before that welder can start another.</summary>
        public const float RepairRescanSeconds = 2.5f;
        /// <summary>Max simultaneous Damage Control EVAs on one grid (0 = unlimited).</summary>
        public const int RepairMaxParallelPerGrid = 0;
        public const float RepairNoCompAbortSeconds = 3f;
        /// <summary>Wait this long with no remaining targets before ending the sortie.</summary>
        public const float RepairNoWorkReturnSeconds = 2.5f;
        /// <summary>How often each welder may rescan for work (avoids full-grid scans every frame).</summary>
        public const float RepairAcquireThrottleSeconds = 0.5f;
        /// <summary>Shared work-target cache lifetime in frames (~60 fps).</summary>
        public const int RepairWorkCacheFrames = 30;
        public const float RepairLogicalWalkSpeedMeters = 1.2f;

        public static int ClampStars(int stars)
        {
            if (stars < MinStars) return MinStars;
            if (stars > MaxStars) return MaxStars;
            return stars;
        }

        public static CrewRole ClampRole(int roleInt)
        {
            if (roleInt < (int)CrewRole.Gunner) return CrewRole.Gunner;
            if (roleInt > MaxRole) return (CrewRole)MaxRole;
            return (CrewRole)roleInt;
        }

        public static bool NeedsWeapon(CrewRole role)
        {
            return role == CrewRole.Gunner;
        }

        public static int ClampRefreshMinutes(int minutes)
        {
            if (minutes < MinRefreshMinutes) return MinRefreshMinutes;
            if (minutes > MaxRefreshMinutes) return MaxRefreshMinutes;
            return minutes;
        }

        public static int ClampPriceMultiplierPercent(int percent)
        {
            if (percent < MinPriceMultiplierPercent) return MinPriceMultiplierPercent;
            if (percent > MaxPriceMultiplierPercent) return MaxPriceMultiplierPercent;
            return percent;
        }

        public static float PriceMultiplierFromPercent(int percent)
        {
            return ClampPriceMultiplierPercent(percent) / 100f;
        }

        public static long GetPrice(int stars)
        {
            stars = ClampStars(stars);
            var prices = PriceByStars;
            if (prices == null || prices.Length <= stars) return FallbackPriceByStars[stars];
            long p = prices[stars];
            return p < 1 ? 1 : p;
        }

        /// <summary>Cost to train from this star to star+1. Returns 0 if not trainable (e.g. max stars).</summary>
        public static long GetTrainCost(int stars)
        {
            return GetTrainCost(stars, 0f);
        }

        public static long GetTrainCost(int stars, float discountFraction)
        {
            if (stars < MinStars || stars >= MaxStars) return 0;
            long cost = TrainCostByStars[stars];
            if (discountFraction < 0f) discountFraction = 0f;
            if (discountFraction > TrainDiscountCap) discountFraction = TrainDiscountCap;
            if (discountFraction <= 0f) return cost;
            long discounted = (long)Math.Round(cost * (1.0 - discountFraction));
            if (discounted < 1) discounted = 1;
            return discounted;
        }

        /// <summary>Minutes to train from this star to star+1. Returns 0 if not trainable.</summary>
        public static int GetTrainMinutes(int stars)
        {
            if (stars < MinStars || stars >= MaxStars) return 0;
            return TrainMinutesByStars[stars];
        }

        public static bool IsTraining(CrewRecord crew)
        {
            return crew != null && crew.TrainingEndsUtcTicks > 0;
        }

        public static float GetTrackingRange(int stars)
        {
            stars = ClampStars(stars);
            return RangeByStars[stars];
        }

        public static float GetPowerBonus(int stars)
        {
            stars = ClampStars(stars);
            return PowerBonusByStars[stars];
        }

        public static float GetEfficiencyMultiplier(int amenityCount)
        {
            if (amenityCount < 0) amenityCount = 0;
            if (amenityCount > 3) amenityCount = 3;
            return 1f + AmenityEfficiencyBonus * amenityCount;
        }

        public static float GetTrackingRange(int stars, float efficiencyMultiplier)
        {
            if (efficiencyMultiplier < 1f) efficiencyMultiplier = 1f;
            return GetTrackingRange(stars) * efficiencyMultiplier;
        }

        public static float GetPowerBonus(int stars, float efficiencyMultiplier)
        {
            if (efficiencyMultiplier < 1f) efficiencyMultiplier = 1f;
            return GetPowerBonus(stars) * efficiencyMultiplier;
        }

        public static string RoleLabel(CrewRole role)
        {
            switch (role)
            {
                case CrewRole.Engineer: return "Reactor Tech";
                case CrewRole.Helmsman: return "Helmsman";
                case CrewRole.Propulsion: return "Propulsion Tech";
                case CrewRole.Quartermaster: return "Quartermaster";
                case CrewRole.DamageControl: return "Construction";
                default: return "Gunner";
            }
        }

        public static string FormatStars(int stars)
        {
            stars = ClampStars(stars);
            var sb = new StringBuilder(MaxStars);
            for (int i = 0; i < MaxStars; i++)
                sb.Append(i < stars ? '*' : '-');
            return sb.ToString();
        }

        /// <summary>Map legacy Recruit/Regular/Elite enum ordinals to stars.</summary>
        public static int StarsFromLegacyTier(int legacyTier)
        {
            switch (legacyTier)
            {
                case 0: return 1; // Recruit
                case 1: return 3; // Regular
                case 2: return 5; // Elite
                default: return ClampStars(legacyTier);
            }
        }

        public static int CountAmenities(CrewRecord crew)
        {
            if (crew == null) return 0;
            int amenities = 0;
            if (crew.BedEntityId.HasValue && crew.BedEntityId.Value != 0) amenities++;
            if (crew.ToiletEntityId.HasValue && crew.ToiletEntityId.Value != 0) amenities++;
            if (crew.ShowerEntityId.HasValue && crew.ShowerEntityId.Value != 0) amenities++;
            return amenities;
        }

        public static float GetSeatedEngineerPowerMultiplier(IEnumerable<CrewRecord> gridCrew)
        {
            return GetSeatedRoleMultiplier(gridCrew, CrewRole.Engineer);
        }

        public static float GetSeatedRoleMultiplier(IEnumerable<CrewRecord> gridCrew, CrewRole role)
        {
            float[] bonuses;
            float cap;
            if (!TryGetRoleBonusTable(role, out bonuses, out cap))
                return 1f;

            float bonus = 0f;
            if (gridCrew != null)
            {
                foreach (var crew in gridCrew)
                {
                    if (crew == null || crew.Status != CrewStatus.Seated) continue;
                    if (crew.Role != role) continue;
                    bonus += GetBonus(bonuses, crew.Stars, GetEfficiencyMultiplier(CountAmenities(crew)));
                }
            }

            float mult = 1f + bonus;
            if (mult > cap) mult = cap;
            if (mult < 1f) mult = 1f;
            return mult;
        }

        /// <summary>
        /// Soft-stacked train discount from seated Quartermasters sharing the trainee owner pool.
        /// </summary>
        public static float GetTrainDiscountFraction(IEnumerable<CrewRecord> all, long ownerKey, bool ownerIsFaction)
        {
            float remain = 1f;
            if (all != null)
            {
                foreach (var crew in all)
                {
                    if (crew == null || crew.Status != CrewStatus.Seated) continue;
                    if (crew.Role != CrewRole.Quartermaster) continue;
                    if (crew.OwnerKey != ownerKey || crew.OwnerIsFaction != ownerIsFaction) continue;
                    float contrib = GetBonus(TrainDiscountByStars, crew.Stars, GetEfficiencyMultiplier(CountAmenities(crew)));
                    if (contrib < 0f) contrib = 0f;
                    if (contrib > 1f) contrib = 1f;
                    remain *= (1f - contrib);
                }
            }

            float discount = 1f - remain;
            if (discount < 0f) discount = 0f;
            if (discount > TrainDiscountCap) discount = TrainDiscountCap;
            return discount;
        }

        private static bool TryGetRoleBonusTable(CrewRole role, out float[] bonuses, out float cap)
        {
            switch (role)
            {
                case CrewRole.Engineer:
                    bonuses = PowerBonusByStars;
                    cap = PowerMultiplierCap;
                    return true;
                case CrewRole.Helmsman:
                    bonuses = GyroBonusByStars;
                    cap = GyroMultiplierCap;
                    return true;
                case CrewRole.Propulsion:
                    bonuses = ThrustBonusByStars;
                    cap = ThrustMultiplierCap;
                    return true;
                default:
                    bonuses = null;
                    cap = 1f;
                    return false;
            }
        }

        private static float GetBonus(float[] table, int stars, float efficiencyMultiplier)
        {
            stars = ClampStars(stars);
            float bonus = table[stars];
            if (efficiencyMultiplier < 1f) efficiencyMultiplier = 1f;
            return bonus * efficiencyMultiplier;
        }
    }
}
