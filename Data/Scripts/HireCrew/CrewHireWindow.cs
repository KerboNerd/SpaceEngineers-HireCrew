using System;
using RichHudFramework.UI;
using RichHudFramework.UI.Client;
using Sandbox.ModAPI;
using VRageMath;

namespace HireCrew
{
    /// <summary>Larger RichHud desk UI: sticky candidate pool for one hire block.</summary>
    public sealed class CrewHireWindow : HudElementBase
    {
        private const int MaxRows = 8;
        private const float RowH = 44f;
        private const float PanelW = 720f;
        private const float PanelH = 640f;
        private const float RowTopY = -78f;

        private bool _built;
        private LabelBox _header;
        private Label _status;
        private readonly CrewHudButton[] _rows = new CrewHudButton[MaxRows];
        private readonly TexturedBox[] _rowRoleIcons = new TexturedBox[MaxRows];
        private readonly TexturedBox[][] _rowStarIcons = new TexturedBox[MaxRows][];
        private readonly Label[] _rowRoleLabels = new Label[MaxRows];
        private readonly Label[] _rowPriceLabels = new Label[MaxRows];
        private readonly string[] _rowCandidateIds = new string[MaxRows];
        private CrewHudButton _btnHire;
        private CrewHudButton _btnClose;

        private long _blockEntityId;
        private HireBlockPool _pool;
        private string _selectedCandidateId;
        private int _refreshCooldown;

        public event Action CloseRequested;

        public bool IsOpen { get { return Visible; } }

        public CrewHireWindow(HudParentBase parent) : base(parent)
        {
            Size = new Vector2(PanelW, PanelH);
            Offset = new Vector2(0f, 20f);
            Visible = false;
            UseCursor = false;
            ShareCursor = true;
        }

        public void Show(long blockEntityId, HireBlockPool pool)
        {
            EnsureBuilt();
            _blockEntityId = blockEntityId;
            _pool = pool;
            _selectedCandidateId = null;
            Visible = true;
            HudMain.EnableCursor = true;
            Refresh();
        }

        public void Hide()
        {
            Visible = false;
            _pool = null;
            _selectedCandidateId = null;
        }

        public void ApplyPool(HireBlockPool pool)
        {
            if (pool == null) return;
            if (_blockEntityId != 0 && pool.BlockEntityId != _blockEntityId) return;
            _pool = pool;
            if (!string.IsNullOrEmpty(_selectedCandidateId))
            {
                bool stillThere = false;
                if (pool.Candidates != null)
                {
                    for (int i = 0; i < pool.Candidates.Count; i++)
                    {
                        var c = pool.Candidates[i];
                        if (c != null && c.CandidateId == _selectedCandidateId)
                        {
                            stillThere = true;
                            break;
                        }
                    }
                }
                if (!stillThere) _selectedCandidateId = null;
            }
            if (Visible)
                Refresh();
        }

        public void UpdateOpen()
        {
            if (!Visible) return;
            _refreshCooldown++;
            if (_refreshCooldown >= 30)
            {
                _refreshCooldown = 0;
                Refresh();
            }
        }

