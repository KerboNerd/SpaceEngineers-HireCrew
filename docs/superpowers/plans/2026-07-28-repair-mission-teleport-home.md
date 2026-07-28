# Repair Mission Teleport Home Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** End every Damage Control repair return by instantly teleporting the crew to their station instead of flying/walking home.

**Architecture:** Rewrite `BeginReturn` in `CrewRepairMission` so Recall, ship-move abort, and normal completion all clear the weld target (refunding stockpile), snap any live character to the seat offset, disable jetpack, and `FinishMission`. Leave outbound WalkOut → Exit → EVA → Welding unchanged. Defensively finish if any residual `ReturnExit` / `WalkHome` state is still entered.

**Tech Stack:** Space Engineers ModAPI, existing `CrewRepairMission` / `CrewAmbientPresence` helpers.

**Spec:** `docs/superpowers/specs/2026-07-28-repair-mission-teleport-home-design.md`

## Global Constraints

- Instant home on **all** returns (Recall, ship move, job done / no comps / no targets).
- Teleport destination: station offset `seat.WorldMatrix.Translation + seat.WorldMatrix.Right * 1.2` (+ small Up), seat forward; no forced `AttachPilot`.
- Outbound mission theater unchanged.
- No UI / network / DTO changes.
- `HireCrew.Logic.Tests` cannot host `CrewRepairMission` (SE API); no new xunit coverage. Manual in-game verify.
- Do **not** run `dotnet` / `dotnet build` / `dotnet test`.
- Do not commit unless the user explicitly asks.
- Do not edit unrelated docs or RichHudFramework.

## File structure

| File | Role |
|------|------|
| `Data/Scripts/HireCrew/CrewRepairMission.cs` | Instant home in `BeginReturn`; defensive finish for dead return states |
| `docs/superpowers/specs/2026-07-28-repair-mission-teleport-home-design.md` | Approved behavior (already written) |

---

### Task 1: Instant home in `BeginReturn`

**Files:**
- Modify: `Data/Scripts/HireCrew/CrewRepairMission.cs` (`BeginReturn` ~1060–1078; add `TeleportHome` helper nearby)

**Interfaces:**
- Consumes: `ClearCurrentTarget`, `ClearFlyDynamics`, `FinishMission`, `TryGetCharacter`, `BindEvaPhysics`, `CrewAmbientPresence.SetCharacterJetpack`, `CrewAmbientPresence.StopCharacterMovement`, `CrewSession.Instance`
- Produces:
  - `BeginReturn(MissionRuntime m)` — clears mission and homes immediately (never sets `ReturnExit`)
  - `TeleportHome(IMyCharacter character, IMyTerminalBlock seat, IMyCubeGrid grid)` — private pose snap

- [x] **Step 1: Add `TeleportHome` helper** near `BeginReturn` (before or after it):

```csharp
        private static void TeleportHome(IMyCharacter character, IMyTerminalBlock seat, IMyCubeGrid grid)
        {
            if (character == null || character.Closed || seat == null)
                return;
            try
            {
                MatrixD wm = seat.WorldMatrix;
                Vector3D up = wm.Up;
                if (up.LengthSquared() < 0.01)
                    up = Vector3D.Up;
                up.Normalize();
                Vector3D pos = wm.Translation + wm.Right * 1.2 + up * 0.1;
                Vector3D forward = wm.Forward;
                if (forward.LengthSquared() < 0.01)
                    forward = Vector3D.Forward;
                forward.Normalize();
                character.WorldMatrix = MatrixD.CreateWorld(pos, forward, up);
                character.SetPosition(pos);
                CrewAmbientPresence.SetCharacterJetpack(character, false);
                CrewAmbientPresence.StopCharacterMovement(character, grid);
                BindEvaPhysics(character, grid);
            }
            catch { }
        }
```

- [x] **Step 2: Replace `BeginReturn` body** so it homes and finishes instead of entering `ReturnExit`:

```csharp
        private static void BeginReturn(MissionRuntime m)
        {
            if (m == null || string.IsNullOrEmpty(m.CrewId))
                return;

            IMyCubeGrid grid = null;
            IMyEntity gridEnt;
            if (m.GridEntityId != 0
                && MyAPIGateway.Entities.TryGetEntityById(m.GridEntityId, out gridEnt))
                grid = gridEnt as IMyCubeGrid;

            if (grid != null)
                ClearCurrentTarget(m, grid);
            else
                StopWeldParticles(m.CrewId);
            ClearFlyDynamics(m);

            IMyTerminalBlock seat = null;
            IMyCharacter character = null;
            var session = CrewSession.Instance;
            if (session != null && session.Store != null)
            {
                var crew = session.Store.Get(m.CrewId);
                if (crew != null)
                {
                    TryGetCharacter(crew, out character);
                    if (crew.SeatEntityId.HasValue)
                    {
                        IMyEntity seatEnt;
                        if (MyAPIGateway.Entities.TryGetEntityById(crew.SeatEntityId.Value, out seatEnt))
                            seat = seatEnt as IMyTerminalBlock;
                    }
                }
            }

            if (character != null && !character.Closed && seat != null && !seat.Closed)
                TeleportHome(character, seat, grid ?? seat.CubeGrid);

            Log("repair home teleport crew=" + m.CrewId);
            FinishMission(m);
        }
```

