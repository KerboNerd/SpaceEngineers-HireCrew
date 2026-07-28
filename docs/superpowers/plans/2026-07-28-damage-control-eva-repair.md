# Damage Control EVA Repair Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship Phase 1 Damage Control: new role, per-grid painted waypoint path (click-to-append tool mode), and auto EVA repair missions that walk → exit → fly → weld from conveyor cargo → return.

**Architecture:** Path edits are client raycasts in an explicit paint mode, applied server-side into `RepairPathStore` (world storage, same pattern as hire pools). `CrewRepairMission` owns a per-grid state machine and drives the ambient character when present; weld uses `IMySlimBlock.IncreaseMountLevel` with grid inventories as the component source. Ambient wander yields while a mission is active.

**Tech Stack:** Space Engineers ModAPI (characters, slim blocks, inventories, multiplayer messages), existing HireCrew ambient stack, protobuf-net request DTOs, WorldStorage persistence.

**Spec:** `docs/superpowers/specs/2026-07-28-damage-control-eva-repair-design.md`

## Global Constraints

- Phase 1 only (no path ghost lines, no multi-EVA, no real welder entity, no interior-only missions).
- Engineer / Reactor Tech behavior must not change.
- One EVA welder per grid at a time.
- Server-authoritative path + mission mutations; clients only request.
- Path tool = **paint mode** with block raycast clicks (custom interaction). No new HandItem SBC in Phase 1.
- Reuse `CrewAmbientPresence.IsGridIdle` spirit for sortie start / abort (do not start EVA on a fast-moving grid).
- No automated tests; agent must not run `dotnet` / `dotnet build`. Manual in-game verify.
- Do not commit unless the user explicitly asks.
- Do not edit `c:\Users\user\.cursor\plans\ambient_walking_npcs_b3887701.plan.md`.

## File structure

| File | Role |
|------|------|
| `Data/Scripts/HireCrew/CrewModels.cs` | `CrewRole.DamageControl`; path/mission request DTOs |
| `Data/Scripts/HireCrew/CrewConfig.cs` | `MaxRole`, `RoleLabel`, weld rate helpers |
| `Data/Scripts/HireCrew/HireWorldConfig.cs` | No structural change (mask uses `MaxRole`) |
| `Data/Scripts/HireCrew/CrewHireBlockLogic.cs` | Allow Damage Control checkbox |
| `Data/Scripts/HireCrew/CrewAdminCommands.cs` | Role parse token for Damage Control |
| `Data/Scripts/HireCrew/CrewHudWindow.cs` | Role color / detail text for Damage Control |
| `Data/Scripts/HireCrew/CrewBlockInfo.cs` | Detail line for Damage Control if needed |
| `Data/Scripts/HireCrew/CrewNetworking.cs` | `PathEditMsg = 41746`, `PathSyncMsg = 41747` |
| `Data/Scripts/HireCrew/RepairPathStore.cs` | Per-grid ordered waypoints + Exit; serialize |
| `Data/Scripts/HireCrew/CrewPathPainter.cs` | Client paint mode + raycast clicks |
| `Data/Scripts/HireCrew/CrewRepairMission.cs` | Mission state machine, weld, cargo pull |
| `Data/Scripts/HireCrew/CrewAmbientPresence.cs` | Yield wander when mission active; expose idle helper if needed |
| `Data/Scripts/HireCrew/CrewSession.cs` | Save/load paths; tick mission; net handlers; path API |
| `Data/Scripts/HireCrew/CrewHud.cs` | `/hc path` (or admin path verbs) to toggle paint mode |

---

### Task 1: Damage Control role wiring

**Files:**
- Modify: `Data/Scripts/HireCrew/CrewModels.cs`
- Modify: `Data/Scripts/HireCrew/CrewConfig.cs`
- Modify: `Data/Scripts/HireCrew/CrewHireBlockLogic.cs`
- Modify: `Data/Scripts/HireCrew/CrewAdminCommands.cs`
- Modify: `Data/Scripts/HireCrew/CrewHudWindow.cs`
- Modify: `Data/Scripts/HireCrew/CrewBlockInfo.cs` (only if it switches on role)

