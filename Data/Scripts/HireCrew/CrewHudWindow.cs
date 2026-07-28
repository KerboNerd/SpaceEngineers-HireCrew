using System;
using System.Collections.Generic;
using RichHudFramework.UI;
using RichHudFramework.UI.Client;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRageMath;

namespace HireCrew
{
    /// <summary>
    /// LabelBoxButton highlight is enter/exit Color swaps; Refresh() overwrites Color and kills hover.
    /// Re-apply BaseColor / HighlightColor every layout instead.
    /// </summary>
    internal sealed class CrewHudButton : LabelBoxButton
    {
        public Color BaseColor;
        private string _lastText;

        public CrewHudButton(HudParentBase parent) : base(parent)
        {
            HighlightEnabled = false;
            BaseColor = Color.DarkGray;
        }

        public void SetTextIfChanged(string text)
        {
            if (text == null) text = "";
            if (_lastText == text) return;
            _lastText = text;
            Text = text;
        }

        public void SetInteractive(bool interactive)
        {
            UseCursor = interactive;
            ShareCursor = interactive;
            if (_mouseInput != null)
            {
                _mouseInput.UseCursor = interactive;
                _mouseInput.ShareCursor = interactive;
            }
        }

        protected override void Layout()
        {
            base.Layout();
            bool hover = UseCursor && MouseInput != null && MouseInput.IsMousedOver;
            Color = hover ? HighlightColor : BaseColor;
        }
    }

    /// <summary>
    /// Rich HUD panel for /crew. Avoids ListBox (it rewrites Size every frame and covers siblings).
    /// </summary>
    public sealed class CrewHudWindow : HudElementBase
    {
        private const int MaxRows = 5;
        private const float RowH = 56f;
        private const float RowGap = 6f;
        private const float PanelW = 580f;
        private const float PanelH = 540f;
        private const float MoraleBarW = 52f;
        private const float MoraleBarH = 16f;
        private const float RowTopY = -64f;
        private const float CardLineH = 20f;
        private const float CardPadTop = 6f;

        private static float RowYAt(int index)
        {
            return RowTopY - index * (RowH + RowGap);
        }

        private readonly CrewHudModel _model;
        private bool _built;

        private TexturedBox _bg;
        private LabelBox _header;
        private Label _status;
        private Label _context;
        private readonly CrewHudButton[] _rows = new CrewHudButton[MaxRows];
        private readonly TexturedBox[] _rowRoleIcons = new TexturedBox[MaxRows];
        private readonly TexturedBox[][] _rowStarIcons = new TexturedBox[MaxRows][];
        private readonly Label[] _rowNameLabels = new Label[MaxRows];
        private readonly Label[] _rowSpecLabels = new Label[MaxRows];
        private readonly Label[] _rowDetailLabels = new Label[MaxRows];
        private readonly TexturedBox[] _moraleTracks = new TexturedBox[MaxRows];
        private readonly TexturedBox[] _moraleFills = new TexturedBox[MaxRows];
        private readonly Label[] _moraleLabels = new Label[MaxRows];
        private readonly string[] _rowCrewIds = new string[MaxRows];
        private readonly long[] _rowEntityIds = new long[MaxRows];
        private int _rowCount;
        private int _listTotalCount;

        private CrewHudButton _btnAssign;
        private CrewHudButton _btnUnassign;
        private CrewHudButton _btnQuarters;
        private CrewHudButton _btnTrain;
        private CrewHudButton _btnDismiss;
        private CrewHudButton _btnBulk;
        private CrewHudButton _btnBulkAssign;
        private CrewHudButton _btnClearBulk;
        private CrewHudButton _btnBulkSeat;
        private CrewHudButton _btnBulkWeapon;
        private CrewHudButton _btnNext;
        private CrewHudButton _btnConfirm;
        private CrewHudButton _btnBack;
        private CrewHudButton _btnCancel;
        private CrewHudButton _btnClose;

        private readonly List<IMySlimBlock> _blockScratch = new List<IMySlimBlock>(64);
        private readonly List<IMyTerminalBlock> _seatScratch = new List<IMyTerminalBlock>(32);
        private readonly List<IMyTerminalBlock> _weaponScratch = new List<IMyTerminalBlock>(32);
        private readonly List<IMyCubeBlock> _amenityScratch = new List<IMyCubeBlock>(32);
        private readonly AmenityKind[] _slotKinds = { AmenityKind.Bed, AmenityKind.Toilet, AmenityKind.Shower };

        public event Action CloseRequested;

        public CrewHudWindow(HudParentBase parent, CrewHudModel model) : base(parent)
        {
            _model = model;
            Size = new Vector2(PanelW, PanelH);
            Offset = new Vector2(0f, 30f);
            Visible = false;
            // Panel itself must not capture cursor; buttons own hit-testing.
            UseCursor = false;
            ShareCursor = true;
        }

        private void EnsureBuilt()
        {
            if (_built) return;

            _bg = new TexturedBox(this)
            {
                DimAlignment = DimAlignments.Both,
                Color = new Color(16, 22, 30, 235),
                ZOffset = -2,
            };
            new BorderBox(this)
            {
                DimAlignment = DimAlignments.Both,
                Color = new Color(100, 160, 200, 255),
                Thickness = 2f,
                ZOffset = -1,
            };

            // Top|InnerV pins the child's top to the panel top; negative Y offset moves down.
            _header = new LabelBox(this)
            {
                ParentAlignment = ParentAlignments.Top | ParentAlignments.InnerH | ParentAlignments.InnerV,
                Offset = new Vector2(0f, -4f),
                Size = new Vector2(PanelW - 12f, 34f),
                AutoResize = false,
                Color = new Color(55, 100, 140, 255),
                Format = GlyphFormat.White.WithAlignment(TextAlignment.Center),
                Text = "Crew",
                ZOffset = 2,
            };

            _status = new Label(this)
            {
                ParentAlignment = ParentAlignments.Top | ParentAlignments.InnerH | ParentAlignments.InnerV,
                Offset = new Vector2(0f, -38f),
                Size = new Vector2(PanelW - 24f, 18f),
                AutoResize = false,
                Format = GlyphFormat.Blueish.WithAlignment(TextAlignment.Center),
                Text = "",
                ZOffset = 2,
            };

            _context = new Label(this)
            {
                ParentAlignment = ParentAlignments.Bottom | ParentAlignments.InnerH | ParentAlignments.InnerV,
                Offset = new Vector2(0f, 56f),
                Size = new Vector2(PanelW - 24f, 22f),
                AutoResize = false,
                Format = GlyphFormat.Blueish.WithAlignment(TextAlignment.Center),
                Text = "",
                ZOffset = 2,
                Visible = false,
            };

            // Below header + tight status line + gap.
            // Morale bars are window children (not nested in rows) so LabelBox sizing cannot blow them up.
            float rowWidth = PanelW - 28f;
            float barCenterX = rowWidth * 0.5f - 12f - MoraleBarW * 0.5f;
            for (int i = 0; i < MaxRows; i++)
            {
                int idx = i;
                float rowY = RowYAt(i);
                float barY = rowY - CardPadTop - CardLineH - 2f - (CardLineH - MoraleBarH) * 0.5f;
                var row = MakeBtn("", rowY, 0f);
                row.FitToTextElement = false;
                row.Size = new Vector2(rowWidth, RowH);
                row.BaseColor = new Color(35, 48, 62, 255);
                row.Color = row.BaseColor;
                row.HighlightColor = new Color(70, 110, 150, 255);
                row.Visible = false;
                row.MouseInput.LeftReleased += (s, a) => OnRowClicked(idx);
                _rows[i] = row;

                var roleIcon = CrewHudIcons.MakeIcon(this, CrewHudIcons.Gunner, 22f);
                roleIcon.ParentAlignment = ParentAlignments.Top | ParentAlignments.InnerH | ParentAlignments.InnerV;
                roleIcon.Visible = false;
                roleIcon.ZOffset = 4;
                _rowRoleIcons[i] = roleIcon;

                var starRow = CrewHudIcons.MakeStarRow(this, 12f);
                for (int s = 0; s < starRow.Length; s++)
                    starRow[s].ParentAlignment = ParentAlignments.Top | ParentAlignments.InnerH | ParentAlignments.InnerV;
                _rowStarIcons[i] = starRow;

                _rowNameLabels[i] = MakeColumnLabel(150f, TextAlignment.Left);
                _rowSpecLabels[i] = MakeColumnLabel(110f, TextAlignment.Center);
                _rowDetailLabels[i] = MakeColumnLabel(200f, TextAlignment.Right);

                _moraleTracks[i] = new TexturedBox(this)
                {
                    ParentAlignment = ParentAlignments.Top | ParentAlignments.InnerH | ParentAlignments.InnerV,
                    Offset = new Vector2(barCenterX, barY),
                    Size = new Vector2(MoraleBarW, MoraleBarH),
                    Color = new Color(20, 28, 36, 255),
                    Visible = false,
                    ZOffset = 4,
                };

                _moraleFills[i] = new TexturedBox(this)
                {
                    ParentAlignment = ParentAlignments.Top | ParentAlignments.InnerH | ParentAlignments.InnerV,
                    Offset = new Vector2(barCenterX, barY),
                    Size = new Vector2(0f, MoraleBarH),
                    Color = new Color(80, 160, 100, 255),
                    Visible = false,
                    ZOffset = 5,
                };

                _moraleLabels[i] = new Label(this)
                {
                    ParentAlignment = ParentAlignments.Top | ParentAlignments.InnerH | ParentAlignments.InnerV,
                    Offset = new Vector2(barCenterX, barY),
                    Size = new Vector2(MoraleBarW, MoraleBarH),
                    AutoResize = false,
                    Format = GlyphFormat.White.WithAlignment(TextAlignment.Center),
                    Text = "",
                    Visible = false,
                    ZOffset = 6,
                };
            }

            _btnAssign = MakeBtn("Assign", 0f, -155f, true);
            _btnUnassign = MakeBtn("Unassign", 0f, -55f, true);
            _btnQuarters = MakeBtn("Quarters", 0f, 55f, true);
            _btnTrain = MakeBtn("Train", 0f, 100f, true);
            _btnDismiss = MakeBtn("Dismiss", 0f, 155f, true);
            _btnBulk = MakeBtn("Bulk", 0f, 0f, true);
            _btnBulkAssign = MakeBtn("Bulk Assign", 0f, 0f, true);
            _btnClearBulk = MakeBtn("Clear", 0f, 0f, true);
            _btnBulkSeat = MakeBtn("Seat", 0f, 0f, true);
            _btnBulkWeapon = MakeBtn("Weapon", 0f, 0f, true);
            _btnNext = MakeBtn("Next", 0f, -155f, true);
            _btnConfirm = MakeBtn("Confirm", 0f, -155f, true);
            _btnBack = MakeBtn("Back", 0f, 0f, true);
            _btnCancel = MakeBtn("Cancel", 0f, 155f, true);
            _btnClose = MakeBtn("Close", 0f, 165f, true);

            PlaceBottom(_btnAssign, -230f, 72f);
            PlaceBottom(_btnUnassign, -153f, 72f);
            PlaceBottom(_btnQuarters, -76f, 72f);
            PlaceBottom(_btnTrain, 0f, 72f);
            PlaceBottom(_btnDismiss, 76f, 72f);
            PlaceBottom(_btnBulk, 153f, 72f);
            PlaceBottom(_btnClose, 230f, 72f);
            PlaceBottom(_btnBulkAssign, -153f, 100f);
            PlaceBottom(_btnClearBulk, 0f, 88f);
            PlaceBottom(_btnBulkSeat, -200f, 88f);
            PlaceBottom(_btnBulkWeapon, -100f, 88f);
            PlaceBottom(_btnNext, -155f);
            PlaceBottom(_btnConfirm, -155f);
            PlaceBottom(_btnBack, 0f);
            PlaceBottom(_btnCancel, 155f);

            _btnAssign.MouseInput.LeftReleased += (s, a) => BeginAssign();
            _btnUnassign.MouseInput.LeftReleased += (s, a) => BeginUnassign();
            _btnQuarters.MouseInput.LeftReleased += (s, a) => BeginQuarters();
            _btnTrain.MouseInput.LeftReleased += (s, a) => BeginTrainOrCancel();
            _btnDismiss.MouseInput.LeftReleased += (s, a) => Dismiss();
            _btnBulk.MouseInput.LeftReleased += (s, a) => ToggleBulkMode();
            _btnBulkAssign.MouseInput.LeftReleased += (s, a) => BeginBulkAssign();
            _btnClearBulk.MouseInput.LeftReleased += (s, a) => ClearBulkSelection();
            _btnBulkSeat.MouseInput.LeftReleased += (s, a) => BeginBulkSeatPick();
            _btnBulkWeapon.MouseInput.LeftReleased += (s, a) => BeginBulkWeaponPick();
            _btnNext.MouseInput.LeftReleased += (s, a) => WizardNext();
            _btnConfirm.MouseInput.LeftReleased += (s, a) => WizardConfirm();
            _btnBack.MouseInput.LeftReleased += (s, a) => { _model.WizardBack(); Refresh(); };
            _btnCancel.MouseInput.LeftReleased += (s, a) => RequestClose();
            _btnClose.MouseInput.LeftReleased += (s, a) => RequestClose();

            _built = true;
        }

