using System.Collections.Generic;
using System.Text;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage.Game.ModAPI;
using VRage.Utils;

namespace HireCrew
{
    /// <summary>
    /// Injects cockpit terminal button + toolbar action via CustomControlGetter/CustomActionGetter.
    /// Avoids AddControl on IMyCockpit during LoadData — that can mark cockpit controls "created"
    /// before vanilla registers them, wiping the rest of the cockpit terminal.
    /// </summary>
    public static class CrewCockpitControls
    {
        public const string ControlId = "HireCrew_OpenManagement";
        public const string ActionId = "HireCrew_OpenManagement";

        private static bool _hooksRegistered;
        private static IMyTerminalControlButton _openButton;
        private static IMyTerminalAction _openAction;

        public static void EnsureTerminalControls()
        {
            if (_hooksRegistered) return;
            if (MyAPIGateway.TerminalControls == null) return;
            if (MyAPIGateway.Utilities != null && MyAPIGateway.Utilities.IsDedicated) return;

            _hooksRegistered = true;
            MyAPIGateway.TerminalControls.CustomControlGetter += OnCustomControlGetter;
            MyAPIGateway.TerminalControls.CustomActionGetter += OnCustomActionGetter;
        }

        public static void Unload()
        {
            if (!_hooksRegistered) return;
            _hooksRegistered = false;
            try
            {
                if (MyAPIGateway.TerminalControls != null)
                {
                    MyAPIGateway.TerminalControls.CustomControlGetter -= OnCustomControlGetter;
                    MyAPIGateway.TerminalControls.CustomActionGetter -= OnCustomActionGetter;
                }
            }
            catch { }
            _openButton = null;
            _openAction = null;
        }

        private static void OnCustomControlGetter(IMyTerminalBlock block, List<IMyTerminalControl> controls)
        {
            if (controls == null || !(block is IMyCockpit)) return;
            if (!CanUse(block)) return;

            EnsureButtonCreated();
            if (_openButton == null) return;
            if (controls.Contains(_openButton)) return;
            controls.Add(_openButton);
        }

        private static void OnCustomActionGetter(IMyTerminalBlock block, List<IMyTerminalAction> actions)
        {
            if (actions == null || !(block is IMyCockpit)) return;
            if (!CanUse(block)) return;

            EnsureActionCreated();
            if (_openAction == null) return;
            if (actions.Contains(_openAction)) return;
            actions.Add(_openAction);
        }

        private static void EnsureButtonCreated()
        {
            if (_openButton != null) return;
            if (MyAPIGateway.TerminalControls == null) return;

            // Created lazily the first time a cockpit terminal is opened — vanilla controls already exist.
            var open = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlButton, IMyCockpit>(ControlId);
            if (open == null) return;
            open.Title = MyStringId.GetOrCompute("Open Crew Management");
            open.Tooltip = MyStringId.GetOrCompute("Open the HireCrew management UI for this ship.");
            open.SupportsMultipleBlocks = false;
            open.Enabled = CanUse;
            open.Visible = b => true;
            open.Action = OpenFromCockpit;
            // Do not AddControl — inject only via CustomControlGetter.
            _openButton = open;
        }

        private static void EnsureActionCreated()
        {
            if (_openAction != null) return;
            if (MyAPIGateway.TerminalControls == null) return;

            var action = MyAPIGateway.TerminalControls.CreateAction<IMyCockpit>(ActionId);
            if (action == null) return;
            action.Name = new StringBuilder("Open Crew Management");
            action.Icon = @"Textures\GUI\Icons\Actions\Toggle.dds";
            action.ValidForGroups = false;
            action.Enabled = CanUse;
            action.Action = OpenFromCockpit;
            action.Writer = (b, sb) =>
            {
                sb.Clear();
                sb.Append("Crew");
            };
            // Do not AddAction — inject only via CustomActionGetter.
            _openAction = action;
        }

        private static void OpenFromCockpit(IMyTerminalBlock block)
        {
            var session = CrewSession.Instance;
            if (session == null || block == null || block.CubeGrid == null) return;
            session.ClientToggleCrewUi(block.CubeGrid.EntityId);
        }

        private static bool CanUse(IMyTerminalBlock block)
        {
            try
            {
                var cockpit = block as IMyCockpit;
                if (cockpit == null || cockpit.CubeGrid == null) return false;
                var session = CrewSession.Instance;
                return session != null && session.CanLocalPlayerManage(cockpit.CubeGrid);
            }
            catch
            {
                return false;
            }
        }
    }
}