**Interfaces:**
- Produces:
  - `CrewRole.DamageControl = 5`
  - `CrewConfig.MaxRole == 5`
  - `CrewConfig.RoleLabel(CrewRole.DamageControl) == "Damage Control"`
  - Hire desk checkbox `HireCrew_Role_DamageControl` / "Allow Damage Control"
  - Admin role tokens: `damage`, `dc`, `welder` → `CrewRole.DamageControl`
  - HUD role color: `new Color(255, 220, 120)` (distinct from Propulsion)

- [ ] **Step 1: Extend enum**

In `CrewRole`:

```csharp
public enum CrewRole
{
    Gunner = 0,
    Engineer = 1,
    Helmsman = 2,
    Propulsion = 3,
    Quartermaster = 4,
    DamageControl = 5
}
```

- [ ] **Step 2: Config**

```csharp
public const int MaxRole = (int)CrewRole.DamageControl;

public static string RoleLabel(CrewRole role)
{
    switch (role)
    {
        case CrewRole.Engineer: return "Reactor Tech";
        case CrewRole.Helmsman: return "Helmsman";
        case CrewRole.Propulsion: return "Propulsion Tech";
        case CrewRole.Quartermaster: return "Quartermaster";
        case CrewRole.DamageControl: return "Damage Control";
        default: return "Gunner";
    }
}
```

Do **not** add Damage Control to `TryGetRoleBonusTable` / power / helm / propulsion tables.

- [ ] **Step 3: Hire desk + admin + HUD color**

- `CrewHireBlockLogic`: `AddRoleCheckbox(CrewRole.DamageControl, "HireCrew_Role_DamageControl", "Allow Damage Control");` and include `D` in `FormatRoles`.
- `CrewAdminCommands` role parser: accept `damage` / `dc` / `welder`.
- `CrewHudWindow` role color switch: add `DamageControl` case.
- Any role-detail switch that lists jobs: Damage Control → `"Hull EVA repair when path + damage exist"`.

- [ ] **Step 4: Manual check**

Reload world. Hire desk shows Allow Damage Control. `/hc hire ... damage` (or existing admin hire verb) can create a Damage Control crew. Roster shows label **Damage Control**. Reactors still only buff from Engineer.

---

### Task 2: Repair path models + store + session save/load

**Files:**
- Create: `Data/Scripts/HireCrew/RepairPathStore.cs`
- Modify: `Data/Scripts/HireCrew/CrewModels.cs`
- Modify: `Data/Scripts/HireCrew/CrewSession.cs`

**Interfaces:**
- Produces:
  - `RepairWaypoint { long BlockEntityId; Vector3D LocalPos; }`
  - `RepairGridPath { long GridEntityId; List<RepairWaypoint> Waypoints; bool HasExit; }` (`HasExit` true when path is finished; last waypoint is Exit)
  - `RepairPathStore` with `Get/Set/Clear/ToBytes/FromBytes`
  - `CrewSession.RepairPaths` property; save `HireCrew_RepairPaths` + `HireCrewRepairPaths.dat`

- [ ] **Step 1: Models**

```csharp
[ProtoContract]
public sealed class RepairWaypoint
{
    [ProtoMember(1)] public long BlockEntityId;
    /// <summary>Position in grid local space (fallback if block id is gone).</summary>
    [ProtoMember(2)] public double LocalX;
    [ProtoMember(3)] public double LocalY;
    [ProtoMember(4)] public double LocalZ;
}

[ProtoContract]
public sealed class RepairGridPath
{
    [ProtoMember(1)] public long GridEntityId;
    [ProtoMember(2)] public List<RepairWaypoint> Waypoints = new List<RepairWaypoint>();
    /// <summary>True when player finished path; last waypoint is the Exit.</summary>
    [ProtoMember(3)] public bool HasExit;
}

[ProtoContract]
public sealed class PathEditRequest
{
    [ProtoMember(1)] public long GridEntityId;
    /// <summary>0=Append, 1=Undo, 2=FinishExit, 3=Clear</summary>
    [ProtoMember(2)] public int Op;
    [ProtoMember(3)] public long BlockEntityId;
    [ProtoMember(4)] public double LocalX;
    [ProtoMember(5)] public double LocalY;
    [ProtoMember(6)] public double LocalZ;
}
```

- [ ] **Step 2: `RepairPathStore`**

