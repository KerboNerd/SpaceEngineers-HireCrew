# Torch Dedicated Fixes Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix Torch dedicated unload crash (RichHud FontManager), ambient harvest `SpawnBot` pool=0, and hire-desk missing icon.

**Architecture:** Lazy GlyphFormat so dedicated unload never touches FontManager. Pure `CrewHarvestSpawnRules` for offset/fallback math (unit-tested); `CrewBotControllers` anchors harvest to a loaded player/grid. SBC icon points at existing mod DDS.

**Tech Stack:** Space Engineers ModAPI, RichHud Framework (client), xunit (`HireCrew.Logic.Tests`), C# 7.3 / net48.

## Global Constraints

- No RichHud Framework edits
- No new hire-desk art; reuse `Textures\Icons\HC_CrewStation_1.dds`
- Harvest subtype order unchanged: `HireCrew_Harvest`, `HireCrew_Crew`, `Female_Astronaut`, `Astronaut`, `SpaceSpider`
- Offset from loaded anchor: **2000–5000** m (not absolute 8e6 m deep space as primary)
- Deep-space absolute fallback remains last resort when no player/grid anchor
- Do not maintain `workshop/` staging
- Spec: `docs/superpowers/specs/2026-07-29-torch-dedicated-fixes-design.md`

## File structure

| File | Responsibility |
| --- | --- |
| `Data/Scripts/HireCrew/CrewAmbientNameplates.cs` | Remove static GlyphFormat; lazy format in CreatePlate |
| `Data/Scripts/HireCrew/CrewHud.cs` | Dedicated-safe Unload (skip nameplate/marker SetReady if dedicated) |
| `Data/Scripts/HireCrew/CrewHarvestSpawnRules.cs` | Pure offset + deep-space fallback + anchor-kind labels |
| `tests/HireCrew.Logic.Tests/CrewHarvestSpawnRulesTests.cs` | Unit tests for harvest math |
| `tests/HireCrew.Logic.Tests/HireCrew.Logic.Tests.csproj` | Compile link |
| `Data/Scripts/HireCrew/CrewBotControllers.cs` | Anchor harvest to player/grid; use rules helper; richer fail log |
| `Data/CubeBlocks/HC_CrewHireDesk.sbc` | Mod-owned Icon path |

---

### Task 1: Nameplate unload safety (FATAL)

**Files:**
- Modify: `Data/Scripts/HireCrew/CrewAmbientNameplates.cs`
- Modify: `Data/Scripts/HireCrew/CrewHud.cs` (`Unload` method)

**Interfaces:**
- Consumes: `RichHudClient.Registered`, `MyAPIGateway.Utilities.IsDedicated`
- Produces: type load of `CrewAmbientNameplates` never constructs `GlyphFormat` / `FontManager`

- [ ] **Step 1: Remove static GlyphFormat**

In `CrewAmbientNameplates.cs`, delete:

```csharp
private static readonly GlyphFormat NameFormat = new GlyphFormat(
    new Color(235, 240, 245),
    TextAlignment.Center,
    1.15f);
```

In `CreatePlate`, set format inline:

```csharp
Format = new GlyphFormat(
    new Color(235, 240, 245),
    TextAlignment.Center,
    1.15f),
```

`CreatePlate` is only reached from `Update` after `RichHudClient.Registered` and non-dedicated checks — keep those guards.

- [ ] **Step 2: Guard Clear on empty / dedicated**

Ensure `Clear` remains safe when `ByCrewId` is empty (no RichHud calls). No new GlyphFormat construction in `SetReady` / `Clear`.

Optional hardening (recommended): at start of `SetReady`:

```csharp
public static void SetReady(bool ready)
{
    _ready = ready;
    if (!ready)
        Clear();
}
```

Leave as-is if Step 1 alone removes FontManager from type init — that is the crash root cause.

- [ ] **Step 3: Dedicated early-out in CrewHud.Unload**

In `CrewHud.Unload`, before nameplate/marker teardown:

```csharp
public void Unload()
{
    if (MyAPIGateway.Utilities != null && MyAPIGateway.Utilities.IsDedicated)
    {
        UnregisterChat(); // no-op if never registered
        _rhfReady = false;
        _model.Close();
        return;
    }

    CrewAmbientNameplates.SetReady(false);
    CrewMissionMarkers.SetReady(false);
    // ... existing body ...
}
```