        private Label MakeColumnLabel(float width, TextAlignment align)
        {
            return new Label(this)
            {
                ParentAlignment = ParentAlignments.Top | ParentAlignments.InnerH | ParentAlignments.InnerV,
                AutoResize = false,
                Size = new Vector2(width, RowH - 8f),
                Format = GlyphFormat.White.WithAlignment(align),
                VertCenterText = true,
                Visible = false,
                ZOffset = 4,
            };
        }

        private CrewHudButton MakeBtn(string text, float y, float x, bool bottom = false)
        {
            var baseColor = new Color(50, 80, 110, 255);
            var btn = new CrewHudButton(this)
            {
                ParentAlignment = bottom
                    ? (ParentAlignments.Bottom | ParentAlignments.InnerH | ParentAlignments.InnerV)
                    : (ParentAlignments.Top | ParentAlignments.InnerH | ParentAlignments.InnerV),
                Offset = new Vector2(x, y),
                Size = new Vector2(140f, 36f),
                AutoResize = false,
                // Keep true for action buttons so Width/Height size the text board (labels visible).
                FitToTextElement = true,
                BaseColor = baseColor,
                Color = baseColor,
                HighlightColor = new Color(90, 140, 190, 255),
                Format = GlyphFormat.White.WithAlignment(TextAlignment.Center),
                UseCursor = true,
                ShareCursor = true,
                ZOffset = 1,
            };
            btn.SetTextIfChanged(text ?? "");
            // Re-apply size after text so FitToTextElement populates TextSize.
            btn.Size = new Vector2(140f, 36f);
            return btn;
        }

        private static void PlaceBottom(CrewHudButton btn, float x, float width = 140f)
        {
            // Bottom without InnerV aligns the child's top to the parent's bottom (outside).
            btn.ParentAlignment = ParentAlignments.Bottom | ParentAlignments.InnerH | ParentAlignments.InnerV;
            btn.Offset = new Vector2(x, 12f);
            btn.FitToTextElement = true;
            btn.Size = new Vector2(width, 36f);
            btn.TextSize = new Vector2(width - 8f, 28f);
        }

        private void RequestClose()
        {
            if (CloseRequested != null)
                CloseRequested();
        }

        protected override void HandleInput(Vector2 cursorPos)
        {
            base.HandleInput(cursorPos);
            if (!Visible) return;

            bool scrolled = false;
            if (SharedBinds.PageUp.IsNewPressed)
            {
                _model.AdjustListScroll(-MaxRows, _listTotalCount, MaxRows);
                scrolled = true;
            }
            else if (SharedBinds.PageDown.IsNewPressed)
            {
                _model.AdjustListScroll(MaxRows, _listTotalCount, MaxRows);
                scrolled = true;
            }
            else if ((State & HudElementStates.IsMouseInBounds) > 0)
            {
                if (SharedBinds.MousewheelUp.IsPressed)
                {
                    _model.AdjustListScroll(-1, _listTotalCount, MaxRows);
                    scrolled = true;
                }
                else if (SharedBinds.MousewheelDown.IsPressed)
                {
                    _model.AdjustListScroll(1, _listTotalCount, MaxRows);
                    scrolled = true;
                }
            }

            if (scrolled)
                Refresh();
        }

        public void Show()
        {
            try
            {
                EnsureBuilt();
                Visible = true;
                HudMain.EnableCursor = true;
                Refresh();
            }
            catch (Exception e)
            {
                MyAPIGateway.Utilities.ShowMessage("HireCrew", "UI error: " + e.Message);
            }
        }

        public void Hide()
        {
            Visible = false;
            HudMain.EnableCursor = false;
        }

