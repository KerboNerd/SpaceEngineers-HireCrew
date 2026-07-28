# Damage Control Manual Dispatch Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Stop auto Damage Control sorties; players Send / Recall a grid batch from the Crew HUD.

**Architecture:** Client HUD fires `RepairDispatchRequest` to the server. Server calls `CrewRepairMission.DispatchGrid` or `RecallGrid`. The mission scan loop no longer calls `TryStartMissions`. Mid-sortie target chaining is unchanged.

**Tech Stack:** Space Engineers ModAPI, existing HireCrew HUD / networking / `CrewRepairMission`, protobuf-net DTOs.

**Spec:** `docs/superpowers/specs/2026-07-28-damage-control-manual-dispatch-design.md`

## Global Constraints

- Manual-only dispatch in v1 (no auto-mode toggle).
- Send launches **all** eligible Damage Control on the selected crew’s `GridEntityId`.
- Recall applies to **all** active repair missions on that grid.
- After natural return, idle until next Send (no auto re-dispatch).
- Reuse `HasManagePermission` + idle-grid guard; keep parallel cap / path vs local start rules.
- No automated tests; do **not** run `dotnet` / `dotnet build`. Manual in-game verify.
- Do not commit unless the user explicitly asks.
- Do not edit unrelated docs or RichHudFramework.

## File structure

| File | Role |
|------|------|
| `Data/Scripts/HireCrew/CrewModels.cs` | `RepairDispatchRequest` DTO |
| `Data/Scripts/HireCrew/CrewNetworking.cs` | `RepairDispatchMsg = 41747` register/unregister |
| `Data/Scripts/HireCrew/CrewRepairMission.cs` | `DispatchGrid` / `RecallGrid` / `IsAnyMissionOnGrid`; remove auto-start call |
| `Data/Scripts/HireCrew/CrewSession.cs` | Client request + server handler + notifies |
| `Data/Scripts/HireCrew/CrewHudWindow.cs` | Send / Recall button on Home |
| `Data/Scripts/HireCrew/CrewHudWindow.cs` | Amenity hint string for Damage Control |

---

### Task 1: Request DTO + network message

**Files:**
- Modify: `Data/Scripts/HireCrew/CrewModels.cs`
- Modify: `Data/Scripts/HireCrew/CrewNetworking.cs`

**Interfaces:**
- Produces:
  - `RepairDispatchRequest { long GridEntityId; bool Recall; }`
  - `CrewNetworking.RepairDispatchMsg = 41747`
  - Registered/unregistered in `Register` / `Unregister`

- [ ] **Step 1: Add DTO** at end of `CrewModels.cs` (after `PathEditRequest`):

```csharp
    [ProtoContract]
    public sealed class RepairDispatchRequest
    {
        [ProtoMember(1)] public long GridEntityId;
        /// <summary>false = Send batch, true = Recall all on grid.</summary>
        [ProtoMember(2)] public bool Recall;
    }
```

- [ ] **Step 2: Wire message id** in `CrewNetworking.cs`:

```csharp
public const ushort PathEditMsg = 41746;
public const ushort RepairDispatchMsg = 41747;
```

Add `RegisterSecureMessageHandler(RepairDispatchMsg, handler)` and matching `Unregister` next to `PathEditMsg`.

- [ ] **Step 3: Manual check**

Confirm `41747` is unused elsewhere (`rg 41747`). Expected: only `RepairDispatchMsg`.

---

### Task 2: Mission Dispatch / Recall API + stop auto-start

**Files:**
- Modify: `Data/Scripts/HireCrew/CrewRepairMission.cs`

**Interfaces:**
- Consumes: existing start preconditions inside current `TryStartMissions` body
- Produces:
  - `public static bool IsAnyMissionOnGrid(long gridEntityId)`
  - `public static int DispatchGrid(CrewSession session, long gridEntityId)` — returns launched count
  - `public static int RecallGrid(long gridEntityId)` — returns recalled count
  - `UpdateMovement` no longer calls `TryStartMissions`

