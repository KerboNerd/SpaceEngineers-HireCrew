# HireCrew Admin Commands

Requires Space Engineers **Admin** promote level. Commands are parsed on the client and executed on the **server**.

Prefixes: `/hirecrew` or `/hc`

Player UI: `/crew` still opens the management HUD (not an admin tool).

## Quick examples

```
/hirecrew help
/hc config show
/hc config reload
/hirecrew hire gunner 5
/hirecrew hire reactor 3 SomePlayerName
/hirecrew roster 76561198000000000
/hirecrew dismiss a1b2c3d4e5f6...
/hirecrew clear roster SomePlayerName
/hirecrew reroll near
/hirecrew clear pool near
/hirecrew transfer <crewId> OtherPlayer
```

## Who can run these

- Space Engineers promote level **Admin** (server re-checks; client cannot bypass).
- Single-player / listen-server hosts are typically Admin.

## Player targeting

Arguments that take `player|steamid`:

1. Numeric **Steam ID** — online player, or identity resolved via the game’s Steam→identity map when available.
2. **Display name** — exact case-insensitive match among **online** players.
3. Multiple name matches → command aborts with an `Ambiguous:` list (no changes).

## Commands

| Command | Description |
|---------|-------------|
| `help` | List verbs |
| `config show` | Dump key world-config values |
| `config reload` | Reload `HireCrewConfig.xml` from world storage (does not mass-reroll desks) |
| `hire <role> <stars> [player]` | Free-hire into that player’s roster pool (default: you) |
| `reroll <blockEntityId>` | Force-refresh that hire desk pool |
| `reroll near` | Reroll nearest hire desk (prefers your current grid) |
| `roster <player\|steamid>` | List that owner’s crew (capped) |
| `dismiss <crewId>` | Remove one crew (any owner) |
| `clear roster <player\|steamid>` | Dismiss all crew for that owner |
| `clear pool <blockEntityId\|near>` | Reroll desk pool |
| `transfer <crewId> <player\|steamid>` | Move crew to another owner (unseats if needed) |

### Role tokens

`gunner` (`g`), `reactor` / `engineer`, `helm` / `helmsman`, `prop` / `propulsion`, `qm` / `quartermaster`

### Stars

`0`–`5` (legacy aliases `recruit`/`regular`/`elite` still accepted)

## Config reload notes

- File: world storage `HireCrewConfig.xml` (written on first load if missing).
- Reload updates live defaults/limits for **new** generation; existing desk pools are not all rerolled automatically.
- Use `reroll` / `clear pool` when you need desks to pick up new rules immediately.

## Removed debug commands

These no longer exist:

- `/chire …`
- `/crew hire …`

Use `/hirecrew hire …` (Admin only) or the hire desk UI instead.
