# HireCrew Admin Commands Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add `/hirecrew` (`/hc`) server-authoritative admin commands for config, hire/reroll, and roster moderation; remove free debug hire aliases.

**Architecture:** Client chat parses `/hirecrew` and sends `AdminCommandRequest` over a new net message. Server checks `MyPromoteLevel.Admin`, runs handlers in a dedicated `CrewAdminCommands` helper, replies via existing `Notify`. `/crew` remains UI-only.

**Tech Stack:** Space Engineers ModAPI (chat + PromoteLevel), protobuf-net messages, existing HireCrew store/pool/session.

**Spec:** `docs/superpowers/specs/2026-07-28-admin-commands-design.md`

## Global Constraints

- Admin only: `MyAPIGateway.Session.GetUserPromoteLevel(steamId) >= MyPromoteLevel.Admin` on the **server**.
- Client chat → server RPC (dedicated-safe). Never trust client for mutations.
- Remove `/chire` and `/crew hire` entirely; `/crew` toggles UI only.
- Player target: Steam ID **or** online display name; ambiguous name → abort.
- No automated tests; agent must not run `dotnet`. Manual in-game verify.
- Do not commit unless the user explicitly asks.
- After code: write `docs/admin-commands.md`.

## File structure

| File | Role |
|------|------|
| `Data/Scripts/HireCrew/CrewModels.cs` | `AdminCommandRequest` |
| `Data/Scripts/HireCrew/CrewNetworking.cs` | `AdminCommandMsg = 41745` |
| `Data/Scripts/HireCrew/CrewAdminCommands.cs` | Parse helpers + server verb handlers (keep session thinner) |
| `Data/Scripts/HireCrew/CrewSession.cs` | Wire message, `ClientRequestAdmin`, `Notify` multi-line helper access, reload config entry point |
| `Data/Scripts/HireCrew/CrewHud.cs` | Register `/hirecrew`/`/hc`; strip debug hire; `/crew` UI only |
| `docs/admin-commands.md` | Operator-facing docs |

---

### Task 1: Message + model + networking

**Files:**
- Modify: `Data/Scripts/HireCrew/CrewModels.cs`
- Modify: `Data/Scripts/HireCrew/CrewNetworking.cs`
- Modify: `Data/Scripts/HireCrew/CrewSession.cs` (register handler stub)

**Interfaces:**
- Produces:
  - `[ProtoContract] class AdminCommandRequest { [ProtoMember(1)] string Verb; [ProtoMember(2)] List<string> Args; }`
  - `CrewNetworking.AdminCommandMsg = 41745` (+ Register/Unregister)
  - `CrewSession.ClientRequestAdmin(AdminCommandRequest req)`
  - `OnMessage` branch → `HandleAdminCommand(req, identityId, sender)` (stub OK: admin check + `Notify(sender, "OK stub")`)

- [ ] **Step 1: Add model**

```csharp
[ProtoContract]
public sealed class AdminCommandRequest
{
    [ProtoMember(1)] public string Verb;
    [ProtoMember(2)] public List<string> Args = new List<string>();
}
```

- [ ] **Step 2: Register `AdminCommandMsg = 41745`** in Register/Unregister lists.

- [ ] **Step 3: Session client + stub handler**

```csharp
public void ClientRequestAdmin(AdminCommandRequest req)
{
    if (req == null) return;
    var data = CrewNetworking.Serialize(req);
    if (MyAPIGateway.Multiplayer.IsServer)
        HandleAdminCommand(req, MyAPIGateway.Session.Player.IdentityId, MyAPIGateway.Multiplayer.MyId);
    else
        CrewNetworking.SendToServer(CrewNetworking.AdminCommandMsg, data);
}

private void HandleAdminCommand(AdminCommandRequest req, long identityId, ulong steamId)
{
    if (req == null) return;
    if (!CrewAdminCommands.IsAdmin(steamId))
    {
        Notify(steamId, "Admin only");
        return;
    }
    // Task 3 fills real dispatch; temporary:
    Notify(steamId, "Admin OK: " + (req.Verb ?? ""));
}
```

Wire `OnMessage` for `AdminCommandMsg` like other handlers. For Task 1 only, if `CrewAdminCommands` does not exist yet, inline:

```csharp
private static bool IsAdminSteam(ulong steamId)
{
    try
    {
        return MyAPIGateway.Session.GetUserPromoteLevel(steamId) >= MyPromoteLevel.Admin;
    }
    catch { return false; }
}
```

Move into `CrewAdminCommands.IsAdmin` in Task 3.

- [ ] **Step 4: Manual check** — mod compiles; ignore functional verbs until Task 3.

---

### Task 2: Client chat — `/hirecrew` / `/hc`; remove debug hire

**Files:**
- Modify: `Data/Scripts/HireCrew/CrewHud.cs`

