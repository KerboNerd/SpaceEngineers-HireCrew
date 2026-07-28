# Hire Desk Configurability Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add world XML hire defaults/limits plus per-desk terminal overrides for pool shape, economy, refresh, refill, and manual reroll.

**Architecture:** Load `HireCrewConfig.xml` into a session-held `HireWorldConfig` at world start. Persist per-desk overrides on `HireBlockPool` (binary format v3). Terminal sends a full settings payload; server clamps and applies reroll / price rescale / timer update by priority. Generation and refill read effective desk settings against world limits.

**Tech Stack:** Space Engineers ModAPI (`MyAPIGateway.Utilities` world storage + XML), protobuf-net messages, existing hire-pool binary store.

**Spec:** `docs/superpowers/specs/2026-07-28-hire-desk-config-design.md`

## Global Constraints

- No automated tests for this feature (spec non-goal). Verify in-game / by inspection.
- Agent must not run `dotnet` / builds; user verifies in SE.
- Anyone who already passes hire-desk `HasManagePermission` may change settings (keep existing gate; do not invent a new ACL).
- Pool-shape / ForceReroll → immediate `RefreshPool`; price mult alone → rescale; refresh minutes alone → timer only; refill flag alone → no pool mutation.
- Empty role mask after clamp → first world-allowed role.
- Missing/invalid XML → log once, use compile-time defaults, write default file.
- Edit `Data/Scripts/HireCrew/...` as source of truth; mirror the same edits under `Source/HireCrew/...` when that file exists.
- Do not commit unless the user explicitly asks.

## File structure

| File | Role |
|------|------|
| `Data/Scripts/HireCrew/HireWorldConfig.cs` | World XML DTO + load/save/normalize + star-bias weights + role helpers |
| `Data/Scripts/HireCrew/CrewConfig.cs` | Compile-time fallbacks; clamps/getters prefer `HireWorldConfig.Current` |
| `Data/Scripts/HireCrew/CrewModels.cs` | `StarBias`, extend `HireBlockPool` + `HireRefreshRequest` |
| `Data/Scripts/HireCrew/CrewHirePool.cs` | Generator uses desk settings; pool binary v3; refill helper |
| `Data/Scripts/HireCrew/CrewSession.cs` | Load world config; settings apply priority; refill-on-hire; client API |
| `Data/Scripts/HireCrew/CrewHireBlockLogic.cs` | Terminal controls for new knobs + reroll |
| `Data/Scripts/HireCrew/CrewHireWindow.cs` | Status line shows bias / roles / refill briefly |

---

### Task 1: Models — StarBias, pool fields, settings request

**Files:**
- Modify: `Data/Scripts/HireCrew/CrewModels.cs`
- Mirror: `Source/HireCrew/CrewModels.cs` if present

**Interfaces:**
- Produces:
  - `enum StarBias { Low = 0, Balanced = 1, High = 2 }`
  - `HireBlockPool` new members: `MinCandidates`, `MaxCandidates`, `AllowedRoles`, `StarBias`, `RefillOnHire`
  - `HireRefreshRequest` new members: same + `ForceReroll` (bool)

- [ ] **Step 1: Add enum + extend `HireBlockPool`**

```csharp
public enum StarBias
{
    Low = 0,
    Balanced = 1,
    High = 2
}

// Inside HireBlockPool — add ProtoMembers 7–11:
[ProtoMember(7)] public int MinCandidates;
[ProtoMember(8)] public int MaxCandidates;
[ProtoMember(9)] public int AllowedRoles; // 0 = unset at read → world mask
[ProtoMember(10)] public int StarBias; // StarBias ordinal
[ProtoMember(11)] public bool RefillOnHire;
```

- [ ] **Step 2: Extend `HireRefreshRequest`**

```csharp
[ProtoMember(4)] public int MinCandidates;
[ProtoMember(5)] public int MaxCandidates;
[ProtoMember(6)] public int AllowedRoles;
[ProtoMember(7)] public int StarBias;
[ProtoMember(8)] public bool RefillOnHire;
[ProtoMember(9)] public bool ForceReroll;
```

Keep existing members 1–3. Reuse message id `HireRefreshMsg`.

- [ ] **Step 3: Manual check**

Mod still loads; unused fields only.

---