If `UnregisterChat` is private and dedicated never registered chat, calling it is fine (existing early returns inside). Match existing `UnregisterChat` / field cleanup so dedicated unload does not touch RichHud types unnecessarily.

Full intended `Unload` shape:

```csharp
public void Unload()
{
    if (MyAPIGateway.Utilities != null && MyAPIGateway.Utilities.IsDedicated)
    {
        UnregisterChat();
        _window = null;
        _hireWindow = null;
        _statusSidebar = null;
        _rhfReady = false;
        _model.Close();
        _openHireBlockId = 0;
        return;
    }

    CrewAmbientNameplates.SetReady(false);
    CrewMissionMarkers.SetReady(false);
    CloseUi();
    CloseHireUi();
    UnregisterChat();
    if (_statusSidebar != null)
        _statusSidebar.Apply(null, 0, false);
    _window = null;
    _hireWindow = null;
    _statusSidebar = null;
    _rhfReady = false;
    _model.Close();
}
```

- [ ] **Step 4: Commit**

```bash
git add Data/Scripts/HireCrew/CrewAmbientNameplates.cs Data/Scripts/HireCrew/CrewHud.cs
git commit -m "fix: avoid RichHud FontManager on dedicated unload"
```

---

### Task 2: Harvest spawn position (pool=0)

**Files:**
- Create: `Data/Scripts/HireCrew/CrewHarvestSpawnRules.cs`
- Create: `tests/HireCrew.Logic.Tests/CrewHarvestSpawnRulesTests.cs`
- Modify: `tests/HireCrew.Logic.Tests/HireCrew.Logic.Tests.csproj`
- Modify: `Data/Scripts/HireCrew/CrewBotControllers.cs`

**Interfaces:**
- Consumes: `VRageMath.Vector3D` (already used by linked sources via other files — helper should use `System` + doubles only if Vector3D cannot link; prefer `Vector3D` from VRageMath like other test-linked files… **do not** link VRage in tests if absent).

**Important:** Test project does not reference VRage. Keep the helper pure with `double` coords:

```csharp
namespace HireCrew
{
    public static class CrewHarvestSpawnRules
    {
        public const double OffsetMinMeters = 2000.0;
        public const double OffsetMaxMeters = 5000.0;
        public const double DeepSpaceMinMeters = 8000000.0;
        public const double DeepSpaceSpanMeters = 4000000.0;

        public const string AnchorNone = "none";
        public const string AnchorPlayer = "player";
        public const string AnchorGrid = "grid";
        public const string AnchorDeepSpace = "deepspace";

        /// <summary>Deterministic offset from anchor for harvest dummy (variant seeds RNG).</summary>
        public static void OffsetFromAnchor(
            double ax, double ay, double az,
            int variant,
            out double x, out double y, out double z)
        {
            var rng = new Random(unchecked(variant * 9973 + 17));
            double span = OffsetMaxMeters - OffsetMinMeters;
            double r = OffsetMinMeters + rng.NextDouble() * span;
            double ang = rng.NextDouble() * Math.PI * 2.0;
            x = ax + Math.Cos(ang) * r;
            y = ay + (rng.NextDouble() - 0.5) * r * 0.2;
            z = az + Math.Sin(ang) * r;
        }

        /// <summary>Absolute deep-space fallback when no loaded anchor exists.</summary>
        public static void DeepSpaceFallback(
            int variant,
            out double x, out double y, out double z)
        {
            var rng = new Random(unchecked(variant * 9973 + 17));
            double r = DeepSpaceMinMeters + rng.NextDouble() * DeepSpaceSpanMeters;
            double ang = rng.NextDouble() * Math.PI * 2.0;
            x = Math.Cos(ang) * r;
            y = (rng.NextDouble() - 0.5) * r * 0.2;
            z = Math.Sin(ang) * r;
        }
    }
}
```

- Produces: `CrewHarvestSpawnRules.OffsetFromAnchor`, `DeepSpaceFallback`, constants above
- `CrewBotControllers` stores last anchor kind string for fail logs

- [ ] **Step 1: Write the failing tests**

Create `tests/HireCrew.Logic.Tests/CrewHarvestSpawnRulesTests.cs`:

```csharp
using System;
using HireCrew;
using Xunit;

public class CrewHarvestSpawnRulesTests
{
    [Fact]
    public void OffsetFromAnchor_DistanceInRange()
    {
        double ax = 100, ay = 200, az = -50;
        CrewHarvestSpawnRules.OffsetFromAnchor(ax, ay, az, 1, out double x, out double y, out double z);
        double dx = x - ax, dy = y - ay, dz = z - az;
        double dist = Math.Sqrt(dx * dx + dy * dy + dz * dz);
        Assert.InRange(dist, CrewHarvestSpawnRules.OffsetMinMeters * 0.99, CrewHarvestSpawnRules.OffsetMaxMeters * 1.01);
    }

    [Fact]
    public void OffsetFromAnchor_DeterministicForVariant()
    {
        CrewHarvestSpawnRules.OffsetFromAnchor(0, 0, 0, 42, out double x1, out double y1, out double z1);
        CrewHarvestSpawnRules.OffsetFromAnchor(0, 0, 0, 42, out double x2, out double y2, out double z2);
        Assert.Equal(x1, x2);
        Assert.Equal(y1, y2);
        Assert.Equal(z1, z2);
    }

    [Fact]
    public void DeepSpaceFallback_FarFromOrigin()
    {
        CrewHarvestSpawnRules.DeepSpaceFallback(3, out double x, out double y, out double z);
        double dist = Math.Sqrt(x * x + y * y + z * z);
        Assert.True(dist >= CrewHarvestSpawnRules.DeepSpaceMinMeters * 0.99);
    }

    [Fact]
    public void AnchorKindConstants_Stable()
    {
        Assert.Equal("player", CrewHarvestSpawnRules.AnchorPlayer);
        Assert.Equal("grid", CrewHarvestSpawnRules.AnchorGrid);
        Assert.Equal("deepspace", CrewHarvestSpawnRules.AnchorDeepSpace);
        Assert.Equal("none", CrewHarvestSpawnRules.AnchorNone);
    }
}
```

- [ ] **Step 2: Run tests — expect FAIL (type missing)**

Run (do not run in agent session if user forbids `dotnet test`; leave for human):

```
dotnet test tests/HireCrew.Logic.Tests/HireCrew.Logic.Tests.csproj --filter CrewHarvestSpawnRulesTests
```

Expected: FAIL — `CrewHarvestSpawnRules` not found / not compiled.

- [ ] **Step 3: Add helper + csproj link**

Create `Data/Scripts/HireCrew/CrewHarvestSpawnRules.cs` with the full class from Interfaces above (`using System;`).

Add to `HireCrew.Logic.Tests.csproj`:

```xml
<Compile Include="..\..\Data\Scripts\HireCrew\CrewHarvestSpawnRules.cs" Link="CrewHarvestSpawnRules.cs" />
```

- [ ] **Step 4: Run tests — expect PASS**

```
dotnet test tests/HireCrew.Logic.Tests/HireCrew.Logic.Tests.csproj --filter CrewHarvestSpawnRulesTests
```

Expected: PASS

- [ ] **Step 5: Wire CrewBotControllers**

Add fields near other harvest state:

```csharp
private static string _harvestAnchorKind = CrewHarvestSpawnRules.AnchorNone;
```

Replace `EnsureHarvestPosition` body:

```csharp
private static void EnsureHarvestPosition()
{
    _harvestPosVariant++;
    Vector3D anchor;
    if (TryGetPlayerAnchor(out anchor))
    {
        _harvestAnchorKind = CrewHarvestSpawnRules.AnchorPlayer;
    }
    else if (TryGetGridAnchor(out anchor))
    {
        _harvestAnchorKind = CrewHarvestSpawnRules.AnchorGrid;
    }
    else
    {
        _harvestAnchorKind = CrewHarvestSpawnRules.AnchorDeepSpace;
        double x, y, z;
        CrewHarvestSpawnRules.DeepSpaceFallback(_harvestPosVariant, out x, out y, out z);
        _harvestPos = new Vector3D(x, y, z);
        _harvestPosReady = true;
        return;
    }

    double ox, oy, oz;
    CrewHarvestSpawnRules.OffsetFromAnchor(
        anchor.X, anchor.Y, anchor.Z, _harvestPosVariant, out ox, out oy, out oz);
    _harvestPos = new Vector3D(ox, oy, oz);
    _harvestPosReady = true;
}
```

Add helpers (same file, private static):