        private void EnsureBuilt()
        {
            if (_built) return;
            _built = true;

            new TexturedBox(this)
            {
                DimAlignment = DimAlignments.Both,
                Color = new Color(14, 20, 28, 240),
                ZOffset = -2,
            };
            new BorderBox(this)
            {
                DimAlignment = DimAlignments.Both,
                Color = new Color(70, 110, 150, 220),
                Thickness = 2f,
                ZOffset = -1,
            };

            _header = new LabelBox(this)
            {
                ParentAlignment = ParentAlignments.Top | ParentAlignments.InnerH | ParentAlignments.InnerV,
                Offset = new Vector2(0f, -8f),
                Size = new Vector2(PanelW - 20f, 40f),
                AutoResize = false,
                Color = new Color(40, 70, 100, 255),
                Format = GlyphFormat.White.WithAlignment(TextAlignment.Center),
                Text = "CREW HIRING DESK",
                ZOffset = 2,
            };

            _status = new Label(this)
            {
                ParentAlignment = ParentAlignments.Top | ParentAlignments.InnerH | ParentAlignments.InnerV,
                Offset = new Vector2(0f, -52f),
                Size = new Vector2(PanelW - 32f, 22f),
                AutoResize = false,
                Format = GlyphFormat.Blueish.WithAlignment(TextAlignment.Center),
                Text = "",
                ZOffset = 2,
            };

            float rowWidth = PanelW - 40f;
            for (int i = 0; i < MaxRows; i++)
            {
                int idx = i;
                var btn = new CrewHudButton(this)
                {
                    ParentAlignment = ParentAlignments.Top | ParentAlignments.InnerH | ParentAlignments.InnerV,
                    FitToTextElement = false,
                    Size = new Vector2(rowWidth, RowH - 4f),
                    BaseColor = new Color(34, 44, 58, 255),
                    HighlightColor = new Color(55, 80, 110, 255),
                    Format = GlyphFormat.White.WithAlignment(TextAlignment.Left),
                    Visible = false,
                    ZOffset = 2,
                };
                btn.MouseInput.LeftReleased += (s, a) => OnRowClicked(idx);
                _rows[i] = btn;

                var roleIcon = CrewHudIcons.MakeIcon(this, CrewHudIcons.Gunner, 26f);
                roleIcon.ParentAlignment = ParentAlignments.Top | ParentAlignments.InnerH | ParentAlignments.InnerV;
                roleIcon.Visible = false;
                roleIcon.ZOffset = 4;
                _rowRoleIcons[i] = roleIcon;

                var starRow = CrewHudIcons.MakeStarRow(this, 14f);
                for (int s = 0; s < starRow.Length; s++)
                    starRow[s].ParentAlignment = ParentAlignments.Top | ParentAlignments.InnerH | ParentAlignments.InnerV;
                _rowStarIcons[i] = starRow;

                _rowRoleLabels[i] = new Label(this)
                {
                    ParentAlignment = ParentAlignments.Top | ParentAlignments.InnerH | ParentAlignments.InnerV,
                    AutoResize = false,
                    Size = new Vector2(140f, RowH - 10f),
                    Format = GlyphFormat.White.WithAlignment(TextAlignment.Center),
                    VertCenterText = true,
                    Visible = false,
                    ZOffset = 4,
                };
                _rowPriceLabels[i] = new Label(this)
                {
                    ParentAlignment = ParentAlignments.Top | ParentAlignments.InnerH | ParentAlignments.InnerV,
                    AutoResize = false,
                    Size = new Vector2(160f, RowH - 10f),
                    Format = GlyphFormat.White.WithAlignment(TextAlignment.Right),
                    VertCenterText = true,
                    Visible = false,
                    ZOffset = 4,
                };
            }

            _btnHire = new CrewHudButton(this)
            {
                ParentAlignment = ParentAlignments.Bottom | ParentAlignments.InnerH | ParentAlignments.InnerV,
                FitToTextElement = false,
                Size = new Vector2(220f, 42f),
                Offset = new Vector2(-130f, 16f),
                BaseColor = new Color(40, 90, 55, 255),
                HighlightColor = new Color(60, 130, 80, 255),
                Format = GlyphFormat.White.WithAlignment(TextAlignment.Center),
                Text = "Hire selected",
                ZOffset = 2,
            };
            _btnHire.MouseInput.LeftReleased += (s, a) => HireSelected();

            _btnClose = new CrewHudButton(this)
            {
                ParentAlignment = ParentAlignments.Bottom | ParentAlignments.InnerH | ParentAlignments.InnerV,
                FitToTextElement = false,
                Size = new Vector2(220f, 42f),
                Offset = new Vector2(130f, 16f),
                BaseColor = new Color(70, 40, 40, 255),
                HighlightColor = new Color(110, 60, 60, 255),
                Format = GlyphFormat.White.WithAlignment(TextAlignment.Center),
                Text = "Close",
                ZOffset = 2,
            };
            _btnClose.MouseInput.LeftReleased += (s, a) =>
            {
                if (CloseRequested != null) CloseRequested();
            };
        }

        private void OnRowClicked(int idx)
        {
            if (idx < 0 || idx >= MaxRows) return;
            var id = _rowCandidateIds[idx];
            if (string.IsNullOrEmpty(id)) return;
            _selectedCandidateId = id;
            Refresh();
        }

        private void HireSelected()
        {
            if (string.IsNullOrEmpty(_selectedCandidateId) || _blockEntityId == 0)
            {
                MyAPIGateway.Utilities.ShowMessage("HireCrew", "Select a candidate");
                return;
            }
            var session = CrewSession.Instance;
            if (session == null) return;
            session.ClientRequestHireFromPool(_blockEntityId, _selectedCandidateId);
        }