**Interfaces:**
- Consumes: `CrewSession.ClientRequestAdmin`
- Produces: chat registration for `/hirecrew` and `/hc`; `/crew` with no args → `ToggleUi()`; unknown `/crew` subcommands → usage without hire

- [ ] **Step 1: Constants**

```csharp
public const string Command = "/crew";
public const string AdminCommand = "/hirecrew";
public const string AdminAlias = "/hc";
// Remove HireAlias = "/chire"
```

- [ ] **Step 2: Rewrite `OnMessageEntered`**

```csharp
bool isCrew = string.Equals(head, Command, StringComparison.OrdinalIgnoreCase);
bool isAdmin = string.Equals(head, AdminCommand, StringComparison.OrdinalIgnoreCase)
    || string.Equals(head, AdminAlias, StringComparison.OrdinalIgnoreCase);
if (!isCrew && !isAdmin) return;
sendToOthers = false;

if (isCrew)
{
    if (tokens.Length == 1) { ToggleUi(); return; }
    Tell("Usage: /crew");
    return;
}

// /hirecrew or /hc
var args = new List<string>();
for (int i = 1; i < tokens.Length; i++) args.Add(tokens[i]);
string verb = args.Count > 0 ? args[0] : "help";
if (args.Count > 0) args.RemoveAt(0);

var session = CrewSession.Instance;
if (session == null) { Tell("HireCrew not ready"); return; }
session.ClientRequestAdmin(new AdminCommandRequest { Verb = verb, Args = args });
```

- [ ] **Step 3: Delete** `DebugHire`, and stop using hire parsing from chat (keep `TryParseRole` / `TryParseStars` **only if** moved to shared helper — otherwise delete and reimplement parse in `CrewAdminCommands`).

Prefer moving role/star parse into `CrewAdminCommands.TryParseRole` / `TryParseStars` (copy from current `CrewHud`) so HUD stays UI-only.

- [ ] **Step 4: Update** chat register log line to `/crew, /hirecrew, /hc`.

- [ ] **Step 5: Manual check** — `/crew` opens UI; `/chire` is ignored by game chat (sent to others / normal chat); `/hirecrew help` reaches server stub.

---

### Task 3: `CrewAdminCommands` — full verb dispatch

**Files:**
- Create: `Data/Scripts/HireCrew/CrewAdminCommands.cs`
- Modify: `Data/Scripts/HireCrew/CrewSession.cs` — `HandleAdminCommand` delegates here; expose small helpers as needed (`Notify`, `BroadcastRoster`, `BroadcastHirePool`, `ReloadHireWorldConfig`, store access)

**Interfaces:**
- Produces:
  - `static bool IsAdmin(ulong steamId)`
  - `static void Handle(CrewSession session, AdminCommandRequest req, long adminIdentityId, ulong adminSteamId)`
  - Player resolve: `static bool TryResolvePlayer(string token, out IMyPlayer player, out long identityId, out string error)`
  - Verbs: `help`, `config`, `hire`, `reroll`, `roster`, `dismiss`, `clear`, `transfer`

- [ ] **Step 1: Create `CrewAdminCommands.cs` skeleton** with `IsAdmin`, `Handle` switch on `verb.ToLowerInvariant()`:

| Verb | Args | Action |
|------|------|--------|
| `help` / empty | — | Send usage lines via `session.NotifyAdminLines` |
| `config` | `show` \| `reload` | show fields / call session reload |
| `hire` | `role stars [player]` | free hire into target identity pool |
| `reroll` | `<id>` \| `near` | RefreshPool + broadcast |
| `roster` | `<player\|steamid>` | list up to 40 crew lines |
| `dismiss` | `<crewId>` | admin dismiss (no owner check) |
| `clear` | `roster <player>` \| `pool <id\|near>` | bulk dismiss / refresh pool |
| `transfer` | `<crewId> <player>` | re-own; unassign if seated |

- [ ] **Step 2: Admin notify helper on session**

```csharp
public void Notify(ulong steamId, string text) { /* existing */ }

public void NotifyLines(ulong steamId, IList<string> lines)
{
    if (lines == null) return;
    for (int i = 0; i < lines.Count; i++)
        Notify(steamId, lines[i]);
}
```

(If `Notify` is private, make `internal`/`public` or pass an `Action<string>` into `CrewAdminCommands.Handle`.)

- [ ] **Step 3: Player resolution**

```csharp
// 1) ulong.TryParse → Steam ID
//    online: GetPlayers match SteamUserId
//    offline: if any Store crew has OwnerIdentityId == mapped identity, use that identityId
//      (SE: MyAPIGateway.Players.TryGetIdentityId(steamId, out identityId) if available;
//       else require online for hire/transfer; roster/clear may scan OwnerIdentityId when steam known)
// 2) else name: collect online players where DisplayName equals/contains (prefer exact, then case-insensitive equals)
//    0 → "Player not found"; >1 → "Ambiguous: name (steamid), ..."
```