```csharp
private static bool TryGetPlayerAnchor(out Vector3D pos)
{
    pos = Vector3D.Zero;
    PlayerScratch.Clear();
    MyAPIGateway.Players.GetPlayers(PlayerScratch);
    for (int i = 0; i < PlayerScratch.Count; i++)
    {
        var p = PlayerScratch[i];
        if (p == null || p.IsBot)
            continue;
        if (p.Character != null && !p.Character.Closed)
        {
            pos = p.Character.GetPosition();
            return true;
        }
        if (p.Controller != null && p.Controller.ControlledEntity != null)
        {
            var ent = p.Controller.ControlledEntity as IMyEntity;
            if (ent != null && !ent.Closed)
            {
                pos = ent.GetPosition();
                return true;
            }
        }
    }
    return false;
}

private static bool TryGetGridAnchor(out Vector3D pos)
{
    pos = Vector3D.Zero;
    // Prefer any crew-assigned grid from session store if available.
    var session = CrewSession.Instance;
    if (session != null && session.Store != null)
    {
        foreach (var crew in session.Store.All)
        {
            if (crew == null || crew.GridEntityId == 0)
                continue;
            IMyEntity ent;
            if (!MyAPIGateway.Entities.TryGetEntityById(crew.GridEntityId, out ent) || ent == null || ent.Closed)
                continue;
            var grid = ent as IMyCubeGrid;
            if (grid == null)
                continue;
            pos = grid.WorldMatrix.Translation;
            return true;
        }
    }
    return false;
}
```

Update all-subtype failure log:

```csharp
if (++_logThrottle % 10 == 1)
{
    Log("harvest SpawnBot failed for all subtypes (pool=" + Pool.Count
        + " anchor=" + _harvestAnchorKind
        + " pos=" + _harvestPos.X.ToString("F0") + ","
        + _harvestPos.Y.ToString("F0") + ","
        + _harvestPos.Z.ToString("F0") + ")");
}
```

On successful spawn, keep existing log; optionally append `anchor=` + `_harvestAnchorKind`.

Reset `_harvestAnchorKind` in `Clear()` to `CrewHarvestSpawnRules.AnchorNone`.

Do **not** change `HarvestSubtypes` order.

- [ ] **Step 6: Commit**

```bash
git add Data/Scripts/HireCrew/CrewHarvestSpawnRules.cs Data/Scripts/HireCrew/CrewBotControllers.cs tests/HireCrew.Logic.Tests/CrewHarvestSpawnRulesTests.cs tests/HireCrew.Logic.Tests/HireCrew.Logic.Tests.csproj
git commit -m "fix: harvest SpawnBot near loaded player or grid"
```

---

### Task 3: Hire-desk icon

**Files:**
- Modify: `Data/CubeBlocks/HC_CrewHireDesk.sbc`

**Interfaces:**
- Consumes: existing `Textures\Icons\HC_CrewStation_1.dds`
- Produces: SBC Icon path that resolves inside the mod package

- [ ] **Step 1: Update Icon element**

Change line:

```xml
<Icon>Textures\GUI\Icons\Cubes\StoreBlock.dds</Icon>
```

to:

```xml
<Icon>Textures\Icons\HC_CrewStation_1.dds</Icon>
```

- [ ] **Step 2: Commit**

```bash
git add Data/CubeBlocks/HC_CrewHireDesk.sbc
git commit -m "fix: use mod-owned icon for hire desk block"
```

---

### Task 4: Manual Torch verification checklist

No code. After Tasks 1–3 are on the server mod build:

- [ ] **Step 1: Start Torch** with HireCrew loaded; confirm script init / no missing-icon MOD_ERROR for `HC_CrewHireDesk`.

- [ ] **Step 2: Join as player**; assign or `/hc fill` Construction; watch Keen/Torch log for `harvest dummy spawned` and `pool=` > 0 (not stuck on `ctrlPool=0`).

- [ ] **Step 3: Stop server**; confirm no `ModCrashedException` / FontManager / `CrewAmbientNameplates` FATAL.

---

## Spec coverage (self-review)

| Spec item | Task |
| --- | --- |
| Lazy GlyphFormat / no FontManager on type init | Task 1 |
| Dedicated unload safe | Task 1 |
| Anchor harvest to player then grid then deep space | Task 2 |
| 2–5 km offset | Task 2 (`OffsetMin/MaxMeters`) |
| Throttled fail log with anchor + pos | Task 2 |
| Subtype order unchanged | Task 2 |
| Hire desk mod icon | Task 3 |
| Unit test harvest math | Task 2 |
| Manual Torch verify | Task 4 |
