# Crew Status Sidebar Design

Date: 2026-07-28  
Status: Approved for planning

## Problem

Players sending Construction (Damage Control) crew on EVA repair missions have no always-on readout of who is out and what they are doing. The existing `/crew` RichHud window is a management panel, not a persistent status HUD.

## Goals

- Show a compact left-side crew status sidebar via **RichHudFramework** (not TextHUDAPI).
- Visible when the local player is **seated on a managed grid** and the sidebar toggle is on (default on).
- List only **fieldwork crew with active status**: Construction / `CrewRole.DamageControl` with `RepairMissionState != Idle`.
- Each row: name, role, short status, activity hint (medium density).
- Toggle with `/crew hud` (does not open the management window).

## Non-goals

- TextHUDAPI / HudAPIv2 dependency.
- Showing seated buff roles (Gunner, Engineer, Helmsman, Propulsion, Quartermaster).
- Idle / Ready Construction crew rows (v1: active missions only).
- Click-to-select, recall, or dispatch from the sidebar.
- Keybind beyond chat toggle (can add later).
- Collapsed “N ready” summary (deferred follow-up).

## Visibility rules

Show when **all** of:

1. RichHud client ready
2. Local player seated in a cockpit/control seat
3. That seat’s grid is a HireCrew managed grid for the player
4. Sidebar toggle enabled (default **on**)
5. At least one active Construction mission on that grid

Otherwise hide completely (no empty panel chrome).

## UI layout

- Position: left edge of the screen, vertically mid-left (avoid stacking on vanilla top-left HUD clutter).
- Chrome: soft semi-transparent panel behind the row stack only.
- Rows (up to 6; if more, show first 6 + “+N more”):
  - Line 1: `DisplayName` · `Construction` (role tint consistent with existing `/crew` colors for Damage Control)
  - Line 2: status label from mission state + optional activity hint
  - Optional thin left color bar by state severity (e.g. welding vs returning)
- Display-only; no mouse interaction required.

### Status labels

| `RepairMissionState` | Label |
|----------------------|--------|
| WalkOut | Walking out |
| AtExit | At airlock |
| EvaTransit | EVA |
| Welding | Welding |
| ReturnExit | Returning |
| WalkHome | Walking home |

Activity hints (when applicable): e.g. out of components, projector/build target — use existing mission flags already tracked server-side (`NotifiedOutOfComps`, `TargetIsProjected`, etc.) without inventing new simulation.

## Architecture

Dedicated sidebar separate from `CrewHudWindow`:

| Piece | Responsibility |
|--------|----------------|
| `CrewStatusHudModel` | Build ordered row list from active missions + crew records; label mapping; max-row truncation |
| `CrewStatusSidebar` | RichHud element on `HudMain.Root`; binds to model |
| `CrewHud` | Create/destroy with RHF lifecycle; per-tick visibility + refresh; parse `/crew hud` |
| `CrewRepairMission` | Read API to enumerate active (non-Idle) missions for a grid |
| Networking | `RepairMissionSync` snapshot for dedicated-server clients |

### Data flow

1. Server (or SP host) maintains mission runtimes in `CrewRepairMission`.
2. On state change and/or throttled interval (~0.5–1s), push `RepairMissionSync` with compact entries: `CrewId`, `DisplayName`, `GridEntityId`, `State`, hint flags.
3. Client caches the latest snapshot.
4. Each client tick: if visibility rules pass, `CrewStatusHudModel` rebuilds rows for the local managed grid from cache (DS) or direct mission read (SP/listen server).
5. `CrewStatusSidebar` updates labels/visibility.

SP / listen: may read `CrewRepairMission` directly and still accept sync for consistency.  
DS client: synced cache only.

## Edge cases

- Toggle off → hidden even with active missions.
- Leave seat / change grid → hide; reappear when rules pass again.
- Mission finishes → row removed on next refresh; no stale empty frame.
- Client missing crew name → use synced `DisplayName`; if still empty, skip or show “Crew”.
- Unknown/stale crew id → skip row; do not throw.
- RHF reset/unload → dispose sidebar; recreate on ready.

## Testing

- Unit tests (`HireCrew.Logic.Tests`) for `CrewStatusHudModel`: filter Idle out, only DamageControl, label mapping, max 6 + overflow text, grid filter.
- In-game: seat on managed grid, dispatch Construction, confirm left sidebar; toggle `/crew hud`; leave seat; mission complete clears HUD; DS client sees updates.

## Open follow-ups (out of scope)

- Idle “N ready” summary line.
- Toolbar/keybind for toggle.
- Future fieldwork roles beyond Damage Control (same sidebar filter extension point).
