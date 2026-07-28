# HireCrew Admin Commands Design

Date: 2026-07-28  
Status: Approved for planning

## Problem

Multiplayer admins need server-authoritative tools to manage HireCrew (config, desks, rosters) without giving every player free debug hire. Today `/chire` and `/crew hire` are client-local debug paths with no admin gate.

## Goals

- `/hirecrew` (alias `/hc`) admin command toolkit via client chat → server RPC.
- Gate on Space Engineers `PromoteLevel.Admin` (server re-check).
- Cover config, economy/debug, and moderation (player by Steam ID or display name).
- Remove `/chire` and `/crew hire`; keep `/crew` as player UI only.
- After implementation: write `docs/admin-commands.md` for server ops.

## Non-goals

- Steam ID allowlist or moderator rank.
- Live XML field editors beyond show/reload.
- RichHud admin UI.
- Confirmation prompts for destructive clears.
- Automated tests (manual / in-game verify).

## Architecture

```
Client chat: /hirecrew <verb> [args...]
        |
        v
Parse locally → AdminCommandRequest
        |
        v
Send AdminCommandMsg to server (or Handle locally if IsServer)
        |
        v
Server: resolve sender → PromoteLevel.Admin?
        |
        +-- no  → Notify "Admin only"
        +-- yes → execute verb → Notify result (+ server log for destructive ops)
```

`/crew` stays on the client for UI toggle only.

## Auth

- Allowed: `MyAPIGateway.Session.GetUserPromoteLevel(steamId) >= MyPromoteLevel.Admin` (or equivalent SE API in use).
- Single-player / listen host: same check; treat offline/local host as admin when PromoteLevel is unavailable but session is not dedicated client-only.
- Client may optionally skip send when local check fails (UX); server decision is authoritative.

## Command set

Prefix: `/hirecrew` or `/hc`.

| Command | Effect |
|---------|--------|
| `help` | List verbs + short usage |
| `config show` | Chat dump of key `HireWorldConfig` fields |
| `config reload` | Reload `HireCrewConfig.xml` from world storage; normalize; notify |
| `hire <role> <stars> [player]` | Free hire into target roster (default: invoking admin) |
| `reroll <blockEntityId>` | Force `RefreshPool` for that desk |
| `reroll near` | Reroll nearest hire desk on admin’s current/controlled grid |
| `roster <player\|steamid>` | List that owner’s crew summary lines |
| `dismiss <crewId>` | Dismiss crew by id (any owner) |
| `clear roster <player\|steamid>` | Dismiss all crew for that owner |
| `clear pool <blockEntityId\|near>` | Clear/reroll desk pool |
| `transfer <crewId> <player\|steamid>` | Reassign crew ownership to target |

### Role tokens

Same as former debug hire: `gunner`, `reactor`/`engineer`, `helm`/`helmsman`, `prop`/`propulsion`, `qm`/`quartermaster`. Stars `0`–`5`.

### Player resolution

1. If arg parses as `ulong` Steam ID → that player (must be online unless roster uses stored OwnerIdentityId/OwnerKey — prefer online identity + existing owner-key resolution).
2. Else case-insensitive display-name match among online players.
3. 0 matches → error; 2+ matches → error listing Steam IDs / names; no mutation.

Name matching requires an online player. Steam ID may target an online player, or an offline identity already present on stored crew (`OwnerIdentityId`); if neither resolves, abort with `Player not found`.

## Networking

- New message id (e.g. `AdminCommandMsg`) registered with existing `CrewNetworking`.
- `AdminCommandRequest`: verb string + `List<string>` args (or single args string split server-side). Prefer verb + args list.
- Replies reuse existing `NotifyMessage` / `Notify(steamId, text)`.
- Long `roster` / `config show` output: multiple notify lines or one multiline string (SE chat may truncate — prefer several short notifies, cap roster list e.g. 40 lines with “…and N more”).

## Apply details

- `config reload`: reuse session load path for `HireCrewConfig.xml`; does not rewrite defaults unless file missing; does not force-reroll all desks (new generations use new limits).
- `hire`: create `CrewRecord` like debug hire (skip charge), into target owner pool; no seat assignment.
- `dismiss` / `clear roster`: reuse existing dismiss path (unseat/despawn as today).
- `transfer`: update owner fields via existing ownership helpers; unassign from grid if ownership rules require.
- `reroll` / `clear pool`: `RefreshPool` + broadcast pool sync.

## Player-facing cleanup

- Remove `/chire` registration and `/crew hire` branch from `CrewHud`.
- Update any in-mod help/usage strings accordingly.

## Documentation deliverable

After code is done, add `docs/admin-commands.md` with:

- Who can use commands (SE Admin)
- Full command list + examples
- Config reload behavior
- Note that debug `/chire` / `/crew hire` were removed
- Player targeting rules (Steam ID vs name)

## Error handling

| Case | Message (approx.) |
|------|-------------------|
| Not admin | `Admin only` |
| Unknown verb | `Unknown command. /hirecrew help` |
| Bad usage | Per-verb usage line |
| Ambiguous player | `Ambiguous: …` |
| Missing crew/desk | `Crew not found` / `Hire desk not found` |
| Config reload fail | `Config reload failed — using previous/defaults` |
