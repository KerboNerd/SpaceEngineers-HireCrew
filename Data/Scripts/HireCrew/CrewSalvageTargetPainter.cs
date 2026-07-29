using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRageMath;

namespace HireCrew
{
    /// <summary>
    /// Client salvage mark mode: LMB sets the wreck under the crosshair as this home ship's target.
    /// </summary>
    public static class CrewSalvageTargetPainter
    {
        private static bool _active;
        private static long _homeGridId;
        private static bool _wasLeft;

        public static bool IsActive { get { return _active; } }
        public static long ActiveHomeGridEntityId { get { return _homeGridId; } }

        public static void SetActive(bool active, long homeGridEntityId)
        {
            _active = active;
            _homeGridId = active ? homeGridEntityId : 0;
            _wasLeft = false;
        }

        public static void Update(CrewSession session)
        {
            if (!_active || session == null) return;
            if (MyAPIGateway.Session == null || MyAPIGateway.Session.Player == null) return;
            try
            {
                // Only chat blocks marking — IsCursorVisible is often true with Rich HUD overlays.
                if (MyAPIGateway.Gui.ChatEntryVisible)
                    return;
            }
            catch { }

            bool left = false;
            try { left = MyAPIGateway.Input.IsLeftMousePressed(); }
            catch { return; }

            bool leftNew = left && !_wasLeft;
            _wasLeft = left;
            if (!leftNew)
                return;

            if (_homeGridId == 0)
            {
                Tell("Salvage: mark mode has no home grid — /crew salvage again");
                return;
            }

            IMyCubeGrid target;
            double range = CrewConfig.SalvageScanRadiusMeters;
            if (range < 50.0) range = 50.0;
            if (!CrewLookRay.TryGridUnderCrosshair(range, out target) || target == null)
            {
                Tell("Salvage: no grid under crosshair");
                return;
            }

            // Reject only the real home construct (not "any owned grid").
            IMyEntity homeEnt;
            IMyCubeGrid homeGrid = null;
            if (MyAPIGateway.Entities.TryGetEntityById(_homeGridId, out homeEnt))
                homeGrid = homeEnt as IMyCubeGrid;
            if (homeGrid != null && !homeGrid.Closed && target.EntityId == homeGrid.EntityId)
            {
                Tell("Salvage: that is the home ship — LMB a wreck (mark still ON)");
                return;
            }
            if (homeGrid != null && !homeGrid.Closed)
            {
                try
                {
                    if (target.IsSameConstructAs(homeGrid))
                    {
                        Tell("Salvage: that is part of home — LMB a wreck (mark still ON)");
                        return;
                    }
                }
                catch { }
            }

            long homeId = _homeGridId;
            session.ClientRequestSalvageTargetEdit(homeId, target.EntityId);
            SetActive(false, 0);
            Tell("Salvage mark OFF");
        }

        public static bool TryRayGridUnderCrosshair(out IMyCubeGrid grid)
        {
            double range = CrewConfig.SalvageScanRadiusMeters;
            if (range < 50.0) range = 50.0;
            return CrewLookRay.TryGridUnderCrosshair(range, out grid);
        }

        private static void Tell(string message)
        {
            try
            {
                MyAPIGateway.Utilities.ShowMessage("HireCrew", message);
                MyAPIGateway.Utilities.ShowNotification("HireCrew: " + message, 2500);
            }
            catch { }
        }
    }
}
