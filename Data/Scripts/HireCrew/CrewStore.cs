using System;
using System.Collections.Generic;
using System.Text;

namespace HireCrew
{
    public sealed class CrewStore
    {
        private const int FormatVersion = 6;

        private readonly Dictionary<string, CrewRecord> _byId = new Dictionary<string, CrewRecord>();

        public IEnumerable<CrewRecord> All { get { return _byId.Values; } }

        public CrewRecord Get(string crewId)
        {
            if (string.IsNullOrEmpty(crewId))
                return null;
            CrewRecord r;
            return _byId.TryGetValue(crewId, out r) ? r : null;
        }

        public List<CrewRecord> GetForGrid(long gridEntityId)
        {
            var list = new List<CrewRecord>();
            if (gridEntityId == 0) return list;
            foreach (var r in _byId.Values)
                if (r.GridEntityId == gridEntityId)
                    list.Add(r);
            return list;
        }

        public List<CrewRecord> GetForOwner(long ownerKey, bool ownerIsFaction)
        {
            var list = new List<CrewRecord>();
            foreach (var r in _byId.Values)
                if (CrewOwnership.Matches(r, ownerKey, ownerIsFaction))
                    list.Add(r);
            return list;
        }

        public void Upsert(CrewRecord record)
        {
            if (record == null || string.IsNullOrEmpty(record.CrewId))
                throw new ArgumentException("record");
            _byId[record.CrewId] = record;
        }

        public bool Remove(string crewId)
        {
            return _byId.Remove(crewId);
        }

        public byte[] ToBytes()
        {
            var buf = new List<byte>(256);
            // Negative magic distinguishes v2+ from legacy payloads that start with count.
            WriteInt(buf, -FormatVersion);
            WriteInt(buf, _byId.Count);
            foreach (var r in _byId.Values)
                WriteRecord(buf, r);
            var arr = new byte[buf.Count];
            for (var i = 0; i < buf.Count; i++)
                arr[i] = buf[i];
            return arr;
        }

        public static CrewStore FromBytes(byte[] bytes)
        {
            var store = new CrewStore();
            if (bytes == null || bytes.Length == 0) return store;
            var pos = 0;
            var first = ReadInt(bytes, ref pos);
            int version;
            int count;
            if (first < 0)
            {
                version = -first;
                count = ReadInt(bytes, ref pos);
            }
            else
            {
                version = 1;
                count = first;
            }

            for (var i = 0; i < count; i++)
                store.Upsert(ReadRecord(bytes, ref pos, version));
            return store;
        }

        private static void WriteRecord(List<byte> buf, CrewRecord r)
        {
            WriteString(buf, r.CrewId ?? "");
            WriteInt(buf, CrewConfig.ClampStars(r.Stars));
            WriteLong(buf, r.GridEntityId);
            WriteBool(buf, r.SeatEntityId.HasValue);
            if (r.SeatEntityId.HasValue) WriteLong(buf, r.SeatEntityId.Value);
            WriteBool(buf, r.WeaponEntityId.HasValue);
            if (r.WeaponEntityId.HasValue) WriteLong(buf, r.WeaponEntityId.Value);
            WriteBool(buf, r.CharacterEntityId.HasValue);
            if (r.CharacterEntityId.HasValue) WriteLong(buf, r.CharacterEntityId.Value);
            WriteLong(buf, r.OwnerIdentityId);
            WriteInt(buf, (int)r.Status);
            WriteString(buf, r.DisplayName ?? "");
            WriteBool(buf, r.BedEntityId.HasValue);
            if (r.BedEntityId.HasValue) WriteLong(buf, r.BedEntityId.Value);
            WriteBool(buf, r.ToiletEntityId.HasValue);
            if (r.ToiletEntityId.HasValue) WriteLong(buf, r.ToiletEntityId.Value);
            WriteBool(buf, r.ShowerEntityId.HasValue);
            if (r.ShowerEntityId.HasValue) WriteLong(buf, r.ShowerEntityId.Value);
            WriteInt(buf, (int)r.Role);
            WriteLong(buf, r.OwnerKey);
            WriteBool(buf, r.OwnerIsFaction);
            WriteLong(buf, r.TrainingEndsUtcTicks);
        }

        private static CrewRecord ReadRecord(byte[] bytes, ref int pos, int version)
        {
            var r = new CrewRecord();
            r.CrewId = ReadString(bytes, ref pos);
            int rawStars = ReadInt(bytes, ref pos);
            // v1–v3 stored CrewTier enum (Recruit=0, Regular=1, Elite=2).
            r.Stars = version < 4
                ? CrewConfig.StarsFromLegacyTier(rawStars)
                : CrewConfig.ClampStars(rawStars);
            r.GridEntityId = ReadLong(bytes, ref pos);
            if (ReadBool(bytes, ref pos)) r.SeatEntityId = ReadLong(bytes, ref pos);
            if (ReadBool(bytes, ref pos)) r.WeaponEntityId = ReadLong(bytes, ref pos);
            if (ReadBool(bytes, ref pos)) r.CharacterEntityId = ReadLong(bytes, ref pos);
            r.OwnerIdentityId = ReadLong(bytes, ref pos);
            r.Status = (CrewStatus)ReadInt(bytes, ref pos);
            r.DisplayName = ReadString(bytes, ref pos);
            if (version >= 2)
            {
                if (ReadBool(bytes, ref pos)) r.BedEntityId = ReadLong(bytes, ref pos);
                if (ReadBool(bytes, ref pos)) r.ToiletEntityId = ReadLong(bytes, ref pos);
                if (ReadBool(bytes, ref pos)) r.ShowerEntityId = ReadLong(bytes, ref pos);
            }
            if (version >= 3)
                r.Role = (CrewRole)ReadInt(bytes, ref pos);
            else
                r.Role = CrewRole.Gunner;

            if (version >= 5)
            {
                r.OwnerKey = ReadLong(bytes, ref pos);
                r.OwnerIsFaction = ReadBool(bytes, ref pos);
            }
            else
            {
                // Pre-pool saves: treat as personal roster of the hiring identity.
                r.OwnerKey = r.OwnerIdentityId;
                r.OwnerIsFaction = false;
                if (r.Status == CrewStatus.Unassigned)
                    r.GridEntityId = 0;
            }

            if (version >= 6)
                r.TrainingEndsUtcTicks = ReadLong(bytes, ref pos);
            else
                r.TrainingEndsUtcTicks = 0;

            return r;
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

        private static void WriteString(List<byte> buf, string value)
        {
            var utf8 = Encoding.UTF8.GetBytes(value ?? "");
            WriteInt(buf, utf8.Length);
            buf.AddRange(utf8);
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

        private static string ReadString(byte[] bytes, ref int pos)
        {
            var len = ReadInt(bytes, ref pos);
            if (len < 0 || pos + len > bytes.Length)
                throw new InvalidOperationException("bad string length");
            var s = Encoding.UTF8.GetString(bytes, pos, len);
            pos += len;
            return s;
        }
    }
}