### Task 2: HireWorldConfig + CrewConfig runtime wiring

**Files:**
- Create: `Data/Scripts/HireCrew/HireWorldConfig.cs`
- Modify: `Data/Scripts/HireCrew/CrewConfig.cs`
- Modify: `Data/Scripts/HireCrew/CrewSession.cs` (load on init; clear on unload)
- Mirror under `Source/HireCrew/` when present

**Interfaces:**
- Consumes: `StarBias` from Task 1
- Produces:
  - `HireWorldConfig.Current` (static; null until loaded)
  - `HireWorldConfig.CreateDefaults()` — values matching today’s `CrewConfig` constants
  - `HireWorldConfig.LoadOrCreate(Type sessionType)` — world storage `HireCrewConfig.xml`
  - `void HireWorldConfig.Normalize()` — clamp ranges, fix arrays length 6, ensure ≥1 role bit
  - `int[] GetStarWeights(StarBias bias)` — Balanced = copy; Low = `w[i]*(6-i)`; High = `w[i]*(i+1)`
  - `static int AllRolesMask` = bits for roles 0..MaxRole
  - `static int FirstAllowedRole(int mask)`
  - `CrewConfig` clamp/get methods read from `HireWorldConfig.Current` when non-null

- [ ] **Step 1: Add `HireWorldConfig.cs`**

