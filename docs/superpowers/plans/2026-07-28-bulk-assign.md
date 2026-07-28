# Bulk Assign Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let players multi-select unassigned crew on Home, map each to seat (+ weapon when required), and confirm one bulk assign request.

**Architecture:** Extend `CrewHudModel` with bulk selection + mapping state; add `BulkAssignRequest` over a new net message; server loops shared assign logic with partial success; `CrewHudWindow` gains Bulk mode controls, BulkMap screen, and seat/weapon pick return-to-map.

**Tech Stack:** Space Engineers ModAPI, RichHudFramework, protobuf-net messages, xunit logic tests (net48).

## Global Constraints

- Unassigned crew only in bulk selection (same `CanAssignHome` as single Assign).
- Selection cap **20**; status text `Bulk limit 20` when hit.
- Weapon required only when `CrewConfig.NeedsWeapon(role)` (match existing single-assign).
- Single-assign wizard unchanged.
- No persistence schema changes.
- Partial success: continue batch; one summary notify; roster broadcast if any success.
- Back from BulkMap keeps selection and picks; Close HUD / exit Bulk clears selection + mapping.
- User runs tests manually: `dotnet test tests/HireCrew.Logic.Tests/HireCrew.Logic.Tests.csproj` (agent must not run dotnet).

## File structure

| File | Role |
|------|------|
| `Data/Scripts/HireCrew/CrewHudModel.cs` | BulkMode, selection, mapping entries, screen helpers |
| `Data/Scripts/HireCrew/CrewModels.cs` | `BulkAssignEntry`, `BulkAssignRequest` |
| `Data/Scripts/HireCrew/CrewNetworking.cs` | `BulkAssignMsg = 41744` register/unregister |
| `Data/Scripts/HireCrew/CrewSession.cs` | Extract assign core; `HandleBulkAssign`; `ClientRequestBulkAssign` |
| `Data/Scripts/HireCrew/CrewHudWindow.cs` | Bulk UI + BulkMap + pick wiring |
| `tests/HireCrew.Logic.Tests/CrewHudModelTests.cs` | Model unit tests |

---

### Task 1: CrewHudModel bulk state + tests

**Files:**
- Modify: `Data/Scripts/HireCrew/CrewHudModel.cs`
- Modify: `tests/HireCrew.Logic.Tests/CrewHudModelTests.cs`

**Interfaces:**
- Produces:
  - `CrewHudScreen.BulkMap = 11`, `BulkPickSeat = 12`, `BulkPickWeapon = 13`
  - `class BulkMapEntry { string CrewId; long SeatEntityId; long WeaponEntityId; }`
  - `bool BulkMode`, `IReadOnlyList<string> BulkSelectedCrewIds`, `IReadOnlyList<BulkMapEntry> BulkMapEntries`
  - `const int BulkSelectionCap = 20`
  - `void SetBulkMode(bool on)` — clearing selection+map when turning off
  - `bool TryToggleBulkSelect(CrewRecord r)` — requires `HasManagedGrid && CanAssignHome`; respects cap; returns false if rejected
  - `void ClearBulkSelection()`
  - `bool TryBeginBulkMap(Func<string, CrewRecord> resolve)` — builds map entries from selection; fails if empty
  - `void BeginBulkPickSeat(int mapIndex)` / `BeginBulkPickWeapon(int mapIndex)`
  - `bool TrySetBulkSeat(long seatId)` / `TrySetBulkWeapon(long weaponId)` — writes current pick index; clears conflicting other rows; returns to BulkMap
  - `void ReturnToBulkMap()` / `void BulkMapBackToHome()` — Home keeps selection+picks
  - `bool IsBulkMapReady(Func<string, CrewRecord> resolve)` — every entry has seat; weapon if NeedsWeapon; no duplicate seat/weapon among non-zero ids
  - `HashSet<long> GetBulkReservedSeats(int exceptIndex)` / `GetBulkReservedWeapons(int exceptIndex)`
  - `void PruneBulkSelection(Func<string, CrewRecord> resolve)` — drop ids that no longer pass CanAssignHome
  - Open/Close/GoHome: Close clears bulk entirely; GoHome from non-bulk paths unchanged; exiting bulk via SetBulkMode(false) clears

