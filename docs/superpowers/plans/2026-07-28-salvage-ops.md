# Salvage Ops Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship hireable Salvage Ops crew who manually dispatch from the HUD, pick a legal nearby grid, EVA without a path, grind blocks, deposit components into home-ship cargo, and teleport home on done/full/Recall — without changing Construction.

**Architecture:** Parallel `CrewSalvageMission` mirrors Construction’s EVA theater and teleport-home return, but stores a `TargetGridEntityId`, uses scripted `DecreaseMountLevel` into home physical-group inventories, and never touches `RepairPaths`. Pure `CrewSalvageRules` + status labels stay unit-testable; SE mission code stays in `Data/Scripts/HireCrew/`.

**Tech Stack:** Space Engineers ModAPI (characters, slim blocks, inventories, multiplayer messages), existing HireCrew ambient + RichHud stack, protobuf-net request DTOs, xunit (`HireCrew.Logic.Tests`).

**Spec:** `docs/superpowers/specs/2026-07-28-salvage-ops-design.md`

## Global Constraints

- Approach B only: new `CrewSalvageMission`; do **not** add grind branches to `CrewRepairMission`.
- No path painting / no auto-dispatch / no floating-item drops / no grind filters.
- Manual per-crew Salvage/Recall; Send requires player-picked `TargetGridEntityId`.
- Legal targets: own, faction, unowned/NPC; never enemy. Scan radius **2000 m** from home grid.
- Cargo full → teleport home + notify. Return always teleports (no WalkHome).
- Construction Send/Recall/weld/path/sidebar rows for Construction must keep working.
- Agent must not run `dotnet` / `dotnet build` / `dotnet test`; user runs tests.
- Do not touch commented-out code.
- Commit only when the user explicitly asks (skip commit steps unless requested).
- Keep `Source/HireCrew/` mirrors in sync for files that already exist there. New files that only live under `Data/Scripts/HireCrew/` need no Source copy unless Source already has a counterpart.

## File structure

| File | Role |
|------|------|
| `Data/Scripts/HireCrew/CrewModels.cs` | `CrewRole.SalvageOps`; salvage state/hints/DTOs; dispatch request |
| `Data/Scripts/HireCrew/CrewConfig.cs` | `MaxRole`, `RoleLabel`, salvage rate/range constants |
| `Data/Scripts/HireCrew/CrewSalvageRules.cs` | Pure legality + relation helper (testable) |
| `Data/Scripts/HireCrew/CrewSalvageMission.cs` | Mission state machine, EVA, grind, cargo deposit, teleport home |
| `Data/Scripts/HireCrew/CrewNetworking.cs` | `SalvageDispatchMsg`, `SalvageMissionSyncMsg` |
| `Data/Scripts/HireCrew/CrewSession.cs` | Handlers, client request, tick, sync push |
| `Data/Scripts/HireCrew/CrewAmbientPresence.cs` | Treat salvage missions like repair for wander yield / body-loss |
| `Data/Scripts/HireCrew/CrewHireBlockLogic.cs` | Allow Salvage Ops checkbox + `S` in FormatRoles |
| `Data/Scripts/HireCrew/CrewAdminCommands.cs` | Parse tokens `salvage` / `salv` / `grinder` |
| `Data/Scripts/HireCrew/CrewHudWindow.cs` | Per-row Salvage/Recall + target grid picker |
| `Data/Scripts/HireCrew/CrewHud.cs` | Sidebar lifecycle if salvage sync needs wiring |
| `Data/Scripts/HireCrew/CrewStatusHudModel.cs` | Labels + merged rows for salvage snapshots |
| `Data/Scripts/HireCrew/CrewStatusSidebar.cs` | Render merged salvage rows (if model API changes) |
| `Data/Scripts/HireCrew/CrewBlockInfo.cs` | Role detail line for Salvage Ops |
| `Source/HireCrew/CrewModels.cs` | Mirror DTOs/enum |
| `Source/HireCrew/CrewConfig.cs` | Mirror MaxRole / RoleLabel / constants |
| `Source/HireCrew/CrewNetworking.cs` | Mirror msg ids |
| `Source/HireCrew/CrewSession.cs` | Mirror handlers if Source session still maintained |
| `Source/HireCrew/CrewHudWindow.cs` | Mirror HUD salvage controls if Source window maintained |
| `tests/HireCrew.Logic.Tests/CrewConfigTests.cs` | Clamp/label/NeedsWeapon for SalvageOps |
| `tests/HireCrew.Logic.Tests/CrewSalvageRulesTests.cs` | Legality + status labels |
| `tests/HireCrew.Logic.Tests/CrewStatusHudModelTests.cs` | Salvage sidebar rows |
| `tests/HireCrew.Logic.Tests/HireCrew.Logic.Tests.csproj` | Link `CrewSalvageRules.cs` |

---

### Task 1: Salvage Ops role wiring

