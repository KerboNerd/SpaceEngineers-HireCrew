# Salvage Zone Debris Design

Date: 2026-07-29  
Status: Approved for planning

## Problem

Salvage Ops mark a wreck by **grid entity id**. Grinding can split that wreck into debris grids with new entity ids. The mission stays on the original id, so fragments outside that entity are abandoned and the sortie is hard to finish.

## Goals

- On mark (LMB), capture a **frozen world AABB** of the wreck, inflated by **15 m**, and treat that as the salvage **zone**.
- Salvage Ops grind **all legal blocks** whose world positions lie inside that zone (including split debris).
- Visual highlight shows the **fixed zone**, not a live entity-following bbox.
- Clear / retarget / save-sync continue to work for the zone.

## Non-goals

- Growing or moving the zone after mark (no living volume).
- Chasing debris that drift **outside** the padded box.
- Changing leaf-first grind order, cargo deposit, home resolution, or legal-target rules (own / faction / unowned; never enemy).
- Auto-expanding the zone when new grids appear outside it.
- Shared EVA framework refactor with Construction.

## Approach

**Frozen AABB zone (Approach 1).**  
At mark time: `zone = target.WorldAABB.GetInflated(15)`. Persist zone per home construct. Mission block pick scans grids that intersect the zone; candidates must be legal, not the home construct, and have block world position inside the zone. Done when no such grindable blocks remain.

## Player loop (delta)

1. `/crew salvage` → resolve home (unchanged).
2. LMB wreck → store **padded frozen AABB** for that home; orange highlight draws that box.
3. HUD **Salvage** dispatches against the home’s zone (no reliance on a single target grid id for grinding).
4. Crew EVA → leaf-first grind among in-zone legal blocks → deposit → return when zone empty, cargo full, or Recall.
5. `/crew salvage clear|clearall` clears the zone mark.

## Data

| Field | Meaning |
|---|---|
| Home grid / construct ids | Same as today (stamp linked homes) |
| Zone Min / Max (world) | Frozen padded AABB corners |
| Optional seed `TargetGridEntityId` | Debug / migration only; **not** required for grind pick |

### Persistence / sync

- Bump `SalvageTargetStore` / protobuf entries to carry zone min/max (format version bump).
- Old saves that only have a target grid id: on load, if the grid still exists, rebuild zone from its current AABB + 15 m pad; else drop the mark.
- Client highlight sync receives zone extents instead of (or in addition to) entity id for draw.

### Config

- `SalvageZonePadMeters = 15f` in `CrewConfig` (compile-time).

## Mission behavior

- **Pick:** Enumerate candidate grids intersecting the zone (entity query / nearby scan capped by existing salvage scan radius from home). For each legal non-home grid, consider slim blocks with world position inside the zone. Score with existing leaf-first + distance rules across the combined candidate set.
- **Retarget mid-mission:** When the current block is gone, pick next in-zone block (may be on a different debris grid). EVA only if the next block is out of grind range (existing rule).
- **Done:** No grindable in-zone blocks left → return home.
- **Illegal:** Skip enemy grids even if they intersect the zone. Never grind the home construct.
- **Highlight:** Draw fixed world AABB (line/box draw API already used for entity bbox — switch to zone extents).

## Testing (logic)

- Zone inflate: AABB + pad produces expected min/max.
- Block-in-zone: inside / on face / outside.
- PreferGrindCandidate unchanged; zone filter is separate.
- NeedsEvaAfterRetarget unchanged.

## Wiki

- Update [Salvage Ops](.wiki/Salvage-Ops.md): mark creates a padded area; debris inside are salvaged; drift-out are not.

## Success

- Mark a wreck, grind until it splits: crew continue on fragments still inside the orange box.
- Fragments that leave the padded volume are ignored.
- Clear removes the zone; new mark replaces it.
