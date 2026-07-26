# Hire Crew — Crew Training Design

**Date:** 2026-07-26  
**Mod path:** `C:\Users\user\AppData\Roaming\SpaceEngineers\Mods\HireCrew`  
**Status:** Approved for implementation planning  
**Depends on:** Existing star rating (0–5), Crew Station RichHUD, `CrewEconomy`, roster sync / unassign cleanup

## Summary

Players can raise an existing crew member’s **stars** by paying credits at the Crew Station HUD and waiting out a training timer. While training, the crew is auto-unseated and unassigned. Completion grants exactly **+1 star**. No refunds on cancel, dismiss, or loss.

## Goals

- Post-hire progression along the existing 0–5 star ladder
- Active, paid training started from the Crew Station HUD
- Timed sessions during which the crew is unavailable for duty
- Steeper cost and duration for each higher star step
- Server-authoritative timers and charges (dedicated MP safe)

## Non-goals

- Separate skill trees or XP bars inside a star
- Passive on-duty experience
- New training block / physical training seat
- Refunds of any kind
- Multi-star queues in one payment (each session is +1 star only)
- Changing hire-desk candidate generation

## Player loop

1. Open Crew Station HUD; select a crew member with `Stars < 5` who is not already training.
2. Press **Train** → confirm screen shows current → next stars, credit cost, and duration.
3. Confirm → server charges credits; if seated, **auto-unseat** and **clear** seat, weapon, and amenity assignments; set training end time.
4. Roster shows training countdown; Assign / Unassign / Quarters disabled for that crew; Dismiss still allowed.
5. When the timer elapses on the server, `Stars += 1` and training clears; player may reassign at the new rating.

## Approach

**Roster-embedded training** (chosen over a separate job store or block-bound session):

- Persist absolute end time on `CrewRecord` (same pattern as hire-pool `NextRefreshUtcTicks`).
- One timer per crew; completion is always current stars + 1 (clamped at max).
- Minimal new persistence/sync surface; fits existing `CrewStore` roster broadcast.

## Data model

### `CrewRecord` additions

| Field | Type | Meaning |
|-------|------|---------|
| `TrainingEndsUtcTicks` | `long` | UTC end ticks; `0` = not training |

No separate target-star field: completion always applies `Stars = ClampStars(Stars + 1)`.

### Status

Keep `CrewStatus` as `Unassigned` | `Seated`. Training is orthogonal: `TrainingEndsUtcTicks > 0` means In Training. After a successful train start, status must be `Unassigned` with seat/weapon/character/amenities cleared.

### Config (`CrewConfig`)

Arrays indexed by **current** stars for the upgrade step (length 5: steps 0→1 … 4→5):

- `TrainCostByStars[5]` — credit cost (steeper each step)
- `TrainMinutesByStars[5]` — duration in minutes (steeper each step)

Helpers:

- `GetTrainCost(int stars)` / `GetTrainMinutes(int stars)` — valid for `0..4`; reject callers when `stars >= MaxStars`
- `IsTraining(CrewRecord)` → `TrainingEndsUtcTicks > 0`

### Starter balance (tunable)

| From → To | Cost | Duration |
|-----------|------|----------|
| 0 → 1 | 8,000 | 5 min |
| 1 → 2 | 20,000 | 10 min |
| 2 → 3 | 40,000 | 20 min |
| 3 → 4 | 75,000 | 40 min |
| 4 → 5 | 130,000 | 60 min |

Rationale: each step is somewhat cheaper than buying that star at the hire desk, but time makes hire-high vs train-up a real tradeoff.

## Networking & server flow

### New messages

- `TrainRequest { CrewId }`
- `CancelTrainRequest { CrewId }` — clears timer only; no refund; no star change

### Start training (`HandleTrain`)

