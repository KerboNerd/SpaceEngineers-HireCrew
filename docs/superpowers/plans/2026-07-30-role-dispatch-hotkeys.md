# Role Dispatch Hotkeys Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add remappable End/Delete hotkeys that batch Send or Recall all seated Construction or Salvage crew on the local managed grid.

**Architecture:** Pure rules decide bind press + Recall-vs-Send. Client resolves the seated managed grid, scans local roster/mission sync, and sends `RoleDispatchBatchRequest`. Server authenticates once, recalls or dispatches all eligible crew of that role, and returns one summary notify. Keybinds extend the existing HireCrew Rich HUD bind group.

**Tech Stack:** Space Engineers ModAPI, RichHudFramework BindManager, protobuf-net DTOs, xunit (`HireCrew.Logic.Tests`).

## Global Constraints

- Spec: `docs/superpowers/specs/2026-07-30-role-dispatch-hotkeys-design.md`
- Defaults: Construction = `End`, Salvage = `Delete`; remappable in Rich HUD.
- Seated managed grid only (`TryGetLocalManagedGrid`).
- Toggle: any of that role on mission on the grid → Recall all; else Send all idle eligible.
- Msg id: `RoleDispatchBatchMsg = 41753`.
- Roles: `CrewRole.DamageControl`, `CrewRole.SalvageOps` only.
- Do not change per-crew HUD Send/Recall.
- Agent must not run `dotnet` commands; user runs tests.
- Do not touch commented-out code.
- Commit only when the user asks.
- Leave unrelated dirty files (`CrewConfig.cs`, `CrewRepairMission.cs`, `.cursor/`) alone.

## File structure

| File | Role |
|------|------|
| `Data/Scripts/HireCrew/CrewModels.cs` | `RoleDispatchBatchRequest` DTO |
| `Data/Scripts/HireCrew/CrewNetworking.cs` | Msg id + register/unregister |
| `Data/Scripts/HireCrew/CrewKeyBindRules.cs` | Press gate + Recall decision + summary text |
| `Data/Scripts/HireCrew/CrewKeyBinds.cs` | Two new binds + register fix |
| `Data/Scripts/HireCrew/CrewSession.cs` | Client request + server batch handler |
| `Data/Scripts/HireCrew/CrewHud.cs` | Poll binds → request |
| `tests/HireCrew.Logic.Tests/CrewKeyBindRulesTests.cs` | Extended unit tests |
| `tests/HireCrew.Logic.Tests/HireCrew.Logic.Tests.csproj` | Already links `CrewKeyBindRules.cs` |

---

### Task 1: Pure rules + unit tests

**Files:**
- Modify: `Data/Scripts/HireCrew/CrewKeyBindRules.cs`
- Modify: `tests/HireCrew.Logic.Tests/CrewKeyBindRulesTests.cs`

**Interfaces:**
- Consumes: existing `ShouldToggleOpenCrewUi`
- Produces:
  - `public static bool ShouldHandleBind(bool bindNewPressed, bool chatOpen)` — same logic as open-UI gate (implement by calling `ShouldToggleOpenCrewUi` or shared body)
  - `public static bool ShouldRecallRole(bool anyOfRoleOnMission)`
  - `public static string FormatRoleDispatchSummary(string roleLabel, bool recall, int count)` — e.g. `"Construction: sent 3"`, `"Salvage: recalling 1"`, `"Construction: none ready"` when `count == 0`

- [ ] **Step 1: Add failing tests** to `CrewKeyBindRulesTests.cs`

```csharp
        [Fact]
        public void ShouldHandleBind_matches_open_ui_gate()
        {
            Assert.True(CrewKeyBindRules.ShouldHandleBind(true, false));
            Assert.False(CrewKeyBindRules.ShouldHandleBind(true, true));
            Assert.False(CrewKeyBindRules.ShouldHandleBind(false, false));
        }

        [Fact]
        public void ShouldRecallRole_when_any_on_mission()
        {
            Assert.True(CrewKeyBindRules.ShouldRecallRole(true));
            Assert.False(CrewKeyBindRules.ShouldRecallRole(false));
        }

        [Fact]
        public void FormatRoleDispatchSummary_sent_recall_none()
        {
            Assert.Equal("Construction: sent 3", CrewKeyBindRules.FormatRoleDispatchSummary("Construction", false, 3));
            Assert.Equal("Salvage: recalling 2", CrewKeyBindRules.FormatRoleDispatchSummary("Salvage", true, 2));
            Assert.Equal("Construction: none ready", CrewKeyBindRules.FormatRoleDispatchSummary("Construction", false, 0));
            Assert.Equal("Salvage: none ready", CrewKeyBindRules.FormatRoleDispatchSummary("Salvage", true, 0));
        }
```