```csharp
using System;
using System.IO;
using System.Text;
using System.Xml.Serialization;
using Sandbox.ModAPI;

namespace HireCrew
{
    [XmlRoot("HireCrewConfig")]
    public sealed class HireWorldConfig
    {
        public const string FileName = "HireCrewConfig.xml";

        public static HireWorldConfig Current { get; private set; }

        public int RefreshMinutesMin = 1;
        public int RefreshMinutesMax = 300;
        public int RefreshMinutesDefault = 15;

        public int PriceMultiplierPercentMin = 25;
        public int PriceMultiplierPercentMax = 500;
        public int PriceMultiplierPercentDefault = 100;

        public int MinCandidates = 1;
        public int MaxCandidates = 8;

        public long[] PriceByStars = { 10000, 25000, 50000, 90000, 150000, 250000 };
        public float PriceVarianceFraction = 0.15f;
        public int[] StarWeights = { 25, 25, 20, 15, 10, 5 };

        /// <summary>Bit (1 &lt;&lt; (int)CrewRole). Default = all roles.</summary>
        public int AllowedRolesMask = AllRolesMask;

        public bool RefillOnHireDefault = false;

        public static int AllRolesMask
        {
            get
            {
                int m = 0;
                for (int i = 0; i <= CrewConfig.MaxRole; i++)
                    m |= (1 << i);
                return m;
            }
        }

        public static HireWorldConfig CreateDefaults()
        {
            return new HireWorldConfig();
        }

        public static void SetCurrent(HireWorldConfig cfg)
        {
            Current = cfg ?? CreateDefaults();
            Current.Normalize();
        }

        public static void ClearCurrent()
        {
            Current = null;
        }

        public void Normalize()
        {
            if (RefreshMinutesMin < 1) RefreshMinutesMin = 1;
            if (RefreshMinutesMax < RefreshMinutesMin) RefreshMinutesMax = RefreshMinutesMin;
            if (RefreshMinutesDefault < RefreshMinutesMin) RefreshMinutesDefault = RefreshMinutesMin;
            if (RefreshMinutesDefault > RefreshMinutesMax) RefreshMinutesDefault = RefreshMinutesMax;

            if (PriceMultiplierPercentMin < 1) PriceMultiplierPercentMin = 1;
            if (PriceMultiplierPercentMax < PriceMultiplierPercentMin)
                PriceMultiplierPercentMax = PriceMultiplierPercentMin;
            if (PriceMultiplierPercentDefault < PriceMultiplierPercentMin)
                PriceMultiplierPercentDefault = PriceMultiplierPercentMin;
            if (PriceMultiplierPercentDefault > PriceMultiplierPercentMax)
                PriceMultiplierPercentDefault = PriceMultiplierPercentMax;

            if (MinCandidates < 1) MinCandidates = 1;
            if (MaxCandidates < MinCandidates) MaxCandidates = MinCandidates;

            PriceByStars = FixLongArray(PriceByStars, new long[] { 10000, 25000, 50000, 90000, 150000, 250000 });
            StarWeights = FixIntArray(StarWeights, new int[] { 25, 25, 20, 15, 10, 5 });
            int weightSum = 0;
            for (int i = 0; i < StarWeights.Length; i++)
            {
                if (StarWeights[i] < 0) StarWeights[i] = 0;
                weightSum += StarWeights[i];
            }
            if (weightSum <= 0)
                StarWeights = new int[] { 25, 25, 20, 15, 10, 5 };

            if (PriceVarianceFraction < 0f) PriceVarianceFraction = 0f;
            if (PriceVarianceFraction > 0.9f) PriceVarianceFraction = 0.9f;

            AllowedRolesMask &= AllRolesMask;
            if (AllowedRolesMask == 0)
                AllowedRolesMask = AllRolesMask;
        }

        public int[] GetStarWeights(StarBias bias)
        {
            var src = StarWeights ?? new int[] { 25, 25, 20, 15, 10, 5 };
            var w = new int[6];
            for (int i = 0; i < 6; i++)
            {
                int baseW = i < src.Length ? src[i] : 0;
                if (baseW < 0) baseW = 0;
                if (bias == StarBias.Low)
                    w[i] = baseW * (5 - i + 1); // 6,5,4,3,2,1
                else if (bias == StarBias.High)
                    w[i] = baseW * (i + 1); // 1..6
                else
                    w[i] = baseW;
            }
            int sum = 0;
            for (int i = 0; i < w.Length; i++) sum += w[i];
            if (sum <= 0)
            {
                for (int i = 0; i < 6; i++) w[i] = 1;
            }
            return w;
        }

        public static int FirstAllowedRole(int mask)
        {
            mask &= AllRolesMask;
            if (mask == 0) mask = AllRolesMask;
            for (int i = 0; i <= CrewConfig.MaxRole; i++)
            {
                if ((mask & (1 << i)) != 0)
                    return i;
            }
            return (int)CrewRole.Gunner;
        }

        public static bool RoleAllowed(int mask, int role)
        {
            if (role < 0 || role > CrewConfig.MaxRole) return false;
            return (mask & (1 << role)) != 0;
        }

        public static HireWorldConfig LoadOrCreate(Type sessionType)
        {
            var defaults = CreateDefaults();
            defaults.Normalize();
            try
            {
                if (MyAPIGateway.Utilities != null
                    && MyAPIGateway.Utilities.FileExistsInWorldStorage(FileName, sessionType))
                {
                    using (var reader = MyAPIGateway.Utilities.ReadFileInWorldStorage(FileName, sessionType))
                    {
                        var text = reader.ReadToEnd();
                        if (!string.IsNullOrEmpty(text))
                        {
                            var ser = new XmlSerializer(typeof(HireWorldConfig));
                            using (var sr = new StringReader(text))
                            {
                                var loaded = ser.Deserialize(sr) as HireWorldConfig;
                                if (loaded != null)
                                {
                                    loaded.Normalize();
                                    SetCurrent(loaded);
                                    return Current;
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                MyAPIGateway.Utilities.ShowMessage("HireCrew", "HireCrewConfig.xml invalid — using defaults");
                VRage.Utils.MyLog.Default.WriteLine("HireCrew: HireCrewConfig.xml load failed: " + e.Message);
            }

            SetCurrent(defaults);
            Save(sessionType, Current);
            return Current;
        }

        public static void Save(Type sessionType, HireWorldConfig cfg)
        {
            if (cfg == null || MyAPIGateway.Utilities == null) return;
            cfg.Normalize();
            try
            {
                var ser = new XmlSerializer(typeof(HireWorldConfig));
                var sb = new StringBuilder();
                using (var sw = new StringWriter(sb))
                    ser.Serialize(sw, cfg);
                using (var writer = MyAPIGateway.Utilities.WriteFileInWorldStorage(FileName, sessionType))
                    writer.Write(sb.ToString());
            }
            catch (Exception e)
            {
                VRage.Utils.MyLog.Default.WriteLine("HireCrew: HireCrewConfig.xml save failed: " + e.Message);
            }
        }

        private static long[] FixLongArray(long[] arr, long[] fallback)
        {
            if (arr == null || arr.Length != 6) return (long[])fallback.Clone();
            for (int i = 0; i < 6; i++)
                if (arr[i] < 1) arr[i] = fallback[i];
            return arr;
        }

        private static int[] FixIntArray(int[] arr, int[] fallback)
        {
            if (arr == null || arr.Length != 6) return (int[])fallback.Clone();
            return arr;
        }
    }
}
```

