# Damage Control Per-Crew Dispatch Implementation Plan

> **For agentic workers:** execute task-by-task in this session.

**Goal:** Per-crew Send/Recall on each Damage Control roster row; remove bottom-bar control and grid batch APIs from the HUD path.

**Spec:** `docs/superpowers/specs/2026-07-28-damage-control-per-crew-dispatch-design.md`

## File map

- `CrewModels.cs` — `RepairDispatchRequest.CrewId`
- `CrewRepairMission.cs` — `DispatchCrew` / `RecallCrew` (keep or thin grid helpers if unused)
- `CrewSession.cs` — handle by `CrewId`
- `CrewHudWindow.cs` — row buttons; remove `_btnRepair`

## Tasks

1. DTO + mission APIs + session handler  
2. HUD row buttons + remove bottom Send/Recall  
3. Manual verify: Send one, Send second, Recall one only

## Manual test

- Two+ DC on one grid: Send A only → A flies; B idle with Send.  
- Recall A → A returns; B unchanged.  
- Send B while A out → both out; each Recall independent.  
- Bottom bar has no Send/Recall.
