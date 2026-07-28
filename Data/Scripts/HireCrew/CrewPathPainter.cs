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
                if (MyAPIGateway.Gui.ChatEntryVisible || MyAPIGateway.Gui.IsCursorVisible)
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

            IMyCubeBlock block;
            Vector3D local;
            if (!TryRayBlock(_gridId, out block, out local))
                return;

            session.ClientRequestPathEdit(new PathEditRequest
            {
                GridEntityId = _gridId,
                Op = 0,
                BlockEntityId = block.EntityId,
                LocalX = local.X,
                LocalY = local.Y,
                LocalZ = local.Z
            });
        }

        public static bool TryRayGridUnderCrosshair(out IMyCubeGrid grid)
        {
            grid = null;
            IMyCubeBlock block;
            Vector3D local;
            if (!TryRayAnyBlock(out block, out local) || block == null)
                return false;
            grid = block.CubeGrid;
            return grid != null;
        }

        private static bool TryRayBlock(long gridId, out IMyCubeBlock block, out Vector3D local)
        {
            block = null;
            local = Vector3D.Zero;
            if (!TryRayAnyBlock(out block, out local) || block == null || block.CubeGrid == null)
                return false;
            return block.CubeGrid.EntityId == gridId;
        }

        private static bool TryRayAnyBlock(out IMyCubeBlock block, out Vector3D local)
        {
            block = null;
            local = Vector3D.Zero;
            var cam = MyAPIGateway.Session != null ? MyAPIGateway.Session.Camera : null;
            if (cam == null) return false;

            Vector3D from = cam.WorldMatrix.Translation;
            Vector3D to = from + cam.WorldMatrix.Forward * 40.0;
            IHitInfo hit;
            if (!MyAPIGateway.Physics.CastRay(from, to, out hit)
                || hit == null
                || hit.HitEntity == null)
                return false;

            var grid = hit.HitEntity as IMyCubeGrid;
            var cube = hit.HitEntity as IMyCubeBlock;
            if (grid == null && cube != null)
                grid = cube.CubeGrid;
            if (grid == null)
                return false;

            var line = new LineD(from, to);
            Vector3I cell = Vector3I.Zero;
            double dist = 0;
            if (!grid.GetLineIntersectionExactGrid(ref line, ref cell, ref dist))
                return false;

            var slim = grid.GetCubeBlock(cell);
            if (slim == null || slim.FatBlock == null)
                return false;

            block = slim.FatBlock;
            local = Vector3D.Transform(block.GetPosition(), grid.WorldMatrixNormalizedInv);
            return true;
        }
    }
}