        public void Refresh()
        {
            EnsureBuilt();
            if (!Visible) return;

            string refreshText = "";
            if (_pool != null && _pool.NextRefreshUtcTicks > 0)
            {
                var remaining = new TimeSpan(_pool.NextRefreshUtcTicks - DateTime.UtcNow.Ticks);
                if (remaining.TotalSeconds < 0) remaining = TimeSpan.Zero;
                refreshText = "Refresh in " + FormatRemaining(remaining)
                    + "  |  interval " + CrewConfig.ClampRefreshMinutes(_pool.RefreshMinutes) + "m";
            }

            int multPct = _pool != null && _pool.PriceMultiplierPercent > 0
                ? CrewConfig.ClampPriceMultiplierPercent(_pool.PriceMultiplierPercent)
                : CrewConfig.DefaultPriceMultiplierPercent;
            string multText = "price " + (multPct / 100f).ToString("0.00") + "x";

            string biasText = _pool != null ? ((StarBias)_pool.StarBias).ToString() : "Balanced";
            string refillText = _pool != null && _pool.RefillOnHire ? "refill on" : "refill off";
            string deskText = biasText + "  |  " + refillText;

            int count = _pool != null && _pool.Candidates != null ? _pool.Candidates.Count : 0;
            _status.Text = count == 0
                ? "No candidates available — wait for refresh  |  " + multText + "  |  " + deskText
                : count + " available  |  " + refreshText + "  |  " + multText + "  |  " + deskText;

            float rowWidth = PanelW - 40f;
            const float roleSize = 26f;
            const float starSize = 14f;
            const float starGap = 2f;
            const float leftPad = 16f;
            const float gap = 8f;
            const float roleColW = 140f;
            const float priceColW = 160f;
            const float nameColW = 220f;
            for (int i = 0; i < MaxRows; i++)
            {
                var btn = _rows[i];
                var roleIcon = _rowRoleIcons[i];
                var starRow = _rowStarIcons[i];
                var roleLabel = _rowRoleLabels[i];
                var priceLabel = _rowPriceLabels[i];
                _rowCandidateIds[i] = null;
                if (i >= count)
                {
                    btn.Visible = false;
                    btn.SetInteractive(false);
                    if (roleIcon != null) roleIcon.Visible = false;
                    CrewHudIcons.HideStars(starRow);
                    if (roleLabel != null) roleLabel.Visible = false;
                    if (priceLabel != null) priceLabel.Visible = false;
                    continue;
                }

                var c = _pool.Candidates[i];
                bool selected = c != null && c.CandidateId == _selectedCandidateId;
                float rowY = RowTopY - i * RowH;
                int starCount = c != null ? CrewConfig.ClampStars(c.Stars) : 0;
                float starStripW = CrewHudIcons.StarRowWidth(starSize, starGap);
                float leftReserve = leftPad + roleSize + gap + starStripW + gap;
                float labelH = RowH - 10f;
                float rowBodyH = RowH - 4f;
                float labelY = rowY - (rowBodyH - labelH) * 0.5f;

                btn.Visible = true;
                btn.SetInteractive(true);
                btn.Offset = new Vector2(0f, rowY);
                btn.BaseColor = selected ? new Color(50, 85, 120, 255) : new Color(34, 44, 58, 255);
                btn.HighlightColor = new Color(70, 105, 140, 255);
                btn.Format = GlyphFormat.White.WithAlignment(TextAlignment.Left);
                btn.TextPadding = new Vector2(8f, 4f);
                btn.TextSize = new Vector2(nameColW, labelH);
                btn.textElement.ParentAlignment = ParentAlignments.Left | ParentAlignments.InnerH | ParentAlignments.InnerV;
                btn.textElement.Offset = new Vector2(leftReserve, 0f);
                btn.VertCenterText = true;
                _rowCandidateIds[i] = c != null ? c.CandidateId : null;
                btn.SetTextIfChanged(c != null ? (c.FullName ?? "") : "");

                if (c != null)
                {
                    float iconY = rowY - (rowBodyH - roleSize) * 0.5f;
                    float roleCenterX = -(rowWidth * 0.5f) + leftPad + roleSize * 0.5f;
                    if (roleIcon != null)
                    {
                        roleIcon.Material = CrewHudIcons.ForRole((CrewRole)c.Role);
                        roleIcon.Offset = new Vector2(roleCenterX, iconY);
                        roleIcon.Visible = true;
                    }
                    float starY = rowY - (rowBodyH - starSize) * 0.5f;
                    float starLeft = -(rowWidth * 0.5f) + leftPad + roleSize + gap;
                    CrewHudIcons.LayoutStars(starRow, starCount, starLeft, starY, starSize, starGap, true);

                    if (roleLabel != null)
                    {
                        roleLabel.Text = CrewConfig.RoleLabel((CrewRole)c.Role);
                        roleLabel.VertCenterText = true;
                        roleLabel.Offset = new Vector2(0f, labelY);
                        roleLabel.Size = new Vector2(roleColW, labelH);
                        roleLabel.Visible = true;
                    }
                    if (priceLabel != null)
                    {
                        priceLabel.Text = c.Price.ToString("N0") + " sc";
                        // Right-aligned column: center of label sits near row's right edge.
                        float priceCenterX = (rowWidth * 0.5f) - 16f - priceColW * 0.5f;
                        priceLabel.VertCenterText = true;
                        priceLabel.Offset = new Vector2(priceCenterX, labelY);
                        priceLabel.Size = new Vector2(priceColW, labelH);
                        priceLabel.Visible = true;
                    }
                }
                else
                {
                    if (roleIcon != null) roleIcon.Visible = false;
                    CrewHudIcons.HideStars(starRow);
                    if (roleLabel != null) roleLabel.Visible = false;
                    if (priceLabel != null) priceLabel.Visible = false;
                }
            }

            _btnHire.SetInteractive(true);
            _btnClose.SetInteractive(true);
        }

        private static string FormatRemaining(TimeSpan t)
        {
            if (t.TotalHours >= 1)
                return ((int)t.TotalHours) + "h " + t.Minutes + "m";
            if (t.TotalMinutes >= 1)
                return t.Minutes + "m " + t.Seconds + "s";
            return t.Seconds + "s";
        }
    }
}