**Files:**
- Modify: `Data/Scripts/HireCrew/CrewModels.cs`
- Modify: `Data/Scripts/HireCrew/CrewConfig.cs`
- Modify: `Data/Scripts/HireCrew/CrewHireBlockLogic.cs`
- Modify: `Data/Scripts/HireCrew/CrewAdminCommands.cs`
- Modify: `Data/Scripts/HireCrew/CrewHudWindow.cs` (role color + detail text only)
- Modify: `Data/Scripts/HireCrew/CrewBlockInfo.cs` (role detail if switched)
- Modify: `Source/HireCrew/CrewModels.cs`, `Source/HireCrew/CrewConfig.cs` (mirrors)
- Test: `tests/HireCrew.Logic.Tests/CrewConfigTests.cs`

**Interfaces:**
- Produces:
  - `CrewRole.SalvageOps = 6`
  - `CrewConfig.MaxRole == (int)CrewRole.SalvageOps`
  - `CrewConfig.RoleLabel(CrewRole.SalvageOps) == "Salvage Ops"`
  - `CrewConfig.NeedsWeapon(CrewRole.SalvageOps) == false`
  - Hire desk: `HireCrew_Role_SalvageOps` / "Allow Salvage Ops"; FormatRoles appends `S`
  - Admin tokens: `salvage`, `salv`, `grinder` → `CrewRole.SalvageOps`
  - HUD color: `new Color(220, 140, 70)` (muted orange; distinct from Construction yellow)
  - Config constants (compile-time):
    - `SalvageScanRadiusMeters = 2000f`
    - `SalvageGrindRangeMeters = 5f`
    - `SalvageEvaStandOffMeters = 4f`
    - `SalvageEvaSpeedMeters = 9f` (same base as repair)
    - `GetSalvageGrindMountPerSecond(int stars)` → same curve as weld: base × `(0.75f + 0.1f * ClampStars(stars))`
    - `GetSalvageEvaSpeedMeters(int stars)` → same curve as repair EVA
    - `SalvageMaxParallelPerGrid = 0` (unlimited)

- [x] **Step 1: Write failing tests**

In `CrewConfigTests.cs`, update `ClampRole_accepts_new_roles` and add assertions:

```csharp
[Fact]
public void ClampRole_accepts_new_roles()
{
    Assert.Equal(CrewRole.Propulsion, CrewConfig.ClampRole((int)CrewRole.Propulsion));
    Assert.Equal(CrewRole.Gunner, CrewConfig.ClampRole(-1));
    Assert.Equal(CrewRole.SalvageOps, CrewConfig.ClampRole(999));
    Assert.Equal("Construction", CrewConfig.RoleLabel(CrewRole.DamageControl));
    Assert.Equal("Salvage Ops", CrewConfig.RoleLabel(CrewRole.SalvageOps));
    Assert.False(CrewConfig.NeedsWeapon(CrewRole.DamageControl));
    Assert.False(CrewConfig.NeedsWeapon(CrewRole.SalvageOps));
}

[Fact]
public void Salvage_rate_helpers_scale_with_stars()
{
    Assert.True(CrewConfig.GetSalvageGrindMountPerSecond(0)
        < CrewConfig.GetSalvageGrindMountPerSecond(5));
    Assert.True(CrewConfig.GetSalvageEvaSpeedMeters(0)
        < CrewConfig.GetSalvageEvaSpeedMeters(5));
    Assert.Equal(2000f, CrewConfig.SalvageScanRadiusMeters);
}
```

- [ ] **Step 2: User runs tests (expect FAIL)**

Run: `dotnet test tests/HireCrew.Logic.Tests/HireCrew.Logic.Tests.csproj --filter "FullyQualifiedName~ClampRole_accepts_new_roles|FullyQualifiedName~Salvage_rate"`

Expected: FAIL (SalvageOps / helpers missing).

- [ ] **Step 3: Implement role + config**

In `CrewRole`:

```csharp
public enum CrewRole
{
    Gunner = 0,
    Engineer = 1,
    Helmsman = 2,
    Propulsion = 3,
    Quartermaster = 4,
    DamageControl = 5,
    SalvageOps = 6
}
```

In `CrewConfig`:

```csharp
public const int MaxRole = (int)CrewRole.SalvageOps;

public const float SalvageScanRadiusMeters = 2000f;
public const float SalvageGrindRangeMeters = 5f;
public const float SalvageEvaStandOffMeters = 4f;
public const float SalvageEvaSpeedMeters = 9f;
public const float SalvageEvaAccelMeters = 6f;
public const float SalvageEvaTurnRate = 3.5f;
public const float SalvageEvaArriveMeters = 1.5f;
public const int SalvageMaxParallelPerGrid = 0;

/// <summary>Base Keen grinder-seconds applied per real second at 0★.</summary>
public const float SalvageGrindMountPerSecondBase = 0.35f;

public static float GetSalvageGrindMountPerSecond(int stars)
{
    return SalvageGrindMountPerSecondBase * (0.75f + 0.1f * ClampStars(stars));
}

public static float GetSalvageEvaSpeedMeters(int stars)
{
    return SalvageEvaSpeedMeters * (0.75f + 0.1f * ClampStars(stars));
}
```