- [ ] **Step 2: Wire `CrewConfig` to prefer `HireWorldConfig.Current`**

Change clamp/default accessors so tunable values come from world config when loaded. Keep private compile-time fallbacks (or existing const names as private const) and expose:

```csharp
public static int MinRefreshMinutes
{
    get
    {
        return HireWorldConfig.Current != null
            ? HireWorldConfig.Current.RefreshMinutesMin
            : 1;
    }
}
// Same pattern for MaxRefreshMinutes, DefaultRefreshMinutes,
// Min/Max/DefaultPriceMultiplierPercent, MinCandidates, MaxCandidates,
// PriceVarianceFraction, and GetPrice reading PriceByStars from Current when set.
```

Update `ClampRefreshMinutes` / `ClampPriceMultiplierPercent` to use those properties. Keep `StarWeights` as a static method or property that returns Balanced world weights (or compile-time copy when Current is null). Leave role bonus tables / train costs unchanged (not in hire-desk XML scope).

- [ ] **Step 3: Load/unload in `CrewSession`**

In session init (server path where store/pools load), after utilities are available:

```csharp
HireWorldConfig.LoadOrCreate(typeof(CrewSession));
```

In unload/dispose:

```csharp
HireWorldConfig.ClearCurrent();
```

- [ ] **Step 4: Manual check**

Load a world once; confirm `HireCrewConfig.xml` appears under the world’s Storage folder for the mod session type. Delete/corrupt it and reload — defaults rewrite, no crash.

- [ ] **Step 5: Commit only if user asks**

---

### Task 3: Generator uses desk settings + refill helper

**Files:**
- Modify: `Data/Scripts/HireCrew/CrewHirePool.cs` (`CrewHireGenerator` section)
- Mirror if needed

**Interfaces:**
- Consumes: `HireBlockPool` new fields, `HireWorldConfig.Current`, `StarBias`
- Produces:
  - `CrewHireGenerator.NormalizeDeskSettings(HireBlockPool pool)` — clamp desk vs world
  - `RefreshPool` / `CreateCandidate` / `GeneratePool` honor desk min/max, roles, bias, price mult
  - `HireCandidate CreateCandidateForPool(HireBlockPool pool, Random rng)`
  - `void RefillOne(HireBlockPool pool, Random rng)` — append one candidate

- [ ] **Step 1: Add normalize + weighted rolls from desk**

```csharp
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

public static int RollCandidateCount(Random rng, int min, int max)
{
    if (rng == null) rng = new Random();
    if (max < min) max = min;
    return rng.Next(min, max + 1);
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

public static CrewRole RollRole(Random rng, int allowedMask)
{
    if (rng == null) rng = new Random();
    allowedMask &= HireWorldConfig.AllRolesMask;
    if (allowedMask == 0)
        return (CrewRole)HireWorldConfig.FirstAllowedRole(HireWorldConfig.AllRolesMask);

    // Build compact list of allowed roles, pick uniform.
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
```

- [ ] **Step 2: Update `CreateCandidate` / `GeneratePool` / `RefreshPool`**

```csharp
public static HireCandidate CreateCandidateForPool(HireBlockPool pool, Random rng)
{
    NormalizeDeskSettings(pool);
    if (rng == null) rng = new Random();
    var world = HireWorldConfig.Current ?? HireWorldConfig.CreateDefaults();
    var weights = world.GetStarWeights((StarBias)pool.StarBias);
    int stars = RollStars(rng, weights);
    string first, last;
    CrewNames.RollName(rng, out first, out last);
    long raw = RollPrice(stars, rng); // RollPrice must use world PriceByStars + variance
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
```

Keep legacy `GeneratePool(rng, priceMultiplierPercent)` overloads working for any callers/tests by building a temporary pool with world defaults + that multiplier, or update call sites to the pool-based API.

Update `RollPrice` to use `HireWorldConfig.Current.PriceByStars` / `PriceVarianceFraction` when Current is set (via `CrewConfig.GetPrice` / variance property from Task 1).