- [ ] **Step 1: Add failing tests** to `CrewHudModelTests.cs`:

```csharp
[Fact]
public void Bulk_toggle_select_and_cap()
{
    var m = new CrewHudModel();
    m.Open(1);
    m.SetBulkMode(true);
    Assert.True(m.BulkMode);
    for (int i = 0; i < CrewHudModel.BulkSelectionCap; i++)
        Assert.True(m.TryToggleBulkSelect(new CrewRecord { CrewId = "c" + i, Status = CrewStatus.Unassigned }));
    Assert.False(m.TryToggleBulkSelect(new CrewRecord { CrewId = "overflow", Status = CrewStatus.Unassigned }));
    Assert.Equal(CrewHudModel.BulkSelectionCap, m.BulkSelectedCrewIds.Count);
    Assert.False(m.TryToggleBulkSelect(new CrewRecord { CrewId = "c0", Status = CrewStatus.Seated, GridEntityId = 1 }));
}

[Fact]
public void Bulk_map_ready_requires_seat_and_weapon_for_gunner()
{
    var m = new CrewHudModel();
    m.Open(1);
    m.SetBulkMode(true);
    m.TryToggleBulkSelect(new CrewRecord { CrewId = "g", Status = CrewStatus.Unassigned, Role = CrewRole.Gunner });
    Assert.True(m.TryBeginBulkMap(id => new CrewRecord { CrewId = id, Status = CrewStatus.Unassigned, Role = CrewRole.Gunner }));
    Assert.Equal(CrewHudScreen.BulkMap, m.Screen);
    Assert.False(m.IsBulkMapReady(id => new CrewRecord { CrewId = id, Role = CrewRole.Gunner, Status = CrewStatus.Unassigned }));
    m.BeginBulkPickSeat(0);
    Assert.True(m.TrySetBulkSeat(10));
    Assert.Equal(CrewHudScreen.BulkMap, m.Screen);
    Assert.False(m.IsBulkMapReady(id => new CrewRecord { CrewId = id, Role = CrewRole.Gunner, Status = CrewStatus.Unassigned }));
    m.BeginBulkPickWeapon(0);
    Assert.True(m.TrySetBulkWeapon(20));
    Assert.True(m.IsBulkMapReady(id => new CrewRecord { CrewId = id, Role = CrewRole.Gunner, Status = CrewStatus.Unassigned }));
}

[Fact]
public void Bulk_map_seat_only_role_ready_without_weapon()
{
    var m = new CrewHudModel();
    m.Open(1);
    m.SetBulkMode(true);
    m.TryToggleBulkSelect(new CrewRecord { CrewId = "e", Status = CrewStatus.Unassigned, Role = CrewRole.Engineer });
    m.TryBeginBulkMap(id => new CrewRecord { CrewId = id, Status = CrewStatus.Unassigned, Role = CrewRole.Engineer });
    m.BeginBulkPickSeat(0);
    m.TrySetBulkSeat(11);
    Assert.True(m.IsBulkMapReady(id => new CrewRecord { CrewId = id, Role = CrewRole.Engineer, Status = CrewStatus.Unassigned }));
}

[Fact]
public void Bulk_back_keeps_picks_close_clears()
{
    var m = new CrewHudModel();
    m.Open(1);
    m.SetBulkMode(true);
    m.TryToggleBulkSelect(new CrewRecord { CrewId = "g", Status = CrewStatus.Unassigned, Role = CrewRole.Gunner });
    m.TryBeginBulkMap(id => new CrewRecord { CrewId = id, Status = CrewStatus.Unassigned, Role = CrewRole.Gunner });
    m.BeginBulkPickSeat(0);
    m.TrySetBulkSeat(10);
    m.BulkMapBackToHome();
    Assert.Equal(CrewHudScreen.Home, m.Screen);
    Assert.True(m.BulkMode);
    Assert.Equal(1, m.BulkSelectedCrewIds.Count);
    Assert.Equal(10L, m.BulkMapEntries[0].SeatEntityId);
    m.Close();
    Assert.False(m.BulkMode);
    Assert.Equal(0, m.BulkSelectedCrewIds.Count);
}
```

