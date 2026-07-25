# Task 7 Report: Persistence + integrity hardening + dedicated checklist

**Status:** DONE  
**Base:** `b9eaa091859c521bbb875653ee7ba11f4ebb5801`  
**Commit:** `d2de77c` — `fix: harden HireCrew persistence and integrity watches`

## Deliverables

| Path | Action |
|------|--------|
| `Data/Scripts/HireCrew/CrewSession.cs` | Modified — dual persistence, client roster apply, integrity broadcast |

## Steps completed

### Step 1 — SaveData / BeforeStart hardening

- `SaveData` dual-writes Base64 `Store.ToBytes()` to:
  - world variable `HireCrew_Store`
  - WorldStorage file `HireCrew.dat` via `WriteFileInWorldStorage`
- `BeforeStart` loads via `TryLoadStoreBytes`: prefer `HireCrew.dat` (`FileExistsInWorldStorage` + `ReadFileInWorldStorage`), fall back to `GetVariable("HireCrew_Store")`, then `ReseatAllFromStore`
- Null-safe: skip save when `Store == null`

### Step 2 — Dedicated checklist

| # | Case | Result |
|---|------|--------|
| 1 | Hire + assign | manual — not run |
| 2 | Kill engineer | manual — not run |
| 3 | Destroy seat | manual — not run |
| 4 | Destroy weapon | manual — not run |
| 5 | Grid split separating seat/weapon | manual — not run |
| 6 | Non-owner denied | manual — not run |
| 7 | Insufficient credits | manual — not run |
| 8 | Restart dedicated | manual — not run |
| 9 | Two clients | manual — not run (code fix landed: client `RosterMsg` applies `StoreBytes`) |

### Step 3 — Fixes for checklist enablement

1. **Client roster sync (item 9):** `RosterMsg` handler deserializes `RosterSync` and replaces client `Store` with `CrewStore.FromBytes(sync.StoreBytes)` (try/catch keeps prior store on bad payload).
2. **Integrity → clients:** `WatchCrewIntegrity` broadcasts roster once after any successful `InvalidateCrew` so MP clients drop dead/destroyed/split crew from Terminal UI.

### Step 4 — Unit tests

**Command:**

```powershell
dotnet test "C:\Users\user\AppData\Roaming\SpaceEngineers\Mods\HireCrew\tests\HireCrew.Logic.Tests\HireCrew.Logic.Tests.csproj" -v n
```

**Result:** Build succeeded; all tests passed (exit code 0)

```
Test Run Successful.
Total tests: 10
     Passed: 10
```

### Step 5 — Commit

Committed `Data/Scripts/HireCrew` with message: `fix: harden HireCrew persistence and integrity watches`

## Self-review / concerns

- Dedicated checklist not executed in-game (resolution: mark manual — not run).
- No join-time roster push: late-joining clients stay empty until next hire/assign/dismiss/integrity broadcast.
- Terminal listboxes refresh on reopen/interaction; no forced UI refresh after client store replace.
- Dual-write not save/load verified on dedicated (WorldStorage path confirmed against SE `IMyUtilities` XML).

---

## Review follow-up (Important Task 7 findings)

**Date:** 2026-07-26  
**Commit:** `fix: sync HireCrew roster on join and prefer variable persistence`

### Fixes

1. **Join-time roster push:** Server tracks `_rosterSyncedSteamIds`. Every 60 ticks in `UpdateAfterSimulation`, `SyncRosterToNewPlayers` sends full `RosterSync` (`StoreBytes`) via `RosterMsg` to any present steam id not yet synced. Departed ids are removed so reconnect gets a fresh push. Throttled catch-up used (no reliable `PlayerJoined` across SE mod API versions).
2. **Load preference:** `TryLoadStoreBytes` now prefers world variable `HireCrew_Store`; falls back to WorldStorage `HireCrew.dat` only when variable is missing/empty/invalid.

### Tests re-run

**Command:**

```powershell
dotnet test "C:\Users\user\AppData\Roaming\SpaceEngineers\Mods\HireCrew\tests\HireCrew.Logic.Tests\HireCrew.Logic.Tests.csproj" -v n
```

**Result:** Build succeeded; all tests passed (exit code 0)

```
Test Run Successful.
Total tests: 10
     Passed: 10
```
