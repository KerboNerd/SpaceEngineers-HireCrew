# Salvage Zone Debris Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Mark stores a frozen padded world AABB; Salvage Ops grind all legal blocks inside that zone (including debris).

**Architecture:** Persist zone min/max per home in `SalvageTargetStore`; mission pick scans grids intersecting the zone; client draws the fixed wireframe box.

**Tech Stack:** Space Engineers ModAPI, existing HireCrew salvage stack, xunit logic tests.

## Global Constraints

- Pad = 15 m (`SalvageZonePadMeters`)
- Frozen at mark time (not living)
- Skip home construct + enemy; leaf-first unchanged
- No Construction / `CrewRepairMission` grind branches

---

### Task 1: Pure zone helpers + tests

- [x] Add `CrewSalvageRules.BuildPaddedZone` / `IsInsideZone`
- [x] Tests for pad and inside/outside
- [x] `CrewConfig.SalvageZonePadMeters = 15f`

### Task 2: Store / models / sync

- [x] Extend `SalvageTargetEntry` with zone doubles + seed grid id
- [x] Rewrite store to hold zone marks; format version 2; migrate v1 via seed grid if present at load time is session-side
- [x] Server builds zone from target AABB on mark

### Task 3: Highlight + mission pick

- [x] Draw fixed wireframe AABB from synced zones
- [x] Mission stores zone; pick across intersecting legal grids
- [x] Dispatch / retarget / done use zone emptiness
- [x] Wiki Salvage-Ops note
