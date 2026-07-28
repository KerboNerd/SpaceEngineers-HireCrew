# Off-Ship Grid Picker Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** When `/crew` opens off-seat, show a Home list of crewed grids plus unassigned crew, with focus enabling Unassign / Train / Dismiss (no remote Assign).

**Architecture:** Add `FocusedGridId` on `CrewHudModel` separate from local `GridEntityId`. Off-seat `FillHome` builds a sectioned scroll list (Grids → On [Grid] → Unassigned). Unassign confirm is allowed when focused; Assign/Quarters/Bulk stay hidden. Server `HandleUnassign` already does not require the player to be seated.

**Tech Stack:** Space Engineers ModAPI, RichHudFramework, xunit logic tests (net48).

## Global Constraints

- Grid picker only when `!HasManagedGrid` (not seated on a managed grid).
- Grid list = unique `GridEntityId` values from local-owner **seated** crew whose grid entity still resolves.
- Off-seat allowed actions: Train, Dismiss, Unassign (focused + seated on focus only).
- Off-seat blocked: Assign, Quarters, Bulk.
- `GridEntityId` remains local seated manage; never set it from the off-seat picker.
- Switching focus clears `SelectedCrewId`.
- User runs tests manually: `dotnet test tests/HireCrew.Logic.Tests/HireCrew.Logic.Tests.csproj` (agent must not run dotnet).
- Spec: `docs/superpowers/specs/2026-07-28-offship-grid-picker-design.md`.

## File structure

| File | Role |
|------|------|
| `Data/Scripts/HireCrew/CrewHudModel.cs` | `FocusedGridId`, toggle/prune helpers, unassign gate, crewed-grid id collect |
| `tests/HireCrew.Logic.Tests/CrewHudModelTests.cs` | Unit tests for focus / unassign / collect |
| `Data/Scripts/HireCrew/CrewHudWindow.cs` | Off-seat `FillHome`, row click, button visibility, status, prune on refresh |
| `Data/Scripts/HireCrew/CrewHud.cs` | Open status message tweak only if needed |

---

### Task 1: CrewHudModel — FocusedGridId + helpers + tests

**Files:**
- Modify: `Data/Scripts/HireCrew/CrewHudModel.cs`
- Modify: `tests/HireCrew.Logic.Tests/CrewHudModelTests.cs`

**Interfaces:**
- Produces:
  - `long FocusedGridId { get; private set; }`
  - `bool HasFocusedGrid { get { return FocusedGridId != 0; } }`
  - `void ClearFocusedGrid()` — sets `FocusedGridId = 0`
  - `void ToggleFocusedGrid(long gridEntityId)` — if `gridEntityId == 0` no-op; if same as current clear; else set; always clears `SelectedCrewId` when focus value changes (including clear)
  - `static List<long> CollectCrewedGridIds(IList<CrewRecord> roster)` — unique non-zero `GridEntityId` among `Status == Seated`, stable first-seen order
  - `bool IsFocusStillValid(IList<CrewRecord> roster)` — true if `FocusedGridId == 0` OR any seated crew has that `GridEntityId`
  - `bool CanUnassignWithFocus(CrewRecord r)` — `CanUnassignHome(r) && HasFocusedGrid && r.GridEntityId == FocusedGridId`
  - Change `TryBeginUnassignFromHome`: allow when `(HasManagedGrid && CanUnassignHome(selected)) || CanUnassignWithFocus(selected)`
  - `Open` / `Close`: reset `FocusedGridId = 0`
  - Do **not** change `IsGridBoundScreen` membership in this task (window handles UnassignPick exception)

- [ ] **Step 1: Add failing tests** to `CrewHudModelTests.cs`:

