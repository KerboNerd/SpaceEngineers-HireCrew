# Crew UI Hotkey Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a remappable Rich HUD hotkey (default Home) that toggles the crew management UI the same way as `/crew`.

**Architecture:** A tiny pure gate (`CrewKeyBindRules`) decides whether a bind press should toggle. `CrewKeyBinds` registers a HireCrew bind group + RebindPage when Rich HUD is ready. `CrewHud` polls the bind every client frame (before the “UI closed” early return) and calls existing `ToggleUi()`.

**Tech Stack:** Space Engineers ModAPI, RichHudFramework (`BindManager`, `RebindPage`, `RichHudTerminal`), xunit (`HireCrew.Logic.Tests`).

## Global Constraints

- Spec: `docs/superpowers/specs/2026-07-30-crew-ui-hotkey-design.md`
- Management UI only — no binds for `/crew hud`, path, or salvage.
- Default combo: `MyKeys.Home`; remappable via Rich HUD Terminal RebindPage.
- Ignore presses while `BindManager.IsChatOpen`.
- Client-only; dedicated servers must not register binds.
- Do not add TextHUDAPI / HudAPIv2.
- Agent must not run `dotnet` commands; user runs tests.
- Do not touch commented-out code.
- Commit only when the user asks (skip commit steps unless explicitly requested).
- No `Source/HireCrew/` mirror required (folder not present).

## File structure

| File | Role |
|------|------|
| `Data/Scripts/HireCrew/CrewKeyBindRules.cs` | Pure press gate (testable) |
| `Data/Scripts/HireCrew/CrewKeyBinds.cs` | Bind group + RebindPage registration |
| `Data/Scripts/HireCrew/CrewHud.cs` | Lifecycle + poll → `ToggleUi()` |
| `tests/HireCrew.Logic.Tests/CrewKeyBindRulesTests.cs` | Unit tests for gate |
| `tests/HireCrew.Logic.Tests/HireCrew.Logic.Tests.csproj` | Link `CrewKeyBindRules.cs` |

---

### Task 1: Pure press gate + unit tests

**Files:**
- Create: `Data/Scripts/HireCrew/CrewKeyBindRules.cs`
- Create: `tests/HireCrew.Logic.Tests/CrewKeyBindRulesTests.cs`
- Modify: `tests/HireCrew.Logic.Tests/HireCrew.Logic.Tests.csproj`

**Interfaces:**
- Consumes: nothing
- Produces: `public static class CrewKeyBindRules` with `public static bool ShouldToggleOpenCrewUi(bool bindNewPressed, bool chatOpen)`

- [ ] **Step 1: Add failing tests**

Create `tests/HireCrew.Logic.Tests/CrewKeyBindRulesTests.cs`:

```csharp
using Xunit;

namespace HireCrew.Logic.Tests
{
    public class CrewKeyBindRulesTests
    {
        [Fact]
        public void ShouldToggle_when_new_press_and_chat_closed()
        {
            Assert.True(CrewKeyBindRules.ShouldToggleOpenCrewUi(true, false));
        }

        [Fact]
        public void ShouldNotToggle_when_chat_open()
        {
            Assert.False(CrewKeyBindRules.ShouldToggleOpenCrewUi(true, true));
        }

        [Fact]
        public void ShouldNotToggle_when_not_new_press()
        {
            Assert.False(CrewKeyBindRules.ShouldToggleOpenCrewUi(false, false));
        }
    }
}
```

- [ ] **Step 2: Link the rules file in the test project**

In `tests/HireCrew.Logic.Tests/HireCrew.Logic.Tests.csproj`, add next to the other `Compile Include` entries:

```xml
    <Compile Include="..\..\Data\Scripts\HireCrew\CrewKeyBindRules.cs" Link="CrewKeyBindRules.cs" />
```

- [ ] **Step 3: Run tests to verify they fail**

User runs:

```powershell
dotnet test tests/HireCrew.Logic.Tests/HireCrew.Logic.Tests.csproj --filter FullyQualifiedName~CrewKeyBindRulesTests
```

Expected: FAIL (type/method missing).

- [ ] **Step 4: Implement the gate**

Create `Data/Scripts/HireCrew/CrewKeyBindRules.cs`:

```csharp
namespace HireCrew
{
    /// <summary>
    /// Pure rules for crew UI hotkey handling (no ModAPI / RichHud).
    /// </summary>
    public static class CrewKeyBindRules
    {
        public static bool ShouldToggleOpenCrewUi(bool bindNewPressed, bool chatOpen)
        {
            return bindNewPressed && !chatOpen;
        }
    }
}
```

- [ ] **Step 5: User re-runs tests — expect PASS**

```powershell
dotnet test tests/HireCrew.Logic.Tests/HireCrew.Logic.Tests.csproj --filter FullyQualifiedName~CrewKeyBindRulesTests
```

Expected: PASS (3 tests).

- [ ] **Step 6: Commit (only if user asked)**

```bash
git add Data/Scripts/HireCrew/CrewKeyBindRules.cs tests/HireCrew.Logic.Tests/CrewKeyBindRulesTests.cs tests/HireCrew.Logic.Tests/HireCrew.Logic.Tests.csproj
git commit -m "feat: add crew UI hotkey press gate"
```

---

### Task 2: Register Rich HUD bind + RebindPage

**Files:**
- Create: `Data/Scripts/HireCrew/CrewKeyBinds.cs`

