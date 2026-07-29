# Admin Fill Crew Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add admin `/hc fill <construction|salvage> [count]` that free-hires N EVA crew and seats them on the admin’s current grid for perf/mission testing.

**Architecture:** Pure arg/result helpers in `CrewAdminFillRules` (unit-tested). New `CmdFill` in `CrewAdminCommands` resolves the admin’s grid, collects free assignable seats, hires + assigns via a thin `CrewSession.AdminTryApplyAssign` wrapper around existing `TryApplyAssign`. Wiki help updated.

**Tech Stack:** Space Engineers ModAPI, existing `/hirecrew` admin RPC, xunit (`HireCrew.Logic.Tests`), C# 7.3 / net48.

## Global Constraints

- Admin-only (existing `IsAdmin` gate)
- Roles: `DamageControl` / `SalvageOps` only (existing role tokens)
- Default count `10`; clamp `1`–`50`
- Stars fixed at `3`; owner = invoking admin
- Grid = admin character’s `GetTopMostParent()` as `IMyCubeGrid`
- Partial fill when seats short; unassigned hires still created
- Reuse normal seat assign path (bots/stationing as today)
- No mission auto-start; no other roles; no remote grid id

## File structure

| File | Responsibility |
| --- | --- |
| `Data/Scripts/HireCrew/CrewAdminFillRules.cs` | Pure count clamp, fill-role gate, result message |
| `tests/HireCrew.Logic.Tests/CrewAdminFillRulesTests.cs` | Unit tests |
| `tests/HireCrew.Logic.Tests/HireCrew.Logic.Tests.csproj` | Compile link |
| `Data/Scripts/HireCrew/CrewSession.cs` | `AdminTryApplyAssign` public wrapper |
| `Data/Scripts/HireCrew/CrewAdminCommands.cs` | `fill` verb, seat collect, hire+assign loop |
| `.wiki/Admin-Commands.md` | Document `fill` |

---

### Task 1: Pure fill rules + tests

**Files:**
- Create: `Data/Scripts/HireCrew/CrewAdminFillRules.cs`
- Create: `tests/HireCrew.Logic.Tests/CrewAdminFillRulesTests.cs`
- Modify: `tests/HireCrew.Logic.Tests/HireCrew.Logic.Tests.csproj`

**Interfaces:**
- Consumes: `CrewRole` from `CrewModels.cs` / `CrewConfig.cs` (already linked in test project)
- Produces:
  - `CrewAdminFillRules.DefaultCount` → `10`
  - `CrewAdminFillRules.MinCount` → `1`
  - `CrewAdminFillRules.MaxCount` → `50`
  - `CrewAdminFillRules.FillStars` → `3`
  - `CrewAdminFillRules.IsFillRole(CrewRole role) -> bool`
  - `CrewAdminFillRules.ClampCount(int requested) -> int` (clamp into 1–50; caller supplies default when arg absent)
  - `CrewAdminFillRules.FormatResult(string roleLabel, int assigned, int requested, int noSeat, string gridName) -> string`

- [ ] **Step 1: Write the failing tests**

Create `tests/HireCrew.Logic.Tests/CrewAdminFillRulesTests.cs`:

```csharp
using HireCrew;
using Xunit;

public class CrewAdminFillRulesTests
{
    [Fact]
    public void IsFillRole_ConstructionAndSalvage_True()
    {
        Assert.True(CrewAdminFillRules.IsFillRole(CrewRole.DamageControl));
        Assert.True(CrewAdminFillRules.IsFillRole(CrewRole.SalvageOps));
    }

    [Fact]
    public void IsFillRole_OtherRoles_False()
    {
        Assert.False(CrewAdminFillRules.IsFillRole(CrewRole.Gunner));
        Assert.False(CrewAdminFillRules.IsFillRole(CrewRole.Engineer));
        Assert.False(CrewAdminFillRules.IsFillRole(CrewRole.Helmsman));
        Assert.False(CrewAdminFillRules.IsFillRole(CrewRole.Propulsion));
        Assert.False(CrewAdminFillRules.IsFillRole(CrewRole.Quartermaster));
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(10, 10)]
    [InlineData(50, 50)]
    [InlineData(0, 1)]
    [InlineData(-3, 1)]
    [InlineData(51, 50)]
    [InlineData(999, 50)]
    public void ClampCount_Bounds(int requested, int expected)
    {
        Assert.Equal(expected, CrewAdminFillRules.ClampCount(requested));
    }

    [Fact]
    public void FormatResult_Partial()
    {
        string msg = CrewAdminFillRules.FormatResult("Construction", 8, 10, 2, "Test Ship");
        Assert.Equal("Filled Construction: assigned 8/10 (2 no seat) on Test Ship", msg);
    }

    [Fact]
    public void FormatResult_Full()
    {
        string msg = CrewAdminFillRules.FormatResult("Salvage Ops", 10, 10, 0, "Grid");
        Assert.Equal("Filled Salvage Ops: assigned 10/10 on Grid", msg);
    }

    [Fact]
    public void Constants()
    {
        Assert.Equal(10, CrewAdminFillRules.DefaultCount);
        Assert.Equal(1, CrewAdminFillRules.MinCount);
        Assert.Equal(50, CrewAdminFillRules.MaxCount);
        Assert.Equal(3, CrewAdminFillRules.FillStars);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/HireCrew.Logic.Tests/HireCrew.Logic.Tests.csproj --filter CrewAdminFillRulesTests`

Expected: FAIL (type/file missing)

- [ ] **Step 3: Implement rules**

Create `Data/Scripts/HireCrew/CrewAdminFillRules.cs`:

```csharp
namespace HireCrew
{
    /// <summary>Pure helpers for /hirecrew fill (no ModAPI).</summary>
    public static class CrewAdminFillRules
    {
        public const int DefaultCount = 10;
        public const int MinCount = 1;
        public const int MaxCount = 50;
        public const int FillStars = 3;

        public static bool IsFillRole(CrewRole role)
        {
            return role == CrewRole.DamageControl || role == CrewRole.SalvageOps;
        }

        public static int ClampCount(int requested)
        {
            if (requested < MinCount) return MinCount;
            if (requested > MaxCount) return MaxCount;
            return requested;
        }

        public static string FormatResult(string roleLabel, int assigned, int requested, int noSeat, string gridName)
        {
            string name = string.IsNullOrEmpty(gridName) ? "?" : gridName;
            string label = string.IsNullOrEmpty(roleLabel) ? "?" : roleLabel;
            if (noSeat > 0)
                return "Filled " + label + ": assigned " + assigned + "/" + requested
                    + " (" + noSeat + " no seat) on " + name;
            return "Filled " + label + ": assigned " + assigned + "/" + requested + " on " + name;
        }
    }
}
```

Add to `tests/HireCrew.Logic.Tests/HireCrew.Logic.Tests.csproj` ItemGroup:

```xml
    <Compile Include="..\..\Data\Scripts\HireCrew\CrewAdminFillRules.cs" Link="CrewAdminFillRules.cs" />
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/HireCrew.Logic.Tests/HireCrew.Logic.Tests.csproj --filter CrewAdminFillRulesTests`

Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add Data/Scripts/HireCrew/CrewAdminFillRules.cs tests/HireCrew.Logic.Tests/CrewAdminFillRulesTests.cs tests/HireCrew.Logic.Tests/HireCrew.Logic.Tests.csproj
git commit -m "$(cat <<'EOF'
Add pure rules for admin fill-crew command.

EOF
)"
```

---

### Task 2: Session assign wrapper for admin fill

**Files:**
- Modify: `Data/Scripts/HireCrew/CrewSession.cs` (near other `Admin*` helpers ~1314)

**Interfaces:**
- Consumes: private `TryApplyAssign(AssignRequest, long, IMyCubeGrid) -> string`
- Produces: `public string AdminTryApplyAssign(AssignRequest req, long adminIdentityId, IMyCubeGrid grid)` — returns null on success, else error string; does not notify/broadcast

- [ ] **Step 1: Add public wrapper**

Near `AdminBroadcastRoster` / other admin helpers in `CrewSession.cs`:

```csharp
/// <summary>Admin fill/hire path: apply seat assign without notify/broadcast. Null = ok.</summary>
public string AdminTryApplyAssign(AssignRequest req, long adminIdentityId, IMyCubeGrid grid)
{
    return TryApplyAssign(req, adminIdentityId, grid);
}
```

- [ ] **Step 2: Commit**

```bash
git add Data/Scripts/HireCrew/CrewSession.cs
git commit -m "$(cat <<'EOF'
Expose admin seat-assign wrapper for fill command.