- [ ] **Step 2: User verifies fail** — `dotnet test tests/HireCrew.Logic.Tests/HireCrew.Logic.Tests.csproj --filter Bulk_`

- [ ] **Step 3: Implement model API** in `CrewHudModel.cs` per Interfaces above. Keep `SelectedCrewId` for single-assign; bulk uses its own lists. When `TrySetBulkSeat`/`Weapon` assigns an id already used on another row, clear the other row’s field. `TryToggleBulkSelect` on already-selected id removes it. Pool-only (`!HasManagedGrid`): `TryToggleBulkSelect` / `TryBeginBulkMap` return false; `SetBulkMode(true)` no-ops or stays false.

- [ ] **Step 4: User verifies pass** — same filter.

- [ ] **Step 5: Commit** — `feat: add bulk assign HUD model state`

---

### Task 2: Network message types

**Files:**
- Modify: `Data/Scripts/HireCrew/CrewModels.cs`
- Modify: `Data/Scripts/HireCrew/CrewNetworking.cs`

**Interfaces:**
- Produces: `BulkAssignMsg = 41744`, `BulkAssignEntry`, `BulkAssignRequest`

- [ ] **Step 1: Add models** after `AssignRequest`:

```csharp
[ProtoContract]
public sealed class BulkAssignEntry
{
    [ProtoMember(1)] public string CrewId;
    [ProtoMember(2)] public long SeatEntityId;
    [ProtoMember(3)] public long WeaponEntityId;
}

[ProtoContract]
public sealed class BulkAssignRequest
{
    [ProtoMember(1)] public long GridEntityId;
    [ProtoMember(2)] public List<BulkAssignEntry> Entries;
}
```

- [ ] **Step 2: Register** `BulkAssignMsg = 41744` in Register/Unregister alongside other handlers.

- [ ] **Step 3: Commit** — `feat: add BulkAssign network message`

---

### Task 3: Server bulk assign + client request

**Files:**
- Modify: `Data/Scripts/HireCrew/CrewSession.cs`

**Interfaces:**
- Consumes: `BulkAssignRequest`, `BulkAssignMsg`
- Produces: `ClientRequestBulkAssign(long gridEntityId, List<BulkAssignEntry> entries)`, `HandleBulkAssign`
- Refactor: extract `string TryApplyAssign(AssignRequest req, long identityId)` returning null on success or error text; no Notify/Broadcast inside. `HandleAssign` calls it then Notify + BroadcastRoster. `HandleBulkAssign` loops, tracks used seats/weapons in-request, aggregates notify, BroadcastRoster once if any ok.

- [ ] **Step 1: Extract `TryApplyAssign`** from current `HandleAssign` body (permission, ownership, training, seated, ValidateAssign, seat/weapon entity checks, write Store). Return error strings identical to today’s Notify texts.

- [ ] **Step 2: `HandleAssign`** becomes: deserialize path unchanged; `var err = TryApplyAssign(...); if (err != null) Notify; else { Notify "Crew assigned"; BroadcastRoster; }`

- [ ] **Step 3: Message handler** branch for `BulkAssignMsg` → `HandleBulkAssign`.

- [ ] **Step 4: Implement `HandleBulkAssign`:**
  - null/empty entries → return
  - grid + HasManagePermission once
  - cap entries to 20
  - HashSets for seats/weapons claimed in this batch
  - foreach entry: if duplicate seat/weapon in batch → fail that row with `"Seat already used in batch"` / `"Weapon already used in batch"`; else build AssignRequest and `TryApplyAssign`; on success add seat/weapon to sets
  - Notify once: `Assigned X/Y` or `Assigned X/Y. Failed: {name} ({reason})` using first failure
  - if X > 0: `BroadcastRoster(gridId)` once

