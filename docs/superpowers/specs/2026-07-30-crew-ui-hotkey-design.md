# Crew UI Hotkey Design

Date: 2026-07-30  
Status: Approved for planning

## Problem

Opening the crew management interface requires typing `/crew` (or using a cockpit terminal/toolbar action). Players want a keyboard hotkey for the same toggle.

## Goals

- Add a client hotkey that toggles the management UI — same behavior as plain `/crew` / `CrewHud.ToggleUi()`.
- Default bind: **Home**.
- Remappable via Rich HUD Terminal (RebindPage).
- Keep existing entry points: `/crew`, cockpit button, toolbar action.

## Non-goals

- Hotkey for `/crew hud` (status sidebar).
- Hotkeys for path paint or salvage modes.
- Vanilla SE Controls screen registration.
- Server/network/config file changes.
- Changing hire-desk UI open behavior.

## Behavior

1. On new press of the bind, call `ToggleUi()` with no preferred grid (same heuristic as `/crew`).
2. Do not fire while `BindManager.IsChatOpen` is true.
3. If the bind fires before RHF is ready, call `ToggleUi()` anyway so the existing “Install Rich Hud Master” message can appear (at most once per press, not every frame).
4. Dedicated servers: no bind registration (client-only).

## Architecture

### Components

| Piece | Role |
|-------|------|
| `CrewKeyBinds` (new) | Register HireCrew bind group + RebindPage on RHF ready; expose `OpenCrewUi` bind; clear on reset |
| `CrewHud` | On HUD ready: init keybinds; in `Update`: if bind new-pressed and chat closed → `ToggleUi()` |
| RichHud `BindManager` / `RebindPage` | Persist and rebind; no custom save format |

### Bind definition

- Group name: `HireCrew`
- Bind name: `OpenCrewUi` (display label suitable for RebindPage, e.g. “Open Crew UI”)
- Default combo: `MyKeys.Home`
- Settings: `RichHudTerminal.Root` ← `RebindPage` containing the HireCrew group

### Lifecycle

1. `CrewHud.OnHudReady` → create/register binds + RebindPage (idempotent).
2. `CrewHud.Update` → poll `OpenCrewUi.IsNewPressed`.
3. `CrewHud.OnHudReset` / `Unload` → drop local references; RHF owns group teardown as usual.

## Error handling

- Missing Rich Hud Master: unchanged; UI already tells the player on `/crew`.
- Bind conflict after player rebind: RHF rebind UI handles; HireCrew does not auto-resolve collisions.
- RHF reset mid-session: stop polling until next ready + re-register.

## Testing (manual)

- Home toggles management UI open/closed while seated and on EVA.
- Rebind in Rich HUD Terminal → HireCrew → Key Binds; new combo works; survives relog.
- With chat open, Home does not toggle UI.
- `/crew` and cockpit open action still work.
- Dedicated server: no client bind path exercised.

## Out of scope follow-ups

- Separate bind for status sidebar.
- Optional default chord (e.g. Ctrl+J) as alternate preset.