        public void Refresh()
        {
            if (!Visible) return;
            EnsureBuilt();

            var session = CrewSession.Instance;
            if (session == null)
            {
                RequestClose();
                return;
            }

            bool poolOnly = !_model.HasManagedGrid;
            IMyCubeGrid grid = null;
            if (!poolOnly)
            {
                string err;
                if (!session.TryGetLocalManagedGrid(out grid, out err) || grid == null || grid.EntityId != _model.GridEntityId)
                {
                    RequestClose();
                    return;
                }
            }
            else if (CrewHudModel.IsGridBoundScreen(_model.Screen))
            {
                _model.GoHome();
            }

            string gridName = grid != null && !string.IsNullOrEmpty(grid.CustomName) ? grid.CustomName : "Grid";
            bool home = _model.Screen == CrewHudScreen.Home;
            bool dismissPick = _model.Screen == CrewHudScreen.DismissPick;
            bool unassignPick = _model.Screen == CrewHudScreen.UnassignPick;
            bool assignWeapon = _model.Screen == CrewHudScreen.AssignWeapon;
            bool quartersCrew = _model.Screen == CrewHudScreen.QuartersCrew;
            bool quartersSlots = _model.Screen == CrewHudScreen.QuartersSlots;
            bool quartersPick = _model.Screen == CrewHudScreen.QuartersPickBlock;
            bool assignSeat = _model.Screen == CrewHudScreen.AssignSeat;
            bool assignCrew = _model.Screen == CrewHudScreen.AssignCrew;
            bool trainConfirm = _model.Screen == CrewHudScreen.TrainConfirm;
            bool cancelTrainConfirm = _model.Screen == CrewHudScreen.CancelTrainConfirm;
            bool bulkMap = _model.Screen == CrewHudScreen.BulkMap;
            bool bulkPickSeat = _model.Screen == CrewHudScreen.BulkPickSeat;
            bool bulkPickWeapon = _model.Screen == CrewHudScreen.BulkPickWeapon;
            bool seatOnlyAssign = IsSelectedSeatOnlyRole(session);
            bool bulkOn = _model.BulkMode;
            var selectedHome = home && !bulkOn ? FindSelectedCrew(session) : null;
            bool selectedTraining = CrewConfig.IsTraining(selectedHome);
            BulkMapEntry bulkEdit = null;
            if (bulkMap && _model.BulkEditIndex >= 0 && _model.BulkEditIndex < _model.BulkMapEntries.Count)
                bulkEdit = _model.BulkMapEntries[_model.BulkEditIndex];
            CrewRecord bulkEditCrew = bulkEdit != null ? FindCrewById(session, bulkEdit.CrewId) : null;
            bool bulkNeedsWeapon = bulkEditCrew != null && CrewConfig.NeedsWeapon(bulkEditCrew.Role);

            _btnAssign.Visible = home && !poolOnly && !bulkOn;
            _btnUnassign.Visible = home && !poolOnly && !bulkOn;
            _btnQuarters.Visible = home && !poolOnly && !bulkOn;
            _btnTrain.Visible = home && !bulkOn;
            _btnDismiss.Visible = home && !bulkOn;
            _btnBulk.Visible = home && !poolOnly;
            _btnBulkAssign.Visible = home && !poolOnly && bulkOn;
            _btnClearBulk.Visible = home && !poolOnly && bulkOn;
            _btnBulkSeat.Visible = bulkMap;
            _btnBulkWeapon.Visible = bulkMap;
            _btnClose.Visible = home;
            _btnNext.Visible = !poolOnly && (assignCrew || (assignSeat && !seatOnlyAssign) || quartersCrew);
            _btnConfirm.Visible = dismissPick || trainConfirm || cancelTrainConfirm || bulkMap
                || (!poolOnly && (assignWeapon || unassignPick || quartersSlots || (assignSeat && seatOnlyAssign)));
            _btnBack.Visible = !home;
            _btnCancel.Visible = !home;

            if (home)
            {
                _context.Visible = true;
                if (bulkOn)
                {
                    string bulkCtx = "Bulk: " + _model.BulkSelectedCrewIds.Count + " selected";
                    if (_model.BulkSelectionCapHit)
                        bulkCtx += " · Bulk limit " + CrewHudModel.BulkSelectionCap;
                    _context.Text = bulkCtx;
                }
                else
                    _context.Text = CrewHudModel.FormatHomeContext(selectedHome, !poolOnly);

                bool canAssign = !poolOnly && !bulkOn && CrewHudModel.CanAssignHome(selectedHome);
                bool canUnassign = !poolOnly && !bulkOn && CrewHudModel.CanUnassignHome(selectedHome);
                bool canQuarters = !poolOnly && !bulkOn && CrewHudModel.CanQuartersHome(selectedHome);
                bool canDismiss = !bulkOn && CrewHudModel.CanDismissHome(selectedHome);
                bool canTrain = !bulkOn && (selectedTraining
                    ? CrewHudModel.CanCancelTrain(selectedHome)
                    : CrewHudModel.CanStartTrain(selectedHome));
                bool canBulkAssign = bulkOn && _model.BulkSelectedCrewIds.Count > 0;

                SetHomeAction(_btnAssign, canAssign, ActionAssign);
                SetHomeAction(_btnUnassign, canUnassign, ActionBase);
                SetHomeAction(_btnQuarters, canQuarters, ActionBase);
                SetHomeAction(_btnDismiss, canDismiss, ActionDismiss);
                SetHomeAction(_btnTrain, canTrain, ActionBase);
                SetHomeAction(_btnBulk, true, bulkOn ? ActionAssign : ActionBase);
                SetHomeAction(_btnBulkAssign, canBulkAssign, ActionAssign);
                SetHomeAction(_btnClearBulk, bulkOn && _model.BulkSelectedCrewIds.Count > 0, ActionBase);
                SetHomeAction(_btnClose, true, ActionBase);

                _btnBulk.SetTextIfChanged(bulkOn ? "Bulk: On" : "Bulk");
                if (selectedTraining)
                    _btnTrain.SetTextIfChanged("Cancel…");
                else
                    _btnTrain.SetTextIfChanged("Train");
            }
            else
            {
                _context.Visible = false;
            }

            if (bulkMap)
            {
                // Five equal slots across PanelW 580: Seat / Weapon / Confirm / Back / Cancel
                PlaceBottom(_btnBulkSeat, -220f, 96f);
                PlaceBottom(_btnBulkWeapon, -110f, 96f);
                PlaceBottom(_btnConfirm, 0f, 96f);
                PlaceBottom(_btnBack, 110f, 96f);
                PlaceBottom(_btnCancel, 220f, 96f);
                SetHomeAction(_btnBulkSeat, bulkEdit != null, ActionBase);
                SetHomeAction(_btnBulkWeapon, bulkEdit != null && bulkNeedsWeapon, ActionBase);
                SetHomeAction(_btnConfirm, IsBulkMapConfirmReady(session), ActionAssign);
            }
            else if (home && !poolOnly && bulkOn)
            {
                PlaceBottom(_btnBulkAssign, -180f, 120f);
                PlaceBottom(_btnClearBulk, -40f, 88f);
                PlaceBottom(_btnBulk, 80f, 88f);
                PlaceBottom(_btnClose, 200f, 88f);
            }
            else
            {
                PlaceBottom(_btnConfirm, -155f);
                PlaceBottom(_btnBack, 0f);
                PlaceBottom(_btnCancel, 155f);
                PlaceBottom(_btnBulk, 153f, 72f);
                PlaceBottom(_btnClose, 230f, 72f);
            }

            if (dismissPick)
                _btnConfirm.SetTextIfChanged("Dismiss");
            else if (unassignPick)
                _btnConfirm.SetTextIfChanged("Unassign");
            else if (cancelTrainConfirm)
                _btnConfirm.SetTextIfChanged("Cancel Train");
            else if (bulkMap)
                _btnConfirm.SetTextIfChanged("Confirm");
            else if (trainConfirm || assignWeapon || (assignSeat && seatOnlyAssign))
                _btnConfirm.SetTextIfChanged("Confirm");
            else if (quartersSlots)
                _btnConfirm.SetTextIfChanged("Done");

            if (home)
            {
                _header.Text = bulkOn ? "Crew Roster · Bulk" : "Crew Roster";
                FillHome(session);
                _status.Text = ScrollStatus(poolOnly
                    ? "Off ship · train & dismiss only"
                    : (bulkOn ? "Tap unassigned crew to multi-select" : gridName + " (faction/personal pool)"));
            }
            else if (trainConfirm)
            {
                FillTrainConfirm(session);
            }
            else if (cancelTrainConfirm)
            {
                FillCancelTrainConfirm(session);
            }
            else if (dismissPick)
            {
                FillDismissPick(session);
            }
            else if (unassignPick)
            {
                FillUnassignPick(session);
            }
            else if (assignCrew)
            {
                _header.Text = seatOnlyAssign ? "Assign 1/2 Crew" : "Assign 1/3 Crew";
                FillAssignCrew(session);
                _status.Text = ScrollStatus("Select unassigned crew");
            }
            else if (assignSeat)
            {
                _header.Text = seatOnlyAssign ? "Assign 1/1 Seat" : "Assign 1/2 Seat";
                FillAssignSeat(session, grid);
                if (seatOnlyAssign)
                {
                    var sel = FindSelectedCrew(session);
                    string roleHint = sel != null ? CrewConfig.RoleLabel(sel.Role) : "crew";
                    _status.Text = ScrollStatus("Select a seat (" + roleHint + ")");
                }
                else
                    _status.Text = ScrollStatus("Select a seat");
            }
            else if (assignWeapon)
            {
                _header.Text = "Assign 2/2 Weapon";
                FillAssignWeapon(session, grid);
                _status.Text = ScrollStatus("Select a weapon");
            }
            else if (bulkMap)
            {
                _header.Text = "Bulk Assign (" + _model.BulkMapEntries.Count + ")";
                FillBulkMap(session);
                _status.Text = ScrollStatus("Select row, then Seat / Weapon");
            }
            else if (bulkPickSeat)
            {
                _header.Text = "Bulk · Pick seat";
                FillBulkPickSeat(session, grid);
                _status.Text = ScrollStatus("Select a seat");
            }
            else if (bulkPickWeapon)
            {
                _header.Text = "Bulk · Pick weapon";
                FillBulkPickWeapon(session, grid);
                _status.Text = ScrollStatus("Select a weapon");
            }
            else if (quartersCrew)
            {
                _header.Text = "Quarters 1/2 Crew";
                FillQuartersCrew(session);
                _status.Text = ScrollStatus("Select assigned crew");
            }
            else if (quartersSlots)
            {
                _header.Text = "Quarters";
                FillQuartersSlots(session);
                _status.Text = ScrollStatus("+10% range per amenity");
            }
            else if (quartersPick)
            {
                _header.Text = "Pick " + CrewAmenities.KindLabel(_model.SelectedAmenityKind);
                FillQuartersPickBlock(session, grid);
                _status.Text = ScrollStatus("Tap block to assign");
            }
        }

        private string ScrollStatus(string baseText)
        {
            if (_listTotalCount <= MaxRows) return baseText;
            int start = _model.ListScrollOffset;
            int end = start + _rowCount;
            if (end > _listTotalCount) end = _listTotalCount;
            return baseText + "  (" + (start + 1) + "-" + end + "/" + _listTotalCount + ", PgUp/PgDn)";
        }

        private void ClearRows()
        {
            _rowCount = 0;
            for (int i = 0; i < MaxRows; i++)
            {
                _rows[i].Visible = false;
                _rowCrewIds[i] = null;
                _rowEntityIds[i] = 0;
                HideMoraleBar(i);
                HideRoleIcon(i);
                HideStarIcons(i);
                HideColumnLabels(i);
            }
        }

        private void AddRow(string text, string crewId, long entityId, bool selected, bool interactive)
        {
            AddRow(text, crewId, entityId, selected, interactive, null, null);
        }

        private void AddRow(string text, string crewId, long entityId, bool selected, bool interactive, CrewRole? role)
        {
            AddRow(text, crewId, entityId, selected, interactive, role, null);
        }

