# Crew Status Sidebar Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a left-side RichHud always-on sidebar that shows active Construction (Damage Control) repair-mission status while seated on a managed grid, toggleable with `/crew hud`.

**Architecture:** Pure `CrewStatusHudModel` builds rows from compact mission snapshots. Server exposes active missions from `CrewRepairMission` and syncs them to clients via a new `RepairMissionSync` message. Client `CrewStatusSidebar` (RichHud on `HudMain.Root`) renders the list; `CrewHud` owns lifecycle, visibility, and the chat toggle.

**Tech Stack:** Space Engineers ModAPI, RichHudFramework, protobuf-net DTOs in `CrewModels`, xunit for logic tests (`HireCrew.Logic.Tests`).

## Global Constraints

- RichHud only — do not add TextHUDAPI / HudAPIv2.
- Show only `CrewRole.DamageControl` with non-`Idle` mission state on the local managed grid.
- Visibility requires seated on managed grid + toggle on (default on) + at least one active row.
- Toggle command: `/crew hud` (does not open management UI).
- Max 6 rows + `+N more`; display-only (no click actions).
- Agent must not run `dotnet` commands; user runs tests.
- Keep `Source/HireCrew/` mirrors in sync for files that already exist there (`CrewModels`, `CrewNetworking`, `CrewHud`, `CrewSession`). New files that only live under `Data/Scripts/HireCrew/` need no Source copy unless Source already has a counterpart.
- Do not touch commented-out code.
- Commit only when the user asks (skip commit steps unless explicitly requested).

## File structure

| File | Role |
|------|------|
| `Data/Scripts/HireCrew/CrewModels.cs` | Host `RepairMissionState` enum + sync DTOs + hint flags |
| `Data/Scripts/HireCrew/CrewStatusHudModel.cs` | Pure row builder / labels / truncation (testable) |
| `Data/Scripts/HireCrew/CrewStatusSidebar.cs` | RichHud left sidebar UI |
| `Data/Scripts/HireCrew/CrewRepairMission.cs` | Enumerate active missions; remove local enum def |
| `Data/Scripts/HireCrew/CrewNetworking.cs` | `RepairMissionSyncMsg` register/unregister |
| `Data/Scripts/HireCrew/CrewSession.cs` | Push/receive sync; tick throttle |
| `Data/Scripts/HireCrew/CrewHud.cs` | Sidebar lifecycle, update, `/crew hud` |
| `Source/HireCrew/CrewModels.cs` | Mirror DTO/enum changes |
| `Source/HireCrew/CrewNetworking.cs` | Mirror msg id |
| `Source/HireCrew/CrewSession.cs` | Mirror sync handling |
| `Source/HireCrew/CrewHud.cs` | Mirror sidebar wiring |
| `tests/HireCrew.Logic.Tests/CrewStatusHudModelTests.cs` | Unit tests |
| `tests/HireCrew.Logic.Tests/HireCrew.Logic.Tests.csproj` | Link `CrewStatusHudModel.cs` |

---

### Task 1: Move `RepairMissionState` + add sync DTOs

**Files:**
- Modify: `Data/Scripts/HireCrew/CrewModels.cs`
- Modify: `Data/Scripts/HireCrew/CrewRepairMission.cs` (remove enum; keep using `HireCrew.RepairMissionState`)
- Modify: `Source/HireCrew/CrewModels.cs` (mirror)
- Test: none yet (types only)

**Interfaces:**
- Consumes: existing `CrewModels` / protobuf patterns
- Produces:
  - `enum RepairMissionState { Idle=0, WalkOut=1, AtExit=2, EvaTransit=3, Welding=4, ReturnExit=5, WalkHome=6 }`
  - `static class RepairMissionHintFlags { public const int OutOfComps = 1; public const int ProjectedTarget = 2; }`
  - `sealed class RepairMissionSnapshotEntry` with ProtoMembers: `CrewId` (string), `DisplayName` (string), `GridEntityId` (long), `State` (int), `Hints` (int)
  - `sealed class RepairMissionSync` with ProtoMember list `Entries`

- [x] **Step 1: Add types to `CrewModels.cs` (before `RepairDispatchRequest`)**

