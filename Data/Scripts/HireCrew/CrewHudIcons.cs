using RichHudFramework.UI;
using RichHudFramework.UI.Rendering;
using VRageMath;

namespace HireCrew
{
    /// <summary>Shared RichHud materials for HireCrew role/star icons (TransparentMaterials SBC).</summary>
    public static class CrewHudIcons
    {
        public const int MaxStarIcons = 5;

        // Texture pixel size must match the DDS (1024x1024 BC7).
        public static readonly Material Star = new Material("HC_Icon_Star", new Vector2(1024f));
        public static readonly Material Gunner = new Material("HC_Icon_Gunner", new Vector2(1024f));
        public static readonly Material Engineer = new Material("HC_Icon_Engineer", new Vector2(1024f));
        public static readonly Material CrewPanel = new Material("HC_Ui_CrewPanel", new Vector2(1024f));

        public static readonly Color StarOn = new Color(255, 220, 90, 255);
        /// <summary>Dim placeholder for empty star slots.</summary>
        public static readonly Color StarOff = new Color(12, 14, 18, 255);

        public static Material ForRole(CrewRole role)
        {
            if (role == CrewRole.Gunner) return Gunner;
            // Tech roles reuse Engineer art until unique icons exist.
            return Engineer;
        }

        public static TexturedBox MakeIcon(HudParentBase parent, Material mat, float size = 28f)
        {
            return new TexturedBox(parent)
            {
                Size = new Vector2(size, size),
                Material = mat,
                MatAlignment = MaterialAlignment.FitAuto,
                Color = Color.White,
                ZOffset = 3,
            };
        }

        public static TexturedBox[] MakeStarRow(HudParentBase parent, float size = 12f)
        {
            var boxes = new TexturedBox[MaxStarIcons];
            for (int i = 0; i < MaxStarIcons; i++)
            {
                boxes[i] = MakeIcon(parent, Star, size);
                boxes[i].Visible = false;
                boxes[i].ZOffset = 4;
            }
            return boxes;
        }

        public static float StarRowWidth(float size, float gap)
        {
            return MaxStarIcons * size + (MaxStarIcons - 1) * gap;
        }

        /// <summary>Place up to 5 stars; lit count = filled, rest dimmed. leftX is left edge of the strip.</summary>
        public static void LayoutStars(TexturedBox[] stars, int filled, float leftX, float centerY, float size, float gap, bool visible)
        {
            if (stars == null) return;
            filled = CrewConfig.ClampStars(filled);
            for (int i = 0; i < MaxStarIcons && i < stars.Length; i++)
            {
                var box = stars[i];
                if (box == null) continue;
                if (!visible)
                {
                    box.Visible = false;
                    continue;
                }
                box.Size = new Vector2(size, size);
                box.Color = i < filled ? StarOn : StarOff;
                box.Offset = new Vector2(leftX + size * 0.5f + i * (size + gap), centerY);
                box.Visible = true;
            }
        }

        public static void HideStars(TexturedBox[] stars)
        {
            if (stars == null) return;
            for (int i = 0; i < stars.Length; i++)
            {
                if (stars[i] != null)
                    stars[i].Visible = false;
            }
        }
    }
}