- [x] **Step 3: Confirm call sites still correct**

`rg "BeginReturn" Data/Scripts/HireCrew/CrewRepairMission.cs` — every caller (Recall, ship-move abort, no-comp, no-work, weld fail, etc.) should keep calling `BeginReturn`; none should manually set `ReturnExit` / `WalkHome` for a normal return.

Expected: `BeginReturn(` only as the return entry; no remaining `m.State = RepairMissionState.ReturnExit` assignment outside dead/defensive cleanup (Task 2 removes those).

- [ ] **Step 4: Manual smoke (in-game)** (your verify)

1. Send a Damage Control crew, Recall mid-EVA → body snaps to station; HUD shows Send again.
2. Send, then move the ship while welding → abort homes instantly (not fly to Exit).
3. Let a short job finish naturally → snaps home, cooldown applies as before.

---

### Task 2: Defensive cleanup of scenic return paths

**Files:**
- Modify: `Data/Scripts/HireCrew/CrewRepairMission.cs` (`AdvanceLogical` ReturnExit/WalkHome branches; `Tick` switch for ReturnExit/WalkHome; `TickReturnExit`)

**Interfaces:**
- Consumes: `FinishMission` from Task 1 behavior
- Produces: No live path remains that walks/flies home after a return; residual `ReturnExit` / `WalkHome` finish immediately

- [x] **Step 1: In `AdvanceLogical`, finish instead of staging WalkHome**

Replace the `EvaTransit || ReturnExit` block so `ReturnExit` finishes immediately (body-less):

```csharp
                if (m.State == RepairMissionState.EvaTransit || m.State == RepairMissionState.ReturnExit)
                {
                    if (m.State == RepairMissionState.ReturnExit)
                    {
                        FinishMission(m);
                        continue;
                    }
                    m.StateSeconds += dt;
                    if (m.StateSeconds > 2.0)
                    {
                        m.State = RepairMissionState.Welding;
                        m.StateSeconds = 0;
                    }
                    continue;
                }
```

Remove the WalkHome logical-progress branch body by finishing if somehow still in WalkHome at the bottom of `AdvanceLogical`:

Where:

```csharp
                else if (m.State == RepairMissionState.WalkHome)
                {
                    if (m.WaypointIndex > 0)
                        m.WaypointIndex--;
                    if (m.WaypointIndex <= 0)
                        FinishMission(m);
                }
```

Replace with:

```csharp
                else if (m.State == RepairMissionState.WalkHome)
                {
                    FinishMission(m);
                }
```

- [x] **Step 2: Make tick cases finish immediately**

In the main `Tick` switch:

```csharp
                    case RepairMissionState.ReturnExit:
                    case RepairMissionState.WalkHome:
                        FinishMission(m);
                        break;
```

Delete the private `TickReturnExit` method entirely (no remaining callers). Keep enum values `ReturnExit` / `WalkHome` so persisted/in-flight mission snapshots do not break; they just finish on next tick.

- [x] **Step 3: Update `TryGetMissionPose` ReturnExit handling (optional safety)**

In `TryGetMissionPose`, `ReturnExit` currently poses at exterior stand-off. After Task 1, missions should not stay in that state; leave as-is (harmless) or treat `ReturnExit` / `WalkHome` like station pose:

```csharp
            // If somehow still marked returning, prefer station pose for ambient snap.
            if (m.State == RepairMissionState.ReturnExit || m.State == RepairMissionState.WalkHome)
            {
                pos = seat.WorldMatrix.Translation + seat.WorldMatrix.Right * 1.2 + up * 0.1;
                forward = seat.WorldMatrix.Forward;
                return true;
            }
```

Place this early return after `up = seat.WorldMatrix.Up` and before path/EVA pose resolution.

- [ ] **Step 4: Manual re-check** (your verify)

Repeat Task 1 manual smoke. Confirm no bot flies to Exit or walks waypoints on Recall / ship move / job complete. Confirm ambient wander resumes near the station after finish.

---

## Spec coverage (self-review)

| Spec requirement | Task |
|---|---|
| Instant home on Recall / ship move / normal completion | Task 1 (`BeginReturn`) |
| Station offset pose, no AttachPilot | Task 1 (`TeleportHome`) |
| Stockpile refund | Task 1 via `ClearCurrentTarget` |
| Body-less finish | Task 1 skip teleport; Task 2 AdvanceLogical |
| Seat missing safe | Task 1 null checks → `FinishMission` only |
| Outbound unchanged | No edits to WalkOut / AtExit / EvaTransit / Welding start |
| Dead scenic return | Task 2 |
| HUD / network unchanged | No edits |

No placeholders. Types match existing `MissionRuntime` / `RepairMissionState` / `CrewAmbientPresence` APIs.