- [ ] **Step 2: User runs tests — expect FAIL**

```powershell
dotnet test tests/HireCrew.Logic.Tests/HireCrew.Logic.Tests.csproj --filter FullyQualifiedName~CrewKeyBindRulesTests
```

- [ ] **Step 3: Implement in `CrewKeyBindRules.cs`**

```csharp
namespace HireCrew
{
    /// <summary>
    /// Pure rules for crew hotkey handling (no ModAPI / RichHud).
    /// </summary>
    public static class CrewKeyBindRules
    {
        public static bool ShouldToggleOpenCrewUi(bool bindNewPressed, bool chatOpen)
        {
            return ShouldHandleBind(bindNewPressed, chatOpen);
        }

        public static bool ShouldHandleBind(bool bindNewPressed, bool chatOpen)
        {
            return bindNewPressed && !chatOpen;
        }

        public static bool ShouldRecallRole(bool anyOfRoleOnMission)
        {
            return anyOfRoleOnMission;
        }

        public static string FormatRoleDispatchSummary(string roleLabel, bool recall, int count)
        {
            if (string.IsNullOrEmpty(roleLabel))
                roleLabel = "Crew";
            if (count <= 0)
                return roleLabel + ": none ready";
            if (recall)
                return roleLabel + ": recalling " + count;
            return roleLabel + ": sent " + count;
        }
    }
}
```

- [ ] **Step 4: User re-runs tests — expect PASS**

- [ ] **Step 5: Commit only if user asked**

```bash
git add Data/Scripts/HireCrew/CrewKeyBindRules.cs tests/HireCrew.Logic.Tests/CrewKeyBindRulesTests.cs
git commit -m "feat: add role dispatch hotkey decision rules"
```

---

### Task 2: DTO + networking

**Files:**
- Modify: `Data/Scripts/HireCrew/CrewModels.cs` (after `RepairDispatchRequest`)
- Modify: `Data/Scripts/HireCrew/CrewNetworking.cs`

**Interfaces:**
- Produces: `RoleDispatchBatchRequest` with ProtoMembers `GridEntityId` (long), `Role` (int), `Recall` (bool)
- Produces: `CrewNetworking.RoleDispatchBatchMsg = 41753` registered/unregistered

- [ ] **Step 1: Add DTO** in `CrewModels.cs` immediately after `RepairDispatchRequest`:

```csharp
    [ProtoContract]
    public sealed class RoleDispatchBatchRequest
    {
        [ProtoMember(1)] public long GridEntityId;
        /// <summary><see cref="CrewRole"/> as int. Only DamageControl and SalvageOps are valid.</summary>
        [ProtoMember(2)] public int Role;
        /// <summary>false = Send all idle eligible; true = Recall all on mission.</summary>
        [ProtoMember(3)] public bool Recall;
    }
```

- [ ] **Step 2: Add msg id** after `SalvageTargetSyncMsg`:

```csharp
        public const ushort RoleDispatchBatchMsg = 41753;
```

- [ ] **Step 3: Register + unregister** in both `Register` and `Unregister` methods (same pattern as `SalvageTargetSyncMsg`):

```csharp
            MyAPIGateway.Multiplayer.RegisterSecureMessageHandler(RoleDispatchBatchMsg, handler);
```

```csharp
            MyAPIGateway.Multiplayer.UnregisterSecureMessageHandler(RoleDispatchBatchMsg, handler);
```

- [ ] **Step 4: Commit only if user asked**

```bash
git add Data/Scripts/HireCrew/CrewModels.cs Data/Scripts/HireCrew/CrewNetworking.cs
git commit -m "feat: add RoleDispatchBatch networking"
```