```csharp
public sealed class RepairPathStore
{
    private const int FormatVersion = 1;
    private readonly Dictionary<long, RepairGridPath> _byGrid = new Dictionary<long, RepairGridPath>();

    public RepairGridPath Get(long gridEntityId) { /* TryGet; null if missing */ }
    public void Upsert(RepairGridPath path) { /* replace by GridEntityId */ }
    public bool Clear(long gridEntityId) { return _byGrid.Remove(gridEntityId); }
    public bool IsReady(long gridEntityId)
    {
        var p = Get(gridEntityId);
        return p != null && p.HasExit && p.Waypoints != null && p.Waypoints.Count >= 2;
    }
    public byte[] ToBytes() { /* version + count + each path */ }
    public static RepairPathStore FromBytes(byte[] bytes) { /* … */ }
}
```

Serialize with the same primitive writers style as `CrewStore` (int/long/double/bool/count) — keep it dependency-light. Minimum ready path: ≥2 waypoints and `HasExit`.

Resolve world position helper (static on store or mission):

```csharp
public static bool TryResolveWorldPos(IMyCubeGrid grid, RepairWaypoint wp, out Vector3D world)
{
    world = Vector3D.Zero;
    if (grid == null || wp == null) return false;
    IMyEntity ent;
    if (wp.BlockEntityId != 0
        && MyAPIGateway.Entities.TryGetEntityById(wp.BlockEntityId, out ent)
        && ent != null && !ent.Closed)
    {
        var block = ent as IMyCubeBlock;
        if (block != null && block.CubeGrid != null && block.CubeGrid.EntityId == grid.EntityId)
        {
            world = block.GetPosition();
            return true;
        }
    }
    var local = new Vector3D(wp.LocalX, wp.LocalY, wp.LocalZ);
    world = Vector3D.Transform(local, grid.WorldMatrix);
    return true;
}
```

- [ ] **Step 3: Session save/load**

Mirror hire pools:

- Field: `public RepairPathStore RepairPaths = new RepairPathStore();`
- `SaveData`: write base64 to `HireCrew_RepairPaths` + `HireCrewRepairPaths.dat`
- `BeforeStart` server load: `TryLoadRepairPathBytes()` → `RepairPathStore.FromBytes`

- [ ] **Step 4: Manual check**

Mod compiles. Save/reload world with a temporary test upsert in a debug path (or Task 3) and confirm file appears under world storage after save. If no debug upsert yet, compile-only is enough for this task.

---

### Task 3: Path edit networking + server apply

**Files:**
- Modify: `Data/Scripts/HireCrew/CrewNetworking.cs`
- Modify: `Data/Scripts/HireCrew/CrewSession.cs`

**Interfaces:**
- Consumes: `PathEditRequest`, `RepairPathStore`
- Produces:
  - `CrewNetworking.PathEditMsg = 41746`
  - `CrewSession.ClientRequestPathEdit(PathEditRequest req)`
  - Server `HandlePathEdit(req, identityId, steamId)` ownership-gated
  - Ops: Append / Undo / FinishExit / Clear
  - `Notify` feedback: `Path 3 wp`, `Path saved (Exit)`, `Path cleared`, errors

- [ ] **Step 1: Register message** `41746` in Register/Unregister.

- [ ] **Step 2: Client request + server handler**