```csharp
public enum RepairMissionState
{
    Idle = 0,
    WalkOut = 1,
    AtExit = 2,
    EvaTransit = 3,
    Welding = 4,
    ReturnExit = 5,
    WalkHome = 6
}

public static class RepairMissionHintFlags
{
    public const int None = 0;
    public const int OutOfComps = 1;
    public const int ProjectedTarget = 2;
}

[ProtoContract]
public sealed class RepairMissionSnapshotEntry
{
    [ProtoMember(1)] public string CrewId;
    [ProtoMember(2)] public string DisplayName;
    [ProtoMember(3)] public long GridEntityId;
    [ProtoMember(4)] public int State;
    [ProtoMember(5)] public int Hints;
}

[ProtoContract]
public sealed class RepairMissionSync
{
    [ProtoMember(1)] public List<RepairMissionSnapshotEntry> Entries = new List<RepairMissionSnapshotEntry>();
}
```

- [ ] **Step 2: Remove the duplicate enum from `CrewRepairMission.cs`**

Delete the local `public enum RepairMissionState { ... }` block at the top of the file. The class already uses `RepairMissionState` in the same namespace — no other renames needed.

- [ ] **Step 3: Mirror the same DTO/enum additions into `Source/HireCrew/CrewModels.cs`**

- [ ] **Step 4: Commit (only if user asked)**

```bash
git add Data/Scripts/HireCrew/CrewModels.cs Data/Scripts/HireCrew/CrewRepairMission.cs Source/HireCrew/CrewModels.cs
git commit -m "refactor: share RepairMissionState and add mission sync DTOs"
```

---

### Task 2: `CrewStatusHudModel` (TDD)

**Files:**
- Create: `Data/Scripts/HireCrew/CrewStatusHudModel.cs`
- Create: `tests/HireCrew.Logic.Tests/CrewStatusHudModelTests.cs`
- Modify: `tests/HireCrew.Logic.Tests/HireCrew.Logic.Tests.csproj`

**Interfaces:**
- Consumes: `RepairMissionState`, `RepairMissionSnapshotEntry`, `RepairMissionHintFlags`, `CrewRole` (not required on entries — filter by non-Idle + optional role check at snapshot build time)
- Produces:
  - `sealed class CrewStatusHudRow { string CrewId; string DisplayName; string RoleLabel; string StatusLabel; string HintLabel; int State; }`
  - `sealed class CrewStatusHudModel` with:
    - `const int MaxVisibleRows = 6`
    - `bool SidebarEnabled { get; set; }` default `true`
    - `void ToggleSidebar()`
    - `static string StatusLabelFor(RepairMissionState state)`
    - `static string HintLabelFor(int hints)`
    - `List<CrewStatusHudRow> BuildRows(IList<RepairMissionSnapshotEntry> entries, long gridEntityId, out int overflowCount)`

- [ ] **Step 1: Add csproj link**

In `HireCrew.Logic.Tests.csproj` ItemGroup, add:

```xml
<Compile Include="..\..\Data\Scripts\HireCrew\CrewStatusHudModel.cs" Link="CrewStatusHudModel.cs" />
```

- [ ] **Step 2: Write failing tests**

