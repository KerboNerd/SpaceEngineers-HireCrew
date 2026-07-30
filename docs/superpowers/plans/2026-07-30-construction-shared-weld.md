# Construction Shared Weld Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:subagent-driven-development` (recommended) or `superpowers:executing-plans` to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let up to 3 Construction welders share a large real block (`MaxIntegrity` ≥ threshold), prefer joining under-full large jobs, lift the cap when that block is the only work left, and keep small blocks / projector holograms exclusive.

**Architecture:** Pure slot/offset rules in `CrewRepairShareRules` (unit-tested). `CrewRepairMission` replaces binary `IsTargetClaimed` with claimant counts + max slots, prefers joinable large sibling targets when picking, and offsets shared hover poses so bodies do not stack.

**Tech Stack:** Space Engineers ModAPI, xunit (`HireCrew.Logic.Tests`), C# 7.3 / net48.

## Global Constraints

- Large = `MaxIntegrity >= RepairShareMaxIntegrity` (default `5000f`)
- Max **3** welders on a large block when other work exists (`RepairShareMaxWelders`)
- Only-remaining large work → unlimited joiners
- Small real blocks and **projected holograms** → max **1** claimant
- Prefer join under-full large sibling jobs before normal scoring
- Compile-time `CrewConfig` only (no XML)
- Do not run `dotnet test` / `dotnet build` unless the user explicitly asks; print the command for them

## File structure

| File | Responsibility |
| --- | --- |
| `Data/Scripts/HireCrew/CrewConfig.cs` | `RepairShareMaxIntegrity`, `RepairShareMaxWelders` |
| `Data/Scripts/HireCrew/CrewRepairShareRules.cs` | Pure max-slots + hover offset helpers |
| `Data/Scripts/HireCrew/CrewRepairMission.cs` | Count claimants, pick/join, hover offset |
| `tests/HireCrew.Logic.Tests/CrewRepairShareRulesTests.cs` | Unit tests |
| `tests/HireCrew.Logic.Tests/HireCrew.Logic.Tests.csproj` | Compile link |
| `.wiki/Damage-Control.md` | Player-facing note |

---

### Task 1: Config + pure share rules + tests

**Files:**
- Modify: `Data/Scripts/HireCrew/CrewConfig.cs`
- Create: `Data/Scripts/HireCrew/CrewRepairShareRules.cs`
- Create: `tests/HireCrew.Logic.Tests/CrewRepairShareRulesTests.cs`
- Modify: `tests/HireCrew.Logic.Tests/HireCrew.Logic.Tests.csproj`

**Interfaces:**
- Produces:
  - `CrewConfig.RepairShareMaxIntegrity` (`float`, default `5000f`)
  - `CrewConfig.RepairShareMaxWelders` (`int`, default `3`)
  - `CrewRepairShareRules.IsLargeBlock(float maxIntegrity, float shareMaxIntegrity) -> bool`
  - `CrewRepairShareRules.MaxClaimSlots(bool projected, float maxIntegrity, int shareMaxWelders, float shareMaxIntegrity, bool onlyRemainingWork) -> int`
  - `CrewRepairShareRules.IsClaimFull(int claimantsExcludingSelf, int maxSlots) -> bool`
  - `CrewRepairShareRules.SharedHoverWorldOffset(Vector3D outwardNorm, Vector3D sideNorm, int slotIndex) -> Vector3D`

- [ ] **Step 1: Add config constants**

In `CrewConfig.cs` near other repair constants, add:

```csharp
/// <summary>Real blocks with MaxIntegrity at or above this may be shared by multiple welders.</summary>
public const float RepairShareMaxIntegrity = 5000f;
/// <summary>Max concurrent welders on one large block when other work still exists.</summary>
public const int RepairShareMaxWelders = 3;
```

- [ ] **Step 2: Write failing tests**

Create `tests/HireCrew.Logic.Tests/CrewRepairShareRulesTests.cs`:

