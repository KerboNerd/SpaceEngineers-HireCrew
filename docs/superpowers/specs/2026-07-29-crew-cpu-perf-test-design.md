# Crew CPU Perf Test Checklist

Date: 2026-07-29  
Status: Ready to run (manual)

## Goal

Decide whether active HireCrew on ~10 ships is **fine** or **noticeably worse** than the same ships with no crew. Subjective pass/fail — no profiler required.

## Approach

Same-save A/B:

1. Build one world with 10 ships + active crew.
2. Copy for **WithCrew**.
3. Copy again for **NoCrew** (strip crew / disable HireCrew; keep ships).
4. Compare feel on the same PC.

## Setup

- [ ] Test world: 10 similar ships.
- [ ] HireCrew on each ship; crew **actively** on missions (repair and/or salvage).
- [ ] Note approximate total crew count and which mission types are running.
- [ ] Save → copy as **WithCrew**.
- [ ] Copy again as **NoCrew**: remove all crew and/or disable HireCrew so grids stay, crew cost goes away.
- [ ] Same machine, same graphics settings, camera parked similarly for both runs.
- [ ] No other mod differences between the two saves (except HireCrew off on NoCrew if that is how crew is removed).

## Runs

1. [ ] Load **NoCrew**. Wait until sim settles (~1–2 min). Observe ~2–3 min.
2. [ ] Record feel: fine / a bit rough / bad.
3. [ ] Load **WithCrew**. Same wait + observe window.
4. [ ] Record feel the same way.
5. [ ] Optional: reverse order once (WithCrew first) to rule out first-load warmth.

## Pass / fail

| Result | Meaning |
| --- | --- |
| **Pass** | WithCrew feels about the same as NoCrew (not noticeably worse). |
| **Fail** | Clear hitching, sim drag, or sustained stutter only on WithCrew. |

## Result log (fill in after run)

| Field | Value |
| --- | --- |
| Date | |
| Ships | 10 |
| ~Crew count | |
| Mission types | |
| NoCrew feel | fine / a bit rough / bad |
| WithCrew feel | fine / a bit rough / bad |
| Order run | NoCrew first / WithCrew first / both |
| Verdict | Pass / Fail |
| Notes | |

## Non-goals

- Exact ms / profiler capture.
- Idle-only crew baseline (active missions only for this checklist).
- Scale ladder (1 → 5 → 10) unless Pass/Fail is unclear and a follow-up is needed.