Add `case CrewRole.SalvageOps: return "Salvage Ops";` in `RoleLabel`. Do **not** add Salvage Ops to power/helm/propulsion/QM tables.

- [ ] **Step 4: Hire desk + admin + HUD color + detail**

- `CrewHireBlockLogic`: `AddRoleCheckbox(CrewRole.SalvageOps, "HireCrew_Role_SalvageOps", "Allow Salvage Ops");` and append `S` in `FormatRoles`.
- `CrewAdminCommands.TryParseRole`: accept `salvage` / `salv` / `grinder` → `CrewRole.SalvageOps`.
- `CrewHudWindow` role color: `case CrewRole.SalvageOps: return new Color(220, 140, 70);`
- Role detail / amenity hint: `"Manual EVA salvage — pick a grid from HUD"`.
- Mirror enum/`MaxRole`/`RoleLabel`/constants in `Source/HireCrew/` counterparts.

- [ ] **Step 5: User runs tests (expect PASS)**

Run: `dotnet test tests/HireCrew.Logic.Tests/HireCrew.Logic.Tests.csproj --filter "FullyQualifiedName~ClampRole_accepts_new_roles|FullyQualifiedName~Salvage_rate"`

Expected: PASS.

- [ ] **Step 6: Commit (only if user asked)**

```bash
git add Data/Scripts/HireCrew/CrewModels.cs Data/Scripts/HireCrew/CrewConfig.cs Data/Scripts/HireCrew/CrewHireBlockLogic.cs Data/Scripts/HireCrew/CrewAdminCommands.cs Data/Scripts/HireCrew/CrewHudWindow.cs Data/Scripts/HireCrew/CrewBlockInfo.cs Source/HireCrew/CrewModels.cs Source/HireCrew/CrewConfig.cs tests/HireCrew.Logic.Tests/CrewConfigTests.cs
git commit -m "feat: add Salvage Ops crew role wiring"
```

---

### Task 2: Pure salvage rules + status labels

**Files:**
- Create: `Data/Scripts/HireCrew/CrewSalvageRules.cs`
- Modify: `Data/Scripts/HireCrew/CrewStatusHudModel.cs`
- Modify: `tests/HireCrew.Logic.Tests/HireCrew.Logic.Tests.csproj`
- Test: `tests/HireCrew.Logic.Tests/CrewSalvageRulesTests.cs`
- Test: `tests/HireCrew.Logic.Tests/CrewStatusHudModelTests.cs`

**Interfaces:**
- Consumes: `CrewRole`, future `SalvageMissionState` / hint flags (added in Task 3 — for this task define labels against ints/enums that Task 3 will place in `CrewModels`)
- Produces:
  - `enum SalvageTargetRelation { Own = 0, Faction = 1, Unowned = 2, Enemy = 3 }`
  - `CrewSalvageRules.IsLegalTarget(SalvageTargetRelation relation) → bool` (false only for Enemy)
  - `CrewSalvageRules.ClassifyTarget(long viewerIdentityId, long viewerFactionIdOrZero, long gridPrimaryOwnerId, long gridOwnerFactionIdOrZero) → SalvageTargetRelation`
    - `gridPrimaryOwnerId == 0` → Unowned
    - `gridPrimaryOwnerId == viewerIdentityId` → Own
    - both faction ids non-zero and equal → Faction
    - else → Enemy
  - Status/hint helpers used by sidebar (may live on `CrewStatusHudModel`):
    - `StatusLabelForSalvage(SalvageMissionState)` → `"EVA"` / `"Grinding"` / `""` for Idle
    - `HintLabelForSalvage(int hints)` → `"Cargo full"` when flag set

Note: If `SalvageMissionState` is not yet in `CrewModels`, add a minimal enum in `CrewModels.cs` as part of this task (Idle=0, EvaTransit=1, Grinding=2) so tests compile; Task 3 only adds DTOs/msg wiring.

- [ ] **Step 1: Write failing tests**

Create `CrewSalvageRulesTests.cs`:

```csharp
using HireCrew;
using Xunit;

public class CrewSalvageRulesTests
{
    [Fact]
    public void IsLegalTarget_rejects_only_enemy()
    {
        Assert.True(CrewSalvageRules.IsLegalTarget(SalvageTargetRelation.Own));
        Assert.True(CrewSalvageRules.IsLegalTarget(SalvageTargetRelation.Faction));
        Assert.True(CrewSalvageRules.IsLegalTarget(SalvageTargetRelation.Unowned));
        Assert.False(CrewSalvageRules.IsLegalTarget(SalvageTargetRelation.Enemy));
    }

    [Fact]
    public void ClassifyTarget_maps_owners()
    {
        Assert.Equal(SalvageTargetRelation.Unowned,
            CrewSalvageRules.ClassifyTarget(10, 100, 0, 0));
        Assert.Equal(SalvageTargetRelation.Own,
            CrewSalvageRules.ClassifyTarget(10, 100, 10, 0));
        Assert.Equal(SalvageTargetRelation.Faction,
            CrewSalvageRules.ClassifyTarget(10, 100, 20, 100));
        Assert.Equal(SalvageTargetRelation.Enemy,
            CrewSalvageRules.ClassifyTarget(10, 100, 20, 200));
        Assert.Equal(SalvageTargetRelation.Enemy,
            CrewSalvageRules.ClassifyTarget(10, 0, 20, 0));
    }
}
```

