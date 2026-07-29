using System;
using System.Collections.Generic;
using VRage.Game.ModAPI;
using VRageMath;

namespace HireCrew
{
    /// <summary>Per home-ship frozen salvage zone (padded AABB) for Salvage Ops.</summary>
    public sealed class SalvageTargetStore
    {
        private const int FormatVersionV1 = 1;
        private const int FormatVersion = 2;

        public struct Mark
        {
            public long SeedGridEntityId;
            public bool HasZone;
            public Vector3D Min;
            public Vector3D Max;
        }

        private readonly Dictionary<long, Mark> _homeToMark = new Dictionary<long, Mark>();

        public long GetTarget(long homeGridEntityId)
        {
            Mark m;
            return _homeToMark.TryGetValue(homeGridEntityId, out m) ? m.SeedGridEntityId : 0;
        }

        public bool HasMark(long homeGridEntityId)
        {
            Mark m;
            if (!_homeToMark.TryGetValue(homeGridEntityId, out m))
                return false;
            return m.HasZone || m.SeedGridEntityId != 0;
        }

        public bool TryGetZone(long homeGridEntityId, out BoundingBoxD zone)
        {
            zone = default(BoundingBoxD);
            Mark m;
            if (!_homeToMark.TryGetValue(homeGridEntityId, out m) || !m.HasZone)
                return false;
            if (!CrewSalvageRules.IsValidZone(m.Min.X, m.Min.Y, m.Min.Z, m.Max.X, m.Max.Y, m.Max.Z))
                return false;
            zone = new BoundingBoxD(m.Min, m.Max);
            return true;
        }

        /// <summary>
        /// Resolve zone for any grid on the home construct (subgrids / rotors share one mark).
        /// Legacy seed-only marks: rebuild padded zone from live seed grid when possible.
        /// </summary>
        public bool TryGetZoneForConstruct(
            IMyCubeGrid homeGrid,
            Func<long, IMyCubeGrid> resolveGrid,
            out BoundingBoxD zone,
            out long seedGridEntityId)
        {
            zone = default(BoundingBoxD);
            seedGridEntityId = 0;
            if (homeGrid == null) return false;

            if (TryResolveMark(homeGrid.EntityId, resolveGrid, out zone, out seedGridEntityId))
                return true;

            if (resolveGrid == null || _homeToMark.Count == 0)
                return false;

            foreach (var kv in _homeToMark)
            {
                if (kv.Key == 0) continue;
                if (kv.Key == homeGrid.EntityId) continue;
                var other = resolveGrid(kv.Key);
                if (other == null || other.Closed || !other.IsSameConstructAs(homeGrid))
                    continue;
                if (TryResolveMark(kv.Key, resolveGrid, out zone, out seedGridEntityId))
                    return true;
            }
            return false;
        }

        private bool TryResolveMark(
            long homeId,
            Func<long, IMyCubeGrid> resolveGrid,
            out BoundingBoxD zone,
            out long seedGridEntityId)
        {
            zone = default(BoundingBoxD);
            seedGridEntityId = 0;
            Mark m;
            if (!_homeToMark.TryGetValue(homeId, out m))
                return false;

            seedGridEntityId = m.SeedGridEntityId;
            if (m.HasZone
                && CrewSalvageRules.IsValidZone(m.Min.X, m.Min.Y, m.Min.Z, m.Max.X, m.Max.Y, m.Max.Z))
            {
                zone = new BoundingBoxD(m.Min, m.Max);
                return true;
            }

            if (m.SeedGridEntityId == 0 || resolveGrid == null)
                return false;
            var seed = resolveGrid(m.SeedGridEntityId);
            if (seed == null || seed.Closed)
                return false;

            zone = BuildZoneFromGrid(seed);
            // Upgrade in place so later syncs/saves carry the frozen zone.
            m.HasZone = true;
            m.Min = zone.Min;
            m.Max = zone.Max;
            _homeToMark[homeId] = m;
            return true;
        }