- [ ] **Step 3: Manual check**

In debugger or temporary log, generate pools with gunner-only mask and High bias; confirm roles/stars skew as expected.

---

### Task 4: HirePoolStore binary v3 + Ensure defaults

**Files:**
- Modify: `Data/Scripts/HireCrew/CrewHirePool.cs` (`HirePoolStore`)
- Mirror if needed

**Interfaces:**
- Produces: `FormatVersion = 3`; Write/Read new desk fields; v1/v2 load defaults
- Consumes: `NormalizeDeskSettings`

- [ ] **Step 1: Bump format and write/read new fields**

After writing candidates (or after price mult — pick one consistent place; **after candidates** is fine):

Write order for v3 extras (after candidate list):
1. `MinCandidates` (int)
2. `MaxCandidates` (int)
3. `AllowedRoles` (int)
4. `StarBias` (int)
5. `RefillOnHire` (int 0/1)

```csharp
private const int FormatVersion = 3;

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
        p.PriceMultiplierPercent = 0; // Normalize fills default
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
```

Update `DeserializePool` comment to v3. `Ensure` for new pools:

```csharp
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
```

- [ ] **Step 2: Manual check**

Load an existing world with v2 pool data — desks still open, new fields defaulted, no exception.

---

### Task 5: Session settings apply + refill-on-hire + client API

**Files:**
- Modify: `Data/Scripts/HireCrew/CrewSession.cs`
- Mirror if needed

**Interfaces:**
- Consumes: extended `HireRefreshRequest`, generator apply helpers
- Produces:
  - `ClientRequestHireDeskSettings(...)` full overload
  - Thin wrappers for individual terminal fields that re-send full current desk state + flags
  - `HandleHireRefresh` apply priority from spec
  - `HandleHireFromPool` calls `RefillOne` when `pool.RefillOnHire`

- [ ] **Step 1: Replace client settings API**

```csharp
public void ClientRequestHireDeskSettings(
    long blockEntityId,
    int refreshMinutes,
    int priceMultiplierPercent,
    int minCandidates,
    int maxCandidates,
    int allowedRoles,
    int starBias,
    bool refillOnHire,
    bool forceReroll)
{
    var req = new HireRefreshRequest
    {
        BlockEntityId = blockEntityId,
        RefreshMinutes = refreshMinutes,
        PriceMultiplierPercent = priceMultiplierPercent,
        MinCandidates = minCandidates,
        MaxCandidates = maxCandidates,
        AllowedRoles = allowedRoles,
        StarBias = starBias,
        RefillOnHire = refillOnHire,
        ForceReroll = forceReroll
    };
    var data = CrewNetworking.Serialize(req);
    if (MyAPIGateway.Multiplayer.IsServer)
        HandleHireRefresh(req, MyAPIGateway.Session.Player.IdentityId, MyAPIGateway.Multiplayer.MyId);
    else
        CrewNetworking.SendToServer(CrewNetworking.HireRefreshMsg, data);
}

/// <summary>Build a settings request from current pool (or world defaults), overlaying one change.</summary>
private HireRefreshRequest BuildDeskSettingsFromPool(long blockEntityId)
{
    var world = HireWorldConfig.Current ?? HireWorldConfig.CreateDefaults();
    var pool = HirePools != null ? HirePools.Get(blockEntityId) : null;
    var req = new HireRefreshRequest
    {
        BlockEntityId = blockEntityId,
        RefreshMinutes = pool != null ? pool.RefreshMinutes : world.RefreshMinutesDefault,
        PriceMultiplierPercent = pool != null && pool.PriceMultiplierPercent > 0
            ? pool.PriceMultiplierPercent
            : world.PriceMultiplierPercentDefault,
        MinCandidates = pool != null && pool.MinCandidates > 0 ? pool.MinCandidates : world.MinCandidates,
        MaxCandidates = pool != null && pool.MaxCandidates > 0 ? pool.MaxCandidates : world.MaxCandidates,
        AllowedRoles = pool != null && pool.AllowedRoles != 0 ? pool.AllowedRoles : world.AllowedRolesMask,
        StarBias = pool != null ? pool.StarBias : (int)StarBias.Balanced,
        RefillOnHire = pool != null && pool.RefillOnHire,
        ForceReroll = false
    };
    return req;
}
```