Extend `CrewStatusHudModelTests` (or add facts) for salvage labels once enum exists:

```csharp
[Fact]
public void Salvage_status_and_hint_labels()
{
    Assert.Equal("EVA", CrewStatusHudModel.StatusLabelForSalvage(SalvageMissionState.EvaTransit));
    Assert.Equal("Grinding", CrewStatusHudModel.StatusLabelForSalvage(SalvageMissionState.Grinding));
    Assert.Equal("", CrewStatusHudModel.StatusLabelForSalvage(SalvageMissionState.Idle));
    Assert.Equal("Cargo full",
        CrewStatusHudModel.HintLabelForSalvage(SalvageMissionHintFlags.CargoFull));
}
```

- [ ] **Step 2: User runs tests (expect FAIL)**

Run: `dotnet test tests/HireCrew.Logic.Tests/HireCrew.Logic.Tests.csproj --filter "FullyQualifiedName~CrewSalvageRulesTests|FullyQualifiedName~Salvage_status"`

Expected: FAIL (types missing).

- [ ] **Step 3: Implement pure helpers**

Add to `CrewModels.cs` (before dispatch DTOs):

```csharp
public enum SalvageMissionState
{
    Idle = 0,
    EvaTransit = 1,
    Grinding = 2
}

public static class SalvageMissionHintFlags
{
    public const int None = 0;
    public const int CargoFull = 1;
}
```

Create `CrewSalvageRules.cs`:

```csharp
namespace HireCrew
{
    public enum SalvageTargetRelation
    {
        Own = 0,
        Faction = 1,
        Unowned = 2,
        Enemy = 3
    }

    public static class CrewSalvageRules
    {
        public static bool IsLegalTarget(SalvageTargetRelation relation)
        {
            return relation != SalvageTargetRelation.Enemy;
        }

        public static SalvageTargetRelation ClassifyTarget(
            long viewerIdentityId,
            long viewerFactionIdOrZero,
            long gridPrimaryOwnerId,
            long gridOwnerFactionIdOrZero)
        {
            if (gridPrimaryOwnerId == 0)
                return SalvageTargetRelation.Unowned;
            if (viewerIdentityId != 0 && gridPrimaryOwnerId == viewerIdentityId)
                return SalvageTargetRelation.Own;
            if (viewerFactionIdOrZero != 0
                && gridOwnerFactionIdOrZero != 0
                && viewerFactionIdOrZero == gridOwnerFactionIdOrZero)
                return SalvageTargetRelation.Faction;
            return SalvageTargetRelation.Enemy;
        }
    }
}
```

In `CrewStatusHudModel`:

```csharp
public static string StatusLabelForSalvage(SalvageMissionState state)
{
    switch (state)
    {
        case SalvageMissionState.EvaTransit: return "EVA";
        case SalvageMissionState.Grinding: return "Grinding";
        default: return "";
    }
}

public static string HintLabelForSalvage(int hints)
{
    if ((hints & SalvageMissionHintFlags.CargoFull) != 0)
        return "Cargo full";
    return "";
}
```

Link `CrewSalvageRules.cs` in the test csproj.

- [ ] **Step 4: User runs tests (expect PASS)**

Run: `dotnet test tests/HireCrew.Logic.Tests/HireCrew.Logic.Tests.csproj --filter "FullyQualifiedName~CrewSalvageRulesTests|FullyQualifiedName~Salvage_status"`

Expected: PASS.

- [ ] **Step 5: Commit (only if user asked)**

```bash
git add Data/Scripts/HireCrew/CrewSalvageRules.cs Data/Scripts/HireCrew/CrewModels.cs Data/Scripts/HireCrew/CrewStatusHudModel.cs Source/HireCrew/CrewModels.cs tests/HireCrew.Logic.Tests/CrewSalvageRulesTests.cs tests/HireCrew.Logic.Tests/CrewStatusHudModelTests.cs tests/HireCrew.Logic.Tests/HireCrew.Logic.Tests.csproj
git commit -m "feat: add Salvage Ops target rules and status labels"
```

---

### Task 3: Salvage dispatch + sync networking

**Files:**
- Modify: `Data/Scripts/HireCrew/CrewModels.cs`
- Modify: `Data/Scripts/HireCrew/CrewNetworking.cs`
- Modify: `Source/HireCrew/CrewModels.cs`, `Source/HireCrew/CrewNetworking.cs`
- Modify: `Data/Scripts/HireCrew/CrewSession.cs` (register handlers stubs that call into mission — full logic in Task 4/5)
- Test: none new (protobuf types only); keep Task 2 tests green

