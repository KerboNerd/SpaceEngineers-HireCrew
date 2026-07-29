using System.Collections.Generic;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRageMath;

namespace HireCrew
{
    /// <summary>Client-side frozen salvage zone wireframes (padded AABB at mark time).</summary>
    public static class CrewSalvageTargetHighlight
    {
        private struct ZoneMark
        {
            public long SeedGridEntityId;
            public bool HasZone;
            public BoundingBoxD Zone;
        }

        private static readonly Dictionary<long, ZoneMark> HomeToMark = new Dictionary<long, ZoneMark>();
        private static readonly List<IMyCubeGrid> GroupScratch = new List<IMyCubeGrid>(16);
        private static readonly List<Vector3D> DrawnMinScratch = new List<Vector3D>(8);
        private static readonly Color BorderColor = new Color(255, 160, 40, 180);
        private const float LineWidth = 0.02f;

        public static void ApplySync(IList<SalvageTargetEntry> entries)
        {
            HomeToMark.Clear();
            if (entries == null)
                return;

            for (int i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                if (e == null || e.HomeGridEntityId == 0)
                    continue;
                if (!e.HasZone && e.TargetGridEntityId == 0)
                    continue;

                var mark = new ZoneMark
                {
                    SeedGridEntityId = e.TargetGridEntityId,
                    HasZone = e.HasZone,
                    Zone = new BoundingBoxD(
                        new Vector3D(e.ZoneMinX, e.ZoneMinY, e.ZoneMinZ),
                        new Vector3D(e.ZoneMaxX, e.ZoneMaxY, e.ZoneMaxZ))
                };

                // Legacy seed-only: rebuild zone from live grid once for draw.
                if (!mark.HasZone && e.TargetGridEntityId != 0)
                {
                    IMyEntity ent;
                    if (MyAPIGateway.Entities.TryGetEntityById(e.TargetGridEntityId, out ent))
                    {
                        var g = ent as IMyCubeGrid;
                        if (g != null && !g.Closed)
                        {
                            mark.Zone = SalvageTargetStore.BuildZoneFromGrid(g);
                            mark.HasZone = true;
                        }
                    }
                }

                if (mark.HasZone
                    || mark.SeedGridEntityId != 0)
                    HomeToMark[e.HomeGridEntityId] = mark;
            }
        }

        public static bool HasMark(long homeGridEntityId)
        {
            ZoneMark m;
            if (TryGetMark(homeGridEntityId, out m))
                return m.HasZone || m.SeedGridEntityId != 0;
            return false;
        }

        /// <summary>Seed wreck id when known; non-zero also when only a zone exists (for HUD Send).</summary>
        public static long GetTarget(long homeGridEntityId)
        {
            ZoneMark m;
            if (!TryGetMark(homeGridEntityId, out m))
                return 0;
            if (m.SeedGridEntityId != 0)
                return m.SeedGridEntityId;
            return m.HasZone ? 1 : 0;
        }

        private static bool TryGetMark(long homeGridEntityId, out ZoneMark mark)
        {
            mark = default(ZoneMark);
            if (homeGridEntityId != 0 && HomeToMark.TryGetValue(homeGridEntityId, out mark))
                return true;

            IMyEntity ent;
            if (!MyAPIGateway.Entities.TryGetEntityById(homeGridEntityId, out ent) || ent == null)
                return false;
            var home = ent as IMyCubeGrid;
            if (home == null || home.Closed)
                return false;

            foreach (var kv in HomeToMark)
            {
                if (kv.Key == 0) continue;
                IMyEntity otherEnt;
                if (!MyAPIGateway.Entities.TryGetEntityById(kv.Key, out otherEnt) || otherEnt == null)
                    continue;
                var other = otherEnt as IMyCubeGrid;
                if (other != null && !other.Closed && other.IsSameConstructAs(home))
                {
                    mark = kv.Value;
                    return true;
                }
            }

            if (TryGetLinkedMark(home, GridLinkTypeEnum.Mechanical, out mark))
                return true;
            if (TryGetLinkedMark(home, GridLinkTypeEnum.Physical, out mark))
                return true;
            return false;
        }

        private static bool TryGetLinkedMark(IMyCubeGrid home, GridLinkTypeEnum link, out ZoneMark mark)
        {
            mark = default(ZoneMark);
            GroupScratch.Clear();
            try { MyAPIGateway.GridGroups.GetGroup(home, link, GroupScratch); }
            catch
            {
                GroupScratch.Clear();
                return false;
            }
            for (int i = 0; i < GroupScratch.Count; i++)
            {
                var g = GroupScratch[i];
                if (g == null || g.Closed) continue;
                ZoneMark m;
                if (HomeToMark.TryGetValue(g.EntityId, out m) && (m.HasZone || m.SeedGridEntityId != 0))
                {
                    mark = m;
                    return true;
                }
            }
            return false;
        }

        public static void ClearAll()
        {
            HomeToMark.Clear();
        }

        public static void Draw()
        {
            if (HomeToMark.Count == 0)
                return;

            DrawnMinScratch.Clear();
            foreach (var kv in HomeToMark)
            {
                ZoneMark m = kv.Value;
                if (!m.HasZone)
                    continue;
                if (AlreadyDrawn(m.Zone.Min))
                    continue;
                DrawnMinScratch.Add(m.Zone.Min);
                try
                {
                    MatrixD world = MatrixD.Identity;
                    BoundingBoxD box = m.Zone;
                    Color color = BorderColor;
                    MySimpleObjectDraw.DrawTransparentBox(
                        ref world,
                        ref box,
                        ref color,
                        MySimpleObjectRasterizer.Wireframe,
                        1,
                        LineWidth);
                }
                catch { }
            }
        }

        private static bool AlreadyDrawn(Vector3D min)
        {
            for (int i = 0; i < DrawnMinScratch.Count; i++)
            {
                if (Vector3D.DistanceSquared(DrawnMinScratch[i], min) < 0.01)
                    return true;
            }
            return false;
        }
    }
}