```csharp
using System.Collections.Generic;
using HireCrew;
using Xunit;

namespace HireCrew.Logic.Tests
{
    public class CrewStatusHudModelTests
    {
        private static RepairMissionSnapshotEntry Entry(string id, long grid, RepairMissionState state, string name = null, int hints = 0)
        {
            return new RepairMissionSnapshotEntry
            {
                CrewId = id,
                DisplayName = name ?? id,
                GridEntityId = grid,
                State = (int)state,
                Hints = hints
            };
        }

        [Fact]
        public void BuildRows_filters_idle_and_other_grids()
        {
            var entries = new List<RepairMissionSnapshotEntry>
            {
                Entry("a", 1, RepairMissionState.Welding),
                Entry("b", 1, RepairMissionState.Idle),
                Entry("c", 2, RepairMissionState.EvaTransit)
            };
            int overflow;
            var rows = CrewStatusHudModel.BuildRows(entries, 1, out overflow);
            Assert.Equal(1, rows.Count);
            Assert.Equal("a", rows[0].CrewId);
            Assert.Equal(0, overflow);
        }

        [Fact]
        public void StatusLabelFor_maps_states()
        {
            Assert.Equal("Welding", CrewStatusHudModel.StatusLabelFor(RepairMissionState.Welding));
            Assert.Equal("Walking out", CrewStatusHudModel.StatusLabelFor(RepairMissionState.WalkOut));
            Assert.Equal("At airlock", CrewStatusHudModel.StatusLabelFor(RepairMissionState.AtExit));
            Assert.Equal("EVA", CrewStatusHudModel.StatusLabelFor(RepairMissionState.EvaTransit));
            Assert.Equal("Returning", CrewStatusHudModel.StatusLabelFor(RepairMissionState.ReturnExit));
            Assert.Equal("Walking home", CrewStatusHudModel.StatusLabelFor(RepairMissionState.WalkHome));
            Assert.Equal("", CrewStatusHudModel.StatusLabelFor(RepairMissionState.Idle));
        }

        [Fact]
        public void HintLabelFor_out_of_comps_and_projected()
        {
            Assert.Equal("Out of comps", CrewStatusHudModel.HintLabelFor(RepairMissionHintFlags.OutOfComps));
            Assert.Equal("Projector", CrewStatusHudModel.HintLabelFor(RepairMissionHintFlags.ProjectedTarget));
            Assert.Equal("Out of comps · Projector",
                CrewStatusHudModel.HintLabelFor(RepairMissionHintFlags.OutOfComps | RepairMissionHintFlags.ProjectedTarget));
            Assert.Equal("", CrewStatusHudModel.HintLabelFor(0));
        }

        [Fact]
        public void BuildRows_truncates_with_overflow()
        {
            var entries = new List<RepairMissionSnapshotEntry>();
            for (int i = 0; i < 8; i++)
                entries.Add(Entry("c" + i, 5, RepairMissionState.Welding));
            int overflow;
            var rows = CrewStatusHudModel.BuildRows(entries, 5, out overflow);
            Assert.Equal(6, rows.Count);
            Assert.Equal(2, overflow);
        }

        [Fact]
        public void BuildRows_skips_null_or_empty_crew_id()
        {
            var entries = new List<RepairMissionSnapshotEntry>
            {
                Entry(null, 1, RepairMissionState.Welding),
                Entry("", 1, RepairMissionState.Welding),
                Entry("ok", 1, RepairMissionState.Welding, name: "")
            };
            int overflow;
            var rows = CrewStatusHudModel.BuildRows(entries, 1, out overflow);
            Assert.Equal(1, rows.Count);
            Assert.Equal("Crew", rows[0].DisplayName);
        }

        [Fact]
        public void ToggleSidebar_flips_default_on()
        {
            var m = new CrewStatusHudModel();
            Assert.True(m.SidebarEnabled);
            m.ToggleSidebar();
            Assert.False(m.SidebarEnabled);
            m.ToggleSidebar();
            Assert.True(m.SidebarEnabled);
        }
    }
}
```

- [ ] **Step 3: Ask user to run failing tests**

User runs:

```powershell
dotnet test tests/HireCrew.Logic.Tests/HireCrew.Logic.Tests.csproj --filter "FullyQualifiedName~CrewStatusHudModelTests"
```

Expected: FAIL (type/file missing or methods missing).

- [ ] **Step 4: Implement `CrewStatusHudModel.cs`**

