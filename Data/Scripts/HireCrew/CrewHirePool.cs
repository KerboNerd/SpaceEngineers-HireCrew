using System;
using System.Collections.Generic;
using System.Text;

namespace HireCrew
{
    /// <summary>
    /// Pure hire-pool generation + per-block pool store (server authority).
    /// </summary>
    public static class CrewHireGenerator
    {
        public static void NormalizeDeskSettings(HireBlockPool pool)
        {
            if (pool == null) return;
            var world = HireWorldConfig.Current ?? HireWorldConfig.CreateDefaults();
            world.Normalize();

            pool.RefreshMinutes = CrewConfig.ClampRefreshMinutes(
                pool.RefreshMinutes <= 0 ? world.RefreshMinutesDefault : pool.RefreshMinutes);
            pool.PriceMultiplierPercent = CrewConfig.ClampPriceMultiplierPercent(
                pool.PriceMultiplierPercent <= 0
                    ? world.PriceMultiplierPercentDefault
                    : pool.PriceMultiplierPercent);

            int minC = pool.MinCandidates > 0 ? pool.MinCandidates : world.MinCandidates;
            int maxC = pool.MaxCandidates > 0 ? pool.MaxCandidates : world.MaxCandidates;
            if (minC < world.MinCandidates) minC = world.MinCandidates;
            if (maxC > world.MaxCandidates) maxC = world.MaxCandidates;
            if (maxC < minC) maxC = minC;
            pool.MinCandidates = minC;
            pool.MaxCandidates = maxC;

            int roles = pool.AllowedRoles;
            if (roles == 0) roles = world.AllowedRolesMask;
            roles &= world.AllowedRolesMask;
            if (roles == 0) roles = 1 << HireWorldConfig.FirstAllowedRole(world.AllowedRolesMask);
            pool.AllowedRoles = roles;

            if (pool.StarBias < (int)StarBias.Low || pool.StarBias > (int)StarBias.High)
                pool.StarBias = (int)StarBias.Balanced;
        }

        public static int RollCandidateCount(Random rng)
        {
            return RollCandidateCount(rng, CrewConfig.MinCandidates, CrewConfig.MaxCandidates);
        }

        public static int RollCandidateCount(Random rng, int min, int max)
        {
            if (rng == null) rng = new Random();
            if (max < min) max = min;
            return rng.Next(min, max + 1);
        }

        public static int RollStars(Random rng)
        {
            return RollStars(rng, CrewConfig.StarWeights);
        }

        public static int RollStars(Random rng, int[] weights)
        {
            if (rng == null) rng = new Random();
            if (weights == null || weights.Length == 0) return 0;
            int total = 0;
            for (int i = 0; i < weights.Length; i++)
                total += weights[i] > 0 ? weights[i] : 0;
            if (total <= 0) return 0;

            int roll = rng.Next(total);
            int acc = 0;
            for (int i = 0; i < weights.Length; i++)
            {
                int w = weights[i] > 0 ? weights[i] : 0;
                acc += w;
                if (roll < acc) return CrewConfig.ClampStars(i);
            }
            return CrewConfig.ClampStars(weights.Length - 1);
        }

        public static CrewRole RollRole(Random rng)
        {
            int mask = HireWorldConfig.Current != null
                ? HireWorldConfig.Current.AllowedRolesMask
                : HireWorldConfig.AllRolesMask;
            return RollRole(rng, mask);
        }

        public static CrewRole RollRole(Random rng, int allowedMask)
        {
            if (rng == null) rng = new Random();
            allowedMask &= HireWorldConfig.AllRolesMask;
            if (allowedMask == 0)
                return (CrewRole)HireWorldConfig.FirstAllowedRole(HireWorldConfig.AllRolesMask);

            int count = 0;
            for (int i = 0; i <= CrewConfig.MaxRole; i++)
                if ((allowedMask & (1 << i)) != 0) count++;
            int pick = rng.Next(count);
            for (int i = 0; i <= CrewConfig.MaxRole; i++)
            {
                if ((allowedMask & (1 << i)) == 0) continue;
                if (pick == 0) return (CrewRole)i;
                pick--;
            }
            return CrewRole.Gunner;
        }

