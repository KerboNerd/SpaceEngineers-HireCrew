# Damage Control EVA Repair Design

Date: 2026-07-28  
Status: Approved for planning

## Problem

Ambient crew presence is theatrical but not functional for hull repair. Players want a specialized crewman who can leave the ship via a player-defined route, EVA around the hull, and weld damage using components from the ship’s conveyor network — with the action visible when the player is nearby.

## Goals

- Add a new hireable **Damage Control** role (separate from Engineer / Reactor Tech).
- Let the player paint an **ordered waypoint path** on the grid with a **custom path tool** (click blocks); mark an **Exit** at the airlock.
- Store **one shared path per grid** used by any stationed Damage Control crew.
- On grid damage (damaged or incomplete blocks), **auto-sortie** one Damage Control crew: walk path → Exit → jetpack EVA → scripted weld → pull conveyor-linked components → return.
- Integrate with existing ambient character spawn/despawn so theater works when the player is near; mission logic continues when far.

## Non-goals (v1)

- Real welder tool entity / perfect vanilla weld animations.
- Full navmesh pathfinding (waypoints only).
- Combat AI or shooting while EVA.
- Interior-only repair missions (hull EVA is the v1 fantasy).
- Multiple simultaneous EVAs on one grid.
- Auto pathfinding without a player-painted route.
- Changing Engineer’s reactor power job.

## Approach

**Lightweight theater (Approach A):** HireCrew drives the ambient bot along stored waypoints, enables jetpack for exterior flight, and applies integrity + component consumption via script. No dependency on AiEnabled repair bots.

## Player fantasy & loop

### Setup (once per ship)

1. Equip the HireCrew **path tool**.
2. Click blocks in order from the crew area toward the airlock.
3. Finish by marking the last point as **Exit**.
4. Path persists for that `GridEntityId` and is shared by all Damage Control crew on the ship.

### Runtime

1. Preconditions: completed path with Exit; stationed Damage Control crew; damaged/incomplete blocks on the grid; grid roughly idle (same class of guard as ambient spawn).
2. One welder at a time enters a repair mission; others stay ambient.
3. Walk ordered waypoints → Exit → jetpack EVA → hover near a damage target → weld (scripted) while consuming required comps from conveyor-linked inventories.
4. When done, out of useful comps, or no remaining targets → return via Exit → reverse/walk path home → resume ambient station behavior.

## Systems & data

| Piece | Responsibility |
|---|---|
| `CrewRole.DamageControl` | New role; hire UI / world config mask; no reactor buff |
| Path tool | Click append waypoint; finish = Exit; undo; clear path; ownership-gated |
| Grid path store | Persisted ordered waypoints + Exit for `GridEntityId` |
| Repair mission AI | State machine layered on ambient presence |
| Weld + logistics | Target slim blocks; pull comps; apply integrity over time + light VFX |

### Persistence (conceptual)

- Per-grid path: ordered list of waypoint references (prefer block `EntityId` when stable; world/local positions as fallback) + which index is Exit.
- Optional mission snapshot: which crew is on sortie, state enum, current waypoint/target — enough to resume after save/despawn.

Exact protobuf field numbers are an implementation detail for the plan.

### Ambient integration

- Ambient spawn/despawn still owns the character body when the player is nearby.
- During a mission, ambient wander is paused; the mission drives movement.
- Far from player: mission advances logically; when the body respawns, snap to the current mission pose (interior waypoint, EVA, or station).
- Shared idle-grid guard: do not start EVA / abort return if the ship is moving hard.

## Path tool UX

- Available as a HireCrew handheld / custom interaction tool.
- **Primary:** click a block → append waypoint (brief highlight / number).
- **Finish action:** mark Exit and save the grid path.
- **Undo** last waypoint; **Clear** path for this grid.
- Only on grids the player may modify (align with existing crew ownership rules).
- Phase 2: faint lines between waypoints while the tool is equipped.

### Path validity

- No Exit → no sorties.
- Missing/destroyed waypoint blocks → invalidate or skip; log/HUD once.
- Exit near a door → attempt open; if stuck, abort with a clear reason.

## Mission edge cases

| Case | Behavior |
|---|---|
| No components | Stop that block; try another or return + cooldown |
| Target repaired by player / gone | Pick next or return |
| Ambient despawn mid-mission | Mission continues; respawn snaps to mission pose |
| Character lost/dead | Clear mission; later respawn at station if still hired |
| Multiple Damage Control | One EVA; others wait |
| Ship starts moving | Abort / return (same spirit as ambient idle guard) |

**Stars:** higher stars → faster weld rate and faster EVA flight speed (same 0.75×–1.25× curve; optionally prefer nearer/more important targets). Not a separate progression system.

## State machine (v1)

`Idle → WalkOut → AtExit → EvaTransit → Welding → ReturnExit → WalkHome → Idle`

- `Idle`: ambient wander / stationed; may transition to `WalkOut` when damage + path + idle grid.
- `WalkOut` / `WalkHome`: follow waypoint list (forward / reverse).
- `AtExit` / `ReturnExit`: door assist, jetpack on/off, transition interior ↔ exterior.
- `EvaTransit`: fly toward chosen damage target; stay near hull.
- `Welding`: apply integrity ticks + consume comps; pick next target or return.

## Phasing

### Phase 1 (ship first)

- Damage Control role + hire / allow-role UI wiring.
- Path tool (ordered waypoints + Exit) + per-grid persistence.
- Mission AI: walk → Exit → EVA → scripted weld + conveyor comps → return.
- Ambient integration, idle-grid guards, one EVA per grid.

### Phase 2 (polish)

- Path ghost lines / numbered highlights.
- Smarter target priority (functional blocks, nearest hull).
- Door open assist polish; abort reasons in HUD.
- Star-scaled weld speed tuning.

## Success criteria

1. Player paints a path to an airlock Exit on a ship.
2. Player damages hull blocks and stocks conveyor cargo with needed comps.
3. A stationed Damage Control crew walks the path, exits, flies to damage, welds visibly (when nearby), consumes comps, and returns to station.
4. Without a completed path, or while the grid is moving hard, no EVA sortie starts.
5. Engineer behavior unchanged (reactor power only).

## Open implementation notes (not design blockers)

- Exact tool delivery (G-menu item vs block use-object) chosen in the plan for SE API practicality.
- Weld rate tables and component pull helpers follow vanilla repair cost APIs where available.
- Display name: **Damage Control** (short label); hire desk copy can say “Damage Control / Welder”.