        public static BoundingBoxD BuildZoneFromGrid(IMyCubeGrid grid)
        {
            BoundingBoxD box = grid != null ? grid.WorldAABB : new BoundingBoxD();
            double minX, minY, minZ, maxX, maxY, maxZ;
            CrewSalvageRules.BuildPaddedZone(
                box.Min.X, box.Min.Y, box.Min.Z,
                box.Max.X, box.Max.Y, box.Max.Z,
                CrewConfig.SalvageZonePadMeters,
                out minX, out minY, out minZ, out maxX, out maxY, out maxZ);
            return new BoundingBoxD(new Vector3D(minX, minY, minZ), new Vector3D(maxX, maxY, maxZ));
        }

        /// <summary>Legacy seed lookup for construct (may be 0 when zone-only).</summary>
        public long GetTargetForConstruct(IMyCubeGrid homeGrid, Func<long, IMyCubeGrid> resolveGrid)
        {
            BoundingBoxD zone;
            long seed;
            if (TryGetZoneForConstruct(homeGrid, resolveGrid, out zone, out seed))
                return seed;
            return 0;
        }

        public long FindTargetWhereHome(Func<long, bool> isLinkedHome)
        {
            if (isLinkedHome == null || _homeToMark.Count == 0)
                return 0;
            foreach (var kv in _homeToMark)
            {
                if (kv.Key == 0) continue;
                if (!kv.Value.HasZone && kv.Value.SeedGridEntityId == 0) continue;
                if (isLinkedHome(kv.Key))
                    return kv.Value.SeedGridEntityId != 0 ? kv.Value.SeedGridEntityId : kv.Key;
            }
            return 0;
        }

        public bool TryFindZoneWhereHome(Func<long, bool> isLinkedHome, out BoundingBoxD zone, out long seed)
        {
            zone = default(BoundingBoxD);
            seed = 0;
            if (isLinkedHome == null || _homeToMark.Count == 0)
                return false;
            foreach (var kv in _homeToMark)
            {
                if (kv.Key == 0) continue;
                if (!isLinkedHome(kv.Key)) continue;
                if (TryResolveMark(kv.Key, null, out zone, out seed) && kv.Value.HasZone)
                    return true;
                // TryResolveMark with null resolve can't rebuild seed-only — allow HasZone path only.
                Mark m = kv.Value;
                if (m.HasZone
                    && CrewSalvageRules.IsValidZone(m.Min.X, m.Min.Y, m.Min.Z, m.Max.X, m.Max.Y, m.Max.Z))
                {
                    zone = new BoundingBoxD(m.Min, m.Max);
                    seed = m.SeedGridEntityId;
                    return true;
                }
            }
            return false;
        }

        public bool HasTarget(long homeGridEntityId)
        {
            return HasMark(homeGridEntityId);
        }

        public void Set(long homeGridEntityId, long targetGridEntityId)
        {
            if (homeGridEntityId == 0)
                throw new ArgumentException("homeGridEntityId");
            if (targetGridEntityId == 0)
            {
                _homeToMark.Remove(homeGridEntityId);
                return;
            }
            _homeToMark[homeGridEntityId] = new Mark
            {
                SeedGridEntityId = targetGridEntityId,
                HasZone = false
            };
        }

        public void SetZoneForHomeIds(IList<long> homeGridEntityIds, long seedGridEntityId, BoundingBoxD zone)
        {
            if (homeGridEntityIds == null || homeGridEntityIds.Count == 0)
                return;
            ClearHomeIds(homeGridEntityIds);
            if (!CrewSalvageRules.IsValidZone(
                    zone.Min.X, zone.Min.Y, zone.Min.Z,
                    zone.Max.X, zone.Max.Y, zone.Max.Z))
                return;

            var mark = new Mark
            {
                SeedGridEntityId = seedGridEntityId,
                HasZone = true,
                Min = zone.Min,
                Max = zone.Max
            };
            for (int i = 0; i < homeGridEntityIds.Count; i++)
            {
                long id = homeGridEntityIds[i];
                if (id != 0)
                    _homeToMark[id] = mark;
            }
        }

