# Task 6 Report: Crew Terminal UI

**Status:** DONE  
**Base:** `d7b12d8bb68ce3fb330dd88ecb7aa9759754b437`  
**Commit:** `c5c0b1e` — `feat: add Crew Terminal hire/assign/dismiss UI`

## Deliverables

| Path | Action |
|------|--------|
| `Data/Scripts/HireCrew/CrewTerminalLogic.cs` | Modified — terminal controls for hire / roster / seats / weapons / assign / dismiss |

## Steps completed

### Step 1 — Implement terminal controls

- Register once via static `_controlsRegistered` on first `CrewTerminalLogic.Init`
- Warm `GetControls<IMyTextPanel>`, then `CreateControl` + `AddControl` for all HireCrew controls
- `Visible` / `Enabled` gated by `IsCrewTerminal` (controls exist on all text panels but only show on Crew Terminal subtypes)
- Selection cached on game-logic fields: `SelectedCrewId`, `SelectedSeatEntityId`, `SelectedWeaponEntityId`
- Controls:
  - `HireCrew_HireRecruit` / `HireCrew_HireRegular` / `HireCrew_HireElite` → `ClientRequestHire`
  - `HireCrew_Roster` — display name + status from `Store.GetForGrid`
  - `HireCrew_Seats` — empty `IMyShipController` (`Pilot == null`), excluding seated-crew seats
  - `HireCrew_Weapons` — `WeaponAi.IsCoreWeapon`, excluding manned weapons
  - `HireCrew_Assign` — enabled when Unassigned crew + seat + weapon selected → `ClientRequestAssign`
  - `HireCrew_Dismiss` — selected roster → `ClientRequestDismiss`
- Owner/faction checks left server-side (per resolution); UI visible to anyone who can open the block

### Step 2 — Manual UI test

**Skipped** (per task resolution: skip in-game UI smoke).

### Step 3 — Commit

Committed `Data/Scripts/HireCrew/CrewTerminalLogic.cs` only with message: `feat: add Crew Terminal hire/assign/dismiss UI`

## Tests

| Check | Result |
|-------|--------|
| In-game hire/assign/dismiss UI smoke | Skipped — resolution |
| SE F11 compile | Skipped — no local client launch |
| Unit tests | Not re-run (shell discipline) |

## Self-review / concerns

- Client `RosterMsg` still does not apply `StoreBytes` to client `Store` (Task 5 leftover). Dedicated MP clients will see empty roster/assign enablement until that sync lands (Task 7 checklist item 9).
- Weapon list empty until `WeaponAi.IsReady` (WC API load).
- Terminal control registration runs from first terminal block `Init`; if that races before terminal systems are ready, controls may need a later re-register (unverified in-game).

## Review fix notes (Important Task 6)

1. **Same-grid filter** — Seat and weapon list queries now require `block.CubeGrid.EntityId == terminalBlock.CubeGrid.EntityId`, so mechanical-group neighbors from `GetTerminalSystemForGrid` are excluded.
2. **Seat type** — Empty-seat list uses `IMyCockpit` (attachable seats/cockpits) instead of bare `IMyShipController`, matching what `NpcSeater` accepts and excluding remotes it rejects.