        public static long RollPrice(int stars, Random rng)
        {
            if (rng == null) rng = new Random();
            long basePrice = CrewConfig.GetPrice(stars);
            double variance = CrewConfig.PriceVarianceFraction;
            double factor = 1.0 - variance + rng.NextDouble() * (2.0 * variance);
            long price = (long)Math.Round(basePrice * factor);
            if (price < 1) price = 1;
            return price;
        }

        public static long ApplyPriceMultiplier(long price, int multiplierPercent)
        {
            float mult = CrewConfig.PriceMultiplierFromPercent(multiplierPercent);
            long scaled = (long)Math.Round(price * (double)mult);
            if (scaled < 1) scaled = 1;
            return scaled;
        }

        public static HireCandidate CreateCandidateForPool(HireBlockPool pool, Random rng)
        {
            NormalizeDeskSettings(pool);
            if (rng == null) rng = new Random();
            var world = HireWorldConfig.Current ?? HireWorldConfig.CreateDefaults();
            var weights = world.GetStarWeights((StarBias)pool.StarBias);
            int stars = RollStars(rng, weights);
            string first, last;
            CrewNames.RollName(rng, out first, out last);
            long raw = RollPrice(stars, rng);
            return new HireCandidate
            {
                CandidateId = Guid.NewGuid().ToString("N"),
                FirstName = first,
                LastName = last,
                Stars = stars,
                Role = (int)RollRole(rng, pool.AllowedRoles),
                Price = ApplyPriceMultiplier(raw, pool.PriceMultiplierPercent)
            };
        }

        public static HireCandidate CreateCandidate(Random rng, int priceMultiplierPercent)
        {
            var pool = new HireBlockPool { PriceMultiplierPercent = priceMultiplierPercent };
            return CreateCandidateForPool(pool, rng);
        }

        public static List<HireCandidate> GeneratePool(HireBlockPool pool, Random rng)
        {
            NormalizeDeskSettings(pool);
            if (rng == null) rng = new Random();
            int count = RollCandidateCount(rng, pool.MinCandidates, pool.MaxCandidates);
            var list = new List<HireCandidate>(count);
            for (int i = 0; i < count; i++)
                list.Add(CreateCandidateForPool(pool, rng));
            return list;
        }

        public static List<HireCandidate> GeneratePool(Random rng, int priceMultiplierPercent)
        {
            var pool = new HireBlockPool { PriceMultiplierPercent = priceMultiplierPercent };
            return GeneratePool(pool, rng);
        }

        /// <summary>Legacy helper for tests — generates at 1.00x.</summary>
        public static List<HireCandidate> GeneratePool(Random rng)
        {
            return GeneratePool(rng, CrewConfig.DefaultPriceMultiplierPercent);
        }

        public static void RefreshPool(HireBlockPool pool, Random rng, DateTime utcNow)
        {
            if (pool == null) return;
            if (rng == null) rng = new Random();
            NormalizeDeskSettings(pool);
            pool.Candidates = GeneratePool(pool, rng);
            pool.NextRefreshUtcTicks = utcNow.AddMinutes(pool.RefreshMinutes).Ticks;
        }

        public static void RefillOne(HireBlockPool pool, Random rng)
        {
            if (pool == null) return;
            if (pool.Candidates == null) pool.Candidates = new List<HireCandidate>();
            pool.Candidates.Add(CreateCandidateForPool(pool, rng));
        }

        /// <summary>Rescale current candidate prices when the desk multiplier changes (no reroll).</summary>
        public static void ApplyMultiplierToPool(HireBlockPool pool, int newMultiplierPercent)
        {
            if (pool == null) return;
            int oldPct = CrewConfig.ClampPriceMultiplierPercent(
                pool.PriceMultiplierPercent <= 0
                    ? CrewConfig.DefaultPriceMultiplierPercent
                    : pool.PriceMultiplierPercent);
            int newPct = CrewConfig.ClampPriceMultiplierPercent(newMultiplierPercent);
            if (oldPct == newPct)
            {
                pool.PriceMultiplierPercent = newPct;
                return;
            }

            double ratio = newPct / (double)oldPct;
            if (pool.Candidates != null)
            {
                for (int i = 0; i < pool.Candidates.Count; i++)
                {
                    var c = pool.Candidates[i];
                    if (c == null) continue;
                    long scaled = (long)Math.Round(c.Price * ratio);
                    if (scaled < 1) scaled = 1;
                    c.Price = scaled;
                }
            }
            pool.PriceMultiplierPercent = newPct;
        }
    }