```csharp
[Fact]
public void Offship_focus_toggle_and_clear_selection()
{
    var m = new CrewHudModel();
    m.Open(0);
    Assert.False(m.HasManagedGrid);
    Assert.False(m.HasFocusedGrid);
    m.SelectedCrewId = "keep-until-change";
    m.ToggleFocusedGrid(100);
    Assert.Equal(100, m.FocusedGridId);
    Assert.True(m.HasFocusedGrid);
    Assert.Null(m.SelectedCrewId);
    m.SelectedCrewId = "a";
    m.ToggleFocusedGrid(100);
    Assert.Equal(0, m.FocusedGridId);
    Assert.Null(m.SelectedCrewId);
    m.ToggleFocusedGrid(200);
    Assert.Equal(200, m.FocusedGridId);
    m.Close();
    Assert.Equal(0, m.FocusedGridId);
}

[Fact]
public void CollectCrewedGridIds_unique_seated_only()
{
    var roster = new List<CrewRecord>
    {
        new CrewRecord { CrewId = "u", Status = CrewStatus.Unassigned, GridEntityId = 0 },
        new CrewRecord { CrewId = "a", Status = CrewStatus.Seated, GridEntityId = 10 },
        new CrewRecord { CrewId = "b", Status = CrewStatus.Seated, GridEntityId = 20 },
        new CrewRecord { CrewId = "c", Status = CrewStatus.Seated, GridEntityId = 10 },
        new CrewRecord { CrewId = "d", Status = CrewStatus.Seated, GridEntityId = 0 },
    };
    var ids = CrewHudModel.CollectCrewedGridIds(roster);
    Assert.Equal(new long[] { 10, 20 }, ids.ToArray());
}

[Fact]
public void TryBeginUnassign_allows_focused_offship()
{
    var m = new CrewHudModel();
    m.Open(0);
    var seated = new CrewRecord { CrewId = "s", Status = CrewStatus.Seated, GridEntityId = 55 };
    Assert.False(m.TryBeginUnassignFromHome(seated));
    m.ToggleFocusedGrid(55);
    Assert.True(m.CanUnassignWithFocus(seated));
    Assert.True(m.TryBeginUnassignFromHome(seated));
    Assert.Equal(CrewHudScreen.UnassignPick, m.Screen);
    Assert.Equal("s", m.SelectedCrewId);
}

[Fact]
public void TryBeginUnassign_rejects_wrong_focus_grid()
{
    var m = new CrewHudModel();
    m.Open(0);
    m.ToggleFocusedGrid(1);
    var seated = new CrewRecord { CrewId = "s", Status = CrewStatus.Seated, GridEntityId = 2 };
    Assert.False(m.CanUnassignWithFocus(seated));
    Assert.False(m.TryBeginUnassignFromHome(seated));
}

[Fact]
public void IsFocusStillValid_false_when_no_seated_on_focus()
{
    var m = new CrewHudModel();
    m.Open(0);
    m.ToggleFocusedGrid(9);
    var roster = new List<CrewRecord>
    {
        new CrewRecord { CrewId = "u", Status = CrewStatus.Unassigned },
        new CrewRecord { CrewId = "s", Status = CrewStatus.Seated, GridEntityId = 8 },
    };
    Assert.False(m.IsFocusStillValid(roster));
    roster.Add(new CrewRecord { CrewId = "t", Status = CrewStatus.Seated, GridEntityId = 9 });
    Assert.True(m.IsFocusStillValid(roster));
}
```

- [ ] **Step 2: Run test to verify it fails**

Run (user): `dotnet test tests/HireCrew.Logic.Tests/HireCrew.Logic.Tests.csproj --filter "Offship_focus|CollectCrewedGridIds|TryBeginUnassign_allows|TryBeginUnassign_rejects|IsFocusStillValid" -v n`

Expected: FAIL (missing members / compile errors).

- [ ] **Step 3: Implement in `CrewHudModel.cs`**

Add property + helpers near `HasManagedGrid`. Update `Open`/`Close` to clear focus. Update `TryBeginUnassignFromHome`:

```csharp
public bool TryBeginUnassignFromHome(CrewRecord selected)
{
    bool ok = (HasManagedGrid && CanUnassignHome(selected)) || CanUnassignWithFocus(selected);
    if (!ok) return false;
    SelectedCrewId = selected.CrewId;
    Screen = CrewHudScreen.UnassignPick;
    ListScrollOffset = 0;
    return true;
}

public bool CanUnassignWithFocus(CrewRecord r)
{
    return CanUnassignHome(r) && HasFocusedGrid && r != null && r.GridEntityId == FocusedGridId;
}

public void ToggleFocusedGrid(long gridEntityId)
{
    if (gridEntityId == 0) return;
    long next = FocusedGridId == gridEntityId ? 0L : gridEntityId;
    if (next == FocusedGridId) return;
    FocusedGridId = next;
    SelectedCrewId = null;
}

public void ClearFocusedGrid()
{
    if (FocusedGridId == 0) return;
    FocusedGridId = 0;
    SelectedCrewId = null;
}

public static List<long> CollectCrewedGridIds(IList<CrewRecord> roster)
{
    var ids = new List<long>();
    if (roster == null) return ids;
    for (int i = 0; i < roster.Count; i++)
    {
        var r = roster[i];
        if (r == null || r.Status != CrewStatus.Seated || r.GridEntityId == 0) continue;
        bool seen = false;
        for (int j = 0; j < ids.Count; j++)
        {
            if (ids[j] == r.GridEntityId) { seen = true; break; }
        }
        if (!seen) ids.Add(r.GridEntityId);
    }
    return ids;
}

public bool IsFocusStillValid(IList<CrewRecord> roster)
{
    if (FocusedGridId == 0) return true;
    if (roster == null) return false;
    for (int i = 0; i < roster.Count; i++)
    {
        var r = roster[i];
        if (r != null && r.Status == CrewStatus.Seated && r.GridEntityId == FocusedGridId)
            return true;
    }
    return false;
}
```

Keep seated-path `TryBeginUnassignFromHome` behavior for `Open(gridId != 0)` covered by existing tests.

- [ ] **Step 4: Run tests to verify they pass**

Run (user): `dotnet test tests/HireCrew.Logic.Tests/HireCrew.Logic.Tests.csproj --filter "FullyQualifiedName~CrewHudModelTests" -v n`

Expected: PASS (including existing unassign/dismiss tests).

- [ ] **Step 5: Commit**

```bash
git add Data/Scripts/HireCrew/CrewHudModel.cs tests/HireCrew.Logic.Tests/CrewHudModelTests.cs
git commit -m "$(cat <<'EOF'
feat: add off-ship FocusedGridId helpers for crew HUD

EOF
)"
```

---

### Task 2: CrewHudWindow — off-seat sectioned Home + Unassign enablement

**Files:**
- Modify: `Data/Scripts/HireCrew/CrewHudWindow.cs`
- Modify: `Data/Scripts/HireCrew/CrewHud.cs` (status string only)

**Interfaces:**
- Consumes: Task 1 APIs (`FocusedGridId`, `ToggleFocusedGrid`, `CollectCrewedGridIds`, `IsFocusStillValid`, `CanUnassignWithFocus`, `ClearFocusedGrid`, `HasFocusedGrid`)
- Produces: Off-seat Home UX per spec; no new public types

- [ ] **Step 1: Fix pool-only gates so UnassignPick works with focus**

In `Refresh()` (`CrewHudWindow.cs`):

1. After resolving `poolOnly`, prune focus:

```csharp
if (poolOnly)
{
    var owned = RosterForManagedGrid(session);
    if (!_model.IsFocusStillValid(owned))
        _model.ClearFocusedGrid();
}
```

2. Replace the `else if (IsGridBoundScreen)` bounce so UnassignPick is allowed when focused:

```csharp
else if (CrewHudModel.IsGridBoundScreen(_model.Screen))
{
    bool allowUnassign = _model.Screen == CrewHudScreen.UnassignPick && _model.HasFocusedGrid;
    if (!allowUnassign)
        _model.GoHome();
}
```

3. Button visibility — show Unassign off-seat when focused (not only `!poolOnly`):

```csharp
bool offshipFocused = poolOnly && _model.HasFocusedGrid;
_btnUnassign.Visible = home && !bulkOn && (!poolOnly || offshipFocused);
```