```csharp
public void ClientRequestPathEdit(PathEditRequest req)
{
    if (req == null) return;
    var data = CrewNetworking.Serialize(req);
    if (MyAPIGateway.Multiplayer.IsServer)
        HandlePathEdit(req, MyAPIGateway.Session.Player.IdentityId, MyAPIGateway.Multiplayer.MyId);
    else
        CrewNetworking.SendToServer(CrewNetworking.PathEditMsg, data);
}

private void HandlePathEdit(PathEditRequest req, long identityId, ulong steamId)
{
    if (req == null || RepairPaths == null) return;
    IMyEntity gridEnt;
    if (!MyAPIGateway.Entities.TryGetEntityById(req.GridEntityId, out gridEnt))
    {
        Notify(steamId, "Path: grid missing");
        return;
    }
    var grid = gridEnt as IMyCubeGrid;
    if (grid == null)
    {
        Notify(steamId, "Path: not a grid");
        return;
    }
    if (!CrewOwnership.CanManageGrid(identityId, grid)) // use existing ownership helper name in repo
    {
        Notify(steamId, "Path: no access");
        return;
    }

    var path = RepairPaths.Get(req.GridEntityId) ?? new RepairGridPath { GridEntityId = req.GridEntityId };
    if (path.Waypoints == null) path.Waypoints = new List<RepairWaypoint>();

    switch (req.Op)
    {
        case 0: // Append — rejects if already HasExit (must Clear first)
            if (path.HasExit) { Notify(steamId, "Path: already finished (Clear first)"); return; }
            path.Waypoints.Add(new RepairWaypoint
            {
                BlockEntityId = req.BlockEntityId,
                LocalX = req.LocalX, LocalY = req.LocalY, LocalZ = req.LocalZ
            });
            RepairPaths.Upsert(path);
            Notify(steamId, "Path " + path.Waypoints.Count + " wp");
            break;
        case 1: // Undo
            if (path.Waypoints.Count == 0) { Notify(steamId, "Path: empty"); return; }
            path.Waypoints.RemoveAt(path.Waypoints.Count - 1);
            path.HasExit = false;
            RepairPaths.Upsert(path);
            Notify(steamId, "Path undo → " + path.Waypoints.Count + " wp");
            break;
        case 2: // FinishExit
            if (path.Waypoints.Count < 2) { Notify(steamId, "Path: need ≥2 waypoints"); return; }
            path.HasExit = true;
            RepairPaths.Upsert(path);
            Notify(steamId, "Path saved (Exit)");
            break;
        case 3: // Clear
            RepairPaths.Clear(req.GridEntityId);
            Notify(steamId, "Path cleared");
            break;
        default:
            Notify(steamId, "Path: bad op");
            break;
    }
}
```

Use the real ownership helper already used for assign/dismiss (search `CrewOwnership` / `CanControl` in `CrewSession`). Do not invent a new ownership system.

- [ ] **Step 3: Manual check**

From a temporary chat stub or Task 4 painter: Append two blocks, FinishExit, save/reload, `RepairPaths.IsReady(gridId)` true.

---

### Task 4: Path painter (custom click interaction)

**Files:**
- Create: `Data/Scripts/HireCrew/CrewPathPainter.cs`
- Modify: `Data/Scripts/HireCrew/CrewSession.cs` (call `Update` from client `UpdateAfterSimulation`)
- Modify: `Data/Scripts/HireCrew/CrewHud.cs` or `CrewAdminCommands.cs` for `/hc path` verbs

**Interfaces:**
- Consumes: `CrewSession.ClientRequestPathEdit`
- Produces:
  - `CrewPathPainter.SetActive(bool, long gridEntityId)`
  - `CrewPathPainter.Update(CrewSession session)` — client only
  - Chat: `/hc path` start on looked-at grid; `/hc path undo|done|clear|stop`

**Interaction rules (locked):**
- While active: **left click** = Append (raycast to block on the paint grid)
- **Finish**: `/hc path done` (marks Exit on current path) — also allow **right click** = FinishExit if path has ≥2 wp
- `/hc path undo`, `/hc path clear`, `/hc path stop`

- [ ] **Step 1: Painter**

```csharp
public static class CrewPathPainter
{
    private static bool _active;
    private static long _gridId;
    private static bool _wasLeft;
    private static bool _wasRight;

    public static bool IsActive { get { return _active; } }

    public static void SetActive(bool active, long gridEntityId)
    {
        _active = active;
        _gridId = active ? gridEntityId : 0;
        _wasLeft = _wasRight = false;
    }

    public static void Update(CrewSession session)
    {
        if (!_active || session == null) return;
        if (MyAPIGateway.Session == null || MyAPIGateway.Session.Player == null) return;
        // Do not steal clicks while chat/terminal open
        if (MyAPIGateway.Gui.ChatEntryVisible || MyAPIGateway.Gui.IsCursorVisible) return;

        bool left = MyAPIGateway.Input.IsLeftMousePressed();
        bool right = MyAPIGateway.Input.IsRightMousePressed();
        bool leftNew = left && !_wasLeft;
        bool rightNew = right && !_wasRight;
        _wasLeft = left;
        _wasRight = right;

        if (leftNew)
            TryClick(session, finish: false);
        else if (rightNew)
            TryClick(session, finish: true);
    }

    private static void TryClick(CrewSession session, bool finish)
    {
        if (finish)
        {
            session.ClientRequestPathEdit(new PathEditRequest { GridEntityId = _gridId, Op = 2 });
            return;
        }

        IMyCubeBlock block;
        Vector3D local;
        if (!TryRayBlock(_gridId, out block, out local))
            return;

        session.ClientRequestPathEdit(new PathEditRequest
        {
            GridEntityId = _gridId,
            Op = 0,
            BlockEntityId = block.EntityId,
            LocalX = local.X,
            LocalY = local.Y,
            LocalZ = local.Z
        });
    }

    private static bool TryRayBlock(long gridId, out IMyCubeBlock block, out Vector3D local)
    {
        block = null;
        local = Vector3D.Zero;
        var cam = MyAPIGateway.Session.Camera;
        if (cam == null) return false;
        var from = cam.WorldMatrix.Translation;
        var to = from + cam.WorldMatrix.Forward * 40.0;
        IHitInfo hit;
        if (!MyAPIGateway.Physics.CastRay(from, to, out hit) || hit == null || hit.HitEntity == null)
            return false;

        var grid = hit.HitEntity as IMyCubeGrid;
        IMyCubeBlock cube = hit.HitEntity as IMyCubeBlock;
        if (grid == null && cube != null) grid = cube.CubeGrid;
        if (grid == null || grid.EntityId != gridId) return false;

        Vector3I cell;
        double dist;
        var line = new LineD(from, to);
        if (!grid.GetLineIntersectionExactGrid(ref line, ref cell, ref dist))
            return false;
        var slim = grid.GetCubeBlock(cell);
        if (slim == null || slim.FatBlock == null) return false;
        block = slim.FatBlock;
        local = Vector3D.Transform(block.GetPosition(), grid.WorldMatrixNormalizedInv);
        return true;
    }
}
```

