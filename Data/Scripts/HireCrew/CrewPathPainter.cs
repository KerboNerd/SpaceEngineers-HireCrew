using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRageMath;

namespace HireCrew
{
    /// <summary>
    /// Client path tool: LMB appends a waypoint on the paint grid; RMB finishes Exit.
    /// </summary>
    public static class CrewPathPainter
    {
        private const double PaintRangeMeters = 80.0;

        private static bool _active;
        private static long _gridId;
        private static bool _wasLeft;
        private static bool _wasRight;

        public static bool IsActive { get { return _active; } }
        public static long ActiveGridEntityId { get { return _gridId; } }

        public static void SetActive(bool active, long gridEntityId)
        {
            _active = active;
            _gridId = active ? gridEntityId : 0;
            _wasLeft = false;
            _wasRight = false;
        }

        public static void Update(CrewSession session)
        {
            if (!_active || session == null) return;
            if (MyAPIGateway.Session == null || MyAPIGateway.Session.Player == null) return;
            try
            {
                // Only chat blocks painting — IsCursorVisible is often true with Rich HUD overlays.
                if (MyAPIGateway.Gui.ChatEntryVisible)
                    return;
            }
            catch { }

            bool left = false;
            bool right = false;
            try
            {
                left = MyAPIGateway.Input.IsLeftMousePressed();
                right = MyAPIGateway.Input.IsRightMousePressed();
            }
            catch { return; }

            bool leftNew = left && !_wasLeft;
            bool rightNew = right && !_wasRight;
            _wasLeft = left;
            _wasRight = right;

            if (leftNew)
                TryClick(session, finish: false);
            else if (rightNew)
                TryClick(session, finish: true);
        }

        private static void TryClick(CrewSession session, bool finish)
        {
            if (finish)
            {
                session.ClientRequestPathEdit(new PathEditRequest
                {
                    GridEntityId = _gridId,
                    Op = 2
                });
                return;
            }

            IMySlimBlock slim;
            IMyCubeGrid grid;
            Vector3D local;
            long fatId;
            if (!CrewLookRay.TrySlimUnderCrosshair(PaintRangeMeters, out slim, out grid, out local, out fatId)
                || grid == null
                || grid.EntityId != _gridId)
                return;

            session.ClientRequestPathEdit(new PathEditRequest
            {
                GridEntityId = _gridId,
                Op = 0,
                BlockEntityId = fatId,
                LocalX = local.X,
                LocalY = local.Y,
                LocalZ = local.Z
            });
        }

        public static bool TryRayGridUnderCrosshair(out IMyCubeGrid grid)
        {
            return CrewLookRay.TryGridUnderCrosshair(PaintRangeMeters, out grid);
        }
    }
}
