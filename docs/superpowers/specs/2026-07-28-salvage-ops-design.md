# Salvage Ops Design

Date: 2026-07-28  
Status: Approved for planning

## Problem

Construction (`CrewRole.DamageControl`) can EVA and weld grids. Players also want a hireable crew who do the reverse: manually dispatched salvage of own/faction/unowned grids, depositing recovered components into the home ship.

## Goals

- Add hireable **Salvage Ops** role (separate from Construction / Reactor Tech).
- Player **manually** dispatches one Salvage Ops crew at a time from the Crew HUD.
- Player **picks a target grid** (own-grid cleanup or nearby wreck) before Send.
- Crew EVA (no painted path), grind blocks, deposit components into **home-ship physical-group cargo**.
- Cargo full, target clear, or Recall → teleport home and idle until next dispatch.
- Keep Construction / `CrewRepairMission` behavior unchanged.

## Non-goals (v1)

- Path painting for salvage.
- Auto-dispatch / damage-scan starts.
- Real grinder tool entity or perfect grind animations.
- Combat AI while EVA.
- Floating-item component drops.
- Grind filters (armor-only, etc.) — grind the whole legal target grid.
- Refactoring Construction into a shared EVA framework (Approach C deferred).

## Approach

**Parallel `CrewSalvageMission` (Approach B).**  
New role + new mission module mirroring Construction’s EVA theater and teleport-home return, but with grind logistics, a target-grid field, and no path states. Share only small existing helpers (idle guard, ambient mission pose hooks) where cheap; do not grow `CrewRepairMission` with salvage branches.

## Player fantasy & loop

1. Hire and seat Salvage Ops on the home ship (ambient station rules same as Construction).
2. Open Crew HUD → on that crew’s row press **Salvage**.
3. Pick a **target grid** from the nearby list (own, faction, unowned/NPC; never enemy). Home grid may be chosen for cleanup.
4. Crew EVA’s out (no path) → flies to the target → grinds → deposits comps into home cargo.
5. When the target is clear, cargo is full, or player **Recall**s → teleport home; resume ambient idle until next dispatch.

## Systems & data

| Piece | Responsibility |
|---|---|
| `CrewRole.SalvageOps` | New role; hire UI / world mask; label **Salvage Ops**; no ship buffs |
| `CrewSalvageMission` | State machine + grind ticks + cargo deposit + teleport home |
| Target grid picker | Client HUD list of nearby legal grids; Send carries `TargetGridEntityId` |
| `SalvageDispatchMsg` | Client → server: `CrewId`, `Recall`, `TargetGridEntityId` (ignored on Recall) |
| Grind + logistics | Scripted grind on slim blocks; push recovered comps into home physical-group inventories |
| Status sidebar | Active salvage sorties (grinding / cargo full / returning) |

### Role wiring

- `CrewRole.SalvageOps = 6`; `CrewConfig.MaxRole` updated (world `AllRolesMask` auto-includes).
- Hire desk checkbox: **Allow Salvage Ops**.
- Admin parse tokens: `salvage`, `salv`, `grinder`.
- FormatRoles letter: e.g. `S`.
- `NeedsWeapon` false; no power/gyro/thrust/QM effects.
- Ambient presence / mission body / damage-immunity during sortie: same class of rules as Construction.

### Networking

- New ushort message id (next free after existing HireCrew messages).
- Proto request: `string CrewId`, `bool Recall`, `long TargetGridEntityId`.
- Auth: manage permission on the crew’s home grid (same as other HUD crew actions).
- Server re-validates target legality and range on Send; rejects enemy / out-of-range / missing grids.

### Persistence

- Mission runtime is in-memory like Construction; optional snapshot for sidebar sync if Construction already snapshots missions.
- No new per-grid path store.
- Persist crew role via existing `CrewRecord.Role` int.

## State machine (v1)

`Idle → EvaTransit → Grinding → (teleport home) → Idle`

- **Idle:** ambient / stationed; waits for manual Salvage.
- **EvaTransit:** jetpack fly from home toward chosen block on `TargetGridEntityId`.
- **Grinding:** apply grind ticks within ~5 m; deposit comps; pick next block or return.
- **Return:** always teleport home (mirror Construction `BeginReturn` / `TeleportHome`); no WalkHome / path reverse.

## Targeting

### Picker (client)

- Scan radius: fixed compile-time constant near home ship (default **2000 m**).
- Include: grids owned by the player, same faction, or unowned/NPC.
- Exclude: enemy-owned grids.
- Home grid listed first when present.
- Show display name + distance.

### Server legality (Send)

Target must still exist, be within scan radius of the home grid, and pass the same ownership rules. Enemy or invalid → notify and do not start.

### Block selection

- Prefer nearer / exterior-accessible blocks (same spirit as Construction target pick).
- Fully ground blocks are removed.
- Stars scale grind rate and EVA flight speed with the same ~0.75×–1.25× curve as Construction weld/EVA.

## Logistics

- Recovered components go into inventories on the **home** physical grid group (connectors / landing gear / rotors etc.), using the same ownership/faction filter Construction uses when pulling comps.
- No floating-item drops in v1.
- **Cargo full:** stop grinding, teleport home, notify owner (e.g. “cargo full”).
- Target gone / no grindable blocks left: teleport home.

## Edge cases

| Case | Behavior |
|---|---|
| Cargo full | Teleport home + notify |
| Target grid gone / empty | Teleport home |
| Home ship moving hard | Abort / teleport home (idle guard) |
| Ambient despawn mid-sortie | Mission continues; respawn snaps to EVA pose |
| Character lost mid-sortie | Keep hire; respawn at mission pose (Construction theater rules) |
| Enemy target | Server rejects dispatch |
| Multiple Salvage Ops | Per-crew Send/Recall; parallel default unlimited |
| Recall | Instant teleport home for that crew only |

## HUD

- Roster row control: **Salvage** when idle, **Recall** when that crew is on a salvage mission.
- **Salvage** opens target-grid picker; confirm sends `SalvageDispatchMsg` with `TargetGridEntityId`.
- Status sidebar lists active salvage sorties with short status text.
- Role color distinct from Construction (e.g. muted orange vs Construction yellow).
- Amenity / role hint: “Manual EVA salvage — pick a grid from HUD”.

## Isolation from Construction

- Do not add grind branches inside `CrewRepairMission`.
- Salvage does not read or require `RepairPaths`.
- Construction Send/Recall / weld / path tooling unchanged.
- Shared helpers only if already extracted or trivially shareable (e.g. `IsGridIdle`); prefer copy-small over couple-large.

## Testing

- Unit (pure logic): role label / clamp / admin parse tokens; legal-target helper if extracted without SE API.
- In-game (manual): hire Salvage Ops → pick wreck → comps appear in home cargo → cargo-full returns → Recall mid-sortie; enemy grid rejected; Construction still welds as before.

## Phasing

### Phase 1 (this feature)

- Role + hire / allow-role / admin tokens.
- Target picker + per-crew Salvage/Recall dispatch.
- `CrewSalvageMission`: EVA → grind → deposit → teleport home.
- Sidebar + basic notifies.

### Phase 2 (later, out of scope)

- Grind filters / priority (e.g. skip reactors).
- Shared EVA framework extraction with Construction.
- Optional path theater for salvage.
- Real grinder VFX polish.