        private void AddRow(string text, string crewId, long entityId, bool selected, bool interactive, CrewRole? role, int? stars)
        {
            if (_rowCount >= MaxRows) return;
            int i = _rowCount++;
            _rowCrewIds[i] = crewId;
            _rowEntityIds[i] = entityId;
            _rows[i].Visible = true;
            _rows[i].SetInteractive(interactive);
            _rows[i].BaseColor = selected
                ? new Color(42, 74, 102, 255)
                : new Color(35, 48, 62, 255);
            _rows[i].HighlightColor = selected
                ? new Color(70, 110, 150, 255)
                : new Color(55, 75, 95, 255);
            HideMoraleBar(i);

            if (role.HasValue && stars.HasValue)
            {
                // text is unused — callers with role/stars should use AddCrewRow.
                ApplyCrewColumns(i, text ?? "", CrewConfig.RoleLabel(role.Value), "", role.Value, stars.Value, false);
                return;
            }

            float rowWidth = PanelW - 28f;
            float rowY = RowYAt(i);
            _rows[i].Offset = new Vector2(0f, rowY);
            _rows[i].SetTextIfChanged(text ?? "");
            _rows[i].Format = GlyphFormat.White.WithAlignment(TextAlignment.Center);
            _rows[i].TextPadding = new Vector2(8f, 4f);
            _rows[i].FitToTextElement = false;
            _rows[i].Size = new Vector2(rowWidth, RowH);
            _rows[i].TextSize = new Vector2(rowWidth - 16f, RowH - 8f);
            _rows[i].textElement.ParentAlignment = ParentAlignments.Center;
            _rows[i].textElement.Offset = Vector2.Zero;
            HideRoleIcon(i);
            HideStarIcons(i);
            HideColumnLabels(i);
        }

        private void AddCrewRow(CrewRecord r, bool selected, bool interactive, bool homeLayout)
        {
            if (_rowCount >= MaxRows || r == null) return;
            int i = _rowCount++;
            _rowCrewIds[i] = r.CrewId;
            _rowEntityIds[i] = 0;
            _rows[i].Visible = true;
            _rows[i].SetInteractive(interactive);
            _rows[i].BaseColor = selected
                ? new Color(42, 74, 102, 255)
                : new Color(35, 48, 62, 255);
            _rows[i].HighlightColor = selected
                ? new Color(70, 110, 150, 255)
                : new Color(55, 75, 95, 255);

            string name = r.DisplayName ?? "";
            string detail = homeLayout ? FormatHomeDetail(r) : CrewHudModel.FormatRosterDetail(r);
            bool moraleSpace = homeLayout && r.Status == CrewStatus.Seated;
            ApplyCrewColumns(i, name, CrewConfig.RoleLabel(r.Role), detail, r.Role, r.Stars, moraleSpace);
            if (moraleSpace)
                SetMoraleBar(i, r);
            else
                HideMoraleBar(i);
        }

        private static GlyphFormat SpecFormatFor(CrewRole role)
        {
            // Regular only — SE billboard fonts often lack Bold and crash in FontManager.
            return new GlyphFormat(
                RoleAccentColor(role),
                TextAlignment.Left,
                1f);
        }

        private static Color RoleAccentColor(CrewRole role)
        {
            switch (role)
            {
                case CrewRole.Gunner: return new Color(255, 128, 96);
                case CrewRole.Engineer: return new Color(96, 210, 255);
                case CrewRole.Helmsman: return new Color(120, 220, 165);
                case CrewRole.Propulsion: return new Color(255, 190, 90);
                case CrewRole.Quartermaster: return new Color(210, 165, 255);
                default: return new Color(255, 214, 120);
            }
        }

        private static RichText BuildNameSpecLine(string name, string spec, CrewRole role)
        {
            var rt = new RichText();
            var nameFmt = GlyphFormat.White.WithAlignment(TextAlignment.Left);
            bool hasName = !string.IsNullOrEmpty(name);
            bool hasSpec = !string.IsNullOrEmpty(spec);
            if (hasName)
                rt.Add(name, nameFmt);
            if (hasSpec)
            {
                if (hasName)
                    rt.Add("  ·  ", nameFmt);
                rt.Add(spec, SpecFormatFor(role));
            }
            return rt;
        }

        private void ApplyCrewColumns(int i, string name, string spec, string detail, CrewRole role, int stars, bool reserveMorale)
        {
            float rowWidth = PanelW - 28f;
            // Line 1: name + colored specialization. Line 2: status/detail.
            float leftReserve = IconLeftPad + RoleIconSize + IconGap;
            float starStripW = CrewHudIcons.StarRowWidth(StarIconSize, StarIconGap);
            float rightPad = 12f + starStripW + 8f;
            float textW = rowWidth - leftReserve - rightPad;
            if (textW < 120f) textW = 120f;

            float rowY = RowYAt(i);
            float line1Y = rowY - CardPadTop;
            float line2Y = rowY - CardPadTop - CardLineH - 2f;

            _rows[i].Offset = new Vector2(0f, rowY);
            _rows[i].SetTextIfChanged("");
            _rows[i].FitToTextElement = false;
            _rows[i].Size = new Vector2(rowWidth, RowH);
            _rows[i].TextSize = new Vector2(8f, 8f);
            _rows[i].textElement.Offset = Vector2.Zero;

            SetRoleIcon(i, role);
            var roleIcon = _rowRoleIcons[i];
            if (roleIcon != null && roleIcon.Visible)
            {
                float iconY = line1Y - (CardLineH - RoleIconSize) * 0.5f;
                float iconCenterX = -(rowWidth * 0.5f) + IconLeftPad + RoleIconSize * 0.5f;
                roleIcon.Offset = new Vector2(iconCenterX, iconY);
            }

            float starsLeft = (rowWidth * 0.5f) - 12f - starStripW;
            float starsY = line1Y - (CardLineH - StarIconSize) * 0.5f;
            CrewHudIcons.LayoutStars(_rowStarIcons[i], stars, starsLeft, starsY, StarIconSize, StarIconGap, true);

            float textLeft = -(rowWidth * 0.5f) + leftReserve;
            float textCenterX = textLeft + textW * 0.5f;

            var nameLabel = _rowNameLabels[i];
            if (nameLabel != null)
            {
                bool hasLine1 = !string.IsNullOrEmpty(name) || !string.IsNullOrEmpty(spec);
                nameLabel.Text = hasLine1 ? BuildNameSpecLine(name, spec, role) : new RichText();
                nameLabel.Format = GlyphFormat.White.WithAlignment(TextAlignment.Left);
                nameLabel.VertCenterText = true;
                nameLabel.Size = new Vector2(textW, CardLineH);
                nameLabel.Offset = new Vector2(textCenterX, line1Y);
                nameLabel.Visible = hasLine1;
            }

            var specLabel = _rowSpecLabels[i];
            if (specLabel != null)
                specLabel.Visible = false;

            var detailLabel = _rowDetailLabels[i];
            if (detailLabel != null)
            {
                float detailRightPad = reserveMorale ? (12f + MoraleBarW + 8f) : 16f;
                float detailW = rowWidth - leftReserve - detailRightPad;
                if (detailW < 120f) detailW = 120f;
                float detailCenterX = -(rowWidth * 0.5f) + leftReserve + detailW * 0.5f;
                detailLabel.Text = detail ?? "";
                detailLabel.Format = GlyphFormat.Blueish.WithAlignment(TextAlignment.Left);
                detailLabel.VertCenterText = true;
                detailLabel.Size = new Vector2(detailW, CardLineH);
                detailLabel.Offset = new Vector2(detailCenterX, line2Y);
                detailLabel.Visible = !string.IsNullOrEmpty(detail);
            }
        }

        private void FillHome(CrewSession session)
        {
            ClearRows();
            if (_model.BulkMode)
                _model.PruneBulkSelection(id => FindCrewById(session, id));
            var roster = RosterForManagedGrid(session);
            _listTotalCount = roster.Count;
            int start = _model.ClampListScroll(_listTotalCount, MaxRows);
            for (int i = start; i < roster.Count && _rowCount < MaxRows; i++)
            {
                var r = roster[i];
                if (r == null) continue;
                bool sel = _model.BulkMode
                    ? _model.IsBulkSelected(r.CrewId)
                    : string.Equals(r.CrewId, _model.SelectedCrewId, StringComparison.Ordinal);
                AddCrewRow(r, sel, true, true);
            }
            if (_rowCount == 0)
                AddRow("(roster empty — hire at a Crew Hiring Desk)", null, 0L, false, false);
        }

        private void FillBulkMap(CrewSession session)
        {
            ClearRows();
            _model.PruneBulkSelection(id => FindCrewById(session, id));
            var entries = _model.BulkMapEntries;
            _listTotalCount = entries.Count;
            int start = _model.ClampListScroll(_listTotalCount, MaxRows);
            for (int i = start; i < entries.Count && _rowCount < MaxRows; i++)
            {
                var e = entries[i];
                if (e == null) continue;
                var crew = FindCrewById(session, e.CrewId);
                string name = crew == null
                    ? (e.CrewId ?? "?")
                    : (string.IsNullOrEmpty(crew.DisplayName) ? CrewConfig.RoleLabel(crew.Role) : crew.DisplayName);
                string seatLabel = e.SeatEntityId == 0 ? "Pick seat…" : ResolveEntityLabel(e.SeatEntityId);
                if (string.IsNullOrEmpty(seatLabel)) seatLabel = "#" + e.SeatEntityId;
                bool needsWep = crew != null && CrewConfig.NeedsWeapon(crew.Role);
                string wepPart;
                if (!needsWep)
                    wepPart = "no weapon";
                else if (e.WeaponEntityId == 0)
                    wepPart = "Pick weapon…";
                else
                {
                    wepPart = ResolveEntityLabel(e.WeaponEntityId);
                    if (string.IsNullOrEmpty(wepPart)) wepPart = "#" + e.WeaponEntityId;
                }
                string line = name + " — " + seatLabel + " / " + wepPart;
                bool sel = i == _model.BulkEditIndex;
                AddRow(line, e.CrewId, i + 1L, sel, true, crew != null ? (CrewRole?)crew.Role : null, crew != null ? (int?)crew.Stars : null);
            }
            if (_rowCount == 0)
                AddRow("(no crew selected)", null, 0L, false, false);
        }