---

### Task 3: Server batch handler + client request API

**Files:**
- Modify: `Data/Scripts/HireCrew/CrewSession.cs`

**Interfaces:**
- Consumes: `RoleDispatchBatchRequest`, `CrewRepairMission.DispatchCrew` / `RecallCrew`, `CrewSalvageMission.DispatchCrew` / `RecallCrew`, `ResolveSalvageZone`, `HasManagePermission`, `CrewAmbientPresence.IsGridIdle`, `Store.All`, `Notify`, `CrewKeyBindRules.FormatRoleDispatchSummary`
- Produces:
  - `public void ClientRequestRoleDispatchBatch(long gridEntityId, CrewRole role, bool recall)`
  - `private void HandleRoleDispatchBatch(RoleDispatchBatchRequest req, long identityId, ulong steamId)`
  - Wire in `OnMessage` when `id == CrewNetworking.RoleDispatchBatchMsg`

- [ ] **Step 1: Add client API** next to `ClientRequestSalvageDispatch`:

```csharp
        public void ClientRequestRoleDispatchBatch(long gridEntityId, CrewRole role, bool recall)
        {
            if (gridEntityId == 0)
                return;
            if (role != CrewRole.DamageControl && role != CrewRole.SalvageOps)
                return;
            var req = new RoleDispatchBatchRequest
            {
                GridEntityId = gridEntityId,
                Role = (int)role,
                Recall = recall
            };
            var data = CrewNetworking.Serialize(req);
            if (MyAPIGateway.Multiplayer.IsServer)
                HandleRoleDispatchBatch(req, MyAPIGateway.Session.Player.IdentityId, MyAPIGateway.Multiplayer.MyId);
            else
                CrewNetworking.SendToServer(CrewNetworking.RoleDispatchBatchMsg, data);
        }
```

- [ ] **Step 2: Wire `OnMessage`** after salvage dispatch branch:

```csharp
            else if (id == CrewNetworking.RoleDispatchBatchMsg)
                HandleRoleDispatchBatch(CrewNetworking.Deserialize<RoleDispatchBatchRequest>(data), identityId, sender);
```

- [ ] **Step 3: Implement `HandleRoleDispatchBatch`** near `HandleRepairDispatch`:

```csharp
        private void HandleRoleDispatchBatch(RoleDispatchBatchRequest req, long identityId, ulong steamId)
        {
            if (req == null || Store == null || req.GridEntityId == 0)
                return;

            var role = (CrewRole)req.Role;
            if (role != CrewRole.DamageControl && role != CrewRole.SalvageOps)
                return;

            string label = role == CrewRole.SalvageOps ? "Salvage" : "Construction";

            IMyCubeGrid grid;
            if (!TryGetGrid(req.GridEntityId, out grid) || grid == null)
            {
                Notify(steamId, label + ": grid not found");
                return;
            }
            if (!HasManagePermission(identityId, grid))
            {
                Notify(steamId, "No permission");
                return;
            }

            if (req.Recall)
            {
                int recalled = 0;
                foreach (var crew in Store.All)
                {
                    if (crew == null || crew.Role != role || crew.GridEntityId != req.GridEntityId)
                        continue;
                    bool ok = role == CrewRole.SalvageOps
                        ? CrewSalvageMission.RecallCrew(crew.CrewId)
                        : CrewRepairMission.RecallCrew(crew.CrewId);
                    if (ok)
                        recalled++;
                }
                Notify(steamId, CrewKeyBindRules.FormatRoleDispatchSummary(label, true, recalled));
                return;
            }

            if (!CrewAmbientPresence.IsGridIdle(grid))
            {
                Notify(steamId, label + ": grid moving — wait");
                return;
            }

            BoundingBoxD zone = default(BoundingBoxD);
            long seedId = 0;
            if (role == CrewRole.SalvageOps)
            {
                if (!ResolveSalvageZone(grid, out zone, out seedId))
                {
                    Notify(steamId, "Salvage: no target — /crew salvage then LMB a wreck");
                    return;
                }
            }

            int sent = 0;
            foreach (var crew in Store.All)
            {
                if (crew == null || crew.Role != role || crew.GridEntityId != req.GridEntityId)
                    continue;
                if (crew.Status != CrewStatus.Seated)
                    continue;

                bool started;
                if (role == CrewRole.SalvageOps)
                {
                    if (CrewSalvageMission.IsCrewOnMission(crew.CrewId))
                        continue;
                    started = CrewSalvageMission.DispatchCrew(this, crew.CrewId, zone, seedId);
                }
                else
                {
                    if (CrewRepairMission.IsCrewOnMission(crew.CrewId))
                        continue;
                    started = CrewRepairMission.DispatchCrew(this, crew.CrewId);
                }
                if (started)
                    sent++;
            }

            Notify(steamId, CrewKeyBindRules.FormatRoleDispatchSummary(label, false, sent));
        }
```

