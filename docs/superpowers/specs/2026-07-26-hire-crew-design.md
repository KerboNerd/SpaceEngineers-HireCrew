# Hire Crew — Design Spec

**Date:** 2026-07-26  
**Mod path:** `C:\Users\user\AppData\Roaming\SpaceEngineers\Mods\HireCrew`  
**Status:** Approved for implementation planning

## Summary

Hire Crew is a Space Engineers ModAPI mod (WeaponCore-dependent) that lets grid owners/faction members pay credits to hire tiered NPC gunners. Each gunner sits in a vanilla cockpit/seat, is assigned to exactly one WeaponCore weapon, and enables that weapon’s AI only while alive and seated. Dedicated multiplayer is a day-one requirement; the server owns all crew state.

## Goals

- Visible NPC characters seated at gunner posts
- Credit-based hiring at a Crew Terminal
- One engineer ↔ one seat ↔ one WeaponCore weapon
- Weapon AI off unless manned by an assigned living engineer
- Permanent loss on engineer death or seat destruction
- Owner/faction-only hire and assign on dedicated servers

## Non-goals (v1)

- Wages over time
- Non-gunner roles (pilots, engineers, etc.)
- Pathfinding between seats / walking the ship
- Blocking players from manually using manned weapons
- Decorative-only crew without real character entities
- Singleplayer-only shortcuts that break dedicated MP

## Player loop

1. Place a **Crew Terminal** on the grid.
2. Open terminal → hire a gunner tier (**Recruit / Regular / Elite**) for credits.
3. Assign hire to a **vanilla seat/cockpit** and **one WeaponCore weapon**.
4. NPC spawns, sits; WeaponCore AI turns **on** for that weapon only.
5. If the engineer dies or their seat is destroyed → crew record deleted, AI **off**; hire a replacement.

**Managed weapons:** A weapon becomes managed when assigned to a crew member. While managed, the mod forces AI off unless a living assigned engineer is seated. On unassign/dismiss/cleanup, AI is left **off** (v1 does not restore any previous AI state).

**Unassigned hires:** Hire creates a roster entry only. The NPC character spawns when assigned to a seat; there is no floating/unseated crew entity in v1.

## Architecture

Server owns truth. Clients show UI and send requests.

| Component | Responsibility |
|-----------|----------------|
| `CrewSession` | Session component: RPCs, validation, sync |
| `CrewStore` | Persistent crew records per grid |
| `CrewTerminal` | Custom block + terminal UI (hire / assign / dismiss) |
| `NpcSeater` | Spawn character, seat, watch death/unseat |
| `WeaponAiBridge` | WeaponCore API: AI off by default; on only when manned |

### Server validation rules

- Requester must own or faction-share the grid
- Seat and weapon must be on the same grid
- Weapon must not already be manned
- Seat must not already be occupied by another hire
- Exactly one engineer ↔ one weapon ↔ one seat

### Sync

- Crew roster and assignment state replicate to clients that can see the grid
- Character presence is the visual representation of a hire

## Data model

Each crew record (persisted in `CrewStore`):

- `CrewId` (stable id)
- `Tier` (`Recruit` | `Regular` | `Elite`)
- `GridEntityId`
- `SeatEntityId` (null if unassigned)
- `WeaponEntityId` (null if unassigned)
- `CharacterEntityId` (null if unassigned; runtime, re-resolved after load)
- `OwnerIdentityId` / faction context at hire time
- `Status` (`Unassigned` | `Seated`)

Death or invalidation deletes the record immediately (no `Dead` status persisted).

## Tiers and economy

Configurable prices (exact numbers set at implementation; not gameplay-locked in this spec):

| Tier | Intent |
|------|--------|
| Recruit | Cheap; weakest WC AI knobs if API allows |
| Regular | Mid price; default WC AI profile |
| Elite | Expensive; best available WC AI knobs |

If WeaponCore only exposes AI on/off, tiers still differ by **hire cost**. Richer AI stats are applied when the API supports them; otherwise document the limitation.

**Dismiss:** removes crew, despawns NPC, AI off. **No refund in v1.**

## WeaponCore contract

- Managed weapons: AI forced **off** until a living assigned engineer is seated
- Unassign / dismiss / death / seat gone / weapon gone → AI **off**, clean up record
- Player manual control of the same weapon is allowed in v1

## Edge cases

| Event | Behavior |
|-------|----------|
| Engineer dies | Delete crew record; despawn; AI off |
| Seat destroyed | Delete crew record; despawn NPC if present; AI off |
| Weapon destroyed | Delete crew record; despawn NPC; AI off (weapon gone) |
| Grid split | Keep crew only if seat and weapon remain on the same resulting grid; else dismiss + AI off |
| World restart / reconnect | Restore from `CrewStore`; respawn/reseat; re-apply AI |
| Insufficient credits | Reject hire; terminal message |
| No permission | Reject action; terminal message |

## UI (Crew Terminal)

Owner/faction only:

- **Hire** — pick tier → show price → confirm → deduct credits
- **Roster** — name/id, tier, seat, weapon, status
- **Assign** — unassigned crew → free seat → free WC weapon
- **Dismiss** — remove crew, despawn, AI off

### Data flow

Client UI → server RPC → validate → update `CrewStore` → seat/AI side effects → replicate roster.

## Testing (manual, dedicated server)

- Hire + assign → AI on for that weapon only
- Kill engineer → AI off, crew gone
- Destroy seat → AI off, crew gone
- Non-owner/non-faction denied
- Restart → crew restored, reseated, AI re-applied
- Two clients see the same roster

## Dependencies

- Space Engineers ModAPI (C# mod)
- WeaponCore (AI control API)
- Vanilla seats/cockpits for seating
- Economy / credits API for hire cost

## Open implementation notes

- Exact WeaponCore API surface for AI on/off and tier knobs to be confirmed against the installed WC version during implementation
- Character spawn/seat APIs and MP replication details to be verified in ModAPI for the target SE version
- Default tier prices left to config/constants at implementation time