EOF
)"
```

---

### Task 3: `fill` admin verb

**Files:**
- Modify: `Data/Scripts/HireCrew/CrewAdminCommands.cs`

**Interfaces:**
- Consumes:
  - `CrewAdminFillRules.*`
  - `CrewAdminCommands.TryParseRole`, `GetOnlinePlayer` (existing private)
  - `session.AdminTryApplyAssign`, `AdminResolveOwnerKey`, `AdminBroadcastRoster`, `AdminNotify`, `Store`, `HireRng`
  - `CrewStationLogic.IsAssignableSeat` / `IsSeatOccupiedByPlayer`
  - `CrewConfig.RoleLabel`, `ClampStars`
- Produces: `CmdFill` wired from `Handle` when `verb == "fill"`

- [ ] **Step 1: Wire verb + help**

In `Handle`, after `hire` block (before `reroll`):

```csharp
if (verb == "fill")
{
    CmdFill(session, args, adminIdentityId, adminSteamId);
    return;
}
```

In `SendHelp` list, add after the hire line:

```csharp
"fill <construction|salvage> [count]",
```

- [ ] **Step 2: Implement CmdFill + seat helpers**

Add methods to `CrewAdminCommands` (same file as other `Cmd*`):

```csharp
private static void CmdFill(CrewSession session, List<string> args, long adminIdentityId, ulong steamId)
{
    if (args.Count < 1)
    {
        session.AdminNotify(steamId, "Usage: /hirecrew fill <construction|salvage> [count]");
        return;
    }

    CrewRole role;
    if (!TryParseRole(args[0], out role) || !CrewAdminFillRules.IsFillRole(role))
    {
        session.AdminNotify(steamId, "Bad role. construction|salvage only");
        return;
    }

    int count = CrewAdminFillRules.DefaultCount;
    if (args.Count >= 2)
    {
        int parsed;
        if (!int.TryParse(args[1], out parsed))
        {
            session.AdminNotify(steamId, "Bad count. 1-" + CrewAdminFillRules.MaxCount);
            return;
        }
        count = CrewAdminFillRules.ClampCount(parsed);
    }

    if (session.Store == null)
    {
        session.AdminNotify(steamId, "Store missing");
        return;
    }

    IMyCubeGrid grid;
    if (!TryGetAdminGrid(steamId, out grid))
    {
        session.AdminNotify(steamId, "Not on a grid");
        return;
    }

    long ownerKey;
    bool ownerIsFaction;
    session.AdminResolveOwnerKey(adminIdentityId, out ownerKey, out ownerIsFaction);

    var freeSeats = new List<IMyTerminalBlock>();
    CollectFreeSeats(session, grid, freeSeats);

    int assigned = 0;
    int noSeat = 0;
    int seatIndex = 0;

    for (int i = 0; i < count; i++)
    {
        var record = new CrewRecord
        {
            CrewId = Guid.NewGuid().ToString("N"),
            Stars = CrewConfig.ClampStars(CrewAdminFillRules.FillStars),
            Role = role,
            GridEntityId = 0,
            OwnerIdentityId = adminIdentityId,
            OwnerKey = ownerKey,
            OwnerIsFaction = ownerIsFaction,
            Status = CrewStatus.Unassigned,
            DisplayName = CrewNames.RollFullName(session.HireRng)
        };
        session.Store.Upsert(record);

        if (seatIndex >= freeSeats.Count)
        {
            noSeat++;
            continue;
        }

        var seat = freeSeats[seatIndex++];
        var assignReq = new AssignRequest
        {
            CrewId = record.CrewId,
            GridEntityId = grid.EntityId,
            SeatEntityId = seat.EntityId,
            WeaponEntityId = 0
        };
        string err = session.AdminTryApplyAssign(assignReq, adminIdentityId, grid);
        if (err != null)
        {
            noSeat++;
            continue;
        }
        assigned++;
    }

    session.AdminBroadcastRoster();
    string gridName = grid.CustomName;
    if (string.IsNullOrEmpty(gridName))
        gridName = grid.DisplayName;
    session.AdminNotify(steamId,
        CrewAdminFillRules.FormatResult(CrewConfig.RoleLabel(role), assigned, count, noSeat, gridName ?? "?"));
    MyLog.Default.WriteLineAndConsole(
        "[HireCrew] admin " + steamId + " fill " + role + " assigned " + assigned + "/" + count
        + " grid " + grid.EntityId);
}

