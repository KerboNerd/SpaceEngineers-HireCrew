# Crew HUD Panel Skin Design

Date: 2026-07-28  
Status: Approved for planning

## Problem

The `/crew` RichHud panel uses a flat dark fill and a plain `BorderBox`. It reads as a temporary HUD, not a finished crew terminal.

## Goals

- Give the `/crew` window a full-panel textured skin (soft sci-fi glass).
- Keep list rows, buttons, and text readable over a light center pattern.
- Reuse the existing TransparentMaterial + DDS pipeline used by role/star icons.

## Non-goals

- Hire desk skin (unchanged in this pass).
- Nine-slice / multi-piece frames.
- Runtime theme switching or config knobs.
- Redesigning row cards, icons, or button chrome beyond what readability needs after the skin is applied.

## Visual direction

- Style: soft sci-fi glass — muted blues/teals, clean HUD feel (not industrial metal or naval wood).
- Layout: full panel skin stretched to the fixed `CrewHudWindow` size (580×540).
- Center: light pattern (faint grid/circuitry), not empty and not busy art.
- Edge: framed border baked into the texture; drop the plain `BorderBox` if the art already carries the edge.

## Asset pipeline

1. Generate candidate PNG panel skins (power-of-two, target **1024×1024**).
2. Player picks one look before conversion/wiring.
3. Convert to BC7 DDS (same convention as `hc_star` / role icons).
4. Path: `Textures\HireCrew\UI\hc_crew_panel.dds` (or equivalent under `Textures/HireCrew/`).
5. Register TransparentMaterial subtype `HC_Ui_CrewPanel` in `TransparentMaterials_HireCrew.sbc`.

## Code changes

- Add shared material on `CrewHudIcons` (e.g. `CrewPanel`) pointing at `HC_Ui_CrewPanel` with `Vector2(1024f)`.
- In `CrewHudWindow.EnsureBuilt()`, assign that material to `_bg`, use white (or slight tint) `Color`, keep `DimAlignment.Both` and `ZOffset = -2`.
- Remove or keep `BorderBox` only as needed after art review (prefer remove if frame is in-texture).
- Keep `Source/` and `Data/Scripts/` in sync the same way other HUD files are maintained.
- `CrewHireWindow` unchanged.

## Failure / readability

- Missing material behaves like missing icons (blank/wrong draw); no special fallback beyond the current TexturedBox behavior.
- If text fights the pattern in-game, revise the art (lower center contrast) rather than adding new layout systems.

## Test plan

- Open `/crew`: panel shows the skin; rows/buttons remain readable; hover/selection still clear.
- Confirm hire desk is unchanged.
- Reload mod / restart client so TransparentMaterials pick up the new subtype.
