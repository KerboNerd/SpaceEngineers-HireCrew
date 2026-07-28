# Damage Control Manual Dispatch Design

Date: 2026-07-28  
Status: Approved for planning

## Problem

Damage Control auto-sorties on a fast damage scan (~0.35s) with a short post-sortie cooldown (~2.5s). That feels always-on: noisy theater and overpowered free hull repair.

## Goals

- Player **manually** dispatches Damage Control from the Crew HUD.
- One **Send** launches **all** eligible Damage Control on that crew’s grid (batch).
- After they clear what they can and return home, they **stay idle** until the next Send.
- Same HUD control becomes **Recall** while any Damage Control on that grid is mid-sortie.
- Keep existing EVA / weld / path / local-mode mission behavior once a sortie is running.

## Non-goals

- Auto-repair mode toggle (v1 is manual-only).
- Terminal / chat dispatch (HUD only for v1).
- Changing weld speed, component rules, or path painting.
- Per-crew selective Send (batch is always grid-scoped).

## Approach

**Explicit Dispatch / Recall (network message).**  
Remove automatic mission starts from the scan loop. HUD sends a server request with `GridEntityId` + `Recall` flag. Server starts or recalls missions; mid-sortie target chaining stays as today until clear / abort / return.

## Player loop

1. Hire and seat Damage Control; optionally paint a repair path (unchanged).
2. Open Crew HUD → select a Damage Control crew on the target grid.
3. Press **Send** → all eligible DC on that grid sortie and repair until no useful work / out of comps / return.
4. They walk/fly home and resume ambient idle; **no** auto re-dispatch.
5. Press **Send** again later if new damage appears.
6. Press **Recall** while any are out → all missions on that grid begin return; button returns to **Send** when idle.

## Systems

| Piece | Responsibility |
|---|---|
| Crew HUD Home actions | Show Send / Recall for selected Damage Control; fire client request |
| `RepairDispatchMsg` | Client → server `{ GridEntityId, Recall }` |
| `CrewRepairMission` | `DispatchGrid` / `RecallGrid`; stop auto `TryStartMissions` |
| Notify | Feedback: nothing to repair, grid busy/moving, recalled, etc. |

### Networking

- New ushort message id (next free after `PathEditMsg`).
- Proto request: `long GridEntityId`, `bool Recall`.
- Auth: same ownership / faction rules as other HUD crew actions for that grid’s crew.
- Dedicated server: only server applies; SP: session handler path unchanged.

### Mission start (Send)

For each seated Damage Control on `GridEntityId` that is not already on a mission:

- Grid exists and passes idle guard (`CrewAmbientPresence.IsGridIdle`).
- Parallel cap still honored (`RepairMaxParallelPerGrid`; `0` = unlimited).
- At least one work target exists for that crew (repair or projector hologram, existing pick logic).
- Path-ready → walk-out start; else local EVA start (existing).

If **zero** crew launch: notify owner “nothing to repair” (or “no Damage Control ready”) and do not change state.

Short post-return cooldown may still reject immediate spam-Send; notify on reject.

### Mission end / Recall

- Natural end: clear what they can → return → `ClearMissionForCrew` → idle until next Send. **Do not** call auto-start from scan.
- Recall: for every active mission on that grid, `BeginReturn` (or equivalent). Clear any “authorized sortie” flag if one is introduced; v1 needs none beyond “no auto-start”.
- While any mission exists on the grid for Damage Control, HUD shows **Recall**; otherwise **Send**.

### HUD

- Home screen, when selected crew is Damage Control and stationed: action button **Send** / **Recall**.
- Prefer a dedicated bottom action (alongside Train / Dismiss) rather than overloading Train.
- Disabled when: not Damage Control, unassigned / not seated, or (for Send) grid fails idle guard.
- Amenity / role hint: “Manual EVA repair — Send from HUD”.

### Scan loop change

- Keep lightweight damage/mission ticking for **active** missions only.
- Remove or no-op the periodic `TryStartMissions` auto-launch path (or gate it behind a dead flag so dead code is not called).

## Edge cases

| Case | Behavior |
|---|---|
| Send, no damage | Notify; no launches |
| Send, grid thrusting hard | Reject via idle guard; notify |
| Some DC already out | Show Recall, not Send |
| Despawn / far player | Mission logic continues; Recall still server-side |
| Mid-sortie new damage | Already-out welders may acquire next targets until clear; no new auto launches of idle welders |
| Parallel cap | Launch up to cap; extras stay idle |

## Success criteria

- No Damage Control leaves station without a HUD Send (except continuing an already-started mission after load if persisted — see below).
- One Send can launch multiple DC on the same grid.
- Recall brings the batch home.
- After a completed sortie, silence until the next Send.

## Persistence note

If missions are already snapshot-persisted across save, leave that alone: a sortie in progress may resume after load without a new Send. Idle crew must not auto-start after load.

## Out of scope follow-ups

- Optional slow auto mode.
- Per-crew Send.
- Terminal controls / toolbar bind.
