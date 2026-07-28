using System.Collections.Generic;
using RichHudFramework.UI;
using RichHudFramework.UI.Client;
using RichHudFramework.UI.Rendering;
using VRageMath;

namespace HireCrew
{
    /// <summary>
    /// Compact left-edge status list for active Construction repair missions.
    /// </summary>
    public sealed class CrewStatusSidebar : HudElementBase
    {
        private const int MaxRows = CrewStatusHudModel.MaxVisibleRows;
        private const float PanelW = 240f;
        private const float RowH = 42f;
        private const float Pad = 8f;
        private const float TitleSize = 0.9f;
        private const float StatusSize = 0.8f;
        private const float OverflowSize = 0.75f;

        private readonly TexturedBox _bg;
        private readonly Label[] _line1;
        private readonly Label[] _line2;
        private readonly TexturedBox[] _bars;
        private readonly Label _overflow;

        public CrewStatusSidebar(HudParentBase parent) : base(parent)
        {
            Size = new Vector2(PanelW, MaxRows * RowH + Pad * 2f + 18f);
            // HudMain.Root is HudParentBase (not HudElementBase), so ParentAlignment is ignored —
            // children are positioned from screen center via Offset only.
            PlaceLeft();
            Visible = false;
            UseCursor = false;
            ShareCursor = false;

            _bg = new TexturedBox(this)
            {
                DimAlignment = DimAlignments.Both,
                Color = new Color(8, 14, 22, 160),
                ZOffset = -1
            };

            _line1 = new Label[MaxRows];
            _line2 = new Label[MaxRows];
            _bars = new TexturedBox[MaxRows];

            for (int i = 0; i < MaxRows; i++)
            {
                float y = Pad + i * RowH;
                float rowCenterY = Size.Y * 0.5f - y - RowH * 0.5f;
                _bars[i] = new TexturedBox(this)
                {
                    Size = new Vector2(3f, RowH - 6f),
                    Offset = new Vector2(-PanelW * 0.5f + 6f, rowCenterY),
                    Color = new Color(255, 220, 120),
                    Visible = false,
                    ZOffset = 1
                };

                _line1[i] = new Label(this)
                {
                    AutoResize = false,
                    Size = new Vector2(PanelW - 22f, 16f),
                    Offset = new Vector2(4f, rowCenterY + 8f),
                    Format = new GlyphFormat(new Color(255, 220, 120), TextAlignment.Left, TitleSize),
                    Visible = false,
                    ZOffset = 2
                };
                _line2[i] = new Label(this)
                {
                    AutoResize = false,
                    Size = new Vector2(PanelW - 22f, 16f),
                    Offset = new Vector2(4f, rowCenterY - 9f),
                    Format = new GlyphFormat(new Color(170, 185, 200), TextAlignment.Left, StatusSize),
                    Visible = false,
                    ZOffset = 2
                };
            }

            _overflow = new Label(this)
            {
                AutoResize = false,
                Size = new Vector2(PanelW - 16f, 16f),
                Format = new GlyphFormat(new Color(160, 170, 180), TextAlignment.Left, OverflowSize),
                Visible = false,
                ZOffset = 2
            };
        }

        public void Apply(IList<CrewStatusHudRow> rows, int overflowCount, bool visible)
        {
            if (!visible || rows == null || rows.Count == 0)
            {
                Visible = false;
                return;
            }

            Visible = true;
            PlaceLeft();

            int n = rows.Count < MaxRows ? rows.Count : MaxRows;
            float h = Pad * 2f + n * RowH + (overflowCount > 0 ? 16f : 0f);
            Size = new Vector2(PanelW, h);

            for (int i = 0; i < MaxRows; i++)
            {
                if (i >= n)
                {
                    _line1[i].Visible = false;
                    _line2[i].Visible = false;
                    _bars[i].Visible = false;
                    continue;
                }

                float y = Pad + i * RowH;
                float rowCenterY = h * 0.5f - y - RowH * 0.5f;
                var r = rows[i];

                _bars[i].Visible = true;
                _bars[i].Color = BarColor(r.State);
                _bars[i].Offset = new Vector2(-PanelW * 0.5f + 6f, rowCenterY);

                _line1[i].Visible = true;
                _line1[i].Offset = new Vector2(4f, rowCenterY + 9f);
                _line1[i].Text = r.DisplayName + " · " + r.RoleLabel;
                _line1[i].Format = new GlyphFormat(new Color(255, 220, 120), TextAlignment.Left, TitleSize);

                string line2 = r.StatusLabel;
                if (!string.IsNullOrEmpty(r.HintLabel))
                    line2 = line2 + " — " + r.HintLabel;
                _line2[i].Visible = true;
                _line2[i].Offset = new Vector2(4f, rowCenterY - 9f);
                _line2[i].Text = line2;
                _line2[i].Format = new GlyphFormat(new Color(170, 185, 200), TextAlignment.Left, StatusSize);
            }

            if (overflowCount > 0)
            {
                _overflow.Visible = true;
                _overflow.Text = "+" + overflowCount + " more";
                _overflow.Offset = new Vector2(4f, -(h * 0.5f) + Pad + 4f);
            }
            else
            {
                _overflow.Visible = false;
            }
        }

        private void PlaceLeft()
        {
            float screenW = HudMain.ScreenWidth;
            if (screenW < 1f)
                screenW = 1920f;
            // Origin is screen center; left edge, upper third (positive Y is up).
            Offset = new Vector2(-screenW * 0.5f + PanelW * 0.5f + 16f, 220f);
        }

        private static Color BarColor(int state)
        {
            switch ((RepairMissionState)state)
            {
                case RepairMissionState.Welding:
                    return new Color(255, 200, 80);
                case RepairMissionState.EvaTransit:
                    return new Color(120, 200, 255);
                case RepairMissionState.ReturnExit:
                case RepairMissionState.WalkHome:
                    return new Color(160, 180, 140);
                default:
                    return new Color(255, 220, 120);
            }
        }
    }
}
