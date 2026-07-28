# Off-Ship Grid Picker Design

Date: 2026-07-28  
Status: Approved for planning

## Problem

`/crew` while not seated opens a pool-only HUD (train & dismiss). Players cannot see which grids their crew occupy or unassign from those grids without sitting in a seat. They need an off-ship entry that lists crewed grids and unassigned crew together.

## Goals

- Off-seat `/crew`: one Home list with **Grids** (selectable), **On [Grid]** seated crew (after focus), and **Unassigned**.
- Grid list includes only grids that currently have the local owner’s seated crew.
- Off-seat actions after focusing a grid: **Unassign**, **Train**, **Dismiss** (view + limited manage).
- Keep seated full-manage UI unchanged.

## Non-goals

- Remote Assign, Quarters, or Bulk from off-seat.
- Listing empty / uncrewed grids.
- Range-based nearby grid discovery independent of seated crew.
- Changing hire desk or server assign/hire rules.

## Player flow

### Seated on a managed grid

Unchanged: full roster Home with Assign / Unassign / Quarters / Train / Dismiss / Bulk. No grid picker.

### Off-seat — open `/crew`

1. Opens with `GridEntityId = 0` (no local managed grid) and `FocusedGridId = 0`.
2. Home list sections (top to bottom):
   - **— Grids —** then one row per crewed grid (name; highlight when focused).
   - If a grid is focused: **— On [Name] —** then that grid’s seated crew rows.
   - **— Unassigned —** then unassigned crew (always).
3. Empty cases:
   - No crewed grids: Grids section shows `(none with crew)`.
   - Focused grid has no seated crew left: clear focus (section hides).
   - No unassigned: `(none unassigned)` under Unassigned.
   - Completely empty roster: keep a single empty hint as today.

### Off-seat — select grid

1. Tap a grid row: set `FocusedGridId` to that grid’s entity id.
2. Tap the same grid again: clear focus (`FocusedGridId = 0`); On-[Grid] section hides.
3. Switching to another grid replaces focus and clears `SelectedCrewId`.

### Off-seat — actions

| Action | When enabled |
|--------|----------------|
| Train / Cancel Train | Selected crew (any status) per existing rules |
| Dismiss | Selected crew per existing rules |
| Unassign | `FocusedGridId != 0` and selected crew is seated on that focused grid |
| Assign / Quarters / Bulk | Hidden |

Status examples: `Off ship · select a grid` / `Off ship · viewing [Name]`.

### Lifecycle while open off-seat

- UI stays open while walking (no seat-lock).
- If focused grid entity disappears or loses all local-owner seated crew: clear `FocusedGridId`, refresh; do not hard-close.
- Local seated manage path still auto-closes if the player leaves the managed seat (`HasManagedGrid` only).

## UI

### Home list (off-seat only)

- Flat scrolled list using existing row budget / scroll.
- Non-interactive section headers (no `crewId`, not clickable).
- Grid rows: `entityId = gridEntityId`, no `crewId`; click toggles focus.
- Crew rows: existing card layout; click selects `SelectedCrewId`.
- Seated path `FillHome` remains a flat roster (no sections).

### Buttons (off-seat)

- Show: Train, Dismiss, Close; Unassign when focus + valid seated selection.
- Hide: Assign, Quarters, Bulk (and bulk-related controls).

## Client model

Extend `CrewHudModel`:

- Keep `GridEntityId` / `HasManagedGrid` = local seated manage only.
- Add `FocusedGridId` (`long`, `0` = none). Used only when `!HasManagedGrid`.
- Helpers: `HasFocusedGrid`, `SetFocusedGrid(long)`, `ClearFocusedGrid()`, toggle helper.
- Clear `FocusedGridId` on `Open` / `Close`.
- `CanUnassignHome` gating in the window uses focus when off-seat (not `HasManagedGrid`).

### Grid list source

Client builds unique grid ids from `GetCrewForLocalOwner()` (or equivalent roster) where:

- `Status == Seated` and `GridEntityId != 0`
- Grid entity still resolves via `TryGetEntityById`
- Label = `CustomName` if set, else a short fallback (e.g. `Grid` / `#id`)

No new network messages for the picker list.

## Server / validation

- Unassign / Train / Dismiss reuse existing client requests and server checks (ownership, training locks, etc.).
- Off-seat Unassign must not require the player to be seated in the target grid (server already keys off crew ownership + crew record; confirm during implementation that no “must be on managed grid” gate blocks this).
- No new persistence fields.

## Error handling

- Stale focus: clear and refresh quietly.
- Unassign with no valid selection: existing “Select a crew member” / “Cannot unassign” messages.
- Missing Rich Hud Master: unchanged.

## Testing (manual)

1. Off-seat, crew on two ships + unassigned: both grids and unassigned appear; focus shows only that ship’s seated crew.
2. Toggle focus off: On-[Grid] section hides; Unassign disabled.
3. Unassign from focused grid off-seat succeeds; crew moves to Unassigned.
4. Train / Dismiss still work off-seat without focus.
5. Assign / Quarters / Bulk never appear off-seat.
6. Sit in a managed seat: full UI, no grid picker; leave seat closes UI.
7. Focused grid deleted / no crew left: focus clears, UI stays open.
8. No seated crew anywhere: `(none with crew)` + Unassigned list.