**Interfaces:**
- Consumes: RichHud `BindManager`, `BindGroupInitializer`, `RebindPage`, `RichHudTerminal`, `MyKeys.Home`
- Produces:
  - `public static class CrewKeyBinds`
  - `public const string GroupName = "HireCrew"`
  - `public const string OpenCrewUiName = "Open Crew UI"`
  - `public static IBind OpenCrewUi { get; }` (null when not registered)
  - `public static void Register()` — idempotent; safe only after RHF ready
  - `public static void Clear()` — null local refs; allow re-register after RHF reset

- [ ] **Step 1: Create `CrewKeyBinds.cs`**

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

        private static IBindGroup _group;
        private static IBind _openCrewUi;
        private static bool _rebindPageAdded;

        public static IBind OpenCrewUi
        {
            get { return _openCrewUi; }
        }

        public static void Register()
        {
            if (_openCrewUi != null)
                return;

            _group = BindManager.GetOrCreateGroup(GroupName);

            var defaults = new BindGroupInitializer
            {
                { OpenCrewUiName, MyKeys.Home }
            };

            if (!_group.DoesBindExist(OpenCrewUiName))
                _group.RegisterBinds(defaults);

            _openCrewUi = _group[OpenCrewUiName];

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
            _group = null;
            _rebindPageAdded = false;
        }
    }
}
```

- [ ] **Step 2: Sanity-check against RHF APIs (no compile run by agent)**

Confirm usings resolve in IDE:
- `BindManager` / `IBind` / `IBindGroup` → `RichHudFramework.UI` + `RichHudFramework.UI.Client`
- `RebindPage` / `RichHudTerminal` → `RichHudFramework.UI.Client`
- `MyKeys` → `VRage.Input`

- [ ] **Step 3: Commit (only if user asked)**

```bash
git add Data/Scripts/HireCrew/CrewKeyBinds.cs
git commit -m "feat: register HireCrew Open Crew UI bind"
```

---

### Task 3: Wire hotkey into `CrewHud`

**Files:**
- Modify: `Data/Scripts/HireCrew/CrewHud.cs`

**Interfaces:**
- Consumes: `CrewKeyBinds.Register` / `Clear` / `OpenCrewUi`, `CrewKeyBindRules.ShouldToggleOpenCrewUi`, `BindManager.IsChatOpen`, existing `ToggleUi()`
- Produces: Home (or rebound key) toggles management UI on client

- [ ] **Step 1: Call `CrewKeyBinds.Register()` from `OnHudReady`**

At end of `OnHudReady` (after windows/nameplates ready), add:

```csharp
CrewKeyBinds.Register();
```

- [ ] **Step 2: Call `CrewKeyBinds.Clear()` from `OnHudReset` and client `Unload`**

In `OnHudReset`, before or after nulling windows:

```csharp
CrewKeyBinds.Clear();
```

In `Unload` (non-dedicated path), after closing UI / before nulling windows:

```csharp
CrewKeyBinds.Clear();
```

Do **not** call Register/Clear on dedicated early-return paths.

- [ ] **Step 3: Poll the bind in `Update` before the `IsOpen` early return**

`Update` currently returns early when `!_model.IsOpen`, which would block opening via hotkey. Insert polling **after** nameplate/hire/sidebar updates and **before** `if (!_model.IsOpen) return;`:

```csharp
            UpdateStatusSidebar();

            var openBind = CrewKeyBinds.OpenCrewUi;
            if (openBind != null
                && CrewKeyBindRules.ShouldToggleOpenCrewUi(openBind.IsNewPressed, BindManager.IsChatOpen))
            {
                ToggleUi();
            }

            if (!_model.IsOpen) return;
```

Add usings at top of `CrewHud.cs` if missing:

```csharp
using RichHudFramework.UI.Client;
```

(`RichHudFramework.Client` may already be present; `BindManager` lives under `RichHudFramework.UI.Client`.)

- [ ] **Step 4: Update the class summary comment**

Extend the existing summary to mention the hotkey, e.g.:

```csharp
    /// /crew — toggle management UI (assign/dismiss)
    /// Hotkey (default Home, remappable in Rich HUD) — same as /crew
    /// /crew hud — toggle construction status sidebar
```

- [ ] **Step 5: Manual in-game checklist (user)**

1. Load world with Rich Hud Master + HireCrew.
2. Press **Home** — management UI opens; press again — closes.
3. Repeat seated and on EVA.
4. Open chat, press Home — UI must not toggle.
5. Rich HUD Terminal → HireCrew → Key Binds → rebind to another unused key → confirm new key works; Home no longer toggles (unless rebound back).
6. Relog — rebound key still works.
7. `/crew` and cockpit “Open Crew Management” still work.

- [ ] **Step 6: Commit (only if user asked)**

```bash
git add Data/Scripts/HireCrew/CrewHud.cs
git commit -m "feat: toggle crew UI with remappable Home hotkey"
```

---

## Spec coverage checklist

| Spec requirement | Task |
|------------------|------|
| Toggle same as `/crew` / `ToggleUi()` | Task 3 |
| Default Home | Task 2 |
| Remappable via RebindPage | Task 2 |
| Keep chat / cockpit entry points | Task 3 (no removals) |
| Ignore when chat open | Task 1 + 3 |
| Client-only / dedicated skip | Task 2–3 (Register only from OnHudReady; Update already skips dedicated) |
| Clear on RHF reset | Task 3 |
| Manual test cases | Task 3 Step 5 |