- [ ] **Step 4: Commit only if user asked**

```bash
git add Data/Scripts/HireCrew/CrewSession.cs
git commit -m "feat: handle role dispatch batch on server"
```

---

### Task 4: Keybinds + CrewHud wiring

**Files:**
- Modify: `Data/Scripts/HireCrew/CrewKeyBinds.cs`
- Modify: `Data/Scripts/HireCrew/CrewHud.cs`

**Interfaces:**
- Consumes: `CrewKeyBinds.SendRecallConstruction` / `SendRecallSalvage`, `CrewKeyBindRules`, `CrewSession.ClientRequestRoleDispatchBatch`, `TryGetLocalManagedGrid`, `IsCrewOnRepairMission` / `IsCrewOnSalvageMission`, `Store.All`
- Produces: End/Delete (defaults) trigger batch dispatch

- [ ] **Step 1: Extend `CrewKeyBinds`**

Replace the early-return / single-bind registration so **all three** binds are ensured:

```csharp
using RichHudFramework.UI;
using RichHudFramework.UI.Client;
using VRage.Input;

namespace HireCrew
{
    /// <summary>
    /// Client keybinds for HireCrew via RichHud BindManager + RebindPage.
    /// </summary>
    public static class CrewKeyBinds
    {
        public const string GroupName = "HireCrew";
        public const string OpenCrewUiName = "Open Crew UI";
        public const string SendRecallConstructionName = "Send/Recall Construction";
        public const string SendRecallSalvageName = "Send/Recall Salvage";

        private static IBindGroup _group;
        private static IBind _openCrewUi;
        private static IBind _sendRecallConstruction;
        private static IBind _sendRecallSalvage;
        private static bool _rebindPageAdded;

        public static IBind OpenCrewUi { get { return _openCrewUi; } }
        public static IBind SendRecallConstruction { get { return _sendRecallConstruction; } }
        public static IBind SendRecallSalvage { get { return _sendRecallSalvage; } }

        public static void Register()
        {
            if (_openCrewUi != null && _sendRecallConstruction != null && _sendRecallSalvage != null)
                return;

            _group = BindManager.GetOrCreateGroup(GroupName);

            var defaults = new BindGroupInitializer
            {
                { OpenCrewUiName, MyKeys.Home },
                { SendRecallConstructionName, MyKeys.End },
                { SendRecallSalvageName, MyKeys.Delete }
            };

            if (!_group.DoesBindExist(OpenCrewUiName)
                || !_group.DoesBindExist(SendRecallConstructionName)
                || !_group.DoesBindExist(SendRecallSalvageName))
            {
                var missing = new BindGroupInitializer();
                if (!_group.DoesBindExist(OpenCrewUiName))
                    missing.Add(OpenCrewUiName, MyKeys.Home);
                if (!_group.DoesBindExist(SendRecallConstructionName))
                    missing.Add(SendRecallConstructionName, MyKeys.End);
                if (!_group.DoesBindExist(SendRecallSalvageName))
                    missing.Add(SendRecallSalvageName, MyKeys.Delete);
                _group.RegisterBinds(missing);
            }

            _openCrewUi = _group[OpenCrewUiName];
            _sendRecallConstruction = _group[SendRecallConstructionName];
            _sendRecallSalvage = _group[SendRecallSalvageName];

            if (!_rebindPageAdded)
            {
                var page = new RebindPage
                {
                    Name = "Key Binds",
                    Enabled = true
                };
                page.Add(_group, defaults.GetBindDefinitions());
                RichHudTerminal.Root.Enabled = true;
                RichHudTerminal.Root.Add(page);
                _rebindPageAdded = true;
            }
        }

        public static void Clear()
        {
            _openCrewUi = null;
            _sendRecallConstruction = null;
            _sendRecallSalvage = null;
            _group = null;
            _rebindPageAdded = false;
        }
    }
}
```

