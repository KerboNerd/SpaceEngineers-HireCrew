# Bulk Assign Design

Date: 2026-07-28  
Status: Approved for planning

## Problem

On weapon-heavy ships, assigning crew one-by-one through the existing Assign wizard (crew → seat → weapon) is too slow. Players need a crew-first bulk path that still keeps explicit seat + weapon control.

## Goals

- Select many **unassigned** crew on Home, then map each to a seat and weapon, then confirm once.
- Reuse existing assign validation and spawn behavior; no new persistence shape.
- Keep the single-assign wizard unchanged for one-offs.

## Non-goals

- Auto-matching seats/weapons without player picks.
- Reassigning already seated crew in bulk (v1).
- Weapon-only or seat-only shortcuts.
- Changes to hire desk, quarters, training, or dismiss.

## Player flow

### Home — Bulk mode off

Current single-select Home behavior unchanged (Assign / Unassign / Quarters / Train / Dismiss).

### Home — Bulk mode on

1. Player toggles **Bulk** on Home.
2. Clicks on unassigned, assignable crew toggle membership in a multi-selection (highlight persists).
3. Non-selectable crew (seated, training, otherwise not assignable): click ignored.
4. While Bulk is on, single-person actions (Assign, Unassign, Quarters, Train, Dismiss) are hidden or disabled.
5. Visible actions: **Bulk Assign** (enabled when ≥1 selected), **Clear**, **Bulk** (toggle off).
6. Context line shows `Bulk: N selected`.
7. Selection cap (default **20**); status `Bulk limit 20` when hit.
8. Pool-only HUD (no managed grid): Bulk unavailable (same gate as Assign).

### Mapping screen

1. **Bulk Assign** opens the mapping screen with one row per selected crew.
2. Each row: crew summary (name, role, stars) + Seat pick + Weapon pick.
3. Seat/Weapon open pick sub-lists (same free / same-grid rules as today’s wizard), then return to the mapping table.
4. Seats/weapons already chosen on other mapping rows are excluded or greyed.
5. **Confirm** enabled only when every row has seat + weapon and there are no cross-row conflicts.
6. **Back** returns to Home with selection **and** seat/weapon picks preserved.
7. Leaving Bulk or closing the HUD clears bulk selection and mapping draft.

### Confirm result

Server applies each row with existing assign rules. Partial success is allowed. Client gets roster sync plus a short notify, e.g. `Assigned 7/8. Failed: Rex (seat taken)`.

## UI

### New Home controls

- **Bulk** toggle button near Assign.
- **Bulk Assign** and **Clear** visible only while Bulk is on (or Clear only in Bulk).

### New screen: BulkMap

- New `CrewHudScreen` value for the mapping table.
- Header: `Bulk Assign (N)`.
- ~5 visible rows with scroll (match Home list density).
- Footer: **Back** | **Confirm**.

### Seat / weapon picking from BulkMap

- Reuse AssignSeat / AssignWeapon list UI patterns.
- Return target is BulkMap (not the single-assign wizard stack).
- Track which mapping row is being edited.

## Client model

Extend `CrewHudModel` with:

- `BulkMode` (bool)
- Ordered selected crew IDs (list; cap 20)
- Mapping entries: `{ CrewId, SeatEntityId, WeaponEntityId }` where `0` means unset
- Helpers: enter/exit bulk, toggle selection, clear selection, begin bulk map, set seat/weapon for row, confirm readiness

Selection rules for toggle:

- Must pass the same “can assign from Home” checks as today’s single Assign (unassigned, not training, managed grid, ownership).

## Networking

### Request

New message `BulkAssignRequest`:

- Owner/context as required by existing hire/assign messages
- Array of `{ CrewId, SeatEntityId, WeaponEntityId }`

### Server

- For each entry, run the **same** validation and assign/spawn path as single `AssignRequest`.
- Do not abort the whole batch on one failure; continue remaining entries.
- Within one request, reject duplicate seat or weapon IDs after the first use.
- After the loop: persist/sync roster as today; send notify with success/fail summary.

### Response / sync

- Existing roster sync is enough for applied rows.
- Notify text must mention counts and at least one failure reason when any fail.

## Conflict rules

**Client (preflight):**

- No two mapping rows share the same seat or weapon.
- Confirm disabled until complete and conflict-free.

**Server (authoritative):**

- Existing taken/invalid/wrong-grid/ownership checks still apply.
- Batch-internal duplicates rejected for later rows.

## Edge cases

| Case | Behavior |
|------|----------|
| Empty selection | Bulk Assign disabled |
| Cap hit | Status `Bulk limit 20`; further selects ignored |
| Incomplete mapping | Confirm disabled |
| Partial server success | Notify; successful rows seated; failed stay unassigned |
| All fail | Stay able to fix picks (mapping or Home with selection); do not silently wipe draft |
| Crew vanishes mid-map (sync) | Drop that row; brief status |
| Close HUD / exit Bulk | Clear selection + mapping draft |
| Back from mapping | Keep selection and picks |

## Done when

- Player can staff multiple unassigned crew with seat+weapon each via one Confirm.
- Single-assign wizard still works.
- Dedicated multiplayer: server validates every row; clients see roster update + notify.

## Out of scope follow-ups

- Bulk reassign of seated crew
- Auto-fill seat near weapon (or reverse)
- “Fill the ship” one-click staffing