    public sealed class HirePoolStore
    {
        private const int FormatVersion = 3;
        private readonly Dictionary<long, HireBlockPool> _byBlock = new Dictionary<long, HireBlockPool>();

        public IEnumerable<HireBlockPool> All { get { return _byBlock.Values; } }

        public HireBlockPool Get(long blockEntityId)
        {
            HireBlockPool p;
            return _byBlock.TryGetValue(blockEntityId, out p) ? p : null;
        }

        public void Upsert(HireBlockPool pool)
        {
            if (pool == null || pool.BlockEntityId == 0)
                throw new ArgumentException("pool");
            _byBlock[pool.BlockEntityId] = pool;
        }

        public bool Remove(long blockEntityId)
        {
            return _byBlock.Remove(blockEntityId);
        }

        public HireBlockPool Ensure(long blockEntityId, long gridEntityId, Random rng, DateTime utcNow)
        {
            HireBlockPool pool;
            if (_byBlock.TryGetValue(blockEntityId, out pool) && pool != null)
            {
                pool.GridEntityId = gridEntityId;
                CrewHireGenerator.NormalizeDeskSettings(pool);
                return pool;
            }

            var world = HireWorldConfig.Current ?? HireWorldConfig.CreateDefaults();
            pool = new HireBlockPool
            {
                BlockEntityId = blockEntityId,
                GridEntityId = gridEntityId,
                RefreshMinutes = world.RefreshMinutesDefault,
                PriceMultiplierPercent = world.PriceMultiplierPercentDefault,
                MinCandidates = world.MinCandidates,
                MaxCandidates = world.MaxCandidates,
                AllowedRoles = world.AllowedRolesMask,
                StarBias = (int)StarBias.Balanced,
                RefillOnHire = world.RefillOnHireDefault,
                Candidates = new List<HireCandidate>()
            };
            CrewHireGenerator.RefreshPool(pool, rng, utcNow);
            _byBlock[blockEntityId] = pool;
            return pool;
        }

        public bool TickRefresh(DateTime utcNow, Random rng)
        {
            bool any = false;
            foreach (var pool in _byBlock.Values)
            {
                if (pool == null) continue;
                if (utcNow.Ticks < pool.NextRefreshUtcTicks) continue;
                CrewHireGenerator.RefreshPool(pool, rng, utcNow);
                any = true;
            }
            return any;
        }

        public HireCandidate TakeCandidate(long blockEntityId, string candidateId)
        {
            var pool = Get(blockEntityId);
            if (pool == null || pool.Candidates == null || string.IsNullOrEmpty(candidateId))
                return null;
            for (int i = 0; i < pool.Candidates.Count; i++)
            {
                var c = pool.Candidates[i];
                if (c == null || c.CandidateId != candidateId) continue;
                pool.Candidates.RemoveAt(i);
                return c;
            }
            return null;
        }

        public byte[] ToBytes()
        {
            var buf = new List<byte>(256);
            WriteInt(buf, FormatVersion);
            WriteInt(buf, _byBlock.Count);
            foreach (var p in _byBlock.Values)
                WritePool(buf, p);
            var arr = new byte[buf.Count];
            for (var i = 0; i < buf.Count; i++)
                arr[i] = buf[i];
            return arr;
        }

        public static HirePoolStore FromBytes(byte[] bytes)
        {
            var store = new HirePoolStore();
            if (bytes == null || bytes.Length == 0) return store;
            var pos = 0;
            var version = ReadInt(bytes, ref pos);
            if (version < 1) return store;
            var count = ReadInt(bytes, ref pos);
            for (var i = 0; i < count; i++)
                store.Upsert(ReadPool(bytes, ref pos, version));
            return store;
        }