```csharp
using HireCrew;
using VRageMath;
using Xunit;

public class CrewRepairShareRulesTests
{
    [Fact]
    public void MaxClaimSlots_Projected_AlwaysOne()
    {
        Assert.Equal(1, CrewRepairShareRules.MaxClaimSlots(
            projected: true,
            maxIntegrity: 99999f,
            shareMaxWelders: 3,
            shareMaxIntegrity: 5000f,
            onlyRemainingWork: true));
    }

    [Fact]
    public void MaxClaimSlots_SmallReal_One()
    {
        Assert.Equal(1, CrewRepairShareRules.MaxClaimSlots(
            projected: false,
            maxIntegrity: 100f,
            shareMaxWelders: 3,
            shareMaxIntegrity: 5000f,
            onlyRemainingWork: false));
    }

    [Fact]
    public void MaxClaimSlots_LargeWithOtherWork_ShareCap()
    {
        Assert.Equal(3, CrewRepairShareRules.MaxClaimSlots(
            projected: false,
            maxIntegrity: 5000f,
            shareMaxWelders: 3,
            shareMaxIntegrity: 5000f,
            onlyRemainingWork: false));
    }

    [Fact]
    public void MaxClaimSlots_LargeOnlyRemaining_Unlimited()
    {
        Assert.Equal(int.MaxValue, CrewRepairShareRules.MaxClaimSlots(
            projected: false,
            maxIntegrity: 8000f,
            shareMaxWelders: 3,
            shareMaxIntegrity: 5000f,
            onlyRemainingWork: true));
    }

    [Fact]
    public void IsClaimFull_AtCap()
    {
        Assert.False(CrewRepairShareRules.IsClaimFull(2, 3));
        Assert.True(CrewRepairShareRules.IsClaimFull(3, 3));
    }

    [Fact]
    public void SharedHoverWorldOffset_SpreadsSlots()
    {
        var o = Vector3D.Forward;
        var s = Vector3D.Right;
        var a = CrewRepairShareRules.SharedHoverWorldOffset(o, s, 0);
        var b = CrewRepairShareRules.SharedHoverWorldOffset(o, s, 1);
        Assert.True(Vector3D.DistanceSquared(a, b) > 0.25);
    }
}
```

Add to `HireCrew.Logic.Tests.csproj` ItemGroup:

```xml
<Compile Include="..\..\Data\Scripts\HireCrew\CrewRepairShareRules.cs" Link="CrewRepairShareRules.cs" />
```

(`CrewConfig.cs` is already linked.)

- [ ] **Step 3: Implement `CrewRepairShareRules.cs`**

```csharp
using System;
using VRageMath;

namespace HireCrew
{
    /// <summary>
    /// Pure Construction shared-weld claim/hover helpers (no ModAPI).
    /// </summary>
    public static class CrewRepairShareRules
    {
        public static bool IsLargeBlock(float maxIntegrity, float shareMaxIntegrity)
        {
            return maxIntegrity >= shareMaxIntegrity;
        }

        public static int MaxClaimSlots(
            bool projected,
            float maxIntegrity,
            int shareMaxWelders,
            float shareMaxIntegrity,
            bool onlyRemainingWork)
        {
            if (projected)
                return 1;
            if (!IsLargeBlock(maxIntegrity, shareMaxIntegrity))
                return 1;
            if (onlyRemainingWork)
                return int.MaxValue;
            if (shareMaxWelders < 1)
                return 1;
            return shareMaxWelders;
        }

        public static bool IsClaimFull(int claimantsExcludingSelf, int maxSlots)
        {
            if (maxSlots == int.MaxValue)
                return false;
            return claimantsExcludingSelf >= maxSlots;
        }

        /// <summary>
        /// Lateral offset so shared welders do not occupy one hover point.
        /// slotIndex 0..n — spread along side axis, slight outward bias.
        /// </summary>
        public static Vector3D SharedHoverWorldOffset(
            Vector3D outwardNorm,
            Vector3D sideNorm,
            int slotIndex)
        {
            if (outwardNorm.LengthSquared() < 1e-8)
                outwardNorm = Vector3D.Forward;
            else
                outwardNorm.Normalize();
            if (sideNorm.LengthSquared() < 1e-8)
                sideNorm = Vector3D.CalculatePerpendicularVector(outwardNorm);
            else
                sideNorm.Normalize();

            int slot = Math.Abs(slotIndex) % 7;
            double lateral = (slot - 3) * 1.35;
            return sideNorm * lateral + outwardNorm * 0.15;
        }
    }
}
```

- [ ] **Step 4: Verify tests**

Tell the user to run (do not run yourself unless asked):

```
dotnet test tests/HireCrew.Logic.Tests/HireCrew.Logic.Tests.csproj --filter CrewRepairShareRulesTests
```

Expected: all PASS.

- [ ] **Step 5: Commit** (only if user asked to commit)

```bash
git add Data/Scripts/HireCrew/CrewConfig.cs Data/Scripts/HireCrew/CrewRepairShareRules.cs tests/HireCrew.Logic.Tests/CrewRepairShareRulesTests.cs tests/HireCrew.Logic.Tests/HireCrew.Logic.Tests.csproj
git commit -m "$(cat <<'EOF'
Add Construction shared-weld claim slot rules and config.

EOF
)"
```

---

### Task 2: Claim counting + pick/join in `CrewRepairMission`

**Files:**
- Modify: `Data/Scripts/HireCrew/CrewRepairMission.cs` (`IsTargetClaimed`, `TryPickWorkTarget`, `TryFinishIfOnlyUnaffordableWork` claim checks ~1200, dispatch if any)