```csharp
using System.Collections.Generic;

namespace HireCrew
{
    public sealed class CrewStatusHudRow
    {
        public string CrewId;
        public string DisplayName;
        public string RoleLabel;
        public string StatusLabel;
        public string HintLabel;
        public int State;
    }

    public sealed class CrewStatusHudModel
    {
        public const int MaxVisibleRows = 6;

        public bool SidebarEnabled = true;

        public void ToggleSidebar()
        {
            SidebarEnabled = !SidebarEnabled;
        }

        public static string StatusLabelFor(RepairMissionState state)
        {
            switch (state)
            {
                case RepairMissionState.WalkOut: return "Walking out";
                case RepairMissionState.AtExit: return "At airlock";
                case RepairMissionState.EvaTransit: return "EVA";
                case RepairMissionState.Welding: return "Welding";
                case RepairMissionState.ReturnExit: return "Returning";
                case RepairMissionState.WalkHome: return "Walking home";
                default: return "";
            }
        }

        public static string HintLabelFor(int hints)
        {
            bool outOfComps = (hints & RepairMissionHintFlags.OutOfComps) != 0;
            bool projected = (hints & RepairMissionHintFlags.ProjectedTarget) != 0;
            if (outOfComps && projected) return "Out of comps · Projector";
            if (outOfComps) return "Out of comps";
            if (projected) return "Projector";
            return "";
        }

        public static List<CrewStatusHudRow> BuildRows(
            IList<RepairMissionSnapshotEntry> entries,
            long gridEntityId,
            out int overflowCount)
        {
            overflowCount = 0;
            var rows = new List<CrewStatusHudRow>();
            if (entries == null || gridEntityId == 0) return rows;

            for (int i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                if (e == null || string.IsNullOrEmpty(e.CrewId)) continue;
                if (e.GridEntityId != gridEntityId) continue;
                if (e.State == (int)RepairMissionState.Idle) continue;

                var state = (RepairMissionState)e.State;
                string status = StatusLabelFor(state);
                if (status.Length == 0) continue;

                if (rows.Count >= MaxVisibleRows)
                {
                    overflowCount++;
                    continue;
                }

                string name = e.DisplayName;
                if (string.IsNullOrEmpty(name)) name = "Crew";

                rows.Add(new CrewStatusHudRow
                {
                    CrewId = e.CrewId,
                    DisplayName = name,
                    RoleLabel = "Construction",
                    StatusLabel = status,
                    HintLabel = HintLabelFor(e.Hints),
                    State = e.State
                });
            }

            return rows;
        }
    }
}
```

- [ ] **Step 5: Ask user to run tests again**

Same `dotnet test` filter. Expected: PASS.

- [ ] **Step 6: Commit (only if user asked)**

```bash
git add Data/Scripts/HireCrew/CrewStatusHudModel.cs tests/HireCrew.Logic.Tests/CrewStatusHudModelTests.cs tests/HireCrew.Logic.Tests/HireCrew.Logic.Tests.csproj
git commit -m "feat: add CrewStatusHudModel for active construction sidebar rows"
```

---

### Task 3: Mission snapshot read API

**Files:**
- Modify: `Data/Scripts/HireCrew/CrewRepairMission.cs`

**Interfaces:**
- Consumes: `ByCrew` dictionary, `MissionRuntime`, `CrewSession`/`CrewStore` for display names when available
- Produces:
  - `static void CollectActiveSnapshots(List<RepairMissionSnapshotEntry> into)`
  - Optionally `static void CollectActiveSnapshotsForGrid(long gridEntityId, List<RepairMissionSnapshotEntry> into)` that filters by grid

- [ ] **Step 1: Add public collect API on `CrewRepairMission`**

Place near other public helpers (`TryGetMissionPose`):

```csharp
public static void CollectActiveSnapshots(List<RepairMissionSnapshotEntry> into)
{
    if (into == null) return;
    into.Clear();
    foreach (var kv in ByCrew)
    {
        MissionRuntime m = kv.Value;
        if (m == null || m.State == RepairMissionState.Idle) continue;
        if (string.IsNullOrEmpty(m.CrewId)) continue;

        int hints = RepairMissionHintFlags.None;
        if (m.NotifiedOutOfComps) hints |= RepairMissionHintFlags.OutOfComps;
        if (m.TargetIsProjected) hints |= RepairMissionHintFlags.ProjectedTarget;

        string name = m.CrewId;
        var session = CrewSession.Instance;
        if (session != null)
        {
            CrewRecord crew = session.Store != null ? session.Store.Get(m.CrewId) : null;
            // If Store.Get name differs in this codebase, use the existing lookup used elsewhere
            // (same pattern as repair notify / HUD rows). Prefer DisplayName when present.
            if (crew != null && !string.IsNullOrEmpty(crew.DisplayName))
                name = crew.DisplayName;
        }

        into.Add(new RepairMissionSnapshotEntry
        {
            CrewId = m.CrewId,
            DisplayName = name,
            GridEntityId = m.GridEntityId,
            State = (int)m.State,
            Hints = hints
        });
    }
}
```

Resolve the exact store accessor used in this file (search for `Store.Get` / `GetCrew` patterns already present in `CrewRepairMission` / `CrewSession` and match them — do not invent a new store API).