Keep Assign / Quarters / Bulk gated on `!poolOnly` as today.

4. `canUnassign`:

```csharp
bool canUnassign = !bulkOn && (
    (!poolOnly && CrewHudModel.CanUnassignHome(selectedHome))
    || (poolOnly && _model.CanUnassignWithFocus(selectedHome)));
```

5. Confirm button: treat `unassignPick` like `dismissPick` (always show when on that screen), not under `!poolOnly`:

```csharp
_btnConfirm.Visible = dismissPick || unassignPick || trainConfirm || cancelTrainConfirm || bulkMap
    || (!poolOnly && (assignWeapon || quartersSlots || (assignSeat && seatOnlyAssign)));
```

6. Home status when `poolOnly`:

```csharp
if (poolOnly)
{
    if (_model.HasFocusedGrid)
    {
        string focusName = ResolveGridLabel(_model.FocusedGridId);
        _status.Text = ScrollStatus("Off ship · viewing " + focusName);
    }
    else
        _status.Text = ScrollStatus("Off ship · select a grid");
}
```

7. Context line: existing `FormatHomeContext(selectedHome, !poolOnly)` is fine for train/dismiss; when `offshipFocused` and selected is seated on focus, prefer a short override so Unassign is discoverable:

```csharp
if (poolOnly && _model.CanUnassignWithFocus(selectedHome))
{
    string name = string.IsNullOrEmpty(selectedHome.DisplayName)
        ? CrewConfig.RoleLabel(selectedHome.Role)
        : selectedHome.DisplayName;
    _context.Text = "Selected: " + name + " — Stationed · Unassign, Train, or Dismiss";
}
else
    _context.Text = CrewHudModel.FormatHomeContext(selectedHome, !poolOnly);
```

- [ ] **Step 2: Implement `FillHome` off-seat branch**

Replace / branch `FillHome` so:

```csharp
private void FillHome(CrewSession session)
{
    ClearRows();
    if (_model.BulkMode)
        _model.PruneBulkSelection(id => FindCrewById(session, id));

    if (!_model.HasManagedGrid)
    {
        FillHomeOffship(session);
        return;
    }

    // existing flat roster loop unchanged
    ...
}
```

Add `FillHomeOffship`:

```csharp
private void FillHomeOffship(CrewSession session)
{
    var roster = RosterForManagedGrid(session);
    // Build flat logical rows: header / grid / header / seated / header / unassigned
    // Use a temporary List of a small private struct or parallel lists:
    // kind: 0=header, 1=grid, 2=crew; text; crewId; entityId; selected; interactive

    var gridIds = CrewHudModel.CollectCrewedGridIds(roster);
    // Drop ids that do not resolve in the world (entity missing)
    for (int i = gridIds.Count - 1; i >= 0; i--)
    {
        IMyEntity ent;
        if (!MyAPIGateway.Entities.TryGetEntityById(gridIds[i], out ent) || ent == null
            || !(ent is IMyCubeGrid))
            gridIds.RemoveAt(i);
    }

    // Build full list into a List<> then apply ClampListScroll / MaxRows
    // Section 1: Add header "— Grids —" (non-interactive)
    //   if gridIds.Count == 0: row "(none with crew)" non-interactive
    //   else each grid: text = ResolveGridLabel(id), crewId=null, entityId=id,
    //        selected = (id == FocusedGridId), interactive=true
    // Section 2: if HasFocusedGrid:
    //   header "— On " + ResolveGridLabel(FocusedGridId) + " —"
    //   each seated crew with GridEntityId == FocusedGridId via AddCrewRow
    //   if none (should be pruned already): skip or empty line
    // Section 3: header "— Unassigned —"
    //   each Unassigned crew via AddCrewRow
    //   if none: "(none unassigned)"
    // If roster completely empty (no grids, no unassigned, no seated anywhere):
    //   single "(roster empty — hire at a Crew Hiring Desk)" as today

    _listTotalCount = logicalRowCount;
    int start = _model.ClampListScroll(_listTotalCount, MaxRows);
    // emit visible slice into AddRow / AddCrewRow
}
```