- [ ] **Step 1: Query helper**

```csharp
public static bool IsAnyMissionOnGrid(long gridEntityId)
{
    if (gridEntityId == 0) return false;
    foreach (var kv in ByCrew)
    {
        if (kv.Value != null && kv.Value.GridEntityId == gridEntityId)
            return true;
    }
    return false;
}
```

- [ ] **Step 2: Refactor start into `DispatchGrid`**

Replace auto-scan entry with a public method that loops seated Damage Control on **one** grid (not all crews). Move the body of `TryStartMissions` into:

```csharp
/// <summary>
/// Manual Send: start all eligible Damage Control on the grid. Returns how many launched.
/// </summary>
public static int DispatchGrid(CrewSession session, long gridEntityId)
{
    if (session == null || session.Store == null || gridEntityId == 0)
        return 0;

    IMyEntity gridEnt;
    if (!MyAPIGateway.Entities.TryGetEntityById(gridEntityId, out gridEnt))
        return 0;
    var grid = gridEnt as IMyCubeGrid;
    if (grid == null || !CrewAmbientPresence.IsGridIdle(grid))
        return 0;

    int started = 0;
    foreach (var crew in session.Store.All)
    {
        if (crew == null || crew.Status != CrewStatus.Seated)
            continue;
        if (crew.Role != CrewRole.DamageControl)
            continue;
        if (crew.GridEntityId != gridEntityId || IsCrewOnMission(crew.CrewId))
            continue;

        DateTime coolUntil;
        if (CrewCooldownUntil.TryGetValue(crew.CrewId, out coolUntil)
            && coolUntil > DateTime.UtcNow)
            continue;

        if (!CanStartAnotherOnGrid(crew.GridEntityId))
            continue;

        Vector3D from = grid.WorldAABB.Center;
        if (crew.SeatEntityId.HasValue)
        {
            IMyEntity seatEnt;
            if (MyAPIGateway.Entities.TryGetEntityById(crew.SeatEntityId.Value, out seatEnt)
                && seatEnt != null)
                from = seatEnt.GetPosition();
        }

        IMyProjector projector;
        bool isProjected;
        IMySlimBlock target;
        if (!TryPickWorkTarget(grid, from, crew.CrewId, out target, out projector, out isProjected)
            || target == null)
            continue;

        bool usesPath = session.RepairPaths != null && session.RepairPaths.IsReady(crew.GridEntityId);
        var m = new MissionRuntime
        {
            CrewId = crew.CrewId,
            GridEntityId = crew.GridEntityId,
            State = usesPath ? RepairMissionState.WalkOut : RepairMissionState.EvaTransit,
            WaypointIndex = 0,
            StateSeconds = 0,
            UsesPath = usesPath
        };
        SetMissionTarget(m, target, projector, isProjected);
        ByCrew[crew.CrewId] = m;
        CrewCooldownUntil.Remove(crew.CrewId);
        started++;
        Log("repair dispatch crew=" + crew.CrewId + " grid=" + crew.GridEntityId
            + (usesPath ? " via=path" : " via=local")
            + (isProjected ? " kind=project" : " kind=repair"));
    }
    return started;
}
```

Delete `TryStartMissions` (or leave as private one-liner that calls `DispatchGrid` for every grid — **prefer delete** to avoid accidental auto use).

- [ ] **Step 3: Recall**

```csharp
public static int RecallGrid(long gridEntityId)
{
    if (gridEntityId == 0) return 0;
    int n = 0;
    CopyCrewKeys(KeyScratch);
    for (int i = 0; i < KeyScratch.Count; i++)
    {
        MissionRuntime m;
        if (!ByCrew.TryGetValue(KeyScratch[i], out m) || m == null)
            continue;
        if (m.GridEntityId != gridEntityId)
            continue;
        BeginReturn(m);
        n++;
    }
    if (n > 0)
        Log("repair recall grid=" + gridEntityId + " n=" + n);
    return n;
}
```