private static bool TryGetAdminGrid(ulong adminSteamId, out IMyCubeGrid grid)
{
    grid = null;
    var player = GetOnlinePlayer(adminSteamId);
    if (player == null || player.Character == null)
        return false;
    grid = player.Character.GetTopMostParent() as IMyCubeGrid;
    return grid != null;
}

private static void CollectFreeSeats(CrewSession session, IMyCubeGrid grid, List<IMyTerminalBlock> into)
{
    into.Clear();
    if (grid == null || session == null) return;

    var taken = new HashSet<long>();
    var constructCrew = session.GetCrewForConstruct(grid);
    if (constructCrew != null)
    {
        for (int i = 0; i < constructCrew.Count; i++)
        {
            var c = constructCrew[i];
            if (c != null && c.Status == CrewStatus.Seated && c.SeatEntityId.HasValue)
                taken.Add(c.SeatEntityId.Value);
        }
    }

    var blocks = new List<IMySlimBlock>();
    var parts = new List<IMyCubeGrid>();
    MyAPIGateway.GridGroups.GetGroup(grid, GridLinkTypeEnum.Mechanical, parts);
    if (parts.Count == 0)
        parts.Add(grid);

    for (int g = 0; g < parts.Count; g++)
    {
        var part = parts[g];
        if (part == null) continue;
        blocks.Clear();
        part.GetBlocks(blocks);
        for (int i = 0; i < blocks.Count; i++)
        {
            var slim = blocks[i];
            if (slim == null) continue;
            var term = slim.FatBlock as IMyTerminalBlock;
            if (term == null || term.MarkedForClose) continue;
            if (!CrewStationLogic.IsAssignableSeat(term)) continue;
            if (CrewStationLogic.IsSeatOccupiedByPlayer(term)) continue;
            if (taken.Contains(term.EntityId)) continue;
            into.Add(term);
        }
    }
}
```

Ensure usings already cover `Guid`, `HashSet`, `GridLinkTypeEnum` (`VRage.Game.ModAPI` / existing file usings). Add `using System.Collections.Generic` if missing (already present).

- [ ] **Step 3: In-game smoke (manual)**

1. Admin on ship with ≥10 free crew stations: `/hc fill construction` → notify `assigned 10/10`, HUD shows 10 Construction seated.
2. `/hc fill salvage 5` → 5 Salvage Ops seated.
3. Ship with 3 free seats: `/hc fill construction 10` → `assigned 3/10 (7 no seat)`.
4. Not on grid / `/hc fill gunner` / non-admin → clear errors.

- [ ] **Step 4: Commit**

```bash
git add Data/Scripts/HireCrew/CrewAdminCommands.cs
git commit -m "$(cat <<'EOF'
Add /hc fill to hire and seat construction or salvage crew.

EOF
)"
```

---

### Task 4: Wiki admin docs

**Files:**
- Modify: `.wiki/Admin-Commands.md`

**Interfaces:**
- Consumes: command behavior from Task 3
- Produces: documented `fill` verb + examples

- [ ] **Step 1: Update wiki**

In Quick examples, add:

```
/hc fill construction
/hc fill salvage 5
```

In Command table, add row:

| `fill <construction\|salvage> [count]` | Free-hire N (default 10, max 50) Construction or Salvage Ops at 3★ and seat them on your current grid; partial if seats run out |

- [ ] **Step 2: Commit**

```bash
git add .wiki/Admin-Commands.md
git commit -m "$(cat <<'EOF'
Document admin fill-crew command in wiki.

EOF
)"
```

---

## Spec coverage

| Spec item | Task |
| --- | --- |
| `/hc fill <role> [count]` | 3 |
| construction/salvage only | 1 + 3 |
| default 10, clamp 1–50 | 1 + 3 |
| stars 3, owner admin | 3 |
| current grid via topmost parent | 3 |
| free seats + existing assign | 2 + 3 |
| partial + notify format | 1 + 3 |
| help + server log | 3 |
| wiki | 4 |
| manual verify | 3 step 3 |