**Interfaces:**
- Produces:
  - `SalvageDispatchRequest { string CrewId; bool Recall; long TargetGridEntityId; }`
  - `SalvageMissionSnapshotEntry { string CrewId; string DisplayName; long GridEntityId; int State; int Hints; }`
  - `SalvageMissionSync { List<SalvageMissionSnapshotEntry> Entries; }`
  - `CrewNetworking.SalvageDispatchMsg = 41749`
  - `CrewNetworking.SalvageMissionSyncMsg = 41750`
  - Register/Unregister both in `CrewNetworking.Register` / `Unregister`

- [ ] **Step 1: Add DTOs to `CrewModels.cs`**

```csharp
[ProtoContract]
public sealed class SalvageMissionSnapshotEntry
{
    [ProtoMember(1)] public string CrewId;
    [ProtoMember(2)] public string DisplayName;
    [ProtoMember(3)] public long GridEntityId;
    [ProtoMember(4)] public int State;
    [ProtoMember(5)] public int Hints;
}

[ProtoContract]
public sealed class SalvageMissionSync
{
    [ProtoMember(1)] public List<SalvageMissionSnapshotEntry> Entries = new List<SalvageMissionSnapshotEntry>();
}

[ProtoContract]
public sealed class SalvageDispatchRequest
{
    [ProtoMember(1)] public string CrewId;
    /// <summary>false = Salvage this crew, true = Recall this crew.</summary>
    [ProtoMember(2)] public bool Recall;
    /// <summary>Target wreck/home grid. Ignored when Recall is true.</summary>
    [ProtoMember(3)] public long TargetGridEntityId;
}
```

- [ ] **Step 2: Wire message ids**

In `CrewNetworking.cs`:

```csharp
public const ushort SalvageDispatchMsg = 41749;
public const ushort SalvageMissionSyncMsg = 41750;
```

Register and unregister both next to the repair messages. Mirror in Source.

- [ ] **Step 3: Session dispatch stubs**

In `CrewSession` message switch, add:

```csharp
else if (id == CrewNetworking.SalvageDispatchMsg)
    HandleSalvageDispatch(CrewNetworking.Deserialize<SalvageDispatchRequest>(data), identityId, sender);
else if (id == CrewNetworking.SalvageMissionSyncMsg)
    HandleSalvageMissionSync(CrewNetworking.Deserialize<SalvageMissionSync>(data));
```

Add:

```csharp
public void ClientRequestSalvageDispatch(string crewId, bool recall, long targetGridEntityId)
{
    if (string.IsNullOrEmpty(crewId))
        return;
    var req = new SalvageDispatchRequest
    {
        CrewId = crewId,
        Recall = recall,
        TargetGridEntityId = targetGridEntityId
    };
    var data = CrewNetworking.Serialize(req);
    if (MyAPIGateway.Multiplayer.IsServer)
        HandleSalvageDispatch(req, MyAPIGateway.Session.Player.IdentityId, MyAPIGateway.Multiplayer.MyId);
    else
        CrewNetworking.SendToServer(CrewNetworking.SalvageDispatchMsg, data);
}
```

`HandleSalvageDispatch` for now: validate crew role/`HasManagePermission`/idle home grid; if `Recall` call `CrewSalvageMission.RecallCrew` when Task 4 exists — until then leave a clear `Notify(steamId, "Salvage: not ready");` path and implement fully in Task 5 after mission API exists. Prefer implementing the full handler in Task 5; this task only needs msg constants + DTO + client serialize helper + switch cases that compile (handler can early-return with notify until Task 5).

- [ ] **Step 4: Manual compile check**

Reload mod / SE script compile. No runtime salvage yet.

- [ ] **Step 5: Commit (only if user asked)**

```bash
git add Data/Scripts/HireCrew/CrewModels.cs Data/Scripts/HireCrew/CrewNetworking.cs Data/Scripts/HireCrew/CrewSession.cs Source/HireCrew/CrewModels.cs Source/HireCrew/CrewNetworking.cs Source/HireCrew/CrewSession.cs
git commit -m "feat: add Salvage Ops dispatch and sync message types"
```

---

### Task 4: `CrewSalvageMission` — dispatch, EVA, grind, home

**Files:**
- Create: `Data/Scripts/HireCrew/CrewSalvageMission.cs`
- Modify: `Data/Scripts/HireCrew/CrewAmbientPresence.cs` (mission checks)
- Test: none for SE mission (API-bound); keep pure tests green

**Interfaces:**
- Consumes:
  - `CrewConfig` salvage constants/helpers
  - `CrewSalvageRules.ClassifyTarget` / `IsLegalTarget`
  - `CrewAmbientPresence.IsGridIdle`, jetpack/move helpers (same calls Construction uses)