        /// <summary>Obsolete: seed-only stamp. Prefer <see cref="SetZoneForHomeIds"/>.</summary>
        public void SetForHomeIds(IList<long> homeGridEntityIds, long targetGridEntityId)
        {
            if (homeGridEntityIds == null || homeGridEntityIds.Count == 0)
                return;
            ClearHomeIds(homeGridEntityIds);
            if (targetGridEntityId == 0)
                return;
            var mark = new Mark { SeedGridEntityId = targetGridEntityId, HasZone = false };
            for (int i = 0; i < homeGridEntityIds.Count; i++)
            {
                long id = homeGridEntityIds[i];
                if (id != 0)
                    _homeToMark[id] = mark;
            }
        }

        public void SetForConstruct(IMyCubeGrid homeGrid, long targetGridEntityId, Func<long, IMyCubeGrid> resolveGrid)
        {
            if (homeGrid == null)
                throw new ArgumentNullException("homeGrid");

            ClearConstruct(homeGrid, resolveGrid);
            if (targetGridEntityId == 0)
                return;
            _homeToMark[homeGrid.EntityId] = new Mark
            {
                SeedGridEntityId = targetGridEntityId,
                HasZone = false
            };
        }

        public bool Clear(long homeGridEntityId)
        {
            return _homeToMark.Remove(homeGridEntityId);
        }

        public void ClearHomeIds(IList<long> homeGridEntityIds)
        {
            if (homeGridEntityIds == null) return;
            for (int i = 0; i < homeGridEntityIds.Count; i++)
            {
                long id = homeGridEntityIds[i];
                if (id != 0)
                    _homeToMark.Remove(id);
            }
        }

        public void ClearConstruct(IMyCubeGrid homeGrid, Func<long, IMyCubeGrid> resolveGrid)
        {
            if (homeGrid == null) return;

            var remove = new List<long>();
            foreach (var kv in _homeToMark)
            {
                if (kv.Key == homeGrid.EntityId)
                {
                    remove.Add(kv.Key);
                    continue;
                }
                if (resolveGrid == null) continue;
                var other = resolveGrid(kv.Key);
                if (other != null && !other.Closed && other.IsSameConstructAs(homeGrid))
                    remove.Add(kv.Key);
            }
            for (int i = 0; i < remove.Count; i++)
                _homeToMark.Remove(remove[i]);
        }

        public int ClearWhereHome(Func<long, bool> canClearHome)
        {
            if (canClearHome == null || _homeToMark.Count == 0)
                return 0;
            var remove = new List<long>();
            foreach (var kv in _homeToMark)
            {
                if (kv.Key != 0 && canClearHome(kv.Key))
                    remove.Add(kv.Key);
            }
            for (int i = 0; i < remove.Count; i++)
                _homeToMark.Remove(remove[i]);
            return remove.Count;
        }

        public void ClearAll()
        {
            _homeToMark.Clear();
        }

        public void CopyTo(List<SalvageTargetEntry> into)
        {
            if (into == null) return;
            into.Clear();
            foreach (var kv in _homeToMark)
            {
                if (kv.Key == 0) continue;
                Mark m = kv.Value;
                if (!m.HasZone && m.SeedGridEntityId == 0) continue;
                into.Add(new SalvageTargetEntry
                {
                    HomeGridEntityId = kv.Key,
                    TargetGridEntityId = m.SeedGridEntityId,
                    HasZone = m.HasZone,
                    ZoneMinX = m.Min.X,
                    ZoneMinY = m.Min.Y,
                    ZoneMinZ = m.Min.Z,
                    ZoneMaxX = m.Max.X,
                    ZoneMaxY = m.Max.Y,
                    ZoneMaxZ = m.Max.Z
                });
            }
        }

