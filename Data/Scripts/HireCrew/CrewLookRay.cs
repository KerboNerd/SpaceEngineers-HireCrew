using System.Collections.Generic;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRageMath;

namespace HireCrew
{
    /// <summary>
    /// Shared camera look-ray helpers. Skips the local character and accepts armor.
    /// Keen's GetLineIntersectionExactGrid writes distance-squared into its dist arg.
    /// </summary>
    public static class CrewLookRay
    {
        private static readonly List<IHitInfo> HitScratch = new List<IHitInfo>(16);
        private static readonly HashSet<IMyEntity> EntityScratch = new HashSet<IMyEntity>();

        public static bool TryGridUnderCrosshair(double rangeMeters, out IMyCubeGrid grid)
        {
            Vector3I cell;
            double distSq;
            return TryGridCellUnderCrosshair(rangeMeters, out grid, out cell, out distSq);
        }

        public static bool TryGridCellUnderCrosshair(
            double rangeMeters,
            out IMyCubeGrid grid,
            out Vector3I cell,
            out double distSq)
        {
            grid = null;
            cell = Vector3I.Zero;
            distSq = 0;
            if (rangeMeters < 1.0) rangeMeters = 1.0;

            Vector3D from;
            Vector3D to;
            Vector3D forward;
            if (!TryCameraLine(rangeMeters, out from, out to, out forward))
                return false;

            var line = new LineD(from, to);

            // Physics hit on a grid is enough to identify the target (salvage mark).
            IMyCubeGrid physGrid;
            Vector3D physHitPos;
            if (TryPhysicsGridHit(from, to, forward, out physGrid, out physHitPos) && physGrid != null)
            {
                grid = physGrid;
                distSq = Vector3D.DistanceSquared(from, physHitPos);
                Vector3I c = Vector3I.Zero;
                double dSq = 0;
                var probe = line;
                if (physGrid.GetLineIntersectionExactGrid(ref probe, ref c, ref dSq))
                    cell = c;
                return true;
            }

            return TryClosestGridOnLine(line, rangeMeters, out grid, out cell, out distSq);
        }

        /// <summary>
        /// Slim under crosshair + local-grid position for path waypoints.
        /// Works on armor (BlockEntityId stays 0; Local* is used).
        /// </summary>
        public static bool TrySlimUnderCrosshair(
            double rangeMeters,
            out IMySlimBlock slim,
            out IMyCubeGrid grid,
            out Vector3D local,
            out long fatBlockEntityId)
        {
            slim = null;
            grid = null;
            local = Vector3D.Zero;
            fatBlockEntityId = 0;

            Vector3I cell;
            double distSq;
            if (!TryGridCellUnderCrosshair(rangeMeters, out grid, out cell, out distSq) || grid == null)
                return false;

            // Prefer exact cell; if physics only found the grid, resolve cell now.
            slim = grid.GetCubeBlock(cell);
            if (slim == null)
            {
                Vector3D from;
                Vector3D to;
                Vector3D forward;
                if (!TryCameraLine(rangeMeters, out from, out to, out forward))
                    return false;
                var line = new LineD(from, to);
                Vector3I c = Vector3I.Zero;
                double dSq = 0;
                if (!grid.GetLineIntersectionExactGrid(ref line, ref c, ref dSq))
                    return false;
                cell = c;
                slim = grid.GetCubeBlock(cell);
                if (slim == null)
                    return false;
            }

            Vector3D world;
            if (slim.FatBlock != null)
            {
                fatBlockEntityId = slim.FatBlock.EntityId;
                world = slim.FatBlock.GetPosition();
            }
            else
            {
                world = grid.GridIntegerToWorld(cell);
            }

            local = Vector3D.Transform(world, grid.WorldMatrixNormalizedInv);
            return true;
        }

        private static bool TryCameraLine(
            double rangeMeters,
            out Vector3D from,
            out Vector3D to,
            out Vector3D forward)
        {
            from = Vector3D.Zero;
            to = Vector3D.Zero;
            forward = Vector3D.Forward;
            var cam = MyAPIGateway.Session != null ? MyAPIGateway.Session.Camera : null;
            if (cam == null) return false;
            forward = cam.WorldMatrix.Forward;
            // Start slightly ahead so the local character capsule is less likely to eat the ray.
            from = cam.WorldMatrix.Translation + forward * 0.35;
            to = cam.WorldMatrix.Translation + forward * rangeMeters;
            return true;
        }