**Interfaces:**
- Consumes: `CrewRepairShareRules.MaxClaimSlots`, `IsClaimFull`, `IsLargeBlock`; `CrewConfig.RepairShareMax*`
- Produces:
  - `CountTargetClaimants(long gridId, string selfCrewId, Vector3I cell, long projectorEntityId, bool projected) -> int`
  - `GetMaxClaimSlotsForWork(IMyCubeGrid grid, CachedWork w, IMySlimBlock slim, string selfCrewId) -> int`
  - `IsTargetClaimFull(...)` replacing binary full-claim checks
  - Join preference inside `TryPickWorkTarget`

- [ ] **Step 1: Replace binary claim with slot fullness**

Replace `IsTargetClaimed` usages for work picking with helpers:

```csharp
private static int CountTargetClaimants(
    long gridId,
    string selfCrewId,
    Vector3I cell,
    long projectorEntityId,
    bool projected)
{
    int n = 0;
    foreach (var kv in ByCrew)
    {
        if (kv.Value == null)
            continue;
        if (!string.IsNullOrEmpty(selfCrewId)
            && string.Equals(kv.Key, selfCrewId, StringComparison.Ordinal))
            continue;
        MissionRuntime m = kv.Value;
        if (m.GridEntityId != gridId || !m.HasTargetCell)
            continue;
        if (m.TargetIsProjected != projected)
            continue;
        if (projected && m.ProjectorEntityId != projectorEntityId)
            continue;
        if (m.TargetCell == cell)
            n++;
    }
    return n;
}

/// <summary>
/// True when other work exists on the cache that this crew could take instead of <paramref name="exclude"/>.
/// </summary>
private static bool WorkCacheHasOtherPickable(
    IMyCubeGrid grid,
    string selfCrewId,
    Vector3I excludeCell,
    long excludeProjectorId,
    bool excludeProjected)
{
    if (grid == null)
        return false;
    EnsureWorkCache(grid);
    long gridId = grid.EntityId;
    HashSet<string> selfSkips = null;
    MissionRuntime selfMission;
    if (!string.IsNullOrEmpty(selfCrewId) && ByCrew.TryGetValue(selfCrewId, out selfMission)
        && selfMission != null && selfMission.SkippedTargets.Count > 0)
        selfSkips = selfMission.SkippedTargets;

    for (int i = 0; i < WorkCache.Count; i++)
    {
        CachedWork w = WorkCache[i];
        if (w.Projected == excludeProjected
            && w.Cell == excludeCell
            && w.ProjectorEntityId == excludeProjectorId)
            continue;
        if (selfSkips != null
            && selfSkips.Contains(SkipTargetKey(w.Cell, w.ProjectorEntityId, w.Projected)))
            continue;
        IMySlimBlock slim;
        if (!TryResolveCachedWork(grid, w, out slim) || slim == null)
            continue;
        if (!CanAffordSlimWork(grid, slim, w.Projected))
            continue;
        // Treat as "other work" even if currently full — extras should not all pile
        // unless this exclude cell is truly alone in the cache after skips/afford.
        return true;
    }
    return false;
}

private static int MaxSlotsForCachedWork(
    IMyCubeGrid grid,
    string selfCrewId,
    CachedWork w,
    IMySlimBlock slim)
{
    float maxIntegrity = 0f;
    try { if (slim != null) maxIntegrity = slim.MaxIntegrity; }
    catch { }
    bool onlyRemaining = !WorkCacheHasOtherPickable(
        grid, selfCrewId, w.Cell, w.ProjectorEntityId, w.Projected);
    return CrewRepairShareRules.MaxClaimSlots(
        w.Projected,
        maxIntegrity,
        CrewConfig.RepairShareMaxWelders,
        CrewConfig.RepairShareMaxIntegrity,
        onlyRemaining);
}

private static bool IsTargetClaimFull(
    IMyCubeGrid grid,
    string selfCrewId,
    CachedWork w,
    IMySlimBlock slim)
{
    int claimants = CountTargetClaimants(
        grid.EntityId, selfCrewId, w.Cell, w.ProjectorEntityId, w.Projected);
    int max = MaxSlotsForCachedWork(grid, selfCrewId, w, slim);
    return CrewRepairShareRules.IsClaimFull(claimants, max);
}
```

Remove or keep old `IsTargetClaimed` as a thin wrapper that calls `IsTargetClaimFull` only when slim can be resolved — prefer updating call sites in `TryPickWorkTarget` and `TryFinishIfOnlyUnaffordableWork` to use `IsTargetClaimFull`.

- [ ] **Step 2: Prefer joining under-full large sibling jobs**

At the start of `TryPickWorkTarget`, after `EnsureWorkCache`:

