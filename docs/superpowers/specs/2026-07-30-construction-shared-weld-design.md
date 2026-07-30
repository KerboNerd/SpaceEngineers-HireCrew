# Construction Shared Weld (Large Blocks)

Date: 2026-07-30  
Status: Approved for planning

## Problem

Construction welders use exclusive cell claims, so each large multi-comp block (e.g. mod thrusters) is welded by only one crew at a time. Sorties on high-`MaxIntegrity` blocks are slow even when several Construction crew are EVA.

## Goals

- Allow up to **3** welders on a single **large** real block so mount ticks stack.
- Keep **one welder per small block** so the team still spreads across normal damage.
- If a large block is the **only** remaining pickable work, lift the cap so everyone can pile on.
- Prefer joining an under-full large sibling job over starting unrelated small work when slots remain.
- Projector **hologram place** stays exclusive (one placer); sharing applies after the block exists as a real incomplete cube.

## Non-goals

- Sharing on every armor/light block.
- Player-configurable thresholds in `HireCrewConfig.xml` (compile-time `CrewConfig` is enough).
- Changes to Salvage Ops grind claims.
- New HUD chrome beyond existing Welding status.

## Design (Approach A — soft claim slots)

### Constants (`CrewConfig`)

| Constant | Proposed default | Role |
|---|---|---|
| `RepairShareMaxIntegrity` | `5000f` | Block is “large” when `MaxIntegrity >=` this |
| `RepairShareMaxWelders` | `3` | Max concurrent claims on a large block when other work exists |

Tune against Epstein-class blocks in playtest; raise/lower threshold if too many/few blocks share.

### Claim rules

Replace binary `IsTargetClaimed` with a slot check:

1. Resolve the slim (or use cached work entry integrity when picking).
2. Count other missions on the same grid with the same cell / projector key (`CountClaimants`).
3. **Projected hologram** (`TargetIsProjected` / cache `Projected`): max claimants = **1** (unchanged).
4. **Real block, not large**: max = **1**.
5. **Real large block**, and work cache has **other** unclaimed/affordable targets for this crew: max = `RepairShareMaxWelders`.
6. **Real large block**, and it is the **only** remaining pickable work (no other affordable unclaimed/joinable cells): max = **unlimited** (all EVA Construction on that grid may join).

A cell is “full” when `claimants >= max` (self excluded). Full cells are skipped in `TryPickWorkTarget` the same way exclusive claims are today.

### Acquire / join preference

In `TryPickWorkTarget` (or a thin wrapper used by `TryAcquireNextTarget`):

1. Scan sibling missions for under-full **large real** targets the crew can afford and has not skipped.
2. Among those, pick nearest to the welder (same distance scoring as today).
3. If none, fall back to current scoring over work cache, treating full cells as claimed.

Idle welders therefore peel onto a big job until 3 are on it; extras keep clearing the rest of the ship.

### Hover / theater

When multiple welders share a cell, offset hover positions using existing crew-id hash slots (same idea as dispatch rally) so bodies do not occupy one point. Weld range (~5 m) must still cover the block.

### Weld ticks

No change to `TryWeldTick` beyond concurrent callers: each welder in range applies their star-scaled `IncreaseMountLevel` per frame. Stockpile feed remains per-tick; Keen mounts from the shared construction stockpile.

### Completion

When the block no longer `NeedsRepair`, each welder clears their target independently and re-acquires (join another large job or pick new work). No special “party leader” state.

## Files

- `Data/Scripts/HireCrew/CrewConfig.cs` — new constants
- `Data/Scripts/HireCrew/CrewRepairMission.cs` — claim counting, pick/join preference, hover offsets
- `.wiki/Damage-Control.md` — short note on shared large-block welding (on stop-hook / doc pass)

## Success criteria

- Three Construction on EVA with one large incomplete block + many small damaged blocks: at most 3 on the large block; others weld small blocks.
- Only the large block left: all welders may target it.
- Small blocks never have two welders at once.
- Projector place of a hologram remains single-claimer until the physical block exists.
- Manual smoke: Epstein-class block welds faster with 3 than with 1; small armor still fans out.

## Out of scope / follow-ups

- XML tuning for share threshold / max welders.
- Explicit “focus fire” player command.