        private void FillBulkPickSeat(CrewSession session, IMyCubeGrid grid)
        {
            ClearRows();
            CollectSeatsAndWeapons(grid);
            var crew = session.GetCrewForConstruct(grid);
            var free = CrewHudModel.FilterAvailableSeats(_seatScratch, crew, SeatId);
            var reserved = _model.GetBulkReservedSeats(_model.BulkEditIndex);
            var filtered = new List<IMyTerminalBlock>();
            for (int i = 0; i < free.Count; i++)
            {
                var seat = free[i];
                if (seat == null) continue;
                if (reserved.Contains(seat.EntityId)) continue;
                filtered.Add(seat);
            }
            _listTotalCount = filtered.Count;
            int start = _model.ClampListScroll(_listTotalCount, MaxRows);
            for (int i = start; i < filtered.Count && _rowCount < MaxRows; i++)
            {
                var seat = filtered[i];
                AddRow(BlockLabel(seat), null, seat.EntityId, false, true);
            }
            if (_rowCount == 0)
                AddRow("(no free seats)", null, 0L, false, false);
        }

        private void FillBulkPickWeapon(CrewSession session, IMyCubeGrid grid)
        {
            ClearRows();
            CollectSeatsAndWeapons(grid);
            var crew = session.GetCrewForConstruct(grid);
            var free = CrewHudModel.FilterAvailableWeapons(_weaponScratch, crew, WeaponId);
            bool wcReady = session.WeaponAi != null && session.WeaponAi.IsReady;
            if (!wcReady)
            {
                _listTotalCount = 0;
                AddRow("(WeaponCore not ready)", null, 0L, false, false);
                return;
            }
            var reserved = _model.GetBulkReservedWeapons(_model.BulkEditIndex);
            var filtered = new List<IMyTerminalBlock>();
            for (int i = 0; i < free.Count; i++)
            {
                var wep = free[i];
                if (wep == null) continue;
                if (reserved.Contains(wep.EntityId)) continue;
                filtered.Add(wep);
            }
            _listTotalCount = filtered.Count;
            int start = _model.ClampListScroll(_listTotalCount, MaxRows);
            for (int i = start; i < filtered.Count && _rowCount < MaxRows; i++)
            {
                var wep = filtered[i];
                AddRow(BlockLabel(wep), null, wep.EntityId, false, true);
            }
            if (_rowCount == 0)
                AddRow("(no free weapons)", null, 0L, false, false);
        }

        private void FillTrainConfirm(CrewSession session)
        {
            ClearRows();
            _listTotalCount = 0;
            var crew = FindSelectedCrew(session);
            if (crew == null)
            {
                _header.Text = "Train";
                _status.Text = "Select a crew member";
                AddRow("(no crew selected)", null, 0L, false, false);
                return;
            }

            string name = string.IsNullOrEmpty(crew.DisplayName)
                ? CrewConfig.RoleLabel(crew.Role)
                : crew.DisplayName;
            _header.Text = "Train " + name;
            if (!CrewHudModel.CanStartTrain(crew))
            {
                _status.Text = "Cannot train";
                AddRow("(cannot train)", null, 0L, false, false);
                return;
            }

            int next = crew.Stars + 1;
            float discount = 0f;
            if (session != null && session.Store != null)
                discount = CrewConfig.GetTrainDiscountFraction(session.Store.All, crew.OwnerKey, crew.OwnerIsFaction);
            long cost = CrewConfig.GetTrainCost(crew.Stars, discount);
            int minutes = CrewConfig.GetTrainMinutes(crew.Stars);
            _status.Text = "Unassigns crew for the duration · no refunds";
            string costLine = crew.Stars + " → " + next + "  ·  " + cost + " cr";
            if (discount > 0.001f)
                costLine += " (QM -" + ((int)System.Math.Round(discount * 100f)) + "%)";
            costLine += "  ·  " + minutes + "m";
            AddRow(costLine, null, 0L, false, false);
        }

        private void FillCancelTrainConfirm(CrewSession session)
        {
            ClearRows();
            _listTotalCount = 0;
            var crew = FindSelectedCrew(session);
            string name = crew == null
                ? "crew"
                : (string.IsNullOrEmpty(crew.DisplayName) ? CrewConfig.RoleLabel(crew.Role) : crew.DisplayName);
            _header.Text = "Cancel training?";
            _status.Text = "No refund · " + name;
            AddRow("Stop training with no star gain", null, 0L, false, false);
        }

        private const float RoleIconSize = 22f;
        private const float StarIconSize = 12f;
        private const float StarIconGap = 2f;
        private const float IconLeftPad = 10f;
        private const float IconGap = 8f;

        private static float LeftIconStripWidth(bool role, bool stars)
        {
            float x = IconLeftPad;
            if (role) x += RoleIconSize + IconGap;
            // Always reserve the full 5-slot strip (lit + dim placeholders).
            if (stars) x += CrewHudIcons.StarRowWidth(StarIconSize, StarIconGap) + IconGap;
            return x;
        }

        private void HideRoleIcon(int index)
        {
            if (_rowRoleIcons[index] != null)
                _rowRoleIcons[index].Visible = false;
        }

        private void HideStarIcons(int index)
        {
            CrewHudIcons.HideStars(_rowStarIcons[index]);
        }

        private void HideColumnLabels(int index)
        {
            if (_rowNameLabels[index] != null) _rowNameLabels[index].Visible = false;
            if (_rowSpecLabels[index] != null) _rowSpecLabels[index].Visible = false;
            if (_rowDetailLabels[index] != null) _rowDetailLabels[index].Visible = false;
        }

        private void SetRoleIcon(int index, CrewRole? role)
        {
            var icon = _rowRoleIcons[index];
            if (icon == null) return;
            if (!role.HasValue)
            {
                icon.Visible = false;
                return;
            }

            float rowWidth = PanelW - 28f;
            float rowY = RowYAt(index);
            float iconY = rowY - (RowH - RoleIconSize) * 0.5f;
            float iconCenterX = -(rowWidth * 0.5f) + IconLeftPad + RoleIconSize * 0.5f;
            icon.Material = CrewHudIcons.ForRole(role.Value);
            icon.Offset = new Vector2(iconCenterX, iconY);
            icon.Visible = true;
        }

        private void SetStarIcons(int index, int? stars, bool hasRole)
        {
            var row = _rowStarIcons[index];
            if (row == null) return;
            if (!stars.HasValue)
            {
                HideStarIcons(index);
                return;
            }

            float rowWidth = PanelW - 28f;
            float rowY = RowYAt(index);
            float line1Y = rowY - CardPadTop;
            float iconY = line1Y - (CardLineH - StarIconSize) * 0.5f;
            float starStripW = CrewHudIcons.StarRowWidth(StarIconSize, StarIconGap);
            float leftX = (rowWidth * 0.5f) - 12f - starStripW;
            if (!hasRole)
                leftX = -(rowWidth * 0.5f) + IconLeftPad;
            CrewHudIcons.LayoutStars(row, stars.Value, leftX, iconY, StarIconSize, StarIconGap, true);
        }

        private void HideMoraleBar(int index)
        {
            if (_moraleTracks[index] != null) _moraleTracks[index].Visible = false;
            if (_moraleFills[index] != null) _moraleFills[index].Visible = false;
            if (_moraleLabels[index] != null) _moraleLabels[index].Visible = false;
        }

        private void SetMoraleBar(int index, CrewRecord r)
        {
            if (r == null || r.Status != CrewStatus.Seated)
            {
                HideMoraleBar(index);
                return;
            }

            float ratio = CrewAmenities.CountAssigned(r) / 3f;
            if (ratio < 0f) ratio = 0f;
            if (ratio > 1f) ratio = 1f;

            float fillW = MoraleBarW * ratio;
            float rowWidth = PanelW - 28f;
            float trackCenterX = rowWidth * 0.5f - 12f - MoraleBarW * 0.5f;
            float trackLeft = trackCenterX - MoraleBarW * 0.5f;
            float fillCenterX = trackLeft + fillW * 0.5f;
            // Morale sits on line 2 (right), under the star strip.
            float rowY = RowYAt(index);
            float line2Y = rowY - CardPadTop - CardLineH - 2f;
            float barY = line2Y - (CardLineH - MoraleBarH) * 0.5f;

            var track = _moraleTracks[index];
            var fill = _moraleFills[index];
            var label = _moraleLabels[index];

            track.Visible = true;
            track.Offset = new Vector2(trackCenterX, barY);
            track.Size = new Vector2(MoraleBarW, MoraleBarH);

            fill.Visible = fillW > 0.5f;
            fill.Offset = new Vector2(fillCenterX, barY);
            fill.Size = new Vector2(fillW, MoraleBarH);
            fill.Color = MoraleFillColor(ratio);

            label.Visible = true;
            label.Offset = new Vector2(trackCenterX, barY);
            label.Size = new Vector2(MoraleBarW, MoraleBarH);
            label.Text = CrewAmenities.GetEfficiencyPercent(r) + "%";
        }

        private static Color MoraleFillColor(float ratio)
        {
            if (ratio <= 0f) return new Color(70, 70, 70, 255);
            if (ratio < 0.34f) return new Color(180, 90, 70, 255);
            if (ratio < 0.67f) return new Color(190, 160, 70, 255);
            return new Color(80, 170, 110, 255);
        }