Fix `GetLineIntersectionExactGrid` / `LineD` usage to match the ModAPI signatures already used elsewhere in the project (adjust ref/out as needed).

- [ ] **Step 2: Chat verbs**

Under `/hc` (admin commands) **or** owner-friendly `/crew path` if simpler — prefer extending admin/path so dedicated clients work:

- `path` / `path start` — raycast grid under crosshair, `CrewPathPainter.SetActive(true, gridId)`, notify `Path tool ON — LMB append, RMB done`
- `path stop` — deactivate
- `path undo` / `path clear` / `path done` — send ops for active `_gridId`

Non-admins may use path tool on grids they can manage: if current `/hc` is admin-only, add a parallel **owner** path entry on `/crew path` in `CrewHud.cs` that only toggles painter + `ClientRequestPathEdit` (no admin check on client; server still ownership-gates).

- [ ] **Step 3: Wire Update**

In `CrewSession.UpdateAfterSimulation` (client path): `CrewPathPainter.Update(this);`

- [ ] **Step 4: Manual check**

Look at ship, `/crew path` (or `/hc path`), LMB several interior blocks toward airlock, RMB to finish, get `Path saved (Exit)`. Undo/clear work. Second player without access gets `Path: no access`.

---

### Task 5: Repair mission skeleton + tick

**Files:**
- Create: `Data/Scripts/HireCrew/CrewRepairMission.cs`
- Modify: `Data/Scripts/HireCrew/CrewSession.cs`
- Modify: `Data/Scripts/HireCrew/CrewAmbientPresence.cs`
- Modify: `Data/Scripts/HireCrew/CrewConfig.cs`

**Interfaces:**
- Produces:
  - `enum RepairMissionState { Idle=0, WalkOut=1, AtExit=2, EvaTransit=3, Welding=4, ReturnExit=5, WalkHome=6 }`
  - `CrewRepairMission.Tick(CrewSession session)`
  - `CrewRepairMission.IsCrewOnMission(string crewId)`
  - `CrewRepairMission.TryGetMissionPose(string crewId, out Vector3D pos, out Vector3D forward, out Vector3D up)` for ambient respawn snap
  - Config: `RepairWeldIntegrityPerSecond = 40f`, `RepairMissionScanSeconds = 2f`, `RepairEvaStandOffMeters = 3.5f`

- [ ] **Step 1: Runtime mission record (not necessarily proto-persisted in v1)**

```csharp
private sealed class MissionRuntime
{
    public string CrewId;
    public long GridEntityId;
    public RepairMissionState State;
    public int WaypointIndex;
    public long TargetBlockEntityId; // fat block id; 0 if none
    public Vector3I TargetCell;
    public double StateSeconds;
    public double WeldCooldown;
}
```

Keep a `Dictionary<long /*grid*/, MissionRuntime>` — one mission per grid.

- [ ] **Step 2: Tick entry**

Every ~0.5–1s (use existing session tick cadence or internal accumulator):