- Produces:
  - `CrewSalvageMission.IsCrewOnMission(string crewId) → bool`
  - `CrewSalvageMission.DispatchCrew(CrewSession session, string crewId, long targetGridEntityId) → bool`
  - `CrewSalvageMission.RecallCrew(string crewId) → bool`
  - `CrewSalvageMission.Tick(CrewSession session, double dt)`
  - `CrewSalvageMission.CollectActiveSnapshots(List<SalvageMissionSnapshotEntry> into)`
  - `CrewSalvageMission.TryGetMissionPose(string crewId, out Vector3D pos, out Vector3D forward)` (for ambient respawn)
  - `CrewSalvageMission.ClearAll()` / `CancelForCrew(string crewId)`
  - Runtime fields: `CrewId`, `HomeGridEntityId`, `TargetGridEntityId`, `State`, `Hints`, target cell/block refs, fly dynamics

**State machine:** `Idle → EvaTransit → Grinding → (BeginReturn teleport) → Idle`

- [ ] **Step 1: Skeleton + dispatch/recall**

```csharp
public static class CrewSalvageMission
{
    private sealed class MissionRuntime
    {
        public string CrewId;
        public long HomeGridEntityId;
        public long TargetGridEntityId;
        public SalvageMissionState State;
        public float StateSeconds;
        public int Hints;
        public bool NotifiedCargoFull;
        // target slim cell + fly dynamics fields mirrored from repair mission as needed
    }

    private static readonly Dictionary<string, MissionRuntime> ByCrew =
        new Dictionary<string, MissionRuntime>();

    public static bool IsCrewOnMission(string crewId)
    {
        return !string.IsNullOrEmpty(crewId) && ByCrew.ContainsKey(crewId);
    }

    public static bool DispatchCrew(CrewSession session, string crewId, long targetGridEntityId)
    {
        // Validate: seated SalvageOps, home grid idle, parallel cap, target exists,
        // distance(home, target) <= SalvageScanRadiusMeters, ClassifyTarget legal for crew owner.
        // Create MissionRuntime State=EvaTransit; pick first grind block near crew/home exit.
        // Return false if invalid.
    }

    public static bool RecallCrew(string crewId)
    {
        MissionRuntime m;
        if (!ByCrew.TryGetValue(crewId, out m) || m == null)
            return false;
        BeginReturn(m, cargoFull: false);
        return true;
    }
}
```

Ownership mapping for `ClassifyTarget`: use crew `OwnerIdentityId`; viewer faction via `MyAPIGateway.Session.Factions.TryGetPlayerFaction`; grid primary owner = first `BigOwners` entry (0 if none); grid faction from that owner’s faction.

Distance: use world AABB centers (or seat→target center). Reject if `DistanceSquared > radius*radius`.

- [ ] **Step 2: Tick EvaTransit + Grinding**

Per-frame (server):

1. If home grid missing / not idle → `BeginReturn`.
2. If target grid missing / no grindable blocks → `BeginReturn`.
3. **EvaTransit:** fly character (or advance logical pose when no body) toward stand-off near current target block using `GetSalvageEvaSpeedMeters`. When within `SalvageGrindRangeMeters`, set `State = Grinding`.
4. **Grinding:**
   - Resolve target slim; if gone, pick next on target grid (nearest to character/logical pos).
   - Find a home physical-group inventory with free volume (`CollectHomeInventories` — copy Construction’s physical-group + `GridsShareCargoAccess` pattern into this file; do not call private repair methods).
   - If no inventory can accept items (`IsFull` / no free space heuristic): set `Hints |= CargoFull`, notify once, `BeginReturn(..., cargoFull: true)`.
   - Else `slim.DecreaseMountLevel(amount, inventory, ...)` with `amount = GetSalvageGrindMountPerSecond(stars) * dt`.
   - If block destroyed/removed, clear target and pick next; if none left, `BeginReturn`.

Copy Construction’s ambient character drive patterns (`FlyToward`, jetpack on, damage-immunity already global for mission bodies if shared — wire ambient `onMission` in Step 3).

- [ ] **Step 3: Teleport home**

```csharp
private static void BeginReturn(MissionRuntime m, bool cargoFull)
{
    // Clear target; disable jetpack; TeleportHome next to seat (copy Construction TeleportHome math);
    // FinishMission → remove from ByCrew.
}
```

- [ ] **Step 4: Ambient integration**

In `CrewAmbientPresence`, wherever `CrewRepairMission.IsCrewOnMission(crew.CrewId)` gates wander / body-loss / pose:

```csharp
bool onMission = CrewRepairMission.IsCrewOnMission(crew.CrewId)
    || CrewSalvageMission.IsCrewOnMission(crew.CrewId);
```

If repair has `TryGetMissionPose`, add salvage equivalent and consult it when salvage is active.

- [ ] **Step 5: Manual smoke (in-game)**

1. Admin-hire Salvage Ops, seat on idle ship with empty cargo room.
2. From a temporary server call or next-task HUD: dispatch toward an unowned scrap grid in range → crew EVA, blocks shrink, comps appear in home cargo.
3. Fill cargo → crew returns; notify mentions cargo full.
4. Construction Send still welds as before.

- [ ] **Step 6: Commit (only if user asked)**