- [ ] **Step 2: Manual sanity**

No unit test for SE-tied mission runtime. Confirm compile via in-game load later.

- [ ] **Step 3: Commit (only if user asked)**

```bash
git add Data/Scripts/HireCrew/CrewRepairMission.cs
git commit -m "feat: expose active repair mission snapshots for HUD sync"
```

---

### Task 4: Network sync of active missions

**Files:**
- Modify: `Data/Scripts/HireCrew/CrewNetworking.cs`
- Modify: `Data/Scripts/HireCrew/CrewSession.cs`
- Modify: `Source/HireCrew/CrewNetworking.cs`
- Modify: `Source/HireCrew/CrewSession.cs`

**Interfaces:**
- Consumes: `CrewRepairMission.CollectActiveSnapshots`, `RepairMissionSync`
- Produces:
  - `CrewNetworking.RepairMissionSyncMsg = 41748`
  - Client cache on session or hud: `List<RepairMissionSnapshotEntry> ClientRepairMissionEntries`
  - Server tick: push sync when snapshot fingerprint changes, else at most every ~1s
  - `CrewSession` handler branch for `RepairMissionSyncMsg`

- [ ] **Step 1: Register message id**

In both Data and Source `CrewNetworking.cs`:

```csharp
public const ushort RepairMissionSyncMsg = 41748;
```

Add `RegisterSecureMessageHandler` / `UnregisterSecureMessageHandler` for `RepairMissionSyncMsg` next to `RepairDispatchMsg`.

- [ ] **Step 2: Client cache + handler in `CrewSession`**

Add field:

```csharp
private readonly List<RepairMissionSnapshotEntry> _clientRepairMissions = new List<RepairMissionSnapshotEntry>();
public IList<RepairMissionSnapshotEntry> ClientRepairMissions { get { return _clientRepairMissions; } }
```

In message handler:

```csharp
if (id == CrewNetworking.RepairMissionSyncMsg)
{
    if (!isFromServer && !MyAPIGateway.Session.IsServer) return; // follow existing sync patterns for server→client
    var sync = CrewNetworking.Deserialize<RepairMissionSync>(data);
    _clientRepairMissions.Clear();
    if (sync != null && sync.Entries != null)
    {
        for (int i = 0; i < sync.Entries.Count; i++)
            if (sync.Entries[i] != null)
                _clientRepairMissions.Add(sync.Entries[i]);
    }
    return;
}
```

Match the exact `isFromServer` / server-check style used for `RosterMsg` / `HirePoolSyncMsg` in the same handler.

- [ ] **Step 3: Server push helper**

Add fields for throttle/fingerprint:

```csharp
private double _repairMissionSyncCooldown;
private string _lastRepairMissionSyncFingerprint;
private readonly List<RepairMissionSnapshotEntry> _repairMissionSyncBuf = new List<RepairMissionSnapshotEntry>();
```

Add method:

```csharp
private void TickRepairMissionSync(double dt)
{
    if (!MyAPIGateway.Multiplayer.IsServer) return;
    _repairMissionSyncCooldown -= dt;
    CrewRepairMission.CollectActiveSnapshots(_repairMissionSyncBuf);
    string fp = BuildRepairMissionFingerprint(_repairMissionSyncBuf);
    bool changed = fp != _lastRepairMissionSyncFingerprint;
    if (!changed && _repairMissionSyncCooldown > 0) return;
    _repairMissionSyncCooldown = 1.0;
    _lastRepairMissionSyncFingerprint = fp;

    var sync = new RepairMissionSync { Entries = new List<RepairMissionSnapshotEntry>(_repairMissionSyncBuf) };
    byte[] data = CrewNetworking.Serialize(sync);

    // Also keep local client cache on listen/SP host
    if (!MyAPIGateway.Utilities.IsDedicated)
    {
        _clientRepairMissions.Clear();
        _clientRepairMissions.AddRange(_repairMissionSyncBuf);
    }

    var players = new List<IMyPlayer>();
    MyAPIGateway.Players.GetPlayers(players);
    for (int i = 0; i < players.Count; i++)
    {
        var p = players[i];
        if (p == null) continue;
        ulong steam = p.SteamUserId;
        if (steam == 0) continue;
        if (MyAPIGateway.Multiplayer.IsServer && steam == MyAPIGateway.Multiplayer.MyId
            && !MyAPIGateway.Utilities.IsDedicated)
            continue; // already applied locally on listen
        CrewNetworking.SendToPlayer(CrewNetworking.RepairMissionSyncMsg, data, steam);
    }
}

private static string BuildRepairMissionFingerprint(List<RepairMissionSnapshotEntry> entries)
{
    var sb = new System.Text.StringBuilder();
    for (int i = 0; i < entries.Count; i++)
    {
        var e = entries[i];
        if (e == null) continue;
        sb.Append(e.CrewId).Append('|')
          .Append(e.GridEntityId).Append('|')
          .Append(e.State).Append('|')
          .Append(e.Hints).Append(';');
    }
    return sb.ToString();
}
```