Update `ClientRequestHireRefreshMinutes` / `ClientRequestHirePriceMultiplier` to use `BuildDeskSettingsFromPool`, overlay the changed field, call full `ClientRequestHireDeskSettings`. Add similar helpers for min/max candidates, star bias, roles, refill, and force reroll.

- [ ] **Step 2: Rewrite `HandleHireRefresh`**

Snapshot old values **before** mutation. `ApplyMultiplierToPool` needs the pool to still hold the old multiplier when called:

```csharp
private void HandleHireRefresh(HireRefreshRequest req, long identityId, ulong steamId)
{
    if (req == null || HirePools == null) return;

    IMyTerminalBlock block;
    IMyCubeGrid grid;
    if (!TryGetHireBlock(req.BlockEntityId, out block, out grid)) return;
    if (!HasManagePermission(identityId, grid)) { Notify(steamId, "No permission"); return; }

    var pool = HirePools.Ensure(block.EntityId, grid.EntityId, _hireRng, DateTime.UtcNow);
    CrewHireGenerator.NormalizeDeskSettings(pool);

    int oldMin = pool.MinCandidates;
    int oldMax = pool.MaxCandidates;
    int oldRoles = pool.AllowedRoles;
    int oldBias = pool.StarBias;
    int oldPrice = pool.PriceMultiplierPercent;
    int oldRefresh = pool.RefreshMinutes;

    pool.RefreshMinutes = req.RefreshMinutes;
    pool.MinCandidates = req.MinCandidates;
    pool.MaxCandidates = req.MaxCandidates;
    pool.AllowedRoles = req.AllowedRoles;
    pool.StarBias = req.StarBias;
    pool.RefillOnHire = req.RefillOnHire;
    // Keep old PriceMultiplierPercent on pool until rescale/reroll decision.
    CrewHireGenerator.NormalizeDeskSettings(pool);

    bool shapeChanged = req.ForceReroll
        || pool.MinCandidates != oldMin
        || pool.MaxCandidates != oldMax
        || pool.AllowedRoles != oldRoles
        || pool.StarBias != oldBias;

    int newPrice = CrewConfig.ClampPriceMultiplierPercent(
        req.PriceMultiplierPercent > 0
            ? req.PriceMultiplierPercent
            : oldPrice);
    bool priceChanged = newPrice != oldPrice;
    bool refreshChanged = pool.RefreshMinutes != oldRefresh;

    if (shapeChanged)
    {
        pool.PriceMultiplierPercent = newPrice;
        CrewHireGenerator.RefreshPool(pool, _hireRng, DateTime.UtcNow);
    }
    else if (priceChanged)
    {
        // pool still has oldPrice; this rescales candidates then sets new percent
        CrewHireGenerator.ApplyMultiplierToPool(pool, newPrice);
    }
    else
    {
        pool.PriceMultiplierPercent = newPrice;
        // refreshChanged or refill-only: no candidate mutation
    }

    BroadcastHirePool(pool);
}
```

- [ ] **Step 3: Refill on hire in `HandleHireFromPool`**

After successful hire (charge ok, record upserted), before `SendHirePoolTo`:

```csharp
if (pool.RefillOnHire)
    CrewHireGenerator.RefillOne(pool, _hireRng);
```

Broadcast or send updated pool to requester (existing `SendHirePoolTo` is enough if only requester needs it; prefer `BroadcastHirePool(pool)` when refilled so other clients see the new candidate).

- [ ] **Step 4: Manual check**

- Change star bias on terminal → candidates reroll immediately  
- Change price only → same names/stars/roles, prices scale  
- Change refresh only → countdown interval changes, list unchanged  
- Enable refill, hire one → slot replaced  
- Disable refill, hire one → count decreases  

---

### Task 6: Terminal controls

**Files:**
- Modify: `Data/Scripts/HireCrew/CrewHireBlockLogic.cs`
- Mirror if needed

**Interfaces:**
- Consumes: session client helpers from Task 5
- Produces: terminal controls ids:
  - `HireCrew_MinCandidates`, `HireCrew_MaxCandidates`
  - `HireCrew_StarBias` (listbox)
  - `HireCrew_Role_Gunner` … `HireCrew_Role_Quartermaster` (checkboxes)
  - `HireCrew_RefillOnHire` (on/off)
  - `HireCrew_RerollNow` (button)