```bash
git add Data/Scripts/HireCrew/CrewSalvageMission.cs Data/Scripts/HireCrew/CrewAmbientPresence.cs
git commit -m "feat: add Salvage Ops EVA grind mission"
```

---

### Task 5: Session handlers, tick, mission sync

**Files:**
- Modify: `Data/Scripts/HireCrew/CrewSession.cs`
- Modify: `Source/HireCrew/CrewSession.cs` (mirror)
- Modify: `Data/Scripts/HireCrew/CrewHud.cs` / `CrewStatusSidebar.cs` as needed for sync consume
- Test: extend `CrewStatusHudModelTests` for merged salvage rows

**Interfaces:**
- Consumes: `CrewSalvageMission.DispatchCrew/RecallCrew/Tick/CollectActiveSnapshots`
- Produces:
  - Full `HandleSalvageDispatch` auth + notify strings
  - Server tick calls `CrewSalvageMission.Tick`
  - Periodic `SalvageMissionSync` to clients (same throttle pattern as repair mission sync)
  - Client stores last salvage snapshots for sidebar
  - `CrewStatusHudModel.BuildRows` (or new `BuildAllRows`) merges repair + salvage entries for the managed grid; salvage rows use `RoleLabel = "Salvage Ops"`

- [ ] **Step 1: Failing sidebar merge test**

```csharp
[Fact]
public void BuildRows_includes_salvage_ops()
{
    var repair = new List<RepairMissionSnapshotEntry>();
    var salvage = new List<SalvageMissionSnapshotEntry>
    {
        new SalvageMissionSnapshotEntry
        {
            CrewId = "s1",
            DisplayName = "Rook",
            GridEntityId = 42,
            State = (int)SalvageMissionState.Grinding,
            Hints = SalvageMissionHintFlags.CargoFull
        }
    };
    int overflow;
    var rows = CrewStatusHudModel.BuildRows(repair, salvage, 42, out overflow);
    Assert.Equal(1, rows.Count);
    Assert.Equal("Salvage Ops", rows[0].RoleLabel);
    Assert.Equal("Grinding", rows[0].StatusLabel);
    Assert.Equal("Cargo full", rows[0].HintLabel);
}
```

Update `BuildRows` signature to accept both lists (overload preferred so existing repair-only call sites keep compiling during migration):

```csharp
public static List<CrewStatusHudRow> BuildRows(
    IList<RepairMissionSnapshotEntry> repairEntries,
    IList<SalvageMissionSnapshotEntry> salvageEntries,
    long gridEntityId,
    out int overflowCount)
```

Keep a wrapper:

```csharp
public static List<CrewStatusHudRow> BuildRows(
    IList<RepairMissionSnapshotEntry> entries,
    long gridEntityId,
    out int overflowCount)
{
    return BuildRows(entries, null, gridEntityId, out overflowCount);
}
```

Salvage branch sets `RoleLabel = "Salvage Ops"`; repair keeps `"Construction"`.

- [ ] **Step 2: User runs test (expect FAIL then implement model → PASS)**

Run: `dotnet test tests/HireCrew.Logic.Tests/HireCrew.Logic.Tests.csproj --filter "FullyQualifiedName~BuildRows_includes_salvage"`

- [ ] **Step 3: Implement `HandleSalvageDispatch`**

```csharp
private void HandleSalvageDispatch(SalvageDispatchRequest req, long identityId, ulong steamId)
{
    if (req == null || string.IsNullOrEmpty(req.CrewId) || Store == null)
        return;
    var crew = Store.Get(req.CrewId);
    if (crew == null || crew.Role != CrewRole.SalvageOps || crew.GridEntityId == 0)
    {
        Notify(steamId, "Salvage: crew not ready");
        return;
    }
    IMyCubeGrid home;
    if (!TryGetGrid(crew.GridEntityId, out home) || home == null)
    {
        Notify(steamId, "Salvage: grid not found");
        return;
    }
    if (!HasManagePermission(identityId, home))
    {
        Notify(steamId, "No permission");
        return;
    }
    if (req.Recall)
    {
        bool ok = CrewSalvageMission.RecallCrew(crew.CrewId);
        Notify(steamId, ok
            ? "Salvage: recalling " + (crew.DisplayName ?? "salvager")
            : "Salvage: not out");
        return;
    }
    if (!CrewAmbientPresence.IsGridIdle(home))
    {
        Notify(steamId, "Salvage: grid moving — wait");
        return;
    }
    bool started = CrewSalvageMission.DispatchCrew(this, crew.CrewId, req.TargetGridEntityId);
    Notify(steamId, started
        ? "Salvage: sent " + (crew.DisplayName ?? "salvager")
        : "Salvage: invalid target / not ready");
}
```

- [ ] **Step 4: Tick + sync**

Where repair mission ticks/syncs, also:

```csharp
CrewSalvageMission.Tick(this, dt);
// collect salvage snapshots → SalvageMissionSync to relevant clients
```

Sidebar update path merges repair + salvage snapshot lists via new `BuildRows`.