- [ ] **Step 4: Remove auto-start from `UpdateMovement`**

Delete the `_missionScanAccum` / `TryStartMissions` block (and the `_missionScanAccum` field + `RepairMissionScanSeconds` usage here). Keep ticking active missions only.

Do **not** remove weld/mission movement logic.

- [ ] **Step 5: Manual check (code)**

`rg TryStartMissions` → no callers. `rg DispatchGrid|RecallGrid|IsAnyMissionOnGrid` → definitions present.

---

### Task 3: Session client/server handler

**Files:**
- Modify: `Data/Scripts/HireCrew/CrewSession.cs`

**Interfaces:**
- Consumes: `RepairDispatchRequest`, `CrewNetworking.RepairDispatchMsg`, `CrewRepairMission.DispatchGrid` / `RecallGrid`
- Produces:
  - `public void ClientRequestRepairDispatch(long gridEntityId, bool recall)`
  - `HandleRepairDispatch(...)` server path with permission + notify

- [ ] **Step 1: Message switch** in `HandleMessage` (same area as `PathEditMsg`):

```csharp
else if (id == CrewNetworking.RepairDispatchMsg)
    HandleRepairDispatch(CrewNetworking.Deserialize<RepairDispatchRequest>(data), identityId, sender);
```

- [ ] **Step 2: Client API** (near `ClientRequestPathEdit` / dismiss):

```csharp
public void ClientRequestRepairDispatch(long gridEntityId, bool recall)
{
    var req = new RepairDispatchRequest { GridEntityId = gridEntityId, Recall = recall };
    var data = CrewNetworking.Serialize(req);
    if (MyAPIGateway.Multiplayer.IsServer)
        HandleRepairDispatch(req, MyAPIGateway.Session.Player.IdentityId, MyAPIGateway.Multiplayer.MyId);
    else
        CrewNetworking.SendToServer(CrewNetworking.RepairDispatchMsg, data);
}
```

- [ ] **Step 3: Server handler**

```csharp
private void HandleRepairDispatch(RepairDispatchRequest req, long identityId, ulong steamId)
{
    if (req == null || req.GridEntityId == 0)
        return;

    IMyCubeGrid grid;
    if (!TryGetGrid(req.GridEntityId, out grid) || grid == null)
    {
        Notify(steamId, "Damage Control: grid not found");
        return;
    }
    if (!HasManagePermission(identityId, grid))
    {
        Notify(steamId, "No permission");
        return;
    }

    if (req.Recall)
    {
        int n = CrewRepairMission.RecallGrid(req.GridEntityId);
        Notify(steamId, n > 0
            ? "Damage Control: recalling (" + n + ")"
            : "Damage Control: none out");
        return;
    }

    if (!CrewAmbientPresence.IsGridIdle(grid))
    {
        Notify(steamId, "Damage Control: grid moving — wait");
        return;
    }

    int started = CrewRepairMission.DispatchGrid(this, req.GridEntityId);
    if (started <= 0)
        Notify(steamId, "Damage Control: nothing to repair / not ready");
    else
        Notify(steamId, "Damage Control: dispatched " + started);
}
```

Use the same `TryGetGrid` helper already used by other handlers (do not invent a new lookup).

- [ ] **Step 4: Manual check**

Compile-in-game later; here confirm handler references compile-clean names only.

---

### Task 4: HUD Send / Recall button

**Files:**
- Modify: `Data/Scripts/HireCrew/CrewHudWindow.cs`

**Interfaces:**
- Consumes: `session.ClientRequestRepairDispatch`, `CrewRepairMission.IsAnyMissionOnGrid`
- Produces: Home `_btnRepair` visible for stationed Damage Control; label Send vs Recall

- [ ] **Step 1: Field + create button** next to `_btnTrain` / `_btnDismiss`:

```csharp
private CrewHudButton _btnRepair;
```

In init (after `_btnDismiss = MakeBtn(...)`):