1. Resolve crew; reject if missing.
2. Validate requester can manage that crew’s owner key / grid (same rules as assign/dismiss).
3. Reject if already training (`TrainingEndsUtcTicks > 0`).
4. Reject if `Stars >= MaxStars`.
5. Compute cost/minutes from current stars.
6. `CrewEconomy.TryCharge` — on failure, stop (no state change).
7. Run existing unassign/cleanup: clear seat, weapon, amenities, despawn/logical character, WeaponCore AI off as today; set `Status = Unassigned`.
8. Set `TrainingEndsUtcTicks = UtcNow + TrainMinutes`.
9. Persist + roster sync; notify success.

### Completion

On session update and after load/rekey:

- If `TrainingEndsUtcTicks > 0` and `UtcNow >= TrainingEndsUtcTicks`:
  - Clear `TrainingEndsUtcTicks` to `0`
  - `Stars = ClampStars(Stars + 1)`
  - Sync roster; notify “Training complete” (or equivalent) to relevant clients

Client must not apply the star locally; server is source of truth.

### Cancel / dismiss / loss

| Event | Stars | Timer | Credits |
|-------|-------|-------|---------|
| Cancel Training | unchanged | cleared | no refund |
| Dismiss while training | record deleted | — | no refund |
| Ownership/grid cleanup / death-equivalent | record removed or cleared per existing rules | cleared with record | no refund |

### Gates while training

- Assign / amenity assign / unassign-as-duty: rejected if training
- Dismiss: allowed
- Train again: rejected until complete or cancelled

## Crew Station HUD

### Home

- Add **Train** action alongside Assign / Unassign / Quarters / Dismiss (adjust layout if the bottom bar is crowded; same `CrewHudButton` style).
- **Train** enabled when selection exists, `Stars < 5`, and not training.
- While selected crew is training: disable Assign / Unassign / Quarters; keep Dismiss; **Train** label switches to **Cancel Training** and opens a confirm that calls `ClientRequestCancelTrain`.

### Confirm Train screen

Reuse lightweight wizard/confirm pattern:

- Title: `Train {DisplayName}`
- Body: stars current → next, formatted cost, formatted duration
- **Confirm** / **Back**
- Confirm → `ClientRequestTrain(crewId)`

### Roster presentation

- While training: detail like `Training — {remaining}` derived from `TrainingEndsUtcTicks`
- Stars icons show **current** (pre-upgrade) stars until server completion

### Errors

Surface via existing Notify / chat path:

- Insufficient credits
- Already training
- Max stars
- No permission / crew missing
- Cannot assign while training

## Components

| Component | Responsibility |
|-----------|----------------|
| `CrewModels` | `TrainingEndsUtcTicks`; `TrainRequest`; `CancelTrainRequest` |
| `CrewConfig` | Cost/duration arrays + getters |
| `CrewValidation` | Pure train-gate helpers where testable |
| `CrewSession` | Charge, unassign-on-train, timer set/complete/cancel, sync |
| `CrewNetworking` | New message id(s) |
| `CrewHudModel` / `CrewHudWindow` | Train/cancel UX, enable rules, countdown label |
| Tests | Config getters, validation gates, completion tick logic |

## Edge cases

| Case | Behavior |
|------|----------|
| Reload / reconnect mid-training | Timer continues from absolute UTC end; complete on next server check after due |
| Charge fails | No unseat, no timer |
| Already at 5 stars | UI disabled; server rejects |
| SP / listen / dedicated | Host/server owns completion; same RPC path as hire |
| Multiple clients | Roster sync shows shared training state |

## Testing

**Unit (preferred where logic is pure):**

- `GetTrainCost` / `GetTrainMinutes` for stars 0–4; reject/clamp at 5
- Train validation: already training, max stars
- Completion helper: past-due ticks → +1 star and clear timer; not-due → unchanged

**Manual / in-game:**

- Train seated gunner/engineer → unseated, assignment cleared, AI/buff off
- Insufficient funds → no state change
- Wait out timer (or debug shorten minutes) → star increases
- Cancel / dismiss mid-training → no refund, no star (dismiss deletes record)
- Assign blocked while training

## Out of scope follow-ups

- Training queues (multi-star in one order)
- Partial progress within a star
- Faction bank vs personal balance split (use current hire charge path)
- Visual “in academy” NPC pose (no character while unassigned remains the rule)
