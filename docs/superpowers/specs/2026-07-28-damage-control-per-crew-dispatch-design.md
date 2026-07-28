# Damage Control Per-Crew Dispatch Design

Date: 2026-07-28  
Status: Approved

## Problem

Grid-batch Send/Recall launches every Damage Control on a ship together and crowds the HUD bottom bar.

## Goals

- Dispatch / recall **one** Damage Control crew at a time.
- **Send** / **Recall** control on **every** Damage Control roster row (no prior selection required).
- Remove the bottom-bar Send/Recall button.

## Non-goals

- Grid-wide Send all / Recall all.
- Auto-repair reintroduction.
- Changes to weld economy, path painting, or EVA AI beyond start/stop targeting one crew.

## Player loop

1. Open Crew HUD; each seated Damage Control row shows **Send** or **Recall**.
2. Press **Send** on a row → that crew sorties (if grid idle and work exists).
3. Press **Recall** on that row → only that crew returns.
4. Other DC on the same grid are unaffected.

## Systems

| Piece | Responsibility |
|---|---|
| Roster row button | Per-crew Send/Recall; visible only for Damage Control rows |
| `RepairDispatchRequest` | `CrewId` + `Recall` (grid taken from crew record server-side) |
| `CrewRepairMission` | `DispatchCrew` / `RecallCrew` |
| Bottom bar | No repair action |

### Networking

- Reuse `RepairDispatchMsg`.
- Proto: `string CrewId`, `bool Recall`. `GridEntityId` may remain for back-compat but server trusts crew record’s grid.
- Auth: manage permission on that crew’s grid (same as other HUD actions).

### Server

- **Send:** one seated DC, not on mission, grid idle, start mission (existing pick/path/local logic). Ignore post-sortie cooldown on manual Send.
- **Recall:** `BeginReturn` for that crew only if on mission.
- Notify: dispatched / recalled / not ready / grid moving / no permission.

### HUD

- Compact row control (right side of card); hidden for non-DC.
- Label = **Recall** iff `IsCrewOnMission(crewId)`, else **Send**.
- Send disabled when grid not idle (or hide/disable appropriately).
- Clicking the control must not be required to select the row first; row click still selects for other actions.