Call `TickRepairMissionSync` from the existing server update path (same place roster sync / repair mission tick runs). Use the session’s actual delta-time or a 1-frame count equivalent already used nearby.

On pure clients that are not server: for SP where `IsServer` is true, local cache fill above covers it. If a code path updates missions only on server, DS clients rely on the packet.

- [ ] **Step 4: Mirror Source `CrewSession` / `CrewNetworking` changes**

- [ ] **Step 5: Commit (only if user asked)**

```bash
git add Data/Scripts/HireCrew/CrewNetworking.cs Data/Scripts/HireCrew/CrewSession.cs Source/HireCrew/CrewNetworking.cs Source/HireCrew/CrewSession.cs
git commit -m "feat: sync active repair missions to clients for status HUD"
```

---

### Task 5: `CrewStatusSidebar` RichHud UI

**Files:**
- Create: `Data/Scripts/HireCrew/CrewStatusSidebar.cs`

**Interfaces:**
- Consumes: `CrewStatusHudRow`, RichHud `HudElementBase`, `HudMain.Root`
- Produces: `sealed class CrewStatusSidebar : HudElementBase` with `void Apply(IList<CrewStatusHudRow> rows, int overflowCount, bool visible)`

- [ ] **Step 1: Implement sidebar**

Create a compact left-aligned panel:

```csharp
using System.Collections.Generic;
using RichHudFramework.UI;
using RichHudFramework.UI.Client;
using RichHudFramework.UI.Rendering;
using VRageMath;

namespace HireCrew
{
    public sealed class CrewStatusSidebar : HudElementBase
    {
        private const int MaxRows = CrewStatusHudModel.MaxVisibleRows;
        private const float PanelW = 220f;
        private const float RowH = 36f;
        private const float Pad = 8f;

        private readonly TexturedBox _bg;
        private readonly Label[] _line1;
        private readonly Label[] _line2;
        private readonly TexturedBox[] _bars;
        private readonly Label _overflow;

        public CrewStatusSidebar(HudParentBase parent) : base(parent)
        {
            Size = new Vector2(PanelW, MaxRows * RowH + Pad * 2 + 18f);
            // Left-middle: negative X toward left of HudMain root
            Offset = new Vector2(-HudMain.ScreenWidth * 0.5f + PanelW * 0.5f + 16f, 40f);
            Visible = false;

            _bg = new TexturedBox(this)
            {
                DimAlignment = DimAlignments.Both,
                Color = new Color(8, 14, 22, 160),
                ZOffset = -1
            };

            _line1 = new Label[MaxRows];
            _line2 = new Label[MaxRows];
            _bars = new TexturedBox[MaxRows];

            for (int i = 0; i < MaxRows; i++)
            {
                float y = Pad + i * RowH;
                _bars[i] = new TexturedBox(this)
                {
                    Size = new Vector2(3f, RowH - 6f),
                    Offset = new Vector2(-PanelW * 0.5f + 6f, -y - RowH * 0.5f),
                    Color = new Color(255, 220, 120),
                    Visible = false
                };
                _line1[i] = new Label(this)
                {
                    AutoResize = false,
                    Size = new Vector2(PanelW - 20f, 16f),
                    Offset = new Vector2(4f, -y - 10f),
                    Format = new GlyphFormat(new Color(230, 235, 240), TextSize: 0.7f),
                    Visible = false
                };
                _line2[i] = new Label(this)
                {
                    AutoResize = false,
                    Size = new Vector2(PanelW - 20f, 14f),
                    Offset = new Vector2(4f, -y - 26f),
                    Format = new GlyphFormat(new Color(170, 185, 200), TextSize: 0.62f),
                    Visible = false
                };
            }

            _overflow = new Label(this)
            {
                AutoResize = false,
                Size = new Vector2(PanelW - 16f, 14f),
                Format = new GlyphFormat(new Color(160, 170, 180), TextSize: 0.6f),
                Visible = false
            };
        }

        public void Apply(IList<CrewStatusHudRow> rows, int overflowCount, bool visible)
        {
            if (!visible || rows == null || rows.Count == 0)
            {
                Visible = false;
                return;
            }

            Visible = true;
            // Recompute left offset each apply in case resolution changes
            Offset = new Vector2(-HudMain.ScreenWidth * 0.5f + PanelW * 0.5f + 16f, 40f);

            int n = rows.Count < MaxRows ? rows.Count : MaxRows;
            float h = Pad * 2 + n * RowH + (overflowCount > 0 ? 16f : 0f);
            Size = new Vector2(PanelW, h);

            for (int i = 0; i < MaxRows; i++)
            {
                if (i >= n)
                {
                    _line1[i].Visible = false;
                    _line2[i].Visible = false;
                    _bars[i].Visible = false;
                    continue;
                }

                var r = rows[i];
                _bars[i].Visible = true;
                _bars[i].Color = BarColor(r.State);
                _line1[i].Visible = true;
                _line1[i].Text = r.DisplayName + " · " + r.RoleLabel;
                _line1[i].Format = new GlyphFormat(new Color(255, 220, 120), TextSize: 0.7f);

                string line2 = r.StatusLabel;
                if (!string.IsNullOrEmpty(r.HintLabel))
                    line2 = line2 + " — " + r.HintLabel;
                _line2[i].Visible = true;
                _line2[i].Text = line2;
            }

            if (overflowCount > 0)
            {
                _overflow.Visible = true;
                _overflow.Text = "+" + overflowCount + " more";
                _overflow.Offset = new Vector2(4f, -(Pad + n * RowH + 4f));
            }
            else
            {
                _overflow.Visible = false;
            }
        }

        private static Color BarColor(int state)
        {
            switch ((RepairMissionState)state)
            {
                case RepairMissionState.Welding: return new Color(255, 200, 80);
                case RepairMissionState.EvaTransit: return new Color(120, 200, 255);
                case RepairMissionState.ReturnExit:
                case RepairMissionState.WalkHome: return new Color(160, 180, 140);
                default: return new Color(255, 220, 120);
            }
        }
    }
}
```

Adjust Offset/Size conventions to match how `CrewHudWindow` positions itself (read its constructor for `ParentAlignment` / coordinate sign). Prefer matching existing RichHud patterns in this mod over the sketch offsets if they conflict.

Disable cursor on the sidebar (`UseCursor = false`, `ShareCursor = false`) so it never steals input.

- [ ] **Step 2: Commit (only if user asked)**

```bash
git add Data/Scripts/HireCrew/CrewStatusSidebar.cs
git commit -m "feat: add RichHud crew status sidebar panel"
```

---

### Task 6: Wire into `CrewHud` + `/crew hud`

**Files:**
- Modify: `Data/Scripts/HireCrew/CrewHud.cs`
- Modify: `Source/HireCrew/CrewHud.cs`

**Interfaces:**
- Consumes: `CrewStatusHudModel`, `CrewStatusSidebar`, `CrewSession.ClientRepairMissions`, `TryGetLocalManagedGrid`
- Produces: working always-on sidebar + chat toggle

- [ ] **Step 1: Fields + RHF lifecycle**

```csharp
private readonly CrewStatusHudModel _statusModel = new CrewStatusHudModel();
private CrewStatusSidebar _statusSidebar;
```

In `OnHudReady`, after windows created:

```csharp
if (_statusSidebar == null)
    _statusSidebar = new CrewStatusSidebar(HudMain.Root);
```

In `OnHudReset` / `Unload`:

```csharp
_statusSidebar = null;
```

- [ ] **Step 2: Fix `Update()` so sidebar runs even when management UI is closed**