- [ ] **Step 5: `ClientRequestBulkAssign`** — mirror `ClientRequestAssign` (server local vs SendToServer).

- [ ] **Step 6: Commit** — `feat: handle bulk assign on server`

---

### Task 4: CrewHudWindow bulk UI

**Files:**
- Modify: `Data/Scripts/HireCrew/CrewHudWindow.cs`

**Interfaces:**
- Consumes: all Task 1 model APIs + `ClientRequestBulkAssign`

- [ ] **Step 1: Home buttons** — add `_btnBulk`, `_btnBulkAssign`, `_btnClearBulk` (reuse `CrewHudButton` / `PlaceBottom` patterns). Visibility:
  - Bulk off + managed grid: show Bulk; hide BulkAssign/Clear
  - Bulk on: show Bulk (toggle off), BulkAssign (≥1), Clear; hide Assign/Unassign/Quarters/Train/Dismiss (or disable)
  - Pool-only: hide Bulk controls

- [ ] **Step 2: Home row click** — if `BulkMode`, call `TryToggleBulkSelect` instead of single select; highlight rows in `BulkSelectedCrewIds`. Context: `Bulk: N selected` (+ limit message when cap blocks).

- [ ] **Step 3: BulkMap screen** — when `Screen == BulkMap`, fill rows from `BulkMapEntries` with crew labels; each row click or dedicated seat/weapon buttons: for RichHud constraints, use pattern: select map row → footer **Seat** / **Weapon** / **Confirm** / **Back** (same density as Home). Prefer: clicking a map row selects `BulkEditIndex`; **Seat** → `BeginBulkPickSeat`; **Weapon** → `BeginBulkPickWeapon` (disabled if !NeedsWeapon).

- [ ] **Step 4: BulkPickSeat / BulkPickWeapon** — reuse `FillAssignSeat` / `FillAssignWeapon` filters, additionally exclude `GetBulkReservedSeats/Weapons(exceptIndex)`. On pick: `TrySetBulkSeat` / `TrySetBulkWeapon` then Refresh. Back from pick: `ReturnToBulkMap`.

- [ ] **Step 5: Confirm** — if `IsBulkMapReady`, build `List<BulkAssignEntry>` from map, `ClientRequestBulkAssign`, stay on BulkMap or GoHome with BulkMode still on and prune selection via `PruneBulkSelection` after request (client may prune on next refresh when roster updates). Prefer: after confirm, `GoHome()` keeping BulkMode; prune seated ids on next FillHome via `PruneBulkSelection`.

- [ ] **Step 6: WizardBack / header** — route BulkMap/BulkPick* through model helpers; header `Bulk Assign (N)`.

- [ ] **Step 7: Commit** — `feat: add bulk assign HUD UI`

---

### Task 5: Smoke checklist (manual)

- [ ] Hire several unassigned gunners; `/crew` → Bulk → select 3 → Bulk Assign → set seat+weapon each → Confirm → all seated.
- [ ] Confirm disabled until complete; duplicate seat blocked client-side.
- [ ] Single Assign still works with Bulk off.
- [ ] Engineer bulk: seat only, Confirm works with weapon 0.
- [ ] Dedicated MP: second client sees roster update + summary notify.

---

## Spec coverage

| Spec item | Task |
|-----------|------|
| Multi-select Home Bulk mode | 1, 4 |
| Mapping seat+weapon / seat-only | 1, 4 |
| Cap 20 / Bulk limit | 1, 4 |
| Unassigned only | 1 |
| BulkAssignRequest + partial success | 2, 3 |
| Back keeps picks; Close clears | 1, 4 |
| Single assign unchanged | 4 (gates) |
