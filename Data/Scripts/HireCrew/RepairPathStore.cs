using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRageMath;

namespace HireCrew
{
    public sealed class RepairPathStore
    {
        private const int FormatVersion = 1;
        private readonly Dictionary<long, RepairGridPath> _byGrid = new Dictionary<long, RepairGridPath>();

        public RepairGridPath Get(long gridEntityId)
        {
            RepairGridPath path;
            return _byGrid.TryGetValue(gridEntityId, out path) ? path : null;
        }

        public void Upsert(RepairGridPath path)
        {
            if (path == null || path.GridEntityId == 0)
                throw new ArgumentException("path");
            if (path.Waypoints == null)
                path.Waypoints = new List<RepairWaypoint>();
            _byGrid[path.GridEntityId] = path;
        }

        public bool Clear(long gridEntityId)
        {
            return _byGrid.Remove(gridEntityId);
        }

        public bool IsReady(long gridEntityId)
        {
            var path = Get(gridEntityId);
            return path != null
                && path.HasExit
                && path.Waypoints != null
                && path.Waypoints.Count >= 2;
        }

        public static bool TryResolveWorldPos(IMyCubeGrid grid, RepairWaypoint wp, out Vector3D world)
        {
            world = Vector3D.Zero;
            if (grid == null || wp == null)
                return false;

            IMyEntity ent;
            if (wp.BlockEntityId != 0
                && MyAPIGateway.Entities.TryGetEntityById(wp.BlockEntityId, out ent)
                && ent != null
                && !ent.Closed)
            {
                var block = ent as IMyCubeBlock;
                if (block != null
                    && block.CubeGrid != null
                    && block.CubeGrid.EntityId == grid.EntityId)
                {
                    world = block.GetPosition();
                    return true;
                }
            }

            var local = new Vector3D(wp.LocalX, wp.LocalY, wp.LocalZ);
            world = Vector3D.Transform(local, grid.WorldMatrix);
            return true;
        }

        public byte[] ToBytes()
        {
            var buf = new List<byte>(256);
            WriteInt(buf, FormatVersion);
            WriteInt(buf, _byGrid.Count);
            foreach (var path in _byGrid.Values)
            {
                WriteLong(buf, path.GridEntityId);
                WriteBool(buf, path.HasExit);
                var wps = path.Waypoints ?? new List<RepairWaypoint>();
                WriteInt(buf, wps.Count);
                for (int i = 0; i < wps.Count; i++)
                {
                    var wp = wps[i] ?? new RepairWaypoint();
                    WriteLong(buf, wp.BlockEntityId);
                    WriteDouble(buf, wp.LocalX);
                    WriteDouble(buf, wp.LocalY);
                    WriteDouble(buf, wp.LocalZ);
                }
            }
            var arr = new byte[buf.Count];
            for (int i = 0; i < buf.Count; i++)
                arr[i] = buf[i];
            return arr;
        }

        public static RepairPathStore FromBytes(byte[] bytes)
        {
            var store = new RepairPathStore();
            if (bytes == null || bytes.Length == 0)
                return store;

            int pos = 0;
            int version = ReadInt(bytes, ref pos);
            if (version != FormatVersion)
                return store;

            int count = ReadInt(bytes, ref pos);
            for (int i = 0; i < count; i++)
            {
                var path = new RepairGridPath
                {
                    GridEntityId = ReadLong(bytes, ref pos),
                    HasExit = ReadBool(bytes, ref pos),
                    Waypoints = new List<RepairWaypoint>()
                };
                int wpCount = ReadInt(bytes, ref pos);
                for (int w = 0; w < wpCount; w++)
                {
                    path.Waypoints.Add(new RepairWaypoint
                    {
                        BlockEntityId = ReadLong(bytes, ref pos),
                        LocalX = ReadDouble(bytes, ref pos),
                        LocalY = ReadDouble(bytes, ref pos),
                        LocalZ = ReadDouble(bytes, ref pos)
                    });
                }
                if (path.GridEntityId != 0)
                    store.Upsert(path);
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

        private static void WriteBool(List<byte> buf, bool value)
        {
            buf.Add(value ? (byte)1 : (byte)0);
        }

        private static void WriteDouble(List<byte> buf, double value)
        {
            buf.AddRange(BitConverter.GetBytes(value));
        }

        private static int ReadInt(byte[] bytes, ref int pos)
        {
            var v = BitConverter.ToInt32(bytes, pos);
            pos += 4;
            return v;
        }

        private static long ReadLong(byte[] bytes, ref int pos)
        {
            var v = BitConverter.ToInt64(bytes, pos);
            pos += 8;
            return v;
        }

        private static bool ReadBool(byte[] bytes, ref int pos)
        {
            var v = bytes[pos] != 0;
            pos += 1;
            return v;
        }

        private static double ReadDouble(byte[] bytes, ref int pos)
        {
            var v = BitConverter.ToDouble(bytes, pos);
            pos += 8;
            return v;
        }
    }
}