        private string FormatHomeDetail(CrewRecord r)
        {
            if (r == null) return "";
            if (CrewConfig.IsTraining(r))
                return CrewHudModel.FormatRosterDetail(r);
            if (r.Status != CrewStatus.Seated || r.GridEntityId == 0)
                return "Pool";

            bool local = IsOnLocalConstruct(r.GridEntityId);
            if (r.Role == CrewRole.Engineer)
            {
                var powerPct = (int)System.Math.Round(
                    CrewConfig.GetPowerBonus(r.Stars, CrewAmenities.GetEfficiency(r)) * 100f);
                if (local)
                    return "+" + powerPct + "% pwr";
                return ResolveGridLabel(r.GridEntityId) + " +" + powerPct + "% pwr";
            }

            string weapon = ResolveEntityLabel(r.WeaponEntityId);
            if (local)
            {
                if (string.IsNullOrEmpty(weapon))
                    return "Seated";
                return weapon;
            }

            string gridLabel = ResolveGridLabel(r.GridEntityId);
            if (string.IsNullOrEmpty(weapon))
                return gridLabel;
            return gridLabel + " / " + weapon;
        }

        private bool IsOnLocalConstruct(long crewGridEntityId)
        {
            if (crewGridEntityId == 0 || _model == null || _model.GridEntityId == 0)
                return false;
            if (crewGridEntityId == _model.GridEntityId)
                return true;

            IMyEntity localEnt;
            IMyEntity crewEnt;
            if (!MyAPIGateway.Entities.TryGetEntityById(_model.GridEntityId, out localEnt) || localEnt == null)
                return false;
            if (!MyAPIGateway.Entities.TryGetEntityById(crewGridEntityId, out crewEnt) || crewEnt == null)
                return false;
            var localGrid = localEnt as IMyCubeGrid;
            var crewGrid = crewEnt as IMyCubeGrid;
            return localGrid != null && crewGrid != null && localGrid.IsSameConstructAs(crewGrid);
        }

        private static string ResolveGridLabel(long gridEntityId)
        {
            if (gridEntityId == 0) return "Pool";
            IMyEntity ent;
            if (!MyAPIGateway.Entities.TryGetEntityById(gridEntityId, out ent) || ent == null)
                return "Grid";
            var grid = ent as IMyCubeGrid;
            if (grid == null) return "Grid";
            return !string.IsNullOrEmpty(grid.CustomName) ? grid.CustomName : "Grid";
        }

        private static string ResolveEntityLabel(long? entityId)
        {
            if (!entityId.HasValue || entityId.Value == 0) return "";
            IMyEntity ent;
            if (!MyAPIGateway.Entities.TryGetEntityById(entityId.Value, out ent) || ent == null)
                return "#" + entityId.Value;
            var block = ent as IMyTerminalBlock;
            if (block == null) return "#" + entityId.Value;
            return BlockLabel(block);
        }

        private void FillUnassignPick(CrewSession session)
        {
            ClearRows();
            _listTotalCount = 0;
            var crew = FindSelectedCrew(session);
            if (crew == null)
            {
                _header.Text = "Unassign";
                _status.Text = "Select a crew member";
                AddRow("(no crew selected)", null, 0L, false, false);
                return;
            }
            string name = string.IsNullOrEmpty(crew.DisplayName)
                ? CrewConfig.RoleLabel(crew.Role)
                : crew.DisplayName;
            _header.Text = "Unassign " + name + "?";
            _status.Text = "Return to pool · clears seat/weapon/amenities";
            AddRow(CrewHudModel.FormatRosterDetail(crew), null, 0L, false, false);
        }

        private void FillDismissPick(CrewSession session)
        {
            ClearRows();
            _listTotalCount = 0;
            var crew = FindSelectedCrew(session);
            if (crew == null)
            {
                _header.Text = "Dismiss";
                _status.Text = "Select a crew member";
                AddRow("(no crew selected)", null, 0L, false, false);
                return;
            }
            string name = string.IsNullOrEmpty(crew.DisplayName)
                ? CrewConfig.RoleLabel(crew.Role)
                : crew.DisplayName;
            _header.Text = "Dismiss " + name + "?";
            _status.Text = "Permanently fire this crew · no refund";
            AddRow(CrewHudModel.FormatRosterDetail(crew), null, 0L, false, false);
        }

        private void FillAssignCrew(CrewSession session)
        {
            ClearRows();
            var roster = RosterForManagedGrid(session);
            int unassignedTotal = 0;
            for (int i = 0; i < roster.Count; i++)
            {
                var r = roster[i];
                if (r != null && r.Status == CrewStatus.Unassigned)
                    unassignedTotal++;
            }
            _listTotalCount = unassignedTotal;
            int start = _model.ClampListScroll(_listTotalCount, MaxRows);
            int unassignedIndex = 0;
            for (int i = 0; i < roster.Count && _rowCount < MaxRows; i++)
            {
                var r = roster[i];
                if (r == null || r.Status != CrewStatus.Unassigned) continue;
                if (unassignedIndex < start)
                {
                    unassignedIndex++;
                    continue;
                }
                unassignedIndex++;
                bool sel = string.Equals(r.CrewId, _model.SelectedCrewId, StringComparison.Ordinal);
                AddCrewRow(r, sel, true, false);
            }
            if (_rowCount == 0)
                AddRow("(none unassigned)", null, 0L, false, false);
        }

        private void FillAssignSeat(CrewSession session, IMyCubeGrid grid)
        {
            ClearRows();
            CollectSeatsAndWeapons(grid);
            var crew = session.GetCrewForConstruct(grid);
            var free = CrewHudModel.FilterAvailableSeats(_seatScratch, crew, SeatId);
            _listTotalCount = free.Count;
            int start = _model.ClampListScroll(_listTotalCount, MaxRows);
            for (int i = start; i < free.Count && _rowCount < MaxRows; i++)
            {
                var seat = free[i];
                if (seat == null) continue;
                bool sel = seat.EntityId == _model.SelectedSeatEntityId;
                AddRow(BlockLabel(seat), null, seat.EntityId, sel, true);
            }
            if (_rowCount == 0)
                AddRow("(no free seats)", null, 0L, false, false);
        }

        private void FillAssignWeapon(CrewSession session, IMyCubeGrid grid)
        {
            ClearRows();
            CollectSeatsAndWeapons(grid);
            var crew = session.GetCrewForConstruct(grid);
            var free = CrewHudModel.FilterAvailableWeapons(_weaponScratch, crew, WeaponId);
            bool wcReady = session.WeaponAi != null && session.WeaponAi.IsReady;
            if (!wcReady)
            {
                _listTotalCount = 0;
                AddRow("(WeaponCore not ready)", null, 0L, false, false);
                return;
            }
            _listTotalCount = free.Count;
            int start = _model.ClampListScroll(_listTotalCount, MaxRows);
            for (int i = start; i < free.Count && _rowCount < MaxRows; i++)
            {
                var wep = free[i];
                if (wep == null) continue;
                bool sel = wep.EntityId == _model.SelectedWeaponEntityId;
                AddRow(BlockLabel(wep), null, wep.EntityId, sel, true);
            }
            if (_rowCount == 0)
                AddRow("(no free weapons)", null, 0L, false, false);
        }

        private void FillQuartersCrew(CrewSession session)
        {
            ClearRows();
            IMyCubeGrid grid;
            string err;
            var roster = new List<CrewRecord>();
            if (session.TryGetLocalManagedGrid(out grid, out err) && grid != null)
            {
                var owned = RosterForManagedGrid(session);
                for (int i = 0; i < owned.Count; i++)
                {
                    var r = owned[i];
                    if (r == null || r.Status != CrewStatus.Seated || r.GridEntityId == 0) continue;
                    if (r.GridEntityId == grid.EntityId)
                    {
                        roster.Add(r);
                        continue;
                    }
                    IMyEntity ent;
                    if (!MyAPIGateway.Entities.TryGetEntityById(r.GridEntityId, out ent) || ent == null)
                        continue;
                    var cg = ent as IMyCubeGrid;
                    if (cg != null && cg.IsSameConstructAs(grid))
                        roster.Add(r);
                }
            }
            _listTotalCount = roster.Count;
            int start = _model.ClampListScroll(_listTotalCount, MaxRows);
            for (int i = start; i < roster.Count && _rowCount < MaxRows; i++)
            {
                var r = roster[i];
                if (r == null) continue;
                bool sel = string.Equals(r.CrewId, _model.SelectedCrewId, StringComparison.Ordinal);
                AddCrewRow(r, sel, true, false);
            }
            if (_rowCount == 0)
                AddRow("(none assigned on this grid — use Assign first)", null, 0L, false, false);
        }

        private void FillQuartersSlots(CrewSession session)
        {
            ClearRows();
            _listTotalCount = _slotKinds.Length;
            var crew = session.Store != null ? session.Store.Get(_model.SelectedCrewId) : null;
            for (int i = 0; i < _slotKinds.Length; i++)
            {
                var kind = _slotKinds[i];
                string label = CrewAmenities.KindLabel(kind) + ": ";
                long? id = crew != null ? CrewAmenities.GetAmenity(crew, kind) : null;
                string blockLabel = ResolveEntityLabel(id);
                if (string.IsNullOrEmpty(blockLabel))
                    label += "\u2014";
                else
                    label += blockLabel;
                AddRow(label, null, (long)kind + 1L, false, true);
            }
        }

