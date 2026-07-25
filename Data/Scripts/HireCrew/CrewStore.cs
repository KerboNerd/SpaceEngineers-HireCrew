using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace HireCrew
{
    public sealed class CrewStore
    {
        private readonly Dictionary<string, CrewRecord> _byId = new Dictionary<string, CrewRecord>();

        public IEnumerable<CrewRecord> All { get { return _byId.Values; } }

        public CrewRecord Get(string crewId)
        {
            CrewRecord r;
            return _byId.TryGetValue(crewId, out r) ? r : null;
        }

        public List<CrewRecord> GetForGrid(long gridEntityId)
        {
            var list = new List<CrewRecord>();
            foreach (var r in _byId.Values)
                if (r.GridEntityId == gridEntityId)
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
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms, Encoding.UTF8))
            {
                w.Write(_byId.Count);
                foreach (var r in _byId.Values)
                    WriteRecord(w, r);
                return ms.ToArray();
            }
        }

        public static CrewStore FromBytes(byte[] bytes)
        {
            var store = new CrewStore();
            if (bytes == null || bytes.Length == 0) return store;
            using (var ms = new MemoryStream(bytes))
            using (var rd = new BinaryReader(ms, Encoding.UTF8))
            {
                var count = rd.ReadInt32();
                for (var i = 0; i < count; i++)
                    store.Upsert(ReadRecord(rd));
            }
            return store;
        }

        private static void WriteRecord(BinaryWriter w, CrewRecord r)
        {
            w.Write(r.CrewId ?? "");
            w.Write((int)r.Tier);
            w.Write(r.GridEntityId);
            w.Write(r.SeatEntityId.HasValue);
            if (r.SeatEntityId.HasValue) w.Write(r.SeatEntityId.Value);
            w.Write(r.WeaponEntityId.HasValue);
            if (r.WeaponEntityId.HasValue) w.Write(r.WeaponEntityId.Value);
            w.Write(r.CharacterEntityId.HasValue);
            if (r.CharacterEntityId.HasValue) w.Write(r.CharacterEntityId.Value);
            w.Write(r.OwnerIdentityId);
            w.Write((int)r.Status);
            w.Write(r.DisplayName ?? "");
        }

        private static CrewRecord ReadRecord(BinaryReader rd)
        {
            var r = new CrewRecord();
            r.CrewId = rd.ReadString();
            r.Tier = (CrewTier)rd.ReadInt32();
            r.GridEntityId = rd.ReadInt64();
            if (rd.ReadBoolean()) r.SeatEntityId = rd.ReadInt64();
            if (rd.ReadBoolean()) r.WeaponEntityId = rd.ReadInt64();
            if (rd.ReadBoolean()) r.CharacterEntityId = rd.ReadInt64();
            r.OwnerIdentityId = rd.ReadInt64();
            r.Status = (CrewStatus)rd.ReadInt32();
            r.DisplayName = rd.ReadString();
            return r;
        }
    }
}
