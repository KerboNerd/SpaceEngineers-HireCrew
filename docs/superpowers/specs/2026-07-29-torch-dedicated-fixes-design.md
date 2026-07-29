# Torch Dedicated Fixes Design

Date: 2026-07-29  
Status: Approved for planning

## Problem

HireCrew on a Torch dedicated server (Gargantua) loaded and ran admin fill, but three issues appeared:

1. **FATAL on unload/stop** — `CrewAmbientNameplates` static init uses RichHud `FontManager` during `CrewHud.Unload` / `CrewSession.UnloadData` after RichHudClient is gone (or never inited on dedicated) → `ModCrashedException`.
2. **Ambient bot spawn fails** — harvest `SpawnBot` fails for all subtypes (`pool=0`), then ambient spawn logs `ctrlPool=0` / unresolved.
3. **Missing icon (non-fatal)** — `HC_CrewHireDesk.sbc` references vanilla `Textures\GUI\Icons\Cubes\StoreBlock.dds`, which is not available to the mod package.

## Goals

- Clean dedicated unload/stop with no FontManager / nameplate crash.
- Controller harvest pool can fill when a player (or loaded grid) is present so ambient Controlled path can proceed.
- Hire desk G-menu / cube icon resolves from mod-owned textures (no MOD_ERROR).

## Non-goals

- New hire-desk art asset.
- Rewriting bot AI / ambient behavior beyond harvest position + unload safety.
- Changing RichHud Framework internals.
- Guaranteeing harvest with zero players and zero loaded grids (deep-space absolute fallback may still fail).

## Root causes (working)

| Issue | Cause |
| --- | --- |
| Unload FATAL | Static `GlyphFormat NameFormat` on `CrewAmbientNameplates` touches `FontManager` on first type load. Dedicated never inits RichHud, but `CrewHud.Unload` still touches the class. |
| Ambient `pool=0` | `EnsureHarvestPosition` uses absolute deep-space coords (~8–12e6 m). `SpawnBot` returns 0 for every subtype (including vanilla `SpaceSpider`) when the sector is not streamed. Ambient then cannot take a controller. |
| Missing icon | SBC path points at a vanilla GUI texture not shipped with the mod. |

`Data/Bots.sbc` (and workshop content copy) already defines `HireCrew_Crew` / `HireCrew_Harvest`; no SBC rewrite unless logs prove subtype-missing.

## Design

### 1. Unload / nameplates

Files: `CrewAmbientNameplates.cs`, light touch `CrewHud.cs` if needed.

- Remove static `readonly GlyphFormat NameFormat`.
- Build `GlyphFormat` only inside `CreatePlate`, and only when RichHud is registered / client path is active.
- On dedicated: `SetReady` / `Clear` / unload must not construct RichHud UI; flip `_ready` and clear dictionary only (plates empty on dedicated).
- `CrewHud.Unload` must remain safe when RichHud was never inited.

Success: Torch stop has no `ModCrashedException` from `CrewAmbientNameplates` / `FontManager`.

### 2. Harvest position

File: `CrewBotControllers.cs` (+ small pure helper + unit test if cheap).

`EnsureHarvestPosition` anchors to a loaded entity:

1. First non-bot player with a character (or controlled entity), else
2. Any available loaded grid, else
3. Existing absolute deep-space fallback (last resort).

Offset: large random vector (~2–5 km) from the anchor so the harvest dummy stays off-camera but in a streamed sector.

On all-subtype failure: log anchor kind + position (throttled) for Torch diagnosis.

Subtype order unchanged: `HireCrew_Harvest`, `HireCrew_Crew`, `Female_Astronaut`, `Astronaut`, `SpaceSpider`.

Success: with a player online, logs show `harvest dummy spawned` / `pool>0`; ambient not stuck on `ctrlPool=0`.

### 3. Hire-desk icon

File: `Data/CubeBlocks/HC_CrewHireDesk.sbc`

```xml
<Icon>Textures\Icons\HC_CrewStation_1.dds</Icon>
```

Reuse existing mod icon; no new DDS. Do not maintain `workshop/` staging.

Success: no missing-icon MOD_ERROR for `HC_CrewHireDesk`.

## Testing

- Unit: harvest offset/anchor math helper if extracted.
- Manual Torch: start → admin fill → ambient harvest pool grows with player online → clean stop → no hire-desk icon MOD_ERROR.

## Out of scope follow-ups

- Dedicated harvest with no players/grids (may need a different strategy later).
- Custom hire-desk icon art.