1. For each grid with `RepairPaths.IsReady` and no active mission: find seated `CrewRole.DamageControl` with ambient-capable seat; if `HasDamagedBlocks(grid)` and `IsGridIdle(grid)` → start `WalkOut` for that crew.
2. Advance active missions (Task 6–7 fill movement/weld).
3. If grid not idle during EVA states → force `ReturnExit`.

- [ ] **Step 3: Ambient yield**

In `CrewAmbientPresence.UpdateMovement`, skip wander steering when `CrewRepairMission.IsCrewOnMission(crew.CrewId)`.

When ambient spawns a character for a crew on mission, if `TryGetMissionPose` succeeds, spawn/snap there instead of station neighborhood (call site in `TrySpawnAndSeat` / post-spawn).

- [ ] **Step 4: Manual check**

With a finished path + Damage Control stationed + no damage: no mission starts. With damage + idle grid: mission enters `WalkOut` (log line `repair mission start crew=… grid=…`). Ambient wander stops for that crew.

---

### Task 6: Walk path + Exit transition

**Files:**
- Modify: `Data/Scripts/HireCrew/CrewRepairMission.cs`
- Modify: `Data/Scripts/HireCrew/CrewAmbientPresence.cs` (reuse steer helpers if accessible; otherwise duplicate a thin `SteerToWorld` wrapper)

**Interfaces:**
- Consumes: `RepairPathStore.TryResolveWorldPos`, ambient character entity id on `CrewRecord`
- Produces: `WalkOut` / `WalkHome` / `AtExit` / `ReturnExit` behavior

- [ ] **Step 1: Interior follow**

While `WalkOut`:
- Target waypoint `WaypointIndex` world pos (stand slightly above deck: `+ up * 0.1`).
- Steer character toward it (reuse ambient walk speeds; jetpack off).
- Arrival radius `1.25m` → increment index.
- If index reaches last waypoint (`HasExit`) → `AtExit`.

`WalkHome`: same with decreasing index toward 0; at 0 → `Idle` and clear mission.

- [ ] **Step 2: Exit transition**

`AtExit` (0.5–1.0s):
- Try open nearby door: if exit block or neighbors implement `IMyDoor`, call `OpenDoor()` once.
- Enable jetpack on character if API available (`character.EnabledJetpacks` / `SwitchJetpack` equivalent used by SE mods — use the property/method that compiles against this game version).
- Move to a point `ExitPos + exteriorNormal * 2.5` where `exteriorNormal` is approximate outward from grid center (`exitPos - grid.WorldAABB.Center` flattened).
- Then → `EvaTransit`.

`ReturnExit`: fly to exit exterior point, disable jetpack, → `WalkHome` with index at last-1.

- [ ] **Step 3: No character**

If mission active but no live `CharacterEntityId` (player far): still advance waypoint index on a timer (logical progress ~ walk speed), so repair continues; on respawn, snap to current pose.

- [ ] **Step 4: Manual check**

Paint path to airlock. Damage a block. Watch Damage Control walk waypoints to Exit, door opens if present, jetpack engages, leaves interior. On abort/moving grid, returns.

---

### Task 7: EVA target pick + weld + conveyor cargo

**Files:**
- Modify: `Data/Scripts/HireCrew/CrewRepairMission.cs`
- Modify: `Data/Scripts/HireCrew/CrewConfig.cs`

**Interfaces:**
- Produces:
  - `TryPickDamageTarget(IMyCubeGrid grid, out IMySlimBlock slim)`
  - `TryWeldTick(IMySlimBlock slim, IMyCubeGrid grid, long welderIdentityId, float amount)`
  - Star scale: `amount *= (0.75f + 0.1f * stars)` clamped

- [ ] **Step 1: Find damage**

```csharp
private static bool TryPickDamageTarget(IMyCubeGrid grid, out IMySlimBlock best)
{
    best = null;
    if (grid == null) return false;
    double bestDist = double.MaxValue;
    Vector3D center = grid.WorldAABB.Center;
    var blocks = new List<IMySlimBlock>();
    grid.GetBlocks(blocks);
    for (int i = 0; i < blocks.Count; i++)
    {
        var s = blocks[i];
        if (s == null || s.IsDestroyed) continue;
        // Damaged or incomplete
        if (s.Integrity >= s.MaxIntegrity - 0.1f && s.BuildLevelRatio >= 0.999f)
            continue;
        double d = Vector3D.DistanceSquared(s.WorldPosition, center);
        // Prefer nearer to hull exterior: larger distance from center
        // Use farthest-from-center among damaged as simple hull bias, or nearest to exit — Phase1: nearest to current EVA pos if available, else first damaged.
        if (best == null || d < bestDist) // nearest-to-center first is fine for v1; swap to DistanceSquared(evaPos) when evaPos known
        {
            best = s;
            bestDist = d;
        }
    }
    return best != null;
}
```

