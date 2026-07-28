# Repair Mission Teleport Home Design

Date: 2026-07-28  
Status: Approved for planning

## Problem

Construction (Damage Control) crews returning from a repair mission fly to Exit and walk the path home. On Recall or when the ship starts moving, that scenic return is slow and can leave the bot stranded relative to a moving grid.

## Goals

- On any mission return, teleport the crew immediately to their station and end the mission.
- Cover Recall, ship-move abort, and normal completion (job done / no comps / no targets).
- Keep outbound walk → Exit → EVA → weld theater unchanged.
- Preserve stockpile refund and existing mission cooldown.

## Non-goals

- Config toggle for walk vs teleport return.
- Changing path tool, weld logistics, or Send/dispatch rules.
- Forced cockpit `AttachPilot` for crew stations.
- Scenic return theater on mission end.

## Approach

**Instant home (Approach A):** Rewrite `BeginReturn` so every return path snaps home and finishes. Stop using `ReturnExit` / `WalkHome` for real returns.

## Behavior

Any call that today enters return via `BeginReturn`:

1. Clear weld target, VFX, and fly dynamics.
2. Refund construction stockpile to grid cargo (existing helper).
3. If a character body exists: teleport to station offset (`seat + Right * 1.2`), match seat facing, sync velocity to grid, disable jetpack.
4. If no body (ambient despawned): skip teleport.
5. `FinishMission` with cooldown so ambient resume owns the crew at the station.

## Implementation

- Primary file: `CrewRepairMission.cs`.
- Replace `BeginReturn` body with immediate home (or extract `TeleportHomeAndFinish` and call it from `BeginReturn`).
- Reuse existing `SetPosition` / grid-velocity / `SetCharacterJetpack(..., false)` patterns.
- `RecallCrew` and the ship-move abort keep calling `BeginReturn` — no UI or network packet changes.
- After finish, HUD Recall/Send flips back to Send because the crew is no longer on mission.
- `ReturnExit` / `WalkHome` tick paths become unused for production returns; remove or leave inert as an implementation detail of the plan.

## Edge cases

| Case | Behavior |
|---|---|
| Body present | Snap to station, finish |
| Body despawned | Finish only; ambient respawn at station |
| Seat missing / closed | Finish mission; no teleport crash |
| Cockpit vs crew station | Same offset pose; no forced `AttachPilot` |
| Mid-weld | Clear target + refund stockpile, then home |

## Testing

- Optional: extract pure home-pose-from-seat helper for a small unit test.
- Manual: Recall mid-EVA; accelerate ship mid-weld; complete a short job — all snap to station with no fly/walk home.
