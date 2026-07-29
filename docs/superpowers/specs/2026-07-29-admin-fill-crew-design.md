# Admin Fill Crew Design

Date: 2026-07-29  
Status: Approved for planning

## Problem

Perf / mission testing needs many Construction and Salvage crew seated on a ship quickly. Today `/hc hire` creates unassigned roster rows one at a time with no grid assign.

## Goals

- Admin-only `/hirecrew fill <role> [count]` (`/hc` alias) that:
  - Free-hires N crew of Construction or Salvage Ops.
  - Assigns each to a free assignable seat on the admin’s current grid (same path as normal seat assign → bots spawn/station as today).
- Default count **10**; optional count arg.
- Partial success when seats run short; clear chat summary.

## Non-goals

- Filling gunner / helm / other roles (v1).
- Auto-starting repair or salvage missions.
- Stars CLI arg (fixed debug stars).
- Targeting another player’s roster or a remote grid id.
- Confirmation prompts.

## Command

```
/hirecrew fill <construction|salvage> [count]
/hc fill <construction|salvage> [count]
```

| Arg | Rules |
| --- | --- |
| `role` | Existing `TryParseRole` tokens that resolve to `DamageControl` or `SalvageOps` only (`construction`, `dc`, `salvage`, etc.). Other roles → error. |
| `count` | Optional. Default `10`. Integer clamp **1–50**. |

Stars: always **3**. Owner: invoking admin (same owner-key resolution as `hire`).

## Grid + seats

1. Resolve admin online player → character → `GetTopMostParent()` as `IMyCubeGrid` (same construct idea as `reroll near`). If no grid → abort `"Not on a grid"`.
2. Collect free assignable seats on that construct (`CrewStationLogic.IsAssignableSeat`, not player-occupied, not already taken by seated crew) — same eligibility normal assign uses.
3. For `i` in `0..count-1`:
   - Create `CrewRecord` (free hire, stars 3, role, admin owner).
   - If a free seat remains: run existing server assign path (seat + grid, status Seated, spawn/presence as today); consume seat.
   - Else: leave Unassigned (still hired) and count as no-seat.
4. Upsert / broadcast roster once at end (or per assign if existing helpers require it — prefer one broadcast).
5. Notify: `Filled construction: assigned 8/10 (2 no seat) on <gridName>` (role label + counts).

## Auth / plumbing

- Reuse existing admin gate and `AdminCommandRequest` dispatch in `CrewAdminCommands`.
- Add `fill` verb + help line.
- Server log one line with steam id, role, assigned/count, grid entity id.

## Docs

- Update admin commands help string in-mod.
- When implementing: add verb to wiki `Admin-Commands.md` if that page lists verbs.

## Manual verify

1. Admin on a ship with ≥10 free crew stations: `/hc fill construction` → 10 seated Construction.
2. `/hc fill salvage 5` → 5 seated Salvage Ops.
3. Ship with 3 free seats: `/hc fill construction 10` → 3 seated, 7 unassigned, notify shows partial.
4. Not on a grid / non-admin / bad role → clear errors, no partial silent success.
5. Seated crew can be dispatched on missions like normally hired crew.