```csharp
// Prefer joining an under-full large real target already claimed by a sibling.
double bestJoin = double.MaxValue;
int bestJoinIndex = -1;
for (int i = 0; i < WorkCache.Count; i++)
{
    CachedWork w = WorkCache[i];
    if (w.Projected)
        continue;
    if (selfSkips != null
        && selfSkips.Contains(SkipTargetKey(w.Cell, w.ProjectorEntityId, w.Projected)))
        continue;

    IMySlimBlock candidate;
    if (!TryResolveCachedWork(grid, w, out candidate) || candidate == null)
        continue;
    float maxIntegrity = 0f;
    try { maxIntegrity = candidate.MaxIntegrity; }
    catch { }
    if (!CrewRepairShareRules.IsLargeBlock(maxIntegrity, CrewConfig.RepairShareMaxIntegrity))
        continue;
    if (!CanAffordSlimWork(grid, candidate, false))
        continue;
    if (IsTargetClaimFull(grid, selfCrewId, w, candidate))
        continue;
    // Only "join" if at least one sibling already claims it.
    if (CountTargetClaimants(gridId, selfCrewId, w.Cell, w.ProjectorEntityId, w.Projected) <= 0)
        continue;

    double d = Vector3D.DistanceSquared(w.World, from);
    if (d < bestJoin)
    {
        bestJoin = d;
        bestJoinIndex = i;
    }
}

if (bestJoinIndex >= 0)
{
    CachedWork join = WorkCache[bestJoinIndex];
    isProjected = false;
    return TryResolveCachedWork(grid, join, out best) && best != null;
}
```

Then keep the existing scoring loop, replacing `IsTargetClaimed(...)` with resolve + `IsTargetClaimFull(grid, selfCrewId, w, candidate)`.

- [ ] **Step 3: Manual logic check (no automated game test)**

Checklist for implementer notes / user smoke later:

1. 4 Construction EVA, 1 large incomplete + many small damaged → ≤3 on large, ≥1 on small.
2. Only large left → all may join.
3. Projector hologram → still 1 placer.

- [ ] **Step 4: Commit** (only if user asked)

```bash
git add Data/Scripts/HireCrew/CrewRepairMission.cs
git commit -m "$(cat <<'EOF'
Allow up to three Construction welders on large shared blocks.

EOF
)"
```

---

### Task 3: Shared hover offsets

**Files:**
- Modify: `Data/Scripts/HireCrew/CrewRepairMission.cs` (`EnsureWeldApproach` / after hover computed)

**Interfaces:**
- Consumes: `CrewRepairShareRules.SharedHoverWorldOffset`
- Produces: distinct `HoverX/Y/Z` per crew sharing a cell

- [ ] **Step 1: Offset hover when sharing**

After `m.HasHover = true` is set in `EnsureWeldApproach` (both success path from `TryComputeWeldHover` and fallback), apply:

```csharp
int shareSlot = 0;
if (!string.IsNullOrEmpty(m.CrewId))
    shareSlot = Math.Abs(m.CrewId.GetHashCode()) % 7;

Vector3D block = GetSlimWorld(slim, grid);
Vector3D outward = new Vector3D(m.HoverX, m.HoverY, m.HoverZ) - block;
if (outward.LengthSquared() < 0.01)
    outward = grid.WorldMatrix.Forward;
outward.Normalize();
Vector3D side = Vector3D.CalculatePerpendicularVector(outward);
Vector3D off = CrewRepairShareRules.SharedHoverWorldOffset(outward, side, shareSlot);
m.HoverX += off.X;
m.HoverY += off.Y;
m.HoverZ += off.Z;
```

Keep staging logic based on the final hover.

- [ ] **Step 2: Commit** (only if user asked)

```bash
git add Data/Scripts/HireCrew/CrewRepairMission.cs
git commit -m "$(cat <<'EOF'
Offset shared Construction weld hover poses.

EOF
)"
```

---

### Task 4: Wiki note

**Files:**
- Modify: `.wiki/Damage-Control.md`

- [ ] **Step 1: Document sharing**

Under Runtime loop or Tips, add a short bullet:

```markdown
* Large blocks (high integrity, e.g. big thrusters) can be welded by up to **3** Construction crew at once; others keep working elsewhere. If that large block is the only work left, everyone may join.
```

- [ ] **Step 2: Commit wiki** (only if user asked; wiki is a separate git repo under `.wiki/`)

---

## Spec coverage checklist

| Spec requirement | Task |
| --- | --- |
| Max 3 on large when other work exists | 1 + 2 |
| Unlimited when only remaining | 1 + 2 (`onlyRemainingWork`) |
| Small blocks exclusive | 1 + 2 |
| Projected place exclusive | 1 + 2 |
| Prefer join under-full large sibling | 2 |
| Hover offsets | 3 |
| Wiki | 4 |
| Weld ticks unchanged / stack naturally | (no code; concurrent `TryWeldTick`) |

## Placeholder scan

None intentional. Threshold `5000f` is the approved playtest default from the spec.