- Update `FilterControls` whitelist + `AppendInfo`

- [ ] **Step 1: Add sliders / list / checks / buttons** following the existing refresh/price slider pattern

Star bias listbox:

```csharp
var bias = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlListbox, IMyTerminalBlock>("HireCrew_StarBias");
bias.Title = MyStringId.GetOrCompute("Star bias");
bias.SupportsMultipleBlocks = false;
bias.Visible = IsHireDesk;
bias.Enabled = IsHireDesk;
bias.Multiselect = false;
bias.ListContent = (b, content, selected) =>
{
    content.Add(new MyTerminalControlListBoxItem(MyStringId.GetOrCompute("Low"), MyStringId.NullOrEmpty, StarBias.Low));
    content.Add(new MyTerminalControlListBoxItem(MyStringId.GetOrCompute("Balanced"), MyStringId.NullOrEmpty, StarBias.Balanced));
    content.Add(new MyTerminalControlListBoxItem(MyStringId.GetOrCompute("High"), MyStringId.GetOrCompute("Favor higher stars"), StarBias.High));
    // mark selected from pool.StarBias
};
bias.ItemSelected = (b, selected) =>
{
    if (selected == null || selected.Count == 0) return;
    var val = (StarBias)selected[0].UserData;
    // session helper: set bias + force shape change (reroll)
};
```

Role checkbox helper (one per role): getter reads bit; setter toggles bit and sends settings (shape change → reroll). If world mask disallows role: `Enabled = false`, force unchecked visually.

Reroll button: `ForceReroll = true` with otherwise-current settings.

Min/Max candidate sliders: limits from `CrewConfig.MinCandidates`/`MaxCandidates` (world). On set, if min>max, bump the other when sending (server normalize also enforces).

Refill: `IMyTerminalControlOnOffSwitch` or checkbox.

- [ ] **Step 2: Update `AppendInfo`**

Include bias label, role summary (e.g. `G E H P Q` letters for enabled), refill on/off, min–max candidates.

- [ ] **Step 3: Whitelist new control ids in `FilterControls`**

- [ ] **Step 4: Manual check**

Open hire desk terminal: all controls visible only on `HC_CrewHireDesk`. Toggle gunner-only → pool becomes all gunners. Reroll button refreshes list. Controls hidden/disabled for roles removed in world XML.

---

### Task 7: Hire window status polish

**Files:**
- Modify: `Data/Scripts/HireCrew/CrewHireWindow.cs`
- Mirror if needed

**Interfaces:**
- Consumes: pool new fields

- [ ] **Step 1: Extend status line**

After price text, append short fragments when `_pool` is set, e.g.:

```csharp
string bias = ((StarBias)_pool.StarBias).ToString();
string refill = _pool.RefillOnHire ? "refill on" : "refill off";
// append "  |  " + bias + "  |  " + refill
```

Do not add full settings UI in RichHud (spec non-goal).

- [ ] **Step 2: Manual check**

Open hire UI after changing bias/refill in terminal; status reflects values after pool sync.

---

## Spec coverage checklist

| Spec item | Task |
|-----------|------|
| Per-desk fields + settings request | 1 |
| World XML load/defaults/rewrite | 2 |
| CrewConfig/runtime clamps | 2 |
| Star bias weight mapping | 2, 3 |
| Role mask generation | 3 |
| Binary v3 + old save defaults | 4 |
| Apply priority (reroll/rescale/timer) | 5 |
| Refill on hire | 3, 5 |
| Terminal knobs + reroll | 6 |
| Custom info | 6 |
| Hire UI status text | 7 |
| No automated tests | Global Constraints |
| No live XML reload command | omitted (follow-up) |

## Self-review notes

- Types aligned: `StarBias`, `AllowedRoles` int mask, `HireRefreshRequest` members 4–9.
- `HandleHireRefresh` must snapshot old price mult before mutation (called out in Task 5).
- Legacy `GeneratePool(rng)` / tests in repo may need overload updates if they still compile against old signatures — update signatures carefully; user asked for no new tests, but existing tests may break if run; only change what’s required for compile if the logic test project references generator APIs.