- [ ] **Step 2: Add private helper on `CrewHud`** (near `Tell`):

```csharp
        private void TryHotkeyRoleDispatch(CrewRole role)
        {
            var session = CrewSession.Instance;
            if (session == null || session.Store == null)
            {
                Tell("HireCrew not ready");
                return;
            }

            IMyCubeGrid grid;
            string err;
            if (!session.TryGetLocalManagedGrid(out grid, out err) || grid == null)
            {
                Tell(string.IsNullOrEmpty(err) ? "Sit in a seat to manage crew" : err);
                return;
            }

            long gridId = grid.EntityId;
            bool anyOnMission = false;
            foreach (var crew in session.Store.All)
            {
                if (crew == null || crew.Role != role || crew.GridEntityId != gridId)
                    continue;
                if (role == CrewRole.SalvageOps
                    ? session.IsCrewOnSalvageMission(crew.CrewId)
                    : session.IsCrewOnRepairMission(crew.CrewId))
                {
                    anyOnMission = true;
                    break;
                }
            }

            bool recall = CrewKeyBindRules.ShouldRecallRole(anyOnMission);
            session.ClientRequestRoleDispatchBatch(gridId, role, recall);
        }
```

- [ ] **Step 3: Poll both binds in `Update`** immediately after the Open Crew UI poll (still before `if (!_model.IsOpen) return`):

```csharp
            var openBind = CrewKeyBinds.OpenCrewUi;
            if (openBind != null
                && CrewKeyBindRules.ShouldHandleBind(openBind.IsNewPressed, BindManager.IsChatOpen))
            {
                ToggleUi();
            }

            var constructionBind = CrewKeyBinds.SendRecallConstruction;
            if (constructionBind != null
                && CrewKeyBindRules.ShouldHandleBind(constructionBind.IsNewPressed, BindManager.IsChatOpen))
            {
                TryHotkeyRoleDispatch(CrewRole.DamageControl);
            }

            var salvageBind = CrewKeyBinds.SendRecallSalvage;
            if (salvageBind != null
                && CrewKeyBindRules.ShouldHandleBind(salvageBind.IsNewPressed, BindManager.IsChatOpen))
            {
                TryHotkeyRoleDispatch(CrewRole.SalvageOps);
            }
```

Also update the class summary to mention End/Delete dispatch hotkeys.

- [ ] **Step 4: Manual checklist (user)**

1. Seat on managed grid with 2+ Construction seated; press **End** → all send; press again → recall.
2. Same for Salvage with zone marked → **Delete**.
3. Salvage with no zone → no-target message.
4. Chat open → binds ignored.
5. Rich HUD → HireCrew → Key Binds shows three binds; rebind works after relog.
6. Per-crew HUD Send still works.
7. EVA / not seated → sit-to-manage message.

- [ ] **Step 5: Commit only if user asked**

```bash
git add Data/Scripts/HireCrew/CrewKeyBinds.cs Data/Scripts/HireCrew/CrewHud.cs
git commit -m "feat: End/Delete hotkeys batch send/recall Construction and Salvage"
```

---

## Spec coverage checklist

| Spec requirement | Task |
|------------------|------|
| End / Delete defaults, remappable | Task 4 |
| Seated managed grid only | Task 4 |
| Recall if any on mission else Send | Tasks 1, 3, 4 |
| Batch msg 41753 + DTO | Task 2 |
| Server auth, idle, salvage zone, summary notify | Task 3 |
| Chat ignore | Tasks 1, 4 |
| Per-crew HUD unchanged | (no HUD edits) |
| Unit tests for decisions | Task 1 |
| Manual tests | Task 4 Step 4 |
