using System.Collections.Generic;
using Sandbox.Common.ObjectBuilders;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRage.Utils;

namespace HireCrew
{
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_TextPanel), false,
        "HireCrew_Terminal", "HireCrew_Terminal_Small")]
    public sealed class CrewTerminalLogic : MyGameLogicComponent
    {
        private static bool _controlsRegistered;

        public string SelectedCrewId;
        public long SelectedSeatEntityId;
        public long SelectedWeaponEntityId;

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            NeedsUpdate = MyEntityUpdateEnum.NONE;
            EnsureControls();
        }

        public static bool IsCrewTerminal(IMyTerminalBlock block)
        {
            if (block == null) return false;
            var sub = block.BlockDefinition.SubtypeName;
            return sub == "HireCrew_Terminal" || sub == "HireCrew_Terminal_Small";
        }

        private static void EnsureControls()
        {
            if (_controlsRegistered) return;
            _controlsRegistered = true;

            List<IMyTerminalControl> existing;
            MyAPIGateway.TerminalControls.GetControls<IMyTextPanel>(out existing);

            AddHireButton("HireCrew_HireRecruit", "Hire Recruit", CrewTier.Recruit);
            AddHireButton("HireCrew_HireRegular", "Hire Regular", CrewTier.Regular);
            AddHireButton("HireCrew_HireElite", "Hire Elite", CrewTier.Elite);

            AddRosterList();
            AddSeatList();
            AddWeaponList();
            AddAssignButton();
            AddDismissButton();
        }

        private static void AddHireButton(string id, string title, CrewTier tier)
        {
            var btn = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlButton, IMyTextPanel>(id);
            btn.Title = MyStringId.GetOrCompute(title);
            btn.Tooltip = MyStringId.GetOrCompute(title);
            btn.Visible = IsCrewTerminal;
            btn.Enabled = IsCrewTerminal;
            btn.Action = b =>
            {
                var session = CrewSession.Instance;
                if (session == null || b == null || b.CubeGrid == null) return;
                session.ClientRequestHire(b.CubeGrid.EntityId, tier);
            };
            MyAPIGateway.TerminalControls.AddControl<IMyTextPanel>(btn);
        }

        private static void AddRosterList()
        {
            var list = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlListbox, IMyTextPanel>("HireCrew_Roster");
            list.Title = MyStringId.GetOrCompute("Crew Roster");
            list.VisibleRowsCount = 6;
            list.Multiselect = false;
            list.Visible = IsCrewTerminal;
            list.Enabled = IsCrewTerminal;
            list.ListContent = (b, items, selected) =>
            {
                items.Clear();
                selected.Clear();
                var logic = GetLogic(b);
                var store = CrewSession.Instance != null ? CrewSession.Instance.Store : null;
                if (store == null || b.CubeGrid == null) return;

                var gridId = b.CubeGrid.EntityId;
                foreach (var crew in store.GetForGrid(gridId))
                {
                    if (crew == null) continue;
                    var label = (crew.DisplayName ?? crew.CrewId) + " [" + crew.Status + "]";
                    var item = new MyTerminalControlListBoxItem(
                        MyStringId.GetOrCompute(label),
                        MyStringId.GetOrCompute(crew.CrewId ?? ""),
                        crew.CrewId);
                    items.Add(item);
                    if (logic != null && logic.SelectedCrewId == crew.CrewId)
                        selected.Add(item);
                }
            };
            list.ItemSelected = (b, selected) =>
            {
                var logic = GetLogic(b);
                if (logic == null) return;
                logic.SelectedCrewId = selected != null && selected.Count > 0
                    ? selected[0].UserData as string
                    : null;
            };
            MyAPIGateway.TerminalControls.AddControl<IMyTextPanel>(list);
        }

        private static void AddSeatList()
        {
            var list = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlListbox, IMyTextPanel>("HireCrew_Seats");
            list.Title = MyStringId.GetOrCompute("Empty Seats");
            list.VisibleRowsCount = 6;
            list.Multiselect = false;
            list.Visible = IsCrewTerminal;
            list.Enabled = IsCrewTerminal;
            list.ListContent = (b, items, selected) =>
            {
                items.Clear();
                selected.Clear();
                var logic = GetLogic(b);
                if (b.CubeGrid == null) return;

                var taken = GetTakenSeatIds(b.CubeGrid.EntityId);
                var seats = new List<IMyShipController>();
                var term = MyAPIGateway.TerminalActionsHelper.GetTerminalSystemForGrid(b.CubeGrid);
                if (term != null)
                    term.GetBlocksOfType(seats, s => s != null && !s.MarkedForClose && s.Pilot == null && !taken.Contains(s.EntityId));

                foreach (var seat in seats)
                {
                    var label = seat.CustomName ?? seat.DisplayNameText ?? ("Seat " + seat.EntityId);
                    var item = new MyTerminalControlListBoxItem(
                        MyStringId.GetOrCompute(label),
                        MyStringId.GetOrCompute(seat.EntityId.ToString()),
                        seat.EntityId);
                    items.Add(item);
                    if (logic != null && logic.SelectedSeatEntityId == seat.EntityId)
                        selected.Add(item);
                }
            };
            list.ItemSelected = (b, selected) =>
            {
                var logic = GetLogic(b);
                if (logic == null) return;
                logic.SelectedSeatEntityId = selected != null && selected.Count > 0 && selected[0].UserData is long
                    ? (long)selected[0].UserData
                    : 0L;
            };
            MyAPIGateway.TerminalControls.AddControl<IMyTextPanel>(list);
        }

        private static void AddWeaponList()
        {
            var list = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlListbox, IMyTextPanel>("HireCrew_Weapons");
            list.Title = MyStringId.GetOrCompute("Weapons");
            list.VisibleRowsCount = 6;
            list.Multiselect = false;
            list.Visible = IsCrewTerminal;
            list.Enabled = IsCrewTerminal;
            list.ListContent = (b, items, selected) =>
            {
                items.Clear();
                selected.Clear();
                var logic = GetLogic(b);
                var session = CrewSession.Instance;
                if (session == null || b.CubeGrid == null) return;

                var manned = GetMannedWeaponIds(b.CubeGrid.EntityId);
                var blocks = new List<IMyTerminalBlock>();
                var term = MyAPIGateway.TerminalActionsHelper.GetTerminalSystemForGrid(b.CubeGrid);
                if (term != null)
                    term.GetBlocksOfType(blocks, w => w != null && !w.MarkedForClose);

                foreach (var weapon in blocks)
                {
                    if (manned.Contains(weapon.EntityId)) continue;
                    if (session.WeaponAi == null || !session.WeaponAi.IsCoreWeapon(weapon)) continue;

                    var label = weapon.CustomName ?? weapon.DisplayNameText ?? ("Weapon " + weapon.EntityId);
                    var item = new MyTerminalControlListBoxItem(
                        MyStringId.GetOrCompute(label),
                        MyStringId.GetOrCompute(weapon.EntityId.ToString()),
                        weapon.EntityId);
                    items.Add(item);
                    if (logic != null && logic.SelectedWeaponEntityId == weapon.EntityId)
                        selected.Add(item);
                }
            };
            list.ItemSelected = (b, selected) =>
            {
                var logic = GetLogic(b);
                if (logic == null) return;
                logic.SelectedWeaponEntityId = selected != null && selected.Count > 0 && selected[0].UserData is long
                    ? (long)selected[0].UserData
                    : 0L;
            };
            MyAPIGateway.TerminalControls.AddControl<IMyTextPanel>(list);
        }

        private static void AddAssignButton()
        {
            var btn = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlButton, IMyTextPanel>("HireCrew_Assign");
            btn.Title = MyStringId.GetOrCompute("Assign");
            btn.Tooltip = MyStringId.GetOrCompute("Assign selected unassigned crew to seat + weapon");
            btn.Visible = IsCrewTerminal;
            btn.Enabled = b =>
            {
                if (!IsCrewTerminal(b)) return false;
                var logic = GetLogic(b);
                if (logic == null || string.IsNullOrEmpty(logic.SelectedCrewId)) return false;
                if (logic.SelectedSeatEntityId == 0 || logic.SelectedWeaponEntityId == 0) return false;
                var store = CrewSession.Instance != null ? CrewSession.Instance.Store : null;
                if (store == null) return false;
                var crew = store.Get(logic.SelectedCrewId);
                return crew != null && crew.Status == CrewStatus.Unassigned;
            };
            btn.Action = b =>
            {
                var session = CrewSession.Instance;
                var logic = GetLogic(b);
                if (session == null || logic == null || b == null || b.CubeGrid == null) return;
                if (string.IsNullOrEmpty(logic.SelectedCrewId) || logic.SelectedSeatEntityId == 0 || logic.SelectedWeaponEntityId == 0)
                    return;
                session.ClientRequestAssign(
                    logic.SelectedCrewId,
                    b.CubeGrid.EntityId,
                    logic.SelectedSeatEntityId,
                    logic.SelectedWeaponEntityId);
            };
            MyAPIGateway.TerminalControls.AddControl<IMyTextPanel>(btn);
        }

        private static void AddDismissButton()
        {
            var btn = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlButton, IMyTextPanel>("HireCrew_Dismiss");
            btn.Title = MyStringId.GetOrCompute("Dismiss");
            btn.Tooltip = MyStringId.GetOrCompute("Dismiss selected crew");
            btn.Visible = IsCrewTerminal;
            btn.Enabled = b =>
            {
                if (!IsCrewTerminal(b)) return false;
                var logic = GetLogic(b);
                if (logic == null || string.IsNullOrEmpty(logic.SelectedCrewId)) return false;
                var store = CrewSession.Instance != null ? CrewSession.Instance.Store : null;
                return store != null && store.Get(logic.SelectedCrewId) != null;
            };
            btn.Action = b =>
            {
                var session = CrewSession.Instance;
                var logic = GetLogic(b);
                if (session == null || logic == null || b == null || b.CubeGrid == null) return;
                if (string.IsNullOrEmpty(logic.SelectedCrewId)) return;
                session.ClientRequestDismiss(logic.SelectedCrewId, b.CubeGrid.EntityId);
                logic.SelectedCrewId = null;
            };
            MyAPIGateway.TerminalControls.AddControl<IMyTextPanel>(btn);
        }

        private static CrewTerminalLogic GetLogic(IMyTerminalBlock block)
        {
            if (block == null || block.GameLogic == null) return null;
            return block.GameLogic.GetAs<CrewTerminalLogic>();
        }

        private static HashSet<long> GetTakenSeatIds(long gridEntityId)
        {
            var taken = new HashSet<long>();
            var store = CrewSession.Instance != null ? CrewSession.Instance.Store : null;
            if (store == null) return taken;
            foreach (var crew in store.GetForGrid(gridEntityId))
            {
                if (crew != null && crew.Status == CrewStatus.Seated && crew.SeatEntityId.HasValue)
                    taken.Add(crew.SeatEntityId.Value);
            }
            return taken;
        }

        private static HashSet<long> GetMannedWeaponIds(long gridEntityId)
        {
            var manned = new HashSet<long>();
            var store = CrewSession.Instance != null ? CrewSession.Instance.Store : null;
            if (store == null) return manned;
            foreach (var crew in store.GetForGrid(gridEntityId))
            {
                if (crew != null && crew.Status == CrewStatus.Seated && crew.WeaponEntityId.HasValue)
                    manned.Add(crew.WeaponEntityId.Value);
            }
            return manned;
        }
    }
}