        public void ReplaceAll(IList<SalvageTargetEntry> entries)
        {
            _homeToMark.Clear();
            if (entries == null) return;
            for (int i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                if (e == null || e.HomeGridEntityId == 0) continue;
                if (!e.HasZone && e.TargetGridEntityId == 0) continue;
                _homeToMark[e.HomeGridEntityId] = new Mark
                {
                    SeedGridEntityId = e.TargetGridEntityId,
                    HasZone = e.HasZone,
                    Min = new Vector3D(e.ZoneMinX, e.ZoneMinY, e.ZoneMinZ),
                    Max = new Vector3D(e.ZoneMaxX, e.ZoneMaxY, e.ZoneMaxZ)
                };
            }
        }

        public byte[] ToBytes()
        {
            var buf = new List<byte>(128);
            WriteInt(buf, FormatVersion);
            WriteInt(buf, _homeToMark.Count);
            foreach (var kv in _homeToMark)
            {
                WriteLong(buf, kv.Key);
                Mark m = kv.Value;
                WriteLong(buf, m.SeedGridEntityId);
                buf.Add(m.HasZone ? (byte)1 : (byte)0);
                WriteDouble(buf, m.Min.X);
                WriteDouble(buf, m.Min.Y);
                WriteDouble(buf, m.Min.Z);
                WriteDouble(buf, m.Max.X);
                WriteDouble(buf, m.Max.Y);
                WriteDouble(buf, m.Max.Z);
            }
            var arr = new byte[buf.Count];
            for (int i = 0; i < buf.Count; i++)
                arr[i] = buf[i];
            return arr;
        }

        public static SalvageTargetStore FromBytes(byte[] bytes)
        {
            var store = new SalvageTargetStore();
            if (bytes == null || bytes.Length == 0)
                return store;

            int pos = 0;
            int version = ReadInt(bytes, ref pos);
            if (version == FormatVersionV1)
            {
                int count = ReadInt(bytes, ref pos);
                for (int i = 0; i < count; i++)
                {
                    long home = ReadLong(bytes, ref pos);
                    long target = ReadLong(bytes, ref pos);
                    if (home != 0 && target != 0)
                    {
                        store._homeToMark[home] = new Mark
                        {
                            SeedGridEntityId = target,
                            HasZone = false
                        };
                    }
                }
                return store;
            }

            if (version != FormatVersion)
                return store;

            int n = ReadInt(bytes, ref pos);
            for (int i = 0; i < n; i++)
            {
                long home = ReadLong(bytes, ref pos);
                long seed = ReadLong(bytes, ref pos);
                bool hasZone = bytes[pos++] != 0;
                double minX = ReadDouble(bytes, ref pos);
                double minY = ReadDouble(bytes, ref pos);
                double minZ = ReadDouble(bytes, ref pos);
                double maxX = ReadDouble(bytes, ref pos);
                double maxY = ReadDouble(bytes, ref pos);
                double maxZ = ReadDouble(bytes, ref pos);
                if (home == 0) continue;
                if (!hasZone && seed == 0) continue;
                store._homeToMark[home] = new Mark
                {
                    SeedGridEntityId = seed,
                    HasZone = hasZone,
                    Min = new Vector3D(minX, minY, minZ),
                    Max = new Vector3D(maxX, maxY, maxZ)
                };
            }
            return store;
        }

        private static void WriteInt(List<byte> buf, int value)
        {
            buf.AddRange(BitConverter.GetBytes(value));
        }

        private static void WriteLong(List<byte> buf, long value)
        {
            buf.AddRange(BitConverter.GetBytes(value));
        }

        private static void WriteDouble(List<byte> buf, double value)
        {
            buf.AddRange(BitConverter.GetBytes(value));
        }

        private static int ReadInt(byte[] bytes, ref int pos)
        {
            int v = BitConverter.ToInt32(bytes, pos);
            pos += 4;
            return v;
        }

        private static long ReadLong(byte[] bytes, ref int pos)
        {
            long v = BitConverter.ToInt64(bytes, pos);
            pos += 8;
            return v;
        }

        private static double ReadDouble(byte[] bytes, ref int pos)
        {
            double v = BitConverter.ToDouble(bytes, pos);
            pos += 8;
            return v;
        }
    }
}
