# Role Dispatch Hotkeys Design

Date: 2026-07-30  
Status: Approved for planning

## Problem

Sending Construction or Salvage crew requires opening the crew HUD and clicking per-crew Send/Salvage. Players want dedicated hotkeys to sortie or recall all assigned crew of each role on the ship they are flying.

## Goals

- Two remappable Rich HUD binds:
  - **Send/Recall Construction** — default `End`
  - **Send/Recall Salvage** — default `Delete`
- Target: seated managed grid only (`TryGetLocalManagedGrid`).
- Toggle semantics: if any of that role on the grid are mid-mission → Recall all; else Send all idle eligible seated of that role.
- Server batch request with one summary notify.
- Keep per-crew HUD Send/Recall unchanged.

## Non-goals

- Off-seat / EVA / HUD-focused-grid dispatch.
- Dispatching every managed grid at once.
- New HUD “Send all” button (batch API may support it later).
- Changing weld/salvage mission simulation, path paint, or zone marking UX.
- Hotkeys for other roles.

## Behavior

1. Ignore presses while `BindManager.IsChatOpen`.
2. If not seated on a managed grid → notify and stop (no network).
3. Client decides `Recall` vs Send from local mission sync (`IsCrewOnRepairMission` / `IsCrewOnSalvageMission`) for seated crew of that role on the grid.
4. Client sends batch request with `GridEntityId`, role, and `Recall`.
5. Server auth: manage permission on grid.
6. **Recall:** recall every on-mission crew of that role assigned to the grid.
7. **Send:**
   - Grid must pass `CrewAmbientPresence.IsGridIdle` (else notify moving).
   - Construction: `CrewRepairMission.DispatchCrew` for each eligible seated idle Damage Control (parallel caps / readiness unchanged).
   - Salvage: resolve zone once (same as single dispatch); if missing → same no-target notify as HUD; else `CrewSalvageMission.DispatchCrew` for each eligible seated idle SalvageOps.
8. One summary notify, e.g. `Construction: sent 3`, `Construction: recalling 2`, `Construction: none ready`, `Salvage: no target — …`.

## Architecture

### Networking

| Piece | Detail |
|-------|--------|
| Msg id | `RoleDispatchBatchMsg = 41753` |
| DTO | `RoleDispatchBatchRequest`: `GridEntityId` (long), `Role` (int / `CrewRole`), `Recall` (bool) |
| Handler | `CrewSession.HandleRoleDispatchBatch` |
| Client API | `CrewSession.ClientRequestRoleDispatchBatch(gridId, role, recall)` |

Roles accepted: `CrewRole.DamageControl`, `CrewRole.SalvageOps` only.

### Keybinds

Extend existing `CrewKeyBinds` / RebindPage group `HireCrew`:

| Bind display name | Default |
|-------------------|---------|
| Send/Recall Construction | `MyKeys.End` |
| Send/Recall Salvage | `MyKeys.Delete` |

Poll in `CrewHud.Update` alongside Open Crew UI (chat gate via `CrewKeyBindRules` or shared helper).

### Pure rules (testable)

- `ShouldHandleBind(bindNewPressed, chatOpen)` — reuse or alias existing press gate.
- `ShouldRecallRole(anyOfRoleOnMission)` — true → Recall, false → Send.
- Optional: summary message formatter for counts.

### Lifecycle

Same as Open Crew UI: register on RHF ready, clear on reset/unload, client-only.

## Error handling

| Case | Result |
|------|--------|
| Chat open | Ignore |
| No managed grid | Client notify |
| No permission | Server notify |
| Grid moving (Send) | Server notify; Recall still runs |
| No salvage zone (Send) | Server notify; no dispatches |
| Zero eligible | Summary `none ready` |
| Partial Send success | Summary with sent count; failures remain silent beyond count |

## Testing

### Unit

- Recall vs Send decision from “any on mission”.
- Bind press ignored when chat open.

### Manual

- Seated: End sends all idle Construction; again recalls when any out.
- Seated: Delete same for Salvage with zone marked; without zone → no-target message.
- Chat open → no dispatch.
- Rebind in Rich HUD Terminal → HireCrew → Key Binds; survives relog.
- Per-crew HUD Send/Recall still works.
- Dedicated: client hotkey → server batch handler.

## Out of scope follow-ups

- HUD button wrapping the batch API.
- EVA dispatch via focused grid.
- Combined “send both roles” chord.