Implementation detail: build `List` of row descriptors first (private nested struct `OffshipRow` with fields `IsHeader`, `IsGrid`, `Text`, `CrewRecord Crew`, `long GridId`, `bool Selected`, `bool Interactive`), then slice. Headers use `AddRow(text, null, 0, false, false)`. Grid rows use `AddRow(label, null, gridId, selected, true)`. Crew use `AddCrewRow`.

Empty roster: if `roster.Count == 0`, one empty hint and return (skip section headers).

- [ ] **Step 3: Wire `OnRowClicked` for grid toggle on Home**

In Home branch of `OnRowClicked`:

```csharp
if (_model.Screen == CrewHudScreen.Home)
{
    if (!string.IsNullOrEmpty(_rowCrewIds[index]))
    {
        // existing bulk / select logic
    }
    else if (!_model.HasManagedGrid && _rowEntityIds[index] != 0)
    {
        _model.ToggleFocusedGrid(_rowEntityIds[index]);
    }
}
```

Call `Refresh()` after focus toggle (existing click path already refreshes via cooldown or call `Refresh()` at end of handler if not already — Home selection currently relies on next Refresh; after toggle call `Refresh()` explicitly so On-[Grid] appears immediately).

- [ ] **Step 4: Update open tell in `CrewHud.cs`**

In `ToggleUi`, when `gridId == 0`:

```csharp
Tell("Crew UI open (off ship)");
```

instead of `"Crew UI open (train & dismiss only)"`.

- [ ] **Step 5: Manual in-game checklist** (agent does not run game)

1. Off-seat, crew on two ships + unassigned: both grids + Unassigned; focus shows only that ship’s seated.
2. Toggle focus off: On section hides; Unassign hidden/disabled.
3. Unassign confirm from focus succeeds; crew appears under Unassigned.
4. Train/Dismiss work without focus.
5. Assign/Quarters/Bulk never appear off-seat.
6. Sit in managed seat: flat roster, full buttons; leave seat closes UI.
7. Unassign last crew on focused grid: focus clears on next refresh.
8. No seated crew: `(none with crew)` + Unassigned.

- [ ] **Step 6: Commit**

```bash
git add Data/Scripts/HireCrew/CrewHudWindow.cs Data/Scripts/HireCrew/CrewHud.cs
git commit -m "$(cat <<'EOF'
feat: off-ship crew HUD grid picker and focused unassign

EOF
)"
```

---

### Task 3: Spec self-check + polish

**Files:**
- Modify: only if gaps found in Task 1–2 files

- [ ] **Step 1: Walk the spec checklist**

Confirm each item from `docs/superpowers/specs/2026-07-28-offship-grid-picker-design.md` Testing section is covered by Task 2 Step 5 or model tests. Fix any miss (e.g. `GoHome` from UnassignPick when focus cleared mid-confirm — Refresh prune + `allowUnassign` false should bounce home).

- [ ] **Step 2: Commit only if code changed**

```bash
git add -A Data/Scripts/HireCrew tests/HireCrew.Logic.Tests
git commit -m "$(cat <<'EOF'
fix: polish off-ship grid picker edge cases

EOF
)"
```

If no code changes, skip commit.

---

## Spec coverage (plan self-review)

| Spec requirement | Task |
|------------------|------|
| Sectioned Home off-seat | Task 2 |
| Grids = seated-only unique ids | Task 1 `CollectCrewedGridIds` + Task 2 resolve filter |
| Focus toggle / clear selection | Task 1 |
| Unassign / Train / Dismiss off-seat | Task 1 + Task 2 |
| No Assign/Quarters/Bulk off-seat | Task 2 visibility |
| Seated UI unchanged | Task 2 `HasManagedGrid` branch |
| No seat-lock off-seat | already true when `GridEntityId==0`; Task 2 prune only |
| Stale focus clears | Task 1 `IsFocusStillValid` + Task 2 Refresh |
| Server unassign without seat | already OK in `HandleUnassign`; no server change |
| Empty states | Task 2 `FillHomeOffship` |
