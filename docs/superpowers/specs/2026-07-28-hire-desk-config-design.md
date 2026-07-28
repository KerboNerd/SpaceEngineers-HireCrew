# Hire Desk Configurability Design

Date: 2026-07-28  
Status: Approved for planning

## Problem

Hire desks only expose refresh interval and price multiplier. Candidate count, star distribution, roles, base prices, and refill behavior are hardcoded in `CrewConfig`. Server admins cannot tune economy/limits without recompiling, and players cannot specialize desks (e.g. gunner-only, elite bias).

## Goals

- World-level XML config for hire defaults and hard limits.
- Per-desk overrides via terminal for pool shape, economy multiplier, refresh, refill, and manual reroll.
- Server authority: clamp desk values to world limits; sync via existing hire-pool path.
- Anyone with terminal access to the block may change desk settings.

## Non-goals

- Automated tests for this feature (explicitly deferred).
- Per-desk custom star weight tables (six sliders); use Low / Balanced / High bias instead.
- Runtime XML reload chat command (load on world start only for v1).
- Changing hire UI layout beyond reflecting new pool fields / status text.
- Permission model beyond “terminal access.”

## Architecture

Two layers, server-authoritative:

1. **World config** — `HireCrewConfig.xml` in world storage. Loaded on session start; if missing or invalid, log once, use compile-time defaults, and write a fresh default file.
2. **Per-desk overrides** — stored on `HireBlockPool` (persisted + synced). Terminal changes go to server; server clamps and applies.
3. **Generation** — `CrewHireGenerator` uses effective settings = desk override resolved against world defaults/limits.

```
Terminal / Open Desk
        |
        v
ClientRequestHireDeskSettings (full payload + ForceReroll)
        |
        v
Server: validate block + access, clamp vs world XML
        |
        +-- pool-shape / ForceReroll --> RefreshPool (immediate)
        +-- price mult only ----------> ApplyMultiplierToPool
        +-- refresh minutes only -----> update interval / next tick
        |
        v
Broadcast HirePoolSync
```

## World config schema

File: world storage `HireCrewConfig.xml` (server).

Fields (defaults match current `CrewConfig` constants):

| Field | Role |
|-------|------|
| RefreshMinutesMin / Max / Default | Interval clamps + new-desk default |
| PriceMultiplierPercentMin / Max / Default | Multiplier clamps + default |
| MinCandidates / MaxCandidates | Global candidate count bounds |
| PriceByStars[6] | Base hire prices by star |
| PriceVarianceFraction | Per-candidate price noise |
| StarWeights[6] | Default weight table (Balanced) |
| AllowedRolesMask | Roles that may ever appear |
| RefillOnHireDefault | Default for new desks |

Invalid arrays (wrong length / non-positive weights) fall back to compile-time defaults for that field only when possible; total file failure uses full defaults.

## Per-desk overrides

Extend `HireBlockPool`:

| Field | Notes |
|-------|--------|
| RefreshMinutes | Existing |
| PriceMultiplierPercent | Existing |
| MinCandidates / MaxCandidates | Clamped to world min/max; min ≤ max |
| AllowedRoles | Bitmask; subset of world mask; empty → first world-allowed role |
| StarBias | Enum: Low / Balanced / High |
| RefillOnHire | Bool |

**Star bias mapping:** derive three weight presets from world `StarWeights` (shift mass toward low or high stars; Balanced = world table as-is). Exact numeric mapping is an implementation detail but must be deterministic and keep all weights ≥ 0 with sum > 0.

**Binary pool format:** bump version; old saves default to Balanced, all world-allowed roles, refill off, and world min/max candidates.

## Apply rules

Settings arrive as one server request. After writing clamped fields, choose **one** apply path (priority order):

1. If `ForceReroll` **or** any pool-shape field changed (roles, candidate min/max, star bias) → immediate `RefreshPool`
2. Else if price multiplier changed → rescale current candidate prices
3. Else if refresh minutes changed → update interval / next refresh only
4. Else → no pool mutation (e.g. refill flag alone)

| Hire behavior | Effect |
|---------------|--------|
| Hire with RefillOnHire | Replace taken slot with one new candidate under same desk rules |
| Hire without refill | Leave gap until next refresh |

Runtime world defaults/limits live in a session-held config object (evolve `CrewConfig` or add `HireWorldConfig`) populated from XML at load; generators and clamps read that, not compile-time constants alone.

## Terminal UI

Hire desk only (`HC_CrewHireDesk`):

- Keep: Open Hiring Desk, Pool refresh (minutes), Price multiplier
- Add: Min candidates, Max candidates
- Add: Star bias listbox (Low / Balanced / High)
- Add: Role checkboxes (Gunner, Reactor Tech, Helmsman, Propulsion Tech, Quartermaster) — disabled if role not in world mask
- Add: Refill on hire on/off
- Add: Reroll pool now (button → ForceReroll)
- Custom info: candidate count, refresh, price mult, bias, roles summary, refill

## Networking

Extend settings request (evolve `HireRefreshRequest` or replace with `HireDeskSettingsRequest`) to carry:

- BlockEntityId
- RefreshMinutes
- PriceMultiplierPercent
- MinCandidates / MaxCandidates
- AllowedRoles
- StarBias
- RefillOnHire
- ForceReroll

Server ignores client clamps; re-clamps against live world config. Same broadcast path as today (`HirePoolSync`).

## Error handling

- Missing/bad XML → defaults + rewrite default file; one log line.
- Out-of-range desk values → silent clamp.
- Empty role mask after clamp → first world-allowed role.
- Missing hire desk / no access → existing notify / no-op patterns.

## Out of scope follow-ups

- `/hirecrew reloadconfig` (or similar) for live XML reload.
- RichHud controls mirroring terminal knobs.
- Per-star custom weight editing on the desk.