        private void FillQuartersPickBlock(CrewSession session, IMyCubeGrid grid)
        {
            ClearRows();
            CollectAmenities(grid);
            var crew = RosterForManagedGrid(session);
            var free = CrewHudModel.FilterAvailableAmenities(
                _amenityScratch,
                crew,
                _model.SelectedCrewId,
                _model.SelectedAmenityKind,
                AmenityId,
                AmenityMatches);

            // Index 0 = clear; then free blocks.
            _listTotalCount = free.Count + 1;
            int start = _model.ClampListScroll(_listTotalCount, MaxRows);
            for (int logical = start; logical < _listTotalCount && _rowCount < MaxRows; logical++)
            {
                if (logical == 0)
                {
                    AddRow("\u2014 Clear \u2014", null, 0L, false, true);
                    continue;
                }

                var block = free[logical - 1];
                if (block == null) continue;
                AddRow(BlockLabel(block), null, block.EntityId, false, true);
            }

            if (_rowCount == 0)
                AddRow("(no matching blocks)", null, 0L, false, false);
        }

        private void OnRowClicked(int index)
        {
            if (index < 0 || index >= _rowCount) return;
            if (!_rows[index].UseCursor) return;

            if (_model.Screen == CrewHudScreen.Home)
            {
                if (!string.IsNullOrEmpty(_rowCrewIds[index]))
                {
                    if (_model.BulkMode)
                    {
                        var crew = FindCrewById(CrewSession.Instance, _rowCrewIds[index]);
                        if (!_model.TryToggleBulkSelect(crew) && _model.BulkSelectionCapHit)
                            MyAPIGateway.Utilities.ShowMessage("HireCrew", "Bulk limit " + CrewHudModel.BulkSelectionCap);
                    }
                    else
                        _model.SelectedCrewId = _rowCrewIds[index];
                }
            }
            else if (_model.Screen == CrewHudScreen.AssignCrew ||
                _model.Screen == CrewHudScreen.DismissPick ||
                _model.Screen == CrewHudScreen.UnassignPick ||
                _model.Screen == CrewHudScreen.QuartersCrew)
            {
                if (!string.IsNullOrEmpty(_rowCrewIds[index]))
                    _model.SelectedCrewId = _rowCrewIds[index];
            }
            else if (_model.Screen == CrewHudScreen.BulkMap)
            {
                int mapIndex = (int)_rowEntityIds[index] - 1;
                if (mapIndex >= 0 && mapIndex < _model.BulkMapEntries.Count)
                    _model.SelectBulkMapRow(mapIndex);
            }
            else if (_model.Screen == CrewHudScreen.AssignSeat)
            {
                if (_rowEntityIds[index] != 0)
                    _model.SelectedSeatEntityId = _rowEntityIds[index];
            }
            else if (_model.Screen == CrewHudScreen.AssignWeapon)
            {
                if (_rowEntityIds[index] != 0)
                    _model.SelectedWeaponEntityId = _rowEntityIds[index];
            }
            else if (_model.Screen == CrewHudScreen.BulkPickSeat)
            {
                if (_rowEntityIds[index] != 0)
                    _model.TrySetBulkSeat(_rowEntityIds[index]);
            }
            else if (_model.Screen == CrewHudScreen.BulkPickWeapon)
            {
                if (_rowEntityIds[index] != 0)
                    _model.TrySetBulkWeapon(_rowEntityIds[index]);
            }
            else if (_model.Screen == CrewHudScreen.QuartersSlots)
            {
                long packed = _rowEntityIds[index];
                if (packed >= 1 && packed <= 3)
                    _model.OpenAmenityPicker((AmenityKind)(packed - 1));
            }
            else if (_model.Screen == CrewHudScreen.QuartersPickBlock)
            {
                ApplyAmenityPick(_rowEntityIds[index]);
                return;
            }
            Refresh();
        }

        private void ApplyAmenityPick(long blockEntityId)
        {
            var session = CrewSession.Instance;
            if (session == null || string.IsNullOrEmpty(_model.SelectedCrewId))
            {
                MyAPIGateway.Utilities.ShowMessage("HireCrew", "Select a crew member");
                return;
            }

            session.ClientRequestAssignAmenity(
                _model.SelectedCrewId,
                _model.GridEntityId,
                _model.SelectedAmenityKind,
                blockEntityId);
            _model.ReturnToQuartersSlots();
            Refresh();
        }

        private static readonly Color ActionBase = new Color(50, 80, 110, 255);
        private static readonly Color ActionDim = new Color(35, 42, 50, 255);
        private static readonly Color ActionAssign = new Color(45, 90, 55, 255);
        private static readonly Color ActionDismiss = new Color(90, 45, 45, 255);

        private void SetHomeAction(CrewHudButton btn, bool enabled, Color enabledColor)
        {
            btn.SetInteractive(enabled);
            btn.BaseColor = enabled ? enabledColor : ActionDim;
            btn.Color = btn.BaseColor;
            btn.HighlightColor = enabled
                ? new Color(
                    Math.Min(255, enabledColor.R + 40),
                    Math.Min(255, enabledColor.G + 40),
                    Math.Min(255, enabledColor.B + 40),
                    255)
                : ActionDim;
        }

        private void ToggleBulkMode()
        {
            _model.SetBulkMode(!_model.BulkMode);
            Refresh();
        }

        private void BeginBulkAssign()
        {
            var session = CrewSession.Instance;
            if (!_model.TryBeginBulkMap(id => FindCrewById(session, id)))
            {
                MyAPIGateway.Utilities.ShowMessage("HireCrew", "Select unassigned crew first");
                Refresh();
                return;
            }
            Refresh();
        }

        private void ClearBulkSelection()
        {
            _model.ClearBulkSelection();
            Refresh();
        }

        private void BeginBulkSeatPick()
        {
            if (_model.Screen != CrewHudScreen.BulkMap) return;
            if (_model.BulkEditIndex < 0 || _model.BulkEditIndex >= _model.BulkMapEntries.Count)
            {
                MyAPIGateway.Utilities.ShowMessage("HireCrew", "Select a crew row");
                return;
            }
            _model.BeginBulkPickSeat(_model.BulkEditIndex);
            Refresh();
        }

        private void BeginBulkWeaponPick()
        {
            if (_model.Screen != CrewHudScreen.BulkMap) return;
            if (_model.BulkEditIndex < 0 || _model.BulkEditIndex >= _model.BulkMapEntries.Count)
            {
                MyAPIGateway.Utilities.ShowMessage("HireCrew", "Select a crew row");
                return;
            }
            var entry = _model.BulkMapEntries[_model.BulkEditIndex];
            var crew = FindCrewById(CrewSession.Instance, entry != null ? entry.CrewId : null);
            if (crew == null || !CrewConfig.NeedsWeapon(crew.Role))
            {
                MyAPIGateway.Utilities.ShowMessage("HireCrew", "This role needs no weapon");
                return;
            }
            _model.BeginBulkPickWeapon(_model.BulkEditIndex);
            Refresh();
        }

        private bool IsBulkMapConfirmReady(CrewSession session)
        {
            return _model.IsBulkMapReady(id => FindCrewById(session, id));
        }

        private void ConfirmBulkAssign()
        {
            var session = CrewSession.Instance;
            if (session == null) return;
            if (!IsBulkMapConfirmReady(session))
            {
                MyAPIGateway.Utilities.ShowMessage("HireCrew", "Set seat (and weapon) for each crew");
                return;
            }

            var entries = new List<BulkAssignEntry>();
            for (int i = 0; i < _model.BulkMapEntries.Count; i++)
            {
                var e = _model.BulkMapEntries[i];
                if (e == null) continue;
                entries.Add(new BulkAssignEntry
                {
                    CrewId = e.CrewId,
                    SeatEntityId = e.SeatEntityId,
                    WeaponEntityId = e.WeaponEntityId
                });
            }

            session.ClientRequestBulkAssign(_model.GridEntityId, entries);
            _model.BulkMapBackToHome();
            _model.PruneBulkSelection(id => FindCrewById(session, id));
            Refresh();
        }

        private CrewRecord FindCrewById(CrewSession session, string crewId)
        {
            if (session == null || session.Store == null || string.IsNullOrEmpty(crewId))
                return null;
            return CrewHudModel.FindCrew(session.Store.All, crewId);
        }

        private void BeginAssign()
        {
            var crew = FindSelectedCrew(CrewSession.Instance);
            if (!_model.TryBeginAssignFromHome(crew))
            {
                MyAPIGateway.Utilities.ShowMessage("HireCrew",
                    crew == null ? "Select a crew member" : "Cannot assign");
                Refresh();
                return;
            }
            Refresh();
        }

        private void BeginQuarters()
        {
            var crew = FindSelectedCrew(CrewSession.Instance);
            if (!_model.TryBeginQuartersFromHome(crew))
            {
                MyAPIGateway.Utilities.ShowMessage("HireCrew",
                    crew == null ? "Select a crew member" : "Cannot open quarters");
                Refresh();
                return;
            }
            Refresh();
        }

        private void Dismiss()
        {
            var crew = FindSelectedCrew(CrewSession.Instance);
            if (!_model.TryBeginDismissFromHome(crew))
            {
                MyAPIGateway.Utilities.ShowMessage("HireCrew", "Select a crew member");
                Refresh();
                return;
            }
            Refresh();
        }

        private void BeginUnassign()
        {
            var crew = FindSelectedCrew(CrewSession.Instance);
            if (!_model.TryBeginUnassignFromHome(crew))
            {
                MyAPIGateway.Utilities.ShowMessage("HireCrew",
                    crew == null ? "Select a crew member" : "Cannot unassign");
                Refresh();
                return;
            }
            Refresh();
        }