Prefer **nearest to current character/EVA position** when available (pass `Vector3D from`).

- [ ] **Step 2: EVA transit + weld**

`EvaTransit`: fly toward `slim.WorldPosition + outward * RepairEvaStandOffMeters`. Arrival → `Welding`.

`Welding` each tick:
1. Collect inventories from conveyor-capable blocks on grid (v1: all `IMyCargoContainer` + blocks with inventory on same grid; try each until weld consumes).
2. For a chosen inventory `inv`:

```csharp
slim.MoveItemsToConstructionStockpile(inv);
slim.IncreaseMountLevel(
    weldAmount,
    welderOwnerIdentityId,
    inv,
    0f,
    false,
    MyOwnershipShareModeEnum.Faction);
```

3. If integrity full / build complete → clear target, `TryPickDamageTarget` again; if none → `ReturnExit`.
4. If no comps progress for ~3s → try another inventory; if all fail → `ReturnExit` and notify owner once `Damage Control: out of components`.

- [ ] **Step 3: Manual check**

Stock cargo with required comps. Damage armor. Welder EVAs, hovers, integrity rises, comps decrease. Empty cargo → returns with notify. Player-repaired target → picks next or returns.

---

### Task 8: End-to-end polish + success criteria

**Files:**
- Modify: `Data/Scripts/HireCrew/CrewRepairMission.cs` (logging, cooldowns)
- Modify: any small gaps from Tasks 5–7

**Interfaces:**
- Produces: cooldown after failed sortie (`RepairRescanSeconds = 15`); clean Idle restore; no mission leak on dismiss

- [ ] **Step 1: Lifecycle hooks**

- On dismiss / unassign of mission crew: `CrewRepairMission.CancelForCrew(crewId)` → Idle, clear grid mission.
- On grid split/close: cancel missions for missing grids.
- After successful return to Idle: `rescanAtUtc` = now + 15s before auto re-sortie.

- [ ] **Step 2: Logging**

Use the same log helper style as ambient (`CrewAmbientPresence` / session log):  
`repair start`, `repair weld`, `repair return`, `repair abort moving`, `repair out of comps`.

- [ ] **Step 3: Full manual checklist (success criteria)**

1. Paint path (≥2 wp) to airlock; RMB/`done` → `Path saved (Exit)`.
2. Hire/assign Damage Control to a station on that grid.
3. Damage hull; stock conveyor cargo.
4. Grid idle: crew walks path, exits, flies, welds, comps drop, returns to station ambient.
5. No path / unfinished path → no sortie.
6. Grid thrusting hard → no new sortie / active EVA aborts home.
7. Engineer still only does reactor buff.
8. Save/reload: path still ready; no duplicate ambient bodies; mission can start again after cooldown.

---

## Spec coverage (self-review)

| Spec requirement | Task |
|---|---|
| New Damage Control role | 1 |
| Engineer unchanged | 1, 8 |
| Ordered click waypoints + Exit | 3, 4 |
| Per-grid shared path | 2, 3 |
| Persist path | 2 |
| Auto on damage | 5 |
| Walk → Exit → EVA → weld → return | 6, 7 |
| Conveyor/cargo comps | 7 |
| Ambient integration / far logical progress | 5, 6 |
| Idle-grid guards | 5, 6, 8 |
| One EVA per grid | 5 |
| Stars affect weld speed | 7 |
| Phase 2 polish out of scope | Global Constraints |

## Placeholder / consistency notes

- Ownership helper name must match existing `CrewOwnership` / session checks (implementer greps; do not invent parallel rules).
- Jetpack enable API may be `character.SwitchThrusts()` / `EnabledThrusts` depending on SE version — use whatever already compiles in ambient code if present; else the property that the game exposes on `IMyCharacter`.
- Path sync to other clients is optional in Phase 1 (server store is enough); painter feedback via `Notify` only.