        public static byte[] SerializePool(HireBlockPool pool)
        {
            var buf = new List<byte>(128);
            WritePool(buf, pool);
            var arr = new byte[buf.Count];
            for (var i = 0; i < buf.Count; i++)
                arr[i] = buf[i];
            return arr;
        }

        public static HireBlockPool DeserializePool(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return null;
            var pos = 0;
            // Sync packets always use current WritePool layout (v3 fields).
            return ReadPool(bytes, ref pos, FormatVersion);
        }

        private static void WritePool(List<byte> buf, HireBlockPool p)
        {
            if (p == null) p = new HireBlockPool();
            CrewHireGenerator.NormalizeDeskSettings(p);
            WriteLong(buf, p.BlockEntityId);
            WriteLong(buf, p.GridEntityId);
            WriteInt(buf, p.RefreshMinutes);
            WriteLong(buf, p.NextRefreshUtcTicks);
            WriteInt(buf, p.PriceMultiplierPercent);
            var list = p.Candidates ?? new List<HireCandidate>();
            WriteInt(buf, list.Count);
            for (int i = 0; i < list.Count; i++)
                WriteCandidate(buf, list[i]);
            WriteInt(buf, p.MinCandidates);
            WriteInt(buf, p.MaxCandidates);
            WriteInt(buf, p.AllowedRoles);
            WriteInt(buf, p.StarBias);
            WriteInt(buf, p.RefillOnHire ? 1 : 0);
        }

        private static HireBlockPool ReadPool(byte[] bytes, ref int pos, int version)
        {
            var p = new HireBlockPool();
            p.BlockEntityId = ReadLong(bytes, ref pos);
            p.GridEntityId = ReadLong(bytes, ref pos);
            p.RefreshMinutes = ReadInt(bytes, ref pos);
            p.NextRefreshUtcTicks = ReadLong(bytes, ref pos);
            if (version >= 2)
                p.PriceMultiplierPercent = ReadInt(bytes, ref pos);
            else
                p.PriceMultiplierPercent = 0;
            int count = ReadInt(bytes, ref pos);
            p.Candidates = new List<HireCandidate>(count);
            for (int i = 0; i < count; i++)
                p.Candidates.Add(ReadCandidate(bytes, ref pos));

            if (version >= 3)
            {
                p.MinCandidates = ReadInt(bytes, ref pos);
                p.MaxCandidates = ReadInt(bytes, ref pos);
                p.AllowedRoles = ReadInt(bytes, ref pos);
                p.StarBias = ReadInt(bytes, ref pos);
                p.RefillOnHire = ReadInt(bytes, ref pos) != 0;
            }
            else
            {
                p.MinCandidates = 0;
                p.MaxCandidates = 0;
                p.AllowedRoles = 0;
                p.StarBias = (int)StarBias.Balanced;
                p.RefillOnHire = false;
            }
            CrewHireGenerator.NormalizeDeskSettings(p);
            return p;
        }

        private static void WriteCandidate(List<byte> buf, HireCandidate c)
        {
            if (c == null) c = new HireCandidate();
            WriteString(buf, c.CandidateId ?? "");
            WriteString(buf, c.FirstName ?? "");
            WriteString(buf, c.LastName ?? "");
            WriteInt(buf, c.Stars);
            WriteInt(buf, c.Role);
            WriteLong(buf, c.Price);
        }

        private static HireCandidate ReadCandidate(byte[] bytes, ref int pos)
        {
            return new HireCandidate
            {
                CandidateId = ReadString(bytes, ref pos),
                FirstName = ReadString(bytes, ref pos),
                LastName = ReadString(bytes, ref pos),
                Stars = CrewConfig.ClampStars(ReadInt(bytes, ref pos)),
                Role = ReadInt(bytes, ref pos),
                Price = ReadLong(bytes, ref pos)
            };
        }

        private static void WriteInt(List<byte> buf, int value)
        {
            buf.AddRange(BitConverter.GetBytes(value));
        }

        private static void WriteLong(List<byte> buf, long value)
        {
            buf.AddRange(BitConverter.GetBytes(value));
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