        private void BeginTrainOrCancel()
        {
            var crew = FindSelectedCrew(CrewSession.Instance);
            if (crew == null)
            {
                MyAPIGateway.Utilities.ShowMessage("HireCrew", "Select a crew member");
                return;
            }
            if (CrewHudModel.CanCancelTrain(crew))
            {
                _model.BeginCancelTrainConfirm();
                Refresh();
                return;
            }
            if (!CrewHudModel.CanStartTrain(crew))
            {
                MyAPIGateway.Utilities.ShowMessage("HireCrew", "Cannot train");
                return;
            }
            _model.BeginTrainConfirm();
            Refresh();
        }

        private void WizardNext()
        {
            if (_model.Screen == CrewHudScreen.AssignCrew)
            {
                if (string.IsNullOrEmpty(_model.SelectedCrewId))
                {
                    MyAPIGateway.Utilities.ShowMessage("HireCrew", "Select a crew member");
                    return;
                }
                _model.WizardNextFromCrew();
            }
            else if (_model.Screen == CrewHudScreen.AssignSeat)
            {
                if (_model.SelectedSeatEntityId == 0)
                {
                    MyAPIGateway.Utilities.ShowMessage("HireCrew", "Select a seat");
                    return;
                }
                if (IsSelectedSeatOnlyRole(CrewSession.Instance))
                {
                    ConfirmSeatOnlyAssign();
                    return;
                }
                _model.WizardNextFromSeat();
            }
            else if (_model.Screen == CrewHudScreen.QuartersCrew)
            {
                if (string.IsNullOrEmpty(_model.SelectedCrewId))
                {
                    MyAPIGateway.Utilities.ShowMessage("HireCrew", "Select a crew member");
                    return;
                }
                _model.QuartersNextFromCrew();
            }
            Refresh();
        }

        private void WizardConfirm()
        {
            if (_model.Screen == CrewHudScreen.BulkMap)
            {
                ConfirmBulkAssign();
                return;
            }

            if (_model.Screen == CrewHudScreen.DismissPick)
            {
                var sessionDismiss = CrewSession.Instance;
                if (sessionDismiss == null || string.IsNullOrEmpty(_model.SelectedCrewId))
                {
                    MyAPIGateway.Utilities.ShowMessage("HireCrew", "Select a crew member");
                    return;
                }
                sessionDismiss.ClientRequestDismiss(_model.SelectedCrewId, _model.GridEntityId);
                _model.GoHome();
                Refresh();
                return;
            }

            if (_model.Screen == CrewHudScreen.UnassignPick)
            {
                var sessionUnassign = CrewSession.Instance;
                if (sessionUnassign == null || string.IsNullOrEmpty(_model.SelectedCrewId))
                {
                    MyAPIGateway.Utilities.ShowMessage("HireCrew", "Select a crew member");
                    return;
                }
                sessionUnassign.ClientRequestUnassign(_model.SelectedCrewId);
                _model.GoHome();
                Refresh();
                return;
            }

            if (_model.Screen == CrewHudScreen.TrainConfirm)
            {
                var sessionTrain = CrewSession.Instance;
                if (sessionTrain == null || string.IsNullOrEmpty(_model.SelectedCrewId))
                {
                    MyAPIGateway.Utilities.ShowMessage("HireCrew", "Select a crew member");
                    return;
                }
                sessionTrain.ClientRequestTrain(_model.SelectedCrewId);
                _model.GoHome();
                Refresh();
                return;
            }

            if (_model.Screen == CrewHudScreen.CancelTrainConfirm)
            {
                var sessionCancel = CrewSession.Instance;
                if (sessionCancel == null || string.IsNullOrEmpty(_model.SelectedCrewId))
                {
                    MyAPIGateway.Utilities.ShowMessage("HireCrew", "Select a crew member");
                    return;
                }
                sessionCancel.ClientRequestCancelTrain(_model.SelectedCrewId);
                _model.GoHome();
                Refresh();
                return;
            }

            if (_model.Screen == CrewHudScreen.QuartersSlots)
            {
                _model.GoHome();
                Refresh();
                return;
            }

            if (_model.Screen == CrewHudScreen.AssignSeat && IsSelectedSeatOnlyRole(CrewSession.Instance))
            {
                ConfirmSeatOnlyAssign();
                return;
            }

            if (string.IsNullOrEmpty(_model.SelectedCrewId) || _model.SelectedSeatEntityId == 0 || _model.SelectedWeaponEntityId == 0)
            {
                MyAPIGateway.Utilities.ShowMessage("HireCrew", "Select crew, seat, and weapon");
                return;
            }

            if (!TryValidateSeatForAssign())
                return;

            var session = CrewSession.Instance;
            if (session == null) return;
            session.ClientRequestAssign(_model.SelectedCrewId, _model.GridEntityId, _model.SelectedSeatEntityId, _model.SelectedWeaponEntityId);
            _model.GoHome();
            Refresh();
        }

        private void ConfirmSeatOnlyAssign()
        {
            if (string.IsNullOrEmpty(_model.SelectedCrewId) || _model.SelectedSeatEntityId == 0)
            {
                MyAPIGateway.Utilities.ShowMessage("HireCrew", "Select crew and seat");
                return;
            }

            if (!TryValidateSeatForAssign())
                return;

            var session = CrewSession.Instance;
            if (session == null) return;
            session.ClientRequestAssign(_model.SelectedCrewId, _model.GridEntityId, _model.SelectedSeatEntityId, 0);
            _model.GoHome();
            Refresh();
        }

        private bool TryValidateSeatForAssign()
        {
            IMyEntity seatEnt;
            if (!MyAPIGateway.Entities.TryGetEntityById(_model.SelectedSeatEntityId, out seatEnt))
            {
                MyAPIGateway.Utilities.ShowMessage("HireCrew", "Seat missing");
                return false;
            }

            var seat = seatEnt as IMyTerminalBlock;
            if (seat == null || !CrewStationLogic.IsAssignableSeat(seat))
            {
                MyAPIGateway.Utilities.ShowMessage("HireCrew", "Invalid seat");
                return false;
            }
            if (CrewStationLogic.IsSeatOccupiedByPlayer(seat))
            {
                MyAPIGateway.Utilities.ShowMessage("HireCrew", "Seat occupied");
                return false;
            }
            return true;
        }

        private CrewRecord FindSelectedCrew(CrewSession session)
        {
            if (session == null || session.Store == null || string.IsNullOrEmpty(_model.SelectedCrewId))
                return null;
            return CrewHudModel.FindCrew(session.Store.All, _model.SelectedCrewId);
        }

        private bool IsSelectedSeatOnlyRole(CrewSession session)
        {
            var crew = FindSelectedCrew(session);
            return crew != null && !CrewConfig.NeedsWeapon(crew.Role);
        }

        private List<CrewRecord> RosterForManagedGrid(CrewSession session)
        {
            if (session == null) return new List<CrewRecord>();
            return session.GetCrewForLocalOwner();
        }

        private void CollectSeatsAndWeapons(IMyCubeGrid grid)
        {
            _seatScratch.Clear();
            _weaponScratch.Clear();
            if (grid == null) return;

            var session = CrewSession.Instance;
            bool weaponsOk = session != null && session.WeaponAi != null && session.WeaponAi.IsReady;
            var grids = GetConstructGrids(grid);
            for (int g = 0; g < grids.Count; g++)
            {
                var part = grids[g];
                if (part == null) continue;
                _blockScratch.Clear();
                part.GetBlocks(_blockScratch);
                for (int i = 0; i < _blockScratch.Count; i++)
                {
                    var slim = _blockScratch[i];
                    if (slim == null) continue;
                    var fat = slim.FatBlock;
                    if (fat == null) continue;

                    var term = fat as IMyTerminalBlock;
                    if (term == null || term.MarkedForClose) continue;

                    if (CrewStationLogic.IsAssignableSeat(term))
                        _seatScratch.Add(term);

                    if (!weaponsOk) continue;
                    if (session.WeaponAi.IsCoreWeapon(term))
                        _weaponScratch.Add(term);
                }
            }
        }

        private void CollectAmenities(IMyCubeGrid grid)
        {
            _amenityScratch.Clear();
            if (grid == null) return;

            var grids = GetConstructGrids(grid);
            for (int g = 0; g < grids.Count; g++)
            {
                var part = grids[g];
                if (part == null) continue;
                _blockScratch.Clear();
                part.GetBlocks(_blockScratch);
                for (int i = 0; i < _blockScratch.Count; i++)
                {
                    var slim = _blockScratch[i];
                    if (slim == null) continue;
                    // Decorative Pack Shower is CubeBlock (not terminal); beds/bathrooms are terminal.
                    var cube = slim.FatBlock as IMyCubeBlock;
                    if (cube == null || cube.MarkedForClose) continue;
                    if (CrewAmenities.DetectKind(cube).HasValue)
                        _amenityScratch.Add(cube);
                }
            }
        }

        private static List<IMyCubeGrid> GetConstructGrids(IMyCubeGrid grid)
        {
            var grids = new List<IMyCubeGrid>();
            if (grid == null) return grids;
            try
            {
                MyAPIGateway.GridGroups.GetGroup(grid, GridLinkTypeEnum.Mechanical, grids);
            }
            catch
            {
                grids.Clear();
            }
            if (grids.Count == 0)
                grids.Add(grid);
            return grids;
        }

        private static long SeatId(IMyTerminalBlock s) { return s != null ? s.EntityId : 0L; }
        private static long WeaponId(IMyTerminalBlock w) { return w != null ? w.EntityId : 0L; }
        private static long AmenityId(IMyCubeBlock b) { return b != null ? b.EntityId : 0L; }
        private static bool AmenityMatches(IMyCubeBlock b, AmenityKind kind)
        {
            return CrewAmenities.MatchesKind(b, kind);
        }

        private static string BlockLabel(IMyCubeBlock block)
        {
            return CrewAmenities.BlockLabel(block);
        }
    }
}