```csharp
_btnRepair = MakeBtn("Send", 0f, 0f, true);
```

Place between Train and Dismiss by shifting bottom offsets:

```csharp
PlaceBottom(_btnTrain, 0f, 72f);
PlaceBottom(_btnRepair, 72f, 72f);
PlaceBottom(_btnDismiss, 144f, 72f);
PlaceBottom(_btnBulk, 216f, 72f);
PlaceBottom(_btnClose, 288f, 72f);
```

(Keep Assign / Unassign / Quarters offsets as they are; if Close clips the panel, nudge widths down to `68f` rather than inventing a second row.)

Wire:

```csharp
_btnRepair.MouseInput.LeftReleased += (s, a) => RepairDispatchOrRecall();
```

- [ ] **Step 2: Visibility / enabled in Home refresh** (with other home buttons):

```csharp
bool selectedDc = selectedHome != null
    && selectedHome.Role == CrewRole.DamageControl
    && selectedHome.Status == CrewStatus.Seated
    && selectedHome.GridEntityId != 0;
_btnRepair.Visible = home && !bulkOn && selectedDc;
```

When `selectedDc`:

```csharp
bool anyOut = CrewRepairMission.IsAnyMissionOnGrid(selectedHome.GridEntityId);
bool canRepair = !bulkOn && selectedDc;
// Send requires idle grid when not recalling
if (!anyOut && canRepair)
{
    IMyEntity gEnt;
    IMyCubeGrid g = null;
    if (MyAPIGateway.Entities.TryGetEntityById(selectedHome.GridEntityId, out gEnt))
        g = gEnt as IMyCubeGrid;
    if (g == null || !CrewAmbientPresence.IsGridIdle(g))
        canRepair = false;
}
SetHomeAction(_btnRepair, canRepair, anyOut ? ActionDismiss /* or ActionBase */ : ActionAssign);
_btnRepair.SetTextIfChanged(anyOut ? "Recall" : "Send");
```

Use existing action colors: prefer `ActionAssign` for Send and `ActionDismiss` (or a warning tint) for Recall — match nearby button patterns via `SetHomeAction`.

- [ ] **Step 3: Click handler**

```csharp
private void RepairDispatchOrRecall()
{
    var session = CrewSession.Instance;
    if (session == null) return;
    var crew = FindSelectedCrew(session);
    if (crew == null || crew.Role != CrewRole.DamageControl || crew.GridEntityId == 0)
        return;
    bool recall = CrewRepairMission.IsAnyMissionOnGrid(crew.GridEntityId);
    session.ClientRequestRepairDispatch(crew.GridEntityId, recall);
}
```

- [ ] **Step 4: Hint copy**

In `AmenityBonusHint`:

```csharp
case CrewRole.DamageControl:
    return "Manual EVA repair — Send from HUD";
```

- [ ] **Step 5: Manual in-game verify**

1. Seat 2+ Damage Control on a damaged static grid — they must **not** auto-leave.
2. HUD select DC → **Send** → both sortie (or up to parallel cap) and repair.
3. After return, idle; new damage does **not** auto-launch.
4. Send again → second sortie.
5. Mid-sortie **Recall** → they head home; button returns to **Send**.
6. Send while thrusting hard → notify reject; no launch.
7. Send with healthy hull → “nothing to repair” notify.

---

## Spec coverage checklist

| Spec requirement | Task |
|------------------|------|
| Manual Send from HUD | 4 |
| Batch all DC on grid | 2 `DispatchGrid` |
| Idle after clear until next Send | 2 remove auto-start |
| Recall toggles same control | 4 |
| Net message + permission | 1, 3 |
| Idle-grid guard | 2, 3, 4 |
| Hint text | 4 |
| No auto scan start | 2 |

## Self-review notes

- No placeholders left.
- Message id `41747` does not collide with current `PathEditMsg = 41746`.
- `TryStartMissions` must have zero callers after Task 2.