Current code returns early on `if (!_model.IsOpen) return;`. Restructure:

```csharp
public void Update()
{
    if (MyAPIGateway.Utilities == null || MyAPIGateway.Utilities.IsDedicated) return;

    if (!_chatRegistered)
        EnsureChatRegistered();

    CrewAmbientNameplates.Update(CrewSession.Instance);

    // hire window block... (unchanged)

    UpdateStatusSidebar();

    if (!_model.IsOpen) return;

    // existing managed-grid close + refresh logic...
}
```

Implement:

```csharp
private void UpdateStatusSidebar()
{
    if (_statusSidebar == null || !_rhfReady) return;

    if (!_statusModel.SidebarEnabled)
    {
        _statusSidebar.Apply(null, 0, false);
        return;
    }

    var session = CrewSession.Instance;
    IMyCubeGrid grid;
    string err;
    if (session == null || !session.TryGetLocalManagedGrid(out grid, out err) || grid == null)
    {
        _statusSidebar.Apply(null, 0, false);
        return;
    }

    IList<RepairMissionSnapshotEntry> entries = session.ClientRepairMissions;
    // SP/listen: if cache empty but we are server, collect directly as fallback
    if ((entries == null || entries.Count == 0) && MyAPIGateway.Multiplayer.IsServer)
    {
        var buf = new List<RepairMissionSnapshotEntry>();
        CrewRepairMission.CollectActiveSnapshots(buf);
        entries = buf;
    }

    int overflow;
    var rows = CrewStatusHudModel.BuildRows(entries, grid.EntityId, out overflow);
    _statusSidebar.Apply(rows, overflow, rows.Count > 0);
}
```

- [ ] **Step 3: Chat command**

In `OnMessageEntered` crew branch, after `path` handling:

```csharp
if (string.Equals(tokens[1], "hud", StringComparison.OrdinalIgnoreCase))
{
    _statusModel.ToggleSidebar();
    Tell(_statusModel.SidebarEnabled ? "Crew status HUD on" : "Crew status HUD off");
    return;
}
```

Update usage string:

```csharp
Tell("Usage: /crew | /crew hud | /crew path [start|undo|done|clear|stop]");
```

- [ ] **Step 4: Mirror Source `CrewHud.cs`**

- [ ] **Step 5: Commit (only if user asked)**

```bash
git add Data/Scripts/HireCrew/CrewHud.cs Source/HireCrew/CrewHud.cs
git commit -m "feat: wire construction status sidebar and /crew hud toggle"
```

---

### Task 7: In-game verification checklist

**Files:** none (manual)

- [ ] **Step 1: User runs unit tests**

```powershell
dotnet test tests/HireCrew.Logic.Tests/HireCrew.Logic.Tests.csproj --filter "FullyQualifiedName~CrewStatusHudModelTests"
```

Expected: all PASS.

- [ ] **Step 2: In-game SP checklist**

1. Sit in a seat on a managed grid with Construction crew assigned.
2. Sidebar hidden while all idle.
3. Dispatch Construction repair → left sidebar shows name · Construction + status.
4. `/crew hud` toggles off/on.
5. Leave seat → sidebar hides.
6. Mission completes → row disappears; no empty panel.

- [ ] **Step 3: DS checklist (if available)**

Client seated on managed grid sees mission state updates without being the server.

---

## Spec coverage check

| Spec requirement | Task |
|------------------|------|
| RichHud always-on left sidebar | 5, 6 |
| Seated + managed grid + toggle | 6 |
| Only active Construction missions | 2, 3 |
| Medium row content + hints | 2, 5 |
| `/crew hud` default on | 2, 6 |
| Hide when empty | 5, 6 |
| Max 6 + overflow | 2, 5 |
| Mission sync for DS | 4 |
| Unit tests for model | 2 |
| No TextHUDAPI | Global Constraints |

## Type consistency

- `RepairMissionState` lives in `CrewModels.cs`; `CrewRepairMission` / model / sidebar all use that enum.
- Sync entry `State`/`Hints` are `int`; model casts/compares to enum and `RepairMissionHintFlags`.
- Msg id `41748` / `RepairMissionSyncMsg` used in networking + session only.
- `CrewStatusHudModel.MaxVisibleRows` shared with sidebar `MaxRows`.