        private static bool TryPhysicsGridHit(
            Vector3D from,
            Vector3D to,
            Vector3D forward,
            out IMyCubeGrid grid,
            out Vector3D hitPos)
        {
            grid = null;
            hitPos = Vector3D.Zero;
            HitScratch.Clear();

            bool listed = false;
            try
            {
                MyAPIGateway.Physics.CastRay(from, to, HitScratch);
                listed = true;
            }
            catch
            {
                HitScratch.Clear();
            }

            if (!listed || HitScratch.Count == 0)
            {
                IHitInfo single;
                if (MyAPIGateway.Physics.CastRay(from, to, out single)
                    && single != null
                    && single.HitEntity != null)
                    HitScratch.Add(single);
            }

            long localCharId = 0;
            try
            {
                var ch = MyAPIGateway.Session != null && MyAPIGateway.Session.Player != null
                    ? MyAPIGateway.Session.Player.Character
                    : null;
                if (ch != null) localCharId = ch.EntityId;
            }
            catch { }

            for (int i = 0; i < HitScratch.Count; i++)
            {
                var hit = HitScratch[i];
                if (hit == null || hit.HitEntity == null) continue;
                if (hit.HitEntity is IMyCharacter) continue;
                if (localCharId != 0 && hit.HitEntity.EntityId == localCharId) continue;

                var g = hit.HitEntity as IMyCubeGrid;
                var cube = hit.HitEntity as IMyCubeBlock;
                if (g == null && cube != null)
                    g = cube.CubeGrid;
                if (g == null || g.Closed) continue;

                grid = g;
                hitPos = hit.Position;
                return true;
            }

            // Single-hit landed on character: nudge past it and retry once.
            if (HitScratch.Count > 0 && HitScratch[0] != null && HitScratch[0].HitEntity != null)
            {
                var first = HitScratch[0].HitEntity;
                bool isChar = first is IMyCharacter
                    || (localCharId != 0 && first.EntityId == localCharId);
                if (isChar)
                {
                    Vector3D restart = HitScratch[0].Position + forward * 0.25;
                    IHitInfo second;
                    if (MyAPIGateway.Physics.CastRay(restart, to, out second)
                        && second != null
                        && second.HitEntity != null)
                    {
                        var g = second.HitEntity as IMyCubeGrid;
                        var cube = second.HitEntity as IMyCubeBlock;
                        if (g == null && cube != null)
                            g = cube.CubeGrid;
                        if (g != null && !g.Closed)
                        {
                            grid = g;
                            hitPos = second.Position;
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        private static bool TryClosestGridOnLine(
            LineD line,
            double rangeMeters,
            out IMyCubeGrid grid,
            out Vector3I cell,
            out double distSq)
        {
            grid = null;
            cell = Vector3I.Zero;
            distSq = 0;

            EntityScratch.Clear();
            try { MyAPIGateway.Entities.GetEntities(EntityScratch); }
            catch
            {
                EntityScratch.Clear();
                return false;
            }

            double bestDistSq = double.MaxValue;
            IMyCubeGrid bestGrid = null;
            Vector3I bestCell = Vector3I.Zero;
            Vector3D origin = line.From;
            double rangeSq = rangeMeters * rangeMeters;

            foreach (var ent in EntityScratch)
            {
                var g = ent as IMyCubeGrid;
                if (g == null || g.Closed) continue;

                // Reject grids whose AABB is far from the camera.
                if (Vector3D.DistanceSquared(origin, g.WorldAABB.Center) > rangeSq * 1.25)
                    continue;

                // Ray vs AABB first (cheap + works when ExactGrid is picky).
                double aabbDist;
                if (!RayAabbDistance(line, g.WorldAABB, out aabbDist) || aabbDist > rangeMeters)
                    continue;

                Vector3I c = Vector3I.Zero;
                double dSq = 0;
                var probe = line;
                bool exact = g.GetLineIntersectionExactGrid(ref probe, ref c, ref dSq);
                double scoreSq;
                if (exact)
                {
                    // Keen writes distance-squared here.
                    scoreSq = dSq;
                    if (scoreSq < 0) scoreSq = aabbDist * aabbDist;
                }
                else
                {
                    // Still accept AABB hit for grid targeting (wreck may be deformed).
                    scoreSq = aabbDist * aabbDist;
                    c = Vector3I.Zero;
                }

                if (scoreSq > rangeSq || scoreSq >= bestDistSq)
                    continue;

                bestDistSq = scoreSq;
                bestGrid = g;
                bestCell = c;
            }

            if (bestGrid == null)
                return false;

            grid = bestGrid;
            cell = bestCell;
            distSq = bestDistSq;
            return true;
        }

        private static bool RayAabbDistance(LineD line, BoundingBoxD box, out double distance)
        {
            distance = 0;
            var ray = new RayD(line.From, line.Direction);
            double? t = box.Intersects(ray);
            if (!t.HasValue || t.Value < 0)
                return false;
            distance = t.Value;
            return true;
        }
    }
}
