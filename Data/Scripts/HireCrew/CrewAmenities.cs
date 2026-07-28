using System;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;

namespace HireCrew
{
    public enum AmenityKind
    {
        Bed = 0,
        Toilet = 1,
        Shower = 2
    }

    public static class CrewAmenities
    {
        public static int CountAssigned(CrewRecord crew)
        {
            if (crew == null) return 0;
            int n = 0;
            if (crew.BedEntityId.HasValue && crew.BedEntityId.Value != 0) n++;
            if (crew.ToiletEntityId.HasValue && crew.ToiletEntityId.Value != 0) n++;
            if (crew.ShowerEntityId.HasValue && crew.ShowerEntityId.Value != 0) n++;
            return n;
        }

        public static float GetEfficiency(CrewRecord crew)
        {
            return CrewConfig.GetEfficiencyMultiplier(CountAssigned(crew));
        }

        public static int GetEfficiencyPercent(CrewRecord crew)
        {
            return (int)Math.Round(GetEfficiency(crew) * 100f);
        }

        public static string FormatAmenityMarks(CrewRecord crew)
        {
            if (crew == null) return "";
            var s = "";
            if (crew.BedEntityId.HasValue && crew.BedEntityId.Value != 0) s += "B";
            if (crew.ToiletEntityId.HasValue && crew.ToiletEntityId.Value != 0) s += "T";
            if (crew.ShowerEntityId.HasValue && crew.ShowerEntityId.Value != 0) s += "S";
            return s;
        }

        public static long? GetAmenity(CrewRecord crew, AmenityKind kind)
        {
            if (crew == null) return null;
            switch (kind)
            {
                case AmenityKind.Bed: return crew.BedEntityId;
                case AmenityKind.Toilet: return crew.ToiletEntityId;
                case AmenityKind.Shower: return crew.ShowerEntityId;
                default: return null;
            }
        }

        public static void SetAmenity(CrewRecord crew, AmenityKind kind, long? entityId)
        {
            if (crew == null) return;
            long? value = (entityId.HasValue && entityId.Value != 0) ? entityId : null;
            switch (kind)
            {
                case AmenityKind.Bed: crew.BedEntityId = value; break;
                case AmenityKind.Toilet: crew.ToiletEntityId = value; break;
                case AmenityKind.Shower: crew.ShowerEntityId = value; break;
            }
        }

        public static void ClearAll(CrewRecord crew)
        {
            if (crew == null) return;
            crew.BedEntityId = null;
            crew.ToiletEntityId = null;
            crew.ShowerEntityId = null;
        }

        /// <summary>
        /// Decorative Pack showers are plain CubeBlocks (not IMyTerminalBlock).
        /// Match on any fat cube block via subtype / display / custom name.
        /// </summary>
        public static bool MatchesKind(IMyCubeBlock block, AmenityKind kind)
        {
            if (block == null) return false;
            var text = BuildMatchText(block);
            switch (kind)
            {
                case AmenityKind.Bed:
                    return ContainsAny(text, "bed", "cryo");
                case AmenityKind.Toilet:
                    return ContainsAny(text, "toilet", "bathroom", "lavatory", "loo");
                case AmenityKind.Shower:
                    return ContainsAny(text, "shower", "wash");
                default:
                    return false;
            }
        }

        public static AmenityKind? DetectKind(IMyCubeBlock block)
        {
            if (block == null) return null;
            // Shower before toilet: "washroom" etc. prefer shower keywords first when both match.
            if (MatchesKind(block, AmenityKind.Shower)) return AmenityKind.Shower;
            if (MatchesKind(block, AmenityKind.Toilet)) return AmenityKind.Toilet;
            if (MatchesKind(block, AmenityKind.Bed)) return AmenityKind.Bed;
            return null;
        }

        public static string KindLabel(AmenityKind kind)
        {
            switch (kind)
            {
                case AmenityKind.Bed: return "Bed";
                case AmenityKind.Toilet: return "Toilet";
                case AmenityKind.Shower: return "Shower";
                default: return "Amenity";
            }
        }

        public static string BlockLabel(IMyCubeBlock block)
        {
            if (block == null) return "";
            var term = block as IMyTerminalBlock;
            if (term != null)
                return string.IsNullOrEmpty(term.CustomName) ? (term.DefinitionDisplayNameText ?? "") : term.CustomName;

            try
            {
                if (block.SlimBlock != null && block.SlimBlock.BlockDefinition != null)
                {
                    var name = block.SlimBlock.BlockDefinition.DisplayNameText;
                    if (!string.IsNullOrEmpty(name))
                        return name;
                }
            }
            catch
            {
            }

            try
            {
                return block.BlockDefinition.SubtypeName ?? "";
            }
            catch
            {
                return "";
            }
        }

        private static string BuildMatchText(IMyCubeBlock block)
        {
            var subtype = "";
            var display = "";
            try
            {
                subtype = block.BlockDefinition.SubtypeName ?? "";
            }
            catch
            {
                subtype = "";
            }

            try
            {
                if (block.SlimBlock != null && block.SlimBlock.BlockDefinition != null)
                {
                    if (string.IsNullOrEmpty(subtype))
                        subtype = block.SlimBlock.BlockDefinition.Id.SubtypeName ?? "";
                    display = block.SlimBlock.BlockDefinition.DisplayNameText ?? "";
                }
            }
            catch
            {
            }

            var custom = "";
            var term = block as IMyTerminalBlock;
            if (term != null)
            {
                if (string.IsNullOrEmpty(display))
                    display = term.DefinitionDisplayNameText ?? "";
                custom = term.CustomName ?? "";
            }

            return (subtype + " " + display + " " + custom).ToLowerInvariant();
        }

        private static bool ContainsAny(string text, params string[] tokens)
        {
            if (string.IsNullOrEmpty(text) || tokens == null) return false;
            for (int i = 0; i < tokens.Length; i++)
            {
                if (!string.IsNullOrEmpty(tokens[i]) && text.IndexOf(tokens[i], StringComparison.Ordinal) >= 0)
                    return true;
            }
            return false;
        }
    }
}