Use exact case-insensitive `DisplayName` equality first; if none, do not fuzzy-contains (reduces accidents). Spec allows name match — exact CI is enough.

- [ ] **Step 4: `config show` / `reload`**

Show (examples): refresh min/max/default, price mult min/max/default, candidate min/max, variance, allowed role mask, refill default.

`reload`: extract session method from existing `LoadHireWorldConfig` body (or call it again). On success `Notify("Config reloaded")`; on failure keep previous Current and notify fail. **Do not** force-reroll all desks.

- [ ] **Step 5: `hire`**

```csharp
// Parse role + stars from args[0], args[1]; optional args[2] player
// Target identity = resolved player or adminIdentityId
// ResolveOwnerKey(targetIdentity)
// Create CrewRecord like HandleHire but GridEntityId=0, SkipCharge always, no grid permission
// Store.Upsert; BroadcastRoster(0); Notify admin with name/stars/role
```

- [ ] **Step 6: `reroll` / `clear pool`**

```csharp
// block id: long.TryParse
// near: find hire desks on admin's TryGetLocalManagedGrid / controlled grid; pick nearest to player character
// CrewHireGenerator.RefreshPool; BroadcastHirePool
```

Expose `BroadcastHirePool` if private, or add `session.AdminRerollDesk(blockId, steamId)`.

- [ ] **Step 7: `roster` / `dismiss` / `clear roster` / `transfer`**

- `roster`: filter `Store.All` where `CrewOwnership.Matches(crew, ownerKey, ownerIsFaction)` OR `OwnerIdentityId == identity` (prefer owner key from `ResolveOwnerKey`).
- `dismiss`: find by `CrewId`; call same cleanup as `HandleDismiss` but **skip** owner permission (admin). Extract shared `DismissCore(crew)` if needed.
- `clear roster`: dismiss all matching owner; log count; notify `Cleared N crew`.
- `transfer`: set `OwnerIdentityId`, recompute `OwnerKey`/`OwnerIsFaction` for target; if seated, unassign/despawn via existing unassign path first; `BroadcastRoster`.

- [ ] **Step 8: Server log** for `clear roster`, `clear pool`, `transfer`, `dismiss`:

```csharp
MyLog.Default.WriteLineAndConsole("[HireCrew] admin " + adminSteamId + " " + verb + " ...");
```

- [ ] **Step 9: Wire `HandleAdminCommand` → `CrewAdminCommands.Handle(this, req, identityId, steamId)`**

- [ ] **Step 10: Manual check** (SP or listen as Admin)

1. Non-admin (if testable) → `Admin only`  
2. `/hirecrew help`  
3. `/hirecrew config show` / `reload`  
4. `/hirecrew hire gunner 3` → roster gains crew  
5. `/hirecrew roster <you>`  
6. `/hirecrew dismiss <id>`  
7. `/hirecrew reroll near` on a grid with a desk  
8. `/chire` no longer handled  

---

### Task 4: Admin documentation

**Files:**
- Create: `docs/admin-commands.md`

**Interfaces:** none (docs only)

- [ ] **Step 1: Write** `docs/admin-commands.md` covering:

- Requirements: SE Admin promote level; Rich Hud not required for these commands
- Prefixes: `/hirecrew`, `/hc`
- Player targeting rules
- Full command table with examples
- Config reload notes (XML path, no mass desk reroll)
- Removed: `/chire`, `/crew hire`
- `/crew` still opens player UI

Example fragment:

```markdown
# HireCrew Admin Commands

Requires Space Engineers **Admin** promote level. Commands run on the server.

## Quick examples

/hirecrew help
/hc config reload
/hirecrew hire gunner 5
/hirecrew roster 76561198...
/hirecrew clear roster SomePlayerName
/hirecrew reroll near
```

- [ ] **Step 2: Manual check** — doc matches implemented verbs.

---

## Spec coverage

| Spec item | Task |
|-----------|------|
| AdminCommand RPC + Admin gate | 1, 3 |
| `/hirecrew` `/hc` client | 2 |
| Remove debug hire | 2 |
| Config show/reload | 3 |
| hire / reroll / roster / dismiss / clear / transfer | 3 |
| Player Steam ID or name | 3 |
| `docs/admin-commands.md` | 4 |

## Self-review notes

- Message id `41745` must not collide (next after `BulkAssignMsg = 41744`).
- Admin hire must **not** reuse `HandleHire` grid permission path blindly — dedicated free-hire into owner pool.
- Keep role/star parsing in one place (`CrewAdminCommands`) after removing it from `CrewHud`.