- [ ] **Step 5: Commit (only if user asked)**

```bash
git add Data/Scripts/HireCrew/CrewSession.cs Data/Scripts/HireCrew/CrewStatusHudModel.cs Data/Scripts/HireCrew/CrewStatusSidebar.cs Data/Scripts/HireCrew/CrewHud.cs Source/HireCrew/CrewSession.cs tests/HireCrew.Logic.Tests/CrewStatusHudModelTests.cs
git commit -m "feat: wire Salvage Ops session dispatch and sidebar sync"
```

---

### Task 6: HUD Salvage/Recall + target grid picker

**Files:**
- Modify: `Data/Scripts/HireCrew/CrewHudWindow.cs`
- Modify: `Source/HireCrew/CrewHudWindow.cs` (mirror)
- Test: none (UI); manual in-game

**Interfaces:**
- Consumes: `CrewSession.ClientRequestSalvageDispatch`, `CrewSalvageMission.IsCrewOnMission`, `CrewConfig.SalvageScanRadiusMeters`, `CrewSalvageRules`
- Produces:
  - Per-row button for `CrewRole.SalvageOps`: label **Salvage** or **Recall**
  - Click Salvage (not on mission) → open target picker overlay listing legal grids within scan radius
  - Confirm picker → `ClientRequestSalvageDispatch(crewId, recall: false, targetGridEntityId)`
  - Click Recall → `ClientRequestSalvageDispatch(crewId, recall: true, targetGridEntityId: 0)`
  - Construction row buttons unchanged (`Send`/`Recall` → repair dispatch)

- [ ] **Step 1: Row button branching**

Where `SetRowRepairBtn` / `OnRowRepairClicked` currently assume Damage Control only, generalize:

- Damage Control → existing Send/Recall → repair dispatch
- Salvage Ops → Salvage/Recall → salvage path (picker or recall)
- Other roles → hide button

```csharp
private void OnRowSalvageClicked(string crewId)
{
    var session = CrewSession.Instance;
    var crew = session.Store.Get(crewId);
    if (crew == null || crew.Role != CrewRole.SalvageOps)
        return;
    if (CrewSalvageMission.IsCrewOnMission(crew.CrewId))
    {
        session.ClientRequestSalvageDispatch(crew.CrewId, true, 0);
        Refresh();
        return;
    }
    OpenSalvageTargetPicker(crew);
}
```

- [ ] **Step 2: Target picker UI**

Minimal overlay on the existing HUD window (reuse list/row budget patterns):

1. Enumerate `MyAPIGateway.Entities.GetEntities` cube grids (or nearby entities) within `SalvageScanRadiusMeters` of home grid center.
2. For each, compute relation via `CrewSalvageRules.ClassifyTarget` + faction lookups; skip illegal.
3. Sort: home grid first, then by distance ascending.
4. Show name + distance meters; tap row confirms and closes picker.
5. Cancel/close control dismisses without dispatch.

Empty list → notify/chat `"Salvage: no legal targets in range"` and do not dispatch.

- [ ] **Step 3: Manual in-game checklist**

1. Hire desk shows Allow Salvage Ops; hire one.
2. Seat on ship; roster shows Salvage Ops + **Salvage** button.
3. Press Salvage → picker lists home + nearby unowned/faction grids; enemy absent.
4. Pick wreck → EVA grind deposits to cargo; sidebar shows Salvage Ops / Grinding.
5. Recall → teleports home; button returns to Salvage.
6. Fill cargo mid-grind → returns with cargo-full hint.
7. Construction **Send** still works on Construction rows.

- [ ] **Step 4: Commit (only if user asked)**

```bash
git add Data/Scripts/HireCrew/CrewHudWindow.cs Source/HireCrew/CrewHudWindow.cs
git commit -m "feat: add Salvage Ops HUD dispatch and target picker"
```

---

## Spec coverage checklist

| Spec requirement | Task |
|---|---|
| `CrewRole.SalvageOps` + hire/admin/mask | Task 1 |
| Manual per-crew dispatch + target pick | Tasks 5–6 |
| No path / EVA → grind → teleport home | Task 4 |
| Components → home physical-group cargo | Task 4 |
| Legal targets own/faction/unowned; never enemy | Tasks 2, 4, 6 |
| 2000 m scan radius | Tasks 1, 4, 6 |
| Cargo full → return + notify | Tasks 4–5 |
| Parallel mission module; Construction unchanged | Tasks 4–6 |
| Status sidebar salvage rows | Tasks 2, 5 |
| Unit tests for rules/labels/clamp | Tasks 1–2, 5 |
| Non-goals (path, auto, filters, floating drops) | Not implemented |

## Plan self-review notes

- No TBD placeholders left; grind uses `DecreaseMountLevel` into home inventories.
- Message ids `41749`/`41750` follow `41748` repair sync.
- `BuildRows` overload preserves Construction callers during migration.
- `TryParseRole` lives in admin commands (SE file); covered by manual hire, not xunit.
