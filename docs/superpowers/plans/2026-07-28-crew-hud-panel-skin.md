# Crew HUD Panel Skin Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a soft sci-fi glass full-panel texture behind the `/crew` RichHud window and wire it through TransparentMaterials like the existing icons.

**Architecture:** One 1024×1024 BC7 DDS registered as TransparentMaterial `HC_Ui_CrewPanel`, exposed as `CrewHudIcons.CrewPanel`, assigned to `CrewHudWindow`’s `_bg` TexturedBox. Hire desk stays on solid color + BorderBox.

**Tech Stack:** Space Engineers TransparentMaterials SBC, DDS BC7, RichHudFramework `Material` / `TexturedBox`, image generation (Cursor GenerateImage or OpenArt).

## Global Constraints

- `/crew` HUD only — do not change `CrewHireWindow` background.
- Full panel skin stretched to fixed panel size 580×540; asset is 1024×1024.
- Soft sci-fi glass (muted blues/teals); center = light grid/circuitry pattern.
- Same material pipeline as icons (`TransparentMaterials_HireCrew.sbc` + `Textures\HireCrew\...`).
- Prefer dropping the plain `BorderBox` when the texture already has a frame.
- No new unit tests for RichHud materials (`CrewHudIcons` is not in `HireCrew.Logic.Tests`); verification is in-game.
- Agent must not run `dotnet` commands; user runs tests if any unrelated suite is needed.
- User must approve the generated look before DDS conversion/wiring.

## File structure

| File | Role |
|------|------|
| `Textures/HireCrew/UI/hc_crew_panel.dds` | Panel skin texture (BC7) |
| `Data/TransparentMaterials_HireCrew.sbc` | Register `HC_Ui_CrewPanel` |
| `Data/Scripts/HireCrew/CrewHudIcons.cs` | `CrewPanel` Material constant |
| `Data/Scripts/HireCrew/CrewHudWindow.cs` | Assign material on `_bg`; remove BorderBox |
| `Source/HireCrew/CrewHudIcons.cs` | Mirror of Data script if present |
| `Source/HireCrew/CrewHudWindow.cs` | Mirror of Data script if present |
| `workshop/content/...` | Mirror only if that tree is used for publish |

---

### Task 1: Generate and pick panel art

**Files:**
- Create (temp): generated PNG candidates under `Textures/HireCrew/UI/` or a scratch folder (e.g. `Textures/HireCrew/UI/_candidates/`)
- Does not modify game scripts yet

**Interfaces:**
- Produces: one approved PNG path chosen by the user (final name before convert: intended `hc_crew_panel.png`)

- [ ] **Step 1: Generate 2–3 candidate images**

Use image generation with a prompt in this spirit (tweak if needed; keep 1:1 square):

```
Soft sci-fi glass UI panel background, square 1024x1024, muted blue and teal translucent glass,
subtle faint circuit grid pattern in the center for readability, stronger framed bezel edges,
clean Space Engineers HUD aesthetic, no text, no logos, no characters, no icons,
semi-transparent dark center so white UI text would remain readable, high quality game UI texture
```

Save candidates as e.g.:
- `Textures/HireCrew/UI/_candidates/hc_crew_panel_a.png`
- `Textures/HireCrew/UI/_candidates/hc_crew_panel_b.png`
- `Textures/HireCrew/UI/_candidates/hc_crew_panel_c.png`

- [ ] **Step 2: User picks one**

Stop and show the candidates. Do not convert or wire until the user names the winner (or asks for a regen).

- [ ] **Step 3: Promote winner**

Copy/rename the chosen PNG to `Textures/HireCrew/UI/hc_crew_panel.png`. Delete or leave `_candidates/` untracked (do not commit rejected options unless user asks).

- [ ] **Step 4: Commit art pick (PNG optional)**

Only commit the PNG if the repo normally tracks source PNGs; otherwise wait for DDS in Task 2.

```bash
git add Textures/HireCrew/UI/hc_crew_panel.png
git commit -m "assets: add crew HUD panel skin source PNG"
```

If PNG is not committed, skip this commit.

---

### Task 2: DDS + TransparentMaterial

**Files:**
- Create: `Textures/HireCrew/UI/hc_crew_panel.dds`
- Modify: `Data/TransparentMaterials_HireCrew.sbc`
- Mirror: `workshop/content/Data/TransparentMaterials_HireCrew.sbc` and `workshop/content/Textures/HireCrew/UI/hc_crew_panel.dds` if that tree is maintained

**Interfaces:**
- Consumes: approved `Textures/HireCrew/UI/hc_crew_panel.png`
- Produces: TransparentMaterial subtype `HC_Ui_CrewPanel` → `Textures\HireCrew\UI\hc_crew_panel.dds`

- [ ] **Step 1: Convert PNG → BC7 DDS**

Match icon convention (1024×1024 BC7). Example with DirectX `texconv` if available:

```bash
texconv -f BC7_UNORM -m 1 -y -o Textures/HireCrew/UI Textures/HireCrew/UI/hc_crew_panel.png
```

If `texconv` is missing, use the same conversion path/tooling used for `hc_star.dds` / role icons in this project. Confirm output is `Textures/HireCrew/UI/hc_crew_panel.dds` at 1024×1024.

- [ ] **Step 2: Register material**

Append this block inside `<TransparentMaterials>` in `Data/TransparentMaterials_HireCrew.sbc` (same fields as icons):

```xml
    <TransparentMaterial>
      <Id>
        <TypeId>TransparentMaterialDefinition</TypeId>
        <SubtypeId>HC_Ui_CrewPanel</SubtypeId>
      </Id>
      <Texture>Textures\HireCrew\UI\hc_crew_panel.dds</Texture>
      <AlphaMistingEnable>false</AlphaMistingEnable>
      <CanBeAffectedByOtherLights>false</CanBeAffectedByOtherLights>
      <AlphaSaturation>1</AlphaSaturation>
      <IgnoreDepth>false</IgnoreDepth>
      <SoftParticleDistanceScale>0</SoftParticleDistanceScale>
      <UseAtlas>false</UseAtlas>
      <Reflectivity>0</Reflectivity>
    </TransparentMaterial>
```

- [ ] **Step 3: Mirror workshop copy if present**

If `workshop/content/Data/TransparentMaterials_HireCrew.sbc` exists, apply the same subtype and copy the DDS to `workshop/content/Textures/HireCrew/UI/hc_crew_panel.dds`.

- [ ] **Step 4: Commit**

```bash
git add Data/TransparentMaterials_HireCrew.sbc Textures/HireCrew/UI/hc_crew_panel.dds
git commit -m "assets: register HC_Ui_CrewPanel transparent material"
```

Include workshop paths in `git add` only when mirrored.

---

### Task 3: Wire material into CrewHudWindow

**Files:**
- Modify: `Data/Scripts/HireCrew/CrewHudIcons.cs`
- Modify: `Data/Scripts/HireCrew/CrewHudWindow.cs` (`EnsureBuilt` background block ~lines 152–164)
- Mirror: `Source/HireCrew/CrewHudIcons.cs`, `Source/HireCrew/CrewHudWindow.cs` if those files exist and are kept in sync
- Do not modify: `Data/Scripts/HireCrew/CrewHireWindow.cs`

**Interfaces:**
- Consumes: subtype `HC_Ui_CrewPanel`, DDS size 1024
- Produces: `public static readonly Material CrewPanel = new Material("HC_Ui_CrewPanel", new Vector2(1024f));`

- [ ] **Step 1: Add material constant**

In `CrewHudIcons.cs`, next to the other materials:

```csharp
// Texture pixel size must match the DDS (1024x1024 BC7).
public static readonly Material Star = new Material("HC_Icon_Star", new Vector2(1024f));
public static readonly Material Gunner = new Material("HC_Icon_Gunner", new Vector2(1024f));
public static readonly Material Engineer = new Material("HC_Icon_Engineer", new Vector2(1024f));
public static readonly Material CrewPanel = new Material("HC_Ui_CrewPanel", new Vector2(1024f));
```

- [ ] **Step 2: Assign on `_bg` and remove BorderBox**

Replace the background construction in `CrewHudWindow.EnsureBuilt()` with:

```csharp
_bg = new TexturedBox(this)
{
    DimAlignment = DimAlignments.Both,
    Material = CrewHudIcons.CrewPanel,
    MatAlignment = MaterialAlignment.StretchToFit,
    Color = Color.White,
    ZOffset = -2,
};
```

Remove the following `new BorderBox(this) { ... }` block that currently draws the cyan outline (frame is in the texture). Do not remove other BorderBox usages elsewhere in the file.

- [ ] **Step 3: Mirror Source scripts if present**

Apply the same two edits to `Source/HireCrew/CrewHudIcons.cs` and `Source/HireCrew/CrewHudWindow.cs` when those files exist.

- [ ] **Step 4: Commit**

```bash
git add Data/Scripts/HireCrew/CrewHudIcons.cs Data/Scripts/HireCrew/CrewHudWindow.cs
git commit -m "feat: use textured panel skin on crew HUD background"
```

Include `Source/HireCrew/...` in `git add` when mirrored.

---

### Task 4: In-game verification

**Files:**
- None (manual)

**Interfaces:**
- Consumes: Tasks 1–3 complete

- [ ] **Step 1: Reload materials**

Restart the game or fully reload the mod so `TransparentMaterials_HireCrew.sbc` is re-read (SBC changes are not hot-reloaded reliably).

- [ ] **Step 2: Check `/crew`**

Open `/crew` and confirm:
- Panel shows the glass skin (not flat dark fill).
- Row text, stars, morale bars, and footer buttons remain readable.
- Hover / selection highlight still obvious.
- No leftover double frame (plain BorderBox gone).

- [ ] **Step 3: Check hire desk unchanged**

Open the hire desk UI; it should still use the solid dark fill + blue border.

- [ ] **Step 4: Art tweak if needed**

If the center pattern fights text, regenerate/edit PNG with lower center contrast, re-run Task 2 DDS conversion, restart client. No layout redesign.

- [ ] **Step 5: Final commit if verification forced art/code tweaks**

```bash
git status
# commit only files changed by verification fixes
git commit -m "fix: tune crew HUD panel skin for readability"
```

---

## Spec coverage checklist

| Spec requirement | Task |
|------------------|------|
| Full panel skin for `/crew` | 1–3 |
| Soft sci-fi glass, light center pattern | 1 |
| 1024 DDS + `HC_Ui_CrewPanel` | 2 |
| Wire `_bg` via `CrewHudIcons` | 3 |
| Drop BorderBox when frame in texture | 3 |
| Hire desk unchanged | 3 (explicit non-touch) + 4 |
| User picks art before wire | 1 |
| In-game readability check | 4 |
