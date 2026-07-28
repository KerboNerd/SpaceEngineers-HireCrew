# GitHub Wiki + Workshop Cleanup Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Publish an exhaustive player/admin GitHub Wiki for HireCrew and remove unused workshop staging from the main repo.

**Architecture:** Wiki content lives only in the GitHub wiki git remote (`SpaceEngineers-HireCrew.wiki`). Pages are feature-centric Markdown with Player/Admin subsections. Workshop SteamCMD staging is deleted from the main repo in a separate commit. Content is rewritten from live code (`CrewConfig`, `HireWorldConfig`, `CrewAdminCommands`, block SBC, HUD) and `docs/admin-commands.md`.

**Tech Stack:** GitHub Wiki (git-backed Markdown), `gh` CLI, PowerShell on Windows, existing HireCrew C# / SBC sources for accuracy.

## Global Constraints

- Host: GitHub Wiki on private repo `KerboNerd/SpaceEngineers-HireCrew` only.
- Organization: feature-centric pages; each feature page has `## Player` then `## Admin` (Home / Getting-Started / Troubleshooting may use a lighter split).
- Depth: exhaustive — player + admin guides, full config reference, troubleshooting, short feature notes.
- Sources (priority): live code → `docs/admin-commands.md` → approved design specs (rewrite; do not paste SDD).
- Do not duplicate wiki pages into the main repo.
- Do not document SteamCMD / workshop upload tooling.
- Delete entire `workshop/` tree; update `.gitignore` accordingly.
- Wiki page filenames must match the page map (GitHub wiki uses hyphenated titles as file names).
- No secrets, dumps, or agent reports on the wiki.

## File structure

### Main repo (HireCrew)

| Path | Responsibility |
|------|----------------|
| `workshop/` | **Delete entirely** (Publish script, VDF, dumps, content staging, steamcmd) |
| `.gitignore` | Remove obsolete `workshop/content/`, `workshop/steamcmd/`, `workshop/preview.jpg` lines |

### Wiki remote (`SpaceEngineers-HireCrew.wiki`)

| Path | Responsibility |
|------|----------------|
| `Home.md` | Landing + dependency + quick links |
| `_Sidebar.md` | Navigation |
| `Getting-Started.md` | Install + first loop |
| `Hiring.md` | Hire desk + terminal settings |
| `Crew-Management.md` | `/crew` HUD features |
| `Roles-and-Effects.md` | Role bonuses and caps |
| `Damage-Control.md` | Construction / repair / path tool |
| `Crew-Stations.md` | Crew Station block + ambient |
| `World-Config.md` | `HireCrewConfig.xml` reference |
| `Admin-Commands.md` | `/hirecrew` / `/hc` |
| `Troubleshooting.md` | Common failures |

Local clone path for wiki work (create if missing):

`C:\Users\user\AppData\Local\Temp\SpaceEngineers-HireCrew.wiki`

---

### Task 1: Remove workshop staging from main repo

**Files:**
- Delete: `workshop/` (entire directory)
- Modify: `.gitignore`

**Interfaces:**
- Consumes: none
- Produces: clean working tree with no `workshop/` path; `.gitignore` without workshop staging lines

- [ ] **Step 1: Confirm what git tracks under workshop**

Run:

```powershell
git ls-files workshop
```

Expected: includes at least `workshop/Publish-HireCrew.ps1`, `workshop/hirecrew.vdf`, and any dumps previously committed.

- [ ] **Step 2: Delete the workshop directory**

Run:

```powershell
Remove-Item -Recurse -Force workshop
```

Expected: `workshop` directory no longer exists under the repo root.

- [ ] **Step 3: Update `.gitignore`**

Replace the entire file contents with:

```gitignore
# Build / test artifacts
obj/
bin/
tests/**/bin/
tests/**/obj/
*.dll
*.pdb

# Local agent / OS junk
.superpowers/
Thumbs.db
Desktop.ini
```

- [ ] **Step 4: Stage and verify**

Run:

```powershell
git add -A
git status --short
```

Expected: deletions under `workshop/`, modified `.gitignore`, and no remaining `workshop/` paths staged as additions.

- [ ] **Step 5: Commit**

```powershell
git commit -m @"
chore: remove unused workshop SteamCMD staging

Workshop upload tooling is retired; keep the mod tree focused on game content and docs.
"@
```

Expected: commit succeeds on `master`.

---

### Task 2: Enable wiki remote and clone

**Files:**
- Create (local only): `C:\Users\user\AppData\Local\Temp\SpaceEngineers-HireCrew.wiki\` (git clone)

**Interfaces:**
- Consumes: authenticated `gh` as `KerboNerd`
- Produces: writable wiki clone with `origin` pointing at `SpaceEngineers-HireCrew.wiki.git`

- [ ] **Step 1: Enable the wiki on the GitHub repo**

Run:

```powershell
gh repo edit KerboNerd/SpaceEngineers-HireCrew --enable-wiki
```

Expected: command exits 0 (wiki enabled, or already enabled).

- [ ] **Step 2: Ensure a seed Home page exists (first-time wiki)**

GitHub may require visiting the Wiki UI once, or creating an initial page. Run:

```powershell
gh api repos/KerboNerd/SpaceEngineers-HireCrew --jq .has_wiki
```

Expected: `true`.

If clone in the next step fails with "repository not found" / empty wiki, open https://github.com/KerboNerd/SpaceEngineers-HireCrew/wiki in a browser once and create a blank Home page, then retry Step 3.

- [ ] **Step 3: Clone the wiki repo**

Run:

```powershell
$wiki = "$env:LOCALAPPDATA\Temp\SpaceEngineers-HireCrew.wiki"
if (Test-Path $wiki) { Remove-Item -Recurse -Force $wiki }
git clone https://github.com/KerboNerd/SpaceEngineers-HireCrew.wiki.git $wiki
Set-Location $wiki
git status -sb
```

Expected: clone succeeds; working tree on `master` (or `main`).

---

### Task 3: Home, Sidebar, Getting-Started

**Files:**
- Create/overwrite in wiki clone: `Home.md`, `_Sidebar.md`, `Getting-Started.md`

**Interfaces:**
- Consumes: Task 2 wiki clone
- Produces: landing + nav + install guide pages

- [ ] **Step 1: Write `_Sidebar.md`**

Write exact contents:

```markdown
**HireCrew**

* [Home](Home)
* [Getting Started](Getting-Started)

**Features**

* [Hiring](Hiring)
* [Crew Management](Crew-Management)
* [Roles and Effects](Roles-and-Effects)
* [Damage Control](Damage-Control)
* [Crew Stations](Crew-Stations)

**Server**

* [World Config](World-Config)
* [Admin Commands](Admin-Commands)

**Help**

* [Troubleshooting](Troubleshooting)
```

- [ ] **Step 2: Write `Home.md`**

Write exact contents:

```markdown
# HireCrew

HireCrew lets you **hire NPC crew**, assign them to seats/stations on your grids, and get role-based bonuses (weapons tracking, reactor power, gyros, thrust, training discounts, and Construction repairs).

## Requirements

* Space Engineers (PC)
* **[Rich Hud Master](https://steamcommunity.com/workshop/filedetails/?id=1965654081)** (workshop) — required for hire desk UI and `/crew` management UI
* This mod loaded in the world (local Mods folder or workshop subscription when published)

## Quick links

| I want to… | Go to |
|---|---|
| Install and hire my first crew | [Getting Started](Getting-Started) |
| Use the hire desk | [Hiring](Hiring) |
| Assign / train / dismiss crew | [Crew Management](Crew-Management) |
| Understand roles | [Roles and Effects](Roles-and-Effects) |
| Set up Construction repairs | [Damage Control](Damage-Control) |
| Tune server economy | [World Config](World-Config) |
| Run admin chat commands | [Admin Commands](Admin-Commands) |
| Fix common errors | [Troubleshooting](Troubleshooting) |

## Commands at a glance

| Command | Who | Purpose |
|---|---|---|
| `/crew` | Players | Open/close crew management HUD |
| `/crew path …` | Players | Edit Construction EVA path on a grid |
| `/hirecrew` or `/hc` | **Admin** | Config, free-hire, roster moderation |

Player debug hire shortcuts `/chire` and `/crew hire` were **removed**. Use the hire desk or admin `/hirecrew hire`.
```

- [ ] **Step 3: Write `Getting-Started.md`**

Write exact contents:

```markdown
# Getting Started

## Player

1. Subscribe to / install **Rich Hud Master** and **HireCrew**.
2. Load a world with both mods enabled.
3. Build a **Crew Hiring Desk** (`HC_CrewHireDesk`) on a grid you control.
4. Aim at the desk screen and press **F**, or open the block terminal and use **Open Hiring Desk**.
5. Pick a candidate and hire (pays credits from your account).
6. Type **`/crew`** to open the management HUD.
7. Select the hired crew, pick a seat (or **Crew Station**), and assign. Gunners also need a weapon selection when applicable.
8. (Optional) Assign bed / toilet / shower amenities for efficiency bonuses.
9. (Optional) For Construction crew, paint an EVA path with `/crew path` — see [Damage Control](Damage-Control).

## Admin

1. Confirm your promote level is **Admin** (single-player / listen host usually is).
2. On first world load, HireCrew writes **`HireCrewConfig.xml`** into world storage if missing.
3. Tune defaults/limits in that XML, then `/hc config reload` (does not mass-reroll every desk).
4. Use `/hc reroll near` or desk **Reroll pool now** when you need pools to pick up new rules immediately.
5. Full field list: [World Config](World-Config). Commands: [Admin Commands](Admin-Commands).
```

- [ ] **Step 4: Commit wiki pages**

```powershell
Set-Location "$env:LOCALAPPDATA\Temp\SpaceEngineers-HireCrew.wiki"
git add Home.md _Sidebar.md Getting-Started.md
git commit -m "docs(wiki): add Home, Sidebar, Getting Started"
```

Expected: commit succeeds (push comes in Task 8).

---

### Task 4: Hiring + World-Config

**Files:**
- Create/overwrite in wiki clone: `Hiring.md`, `World-Config.md`

**Interfaces:**
- Consumes: `HireWorldConfig.cs`, `CrewHireBlockLogic.cs`, `HC_CrewHireDesk.sbc`
- Produces: hire + config reference pages

- [ ] **Step 1: Write `Hiring.md`**

Write exact contents:

```markdown
# Hiring

## Player

### Crew Hiring Desk

* Block display name: **Crew Hiring Desk**
* Subtype: `HC_CrewHireDesk` (large grid)
* Open UI: aim at the screen and press **F**, or terminal button **Open Hiring Desk**
* Requires **Rich Hud Master**

The desk shows a rotating pool of **candidates** (name, role, stars 0–5, price). Hire spends credits and adds the crew to **your owner roster** (unassigned until you use `/crew`).

Pools refresh on a timer. Terminal **Reroll pool now** forces an immediate refresh. If **Refill on hire** is on, hiring replaces that slot with a new candidate; otherwise the slot stays empty until the next refresh.

### Desk terminal settings (anyone with terminal access)

| Control | Meaning |
|---|---|
| Open Hiring Desk | Opens RichHud hire UI |
| Pool refresh (minutes) | How long before candidates reroll |
| Price multiplier | Scales hire prices at this desk (percent; 100 = 1.0×) |
| Min / Max candidates | Pool size bounds for each roll |
| Star bias | Low / Balanced / High skew of star rolls |
| Role checkboxes | Which roles may appear at this desk |
| Refill on hire | Replace hired slot immediately |
| Reroll pool now | Regenerate candidates now |

Desk values are **clamped** to world limits from `HireCrewConfig.xml`.

### Stars and default base prices

Default base prices (before desk multiplier and ±variance):

| Stars | Base price (SC) |
|---|---|
| 0 | 10,000 |
| 1 | 25,000 |
| 2 | 50,000 |
| 3 | 90,000 |
| 4 | 150,000 |
| 5 | 250,000 |

Servers may change these in world config.

## Admin

* World defaults/limits: [World Config](World-Config)
* Force refresh: `/hc reroll near` or `/hc reroll <blockEntityId>`
* Clear/reroll pool: `/hc clear pool near` or `/hc clear pool <blockEntityId>`
* Free hire into a roster (no desk): `/hc hire <role> <stars> [player]` — see [Admin Commands](Admin-Commands)
* `config reload` updates limits for **new** generation; existing desk pools are not all rerolled automatically
```

- [ ] **Step 2: Write `World-Config.md`**

Write exact contents:

```markdown
# World Config

## Player

Players do not edit this file. Desk terminal controls are clamped by these world limits. If a desk setting will not go higher/lower, the server XML is capping it.

## Admin

### File

* Name: **`HireCrewConfig.xml`**
* Location: world storage (server). Created with defaults on first load if missing/invalid.
* Live reload: `/hirecrew config reload` or `/hc config reload`
* Inspect: `/hc config show`

Reload updates live defaults/limits for **new** generation. It does **not** mass-reroll every hire desk. Use desk reroll or `/hc reroll` / `/hc clear pool` when pools must refresh immediately.

### Fields (defaults)

| Field | Default | Notes |
|---|---|---|
| `RefreshMinutesMin` | 1 | Lower clamp for desk refresh |
| `RefreshMinutesMax` | 300 | Upper clamp |
| `RefreshMinutesDefault` | 15 | New desks |
| `PriceMultiplierPercentMin` | 25 | 25% = 0.25× |
| `PriceMultiplierPercentMax` | 500 | 500% = 5.0× |
| `PriceMultiplierPercentDefault` | 100 | 1.0× |
| `MinCandidates` | 1 | Global min pool size |
| `MaxCandidates` | 8 | Global max pool size |
| `PriceByStars` | 10000,25000,50000,90000,150000,250000 | Length 6; stars 0–5 |
| `PriceVarianceFraction` | 0.15 | Per-candidate price noise (0–0.9) |
| `StarWeights` | 25,25,20,15,10,5 | Balanced base weights |
| `AllowedRolesMask` | all roles | Bit `(1 << (int)CrewRole)` |
| `RefillOnHireDefault` | false | New desk default |

### Star bias (per desk)

Uses world `StarWeights` then skews:

* **Low** — favors lower stars
* **Balanced** — raw weights
* **High** — favors higher stars

### Role mask bits

| Role | Enum | Bit |
|---|---|---|
| Gunner | 0 | 1 |
| Reactor Tech (Engineer) | 1 | 2 |
| Helmsman | 2 | 4 |
| Propulsion Tech | 3 | 8 |
| Quartermaster | 4 | 16 |
| Construction (DamageControl) | 5 | 32 |

Default mask allows all roles (`1+2+4+8+16+32`).
```

- [ ] **Step 3: Commit**

```powershell
Set-Location "$env:LOCALAPPDATA\Temp\SpaceEngineers-HireCrew.wiki"
git add Hiring.md World-Config.md
git commit -m "docs(wiki): add Hiring and World Config"
```

---

### Task 5: Crew-Management + Roles-and-Effects

**Files:**
- Create/overwrite in wiki clone: `Crew-Management.md`, `Roles-and-Effects.md`

**Interfaces:**
- Consumes: `CrewHud.cs`, `CrewHudWindow.cs`, `CrewConfig.cs`
- Produces: management + roles pages

- [ ] **Step 1: Write `Crew-Management.md`**

Write exact contents:

```markdown
# Crew Management

## Player

### Open the HUD

* Chat: **`/crew`** — toggles the RichHud management window
* Requires **Rich Hud Master**
* Opens for a managed grid when possible; can open off-ship with the grid picker

### Core actions

* **Select crew** in the list (unassigned vs assigned views)
* **Assign** — seat (+ weapon for Gunners)
* **Unassign** — return to owner pool (not dismissed)
* **Dismiss** — permanently remove crew
* **Train** / **Cancel train** — spend credits/time to raise stars (0→5); Quartermasters can discount training
* **Amenities** — assign bed, toilet, shower blocks for efficiency (+10% effective bonus per amenity, max 3)
* **Bulk assign** — multi-select unassigned crew and map seats/weapons in bulk
* **Grid picker** — manage crew for another owned/permitted grid while off ship

### Ownership

Crew belong to your **owner key** (player or faction roster rules as implemented by the mod). You can only manage crew/grids you are allowed to control; permission failures show chat errors such as `No permission`.

## Admin

* Inspect roster: `/hc roster <player|steamid>`
* Dismiss any crew: `/hc dismiss <crewId>`
* Clear all crew for a player: `/hc clear roster <player|steamid>`
* Move ownership: `/hc transfer <crewId> <player|steamid>` (unseats if needed)
* Free-hire into a roster: `/hc hire <role> <stars> [player]`

See [Admin Commands](Admin-Commands).
```

- [ ] **Step 2: Write `Roles-and-Effects.md`**

Write exact contents:

```markdown
# Roles and Effects

Bonuses apply from **seated** crew of that role on the grid (unless noted). Amenities multiply role efficiency: each of bed/toilet/shower adds **+10%** efficiency (max 3 → +30%). Soft caps apply after stacking.

## Player

| UI label | Internal role | Effect |
|---|---|---|
| Gunner | Gunner | Needs a weapon seat assignment; tracking range scales with stars (and efficiency) |
| Reactor Tech | Engineer | Reactor/power output multiplier from seated techs |
| Helmsman | Helmsman | Gyro power multiplier |
| Propulsion Tech | Propulsion | Thrust multiplier |
| Quartermaster | Quartermaster | Soft-stacked **training cost discount** for the same owner pool (cap 40%) |
| Construction | DamageControl | Hull/block repair sorties — see [Damage Control](Damage-Control) |

### Gunner tracking range (meters, base)

| Stars | Range |
|---|---|
| 0 | 400 |
| 1 | 600 |
| 2 | 900 |
| 3 | 1300 |
| 4 | 1800 |
| 5 | 2500 |

### Power / gyro / thrust bonus fractions per seated crew (before amenity mult + cap)

| Stars | Power (Reactor Tech) | Gyro (Helmsman) | Thrust (Propulsion) |
|---|---|---|---|
| 0 | +1% | +2% | +2% |
| 1 | +2% | +4% | +4% |
| 2 | +3% | +6% | +6% |
| 3 | +4% | +8% | +8% |
| 4 | +5% | +10% | +10% |
| 5 | +7% | +12% | +12% |

**Caps (multiplier):** power **2.5×**, gyro **2.0×**, thrust **2.0×**.

### Training (any trainable crew)

| From → To | Cost (SC) | Minutes |
|---|---|---|
| 0 → 1 | 8,000 | 5 |
| 1 → 2 | 20,000 | 10 |
| 2 → 3 | 40,000 | 20 |
| 3 → 4 | 75,000 | 40 |
| 4 → 5 | 130,000 | 60 |

Quartermaster discount contributions by stars: 5%, 8%, 11%, 14%, 17%, 20% (soft-stacked; total discount capped at **40%**).

### Construction weld rate

Base weld application scales with stars: about **0.75×** base at 0★ to **1.25×** at 5★ (see Damage Control for sortie rules).

## Admin

* Disable roles server-wide via `AllowedRolesMask` in [World Config](World-Config)
* Restrict roles per desk with terminal checkboxes (subset of world mask)
* Free-hire role tokens (from `CrewAdminCommands.TryParseRole`):
  * Gunner: `gunner`, `g`
  * Reactor Tech: `engineer`, `eng`, `reactor`, `technician`, `rt`
  * Helmsman: `helmsman`, `helm`
  * Propulsion: `propulsion`, `prop`
  * Quartermaster: `quartermaster`, `qm`
  * Construction: `construction`, `construct`, `damage`, `dc`, `welder`, `damagecontrol`
  * Stars: `0`–`5`
```

- [ ] **Step 3: Commit**

```powershell
Set-Location "$env:LOCALAPPDATA\Temp\SpaceEngineers-HireCrew.wiki"
git add Crew-Management.md Roles-and-Effects.md
git commit -m "docs(wiki): add Crew Management and Roles"
```

---

### Task 6: Damage-Control + Crew-Stations

**Files:**
- Create/overwrite in wiki clone: `Damage-Control.md`, `Crew-Stations.md`

**Interfaces:**
- Consumes: `CrewHud.cs` path commands, `CrewConfig` repair constants, `HC_CrewStation_1.sbc`, ambient constants
- Produces: repair + stations pages

- [ ] **Step 1: Write `Damage-Control.md`**

Write exact contents:

```markdown
# Damage Control

Construction crew (role **Damage Control**) leave the ship along a player-painted path, EVA, and weld damaged/incomplete blocks using components from the ship’s conveyor network.

## Player

### One-time path setup (per grid)

Path is **shared** by all Construction crew on that grid.

```
/crew path start
```

* Look at / stand on a managed grid
* **LMB** — append waypoint (click blocks toward the airlock)
* **RMB** — finish (marks Exit / done)
* Chat helpers:

```
/crew path undo
/crew path done
/crew path clear
/crew path stop
```

```
/crew path
```

Without a subcommand, usage help is shown:

`Usage: /crew | /crew path [start|undo|done|clear|stop]`

### Runtime loop

1. Preconditions: completed path with Exit; stationed Construction crew; damaged or incomplete blocks; grid roughly idle (same class of guard as ambient presence).
2. Crew walks waypoints → Exit → jetpack EVA → welds within range (~5 m) → consumes required components from conveyor-linked inventories.
3. When work is done, components run out, or no targets remain → return via Exit → resume station/ambient behavior.
4. Manual dispatch/recall is available from the `/crew` HUD on Construction rows when the UI exposes repair controls.

### Tips

* Paint the path from the crew area to a real airlock/exit.
* Keep the ship mostly idle while sorties run.
* Higher stars weld faster (about 0.75×–1.25× vs base).

## Admin

* Construction can be disabled in `AllowedRolesMask` or per-desk role checkboxes.
* No separate admin path editor — players paint paths; moderators can dismiss/transfer Construction crew via [Admin Commands](Admin-Commands).
* Weld/mission tuning constants are compile-time in `CrewConfig` (not in `HireCrewConfig.xml` today).
```

- [ ] **Step 2: Write `Crew-Stations.md`**

Write exact contents:

```markdown
# Crew Stations

## Player

### Crew Station block

* Display name: **Crew Station**
* Subtype: `HC_CrewStation_1` (large grid)
* Purpose: dedicated HireCrew seat/station so assigned crew show a seated character and participate in ambient presence

Assign crew to the station (or other valid seats) through **`/crew`**. The block description in-game: *Dedicated HireCrew station. Assign crew here to show the seated character.*

### Ambient presence

When you are near a grid with seated crew (and the grid is mostly idle), HireCrew may spawn theatrical NPC bodies that sit/wander near their seats. This is presentation — role bonuses still come from the seated assignment logic.

Rough behavior (current constants):

* Player proximity to keep a live bot: ~90 m
* Soft neighborhood around seat: ~25 m (farther starts recover/despawn timing)
* Caps: 8 live bots per grid, 32 global
* Moving grids suppress ambient spawn

Construction repair missions can drive the same character body when a sortie is active.

## Admin

* No world-XML toggles for ambient today (`AmbientEnabled` is compile-time).
* Stations are normal blocks — ownership/permission follows grid access.
* If players report missing bodies, check RichHud is irrelevant here; verify seat assignment, idle grid, proximity, and log spam for bot spawn fallbacks (`HireCrew_Crew` → `Astronaut` / `NPC_Astronaut`).
```

- [ ] **Step 3: Commit**

```powershell
Set-Location "$env:LOCALAPPDATA\Temp\SpaceEngineers-HireCrew.wiki"
git add Damage-Control.md Crew-Stations.md
git commit -m "docs(wiki): add Damage Control and Crew Stations"
```

---

### Task 7: Admin-Commands + Troubleshooting

**Files:**
- Create/overwrite in wiki clone: `Admin-Commands.md`, `Troubleshooting.md`
- Read-only source: `docs/admin-commands.md` in main repo

**Interfaces:**
- Consumes: `docs/admin-commands.md`, `CrewAdminCommands.cs`
- Produces: ops reference + FAQ

- [ ] **Step 1: Write `Admin-Commands.md`**

Write exact contents:

```markdown
# Admin Commands

Requires Space Engineers **Admin** promote level. Commands are parsed on the client and executed on the **server** (server re-checks Admin).

Prefixes: `/hirecrew` or `/hc`

Player UI: `/crew` still opens the management HUD (not an admin tool).

## Player

These commands are **Admin only**. Players use the hire desk and `/crew`. If you are not Admin you will see `Admin only`.

## Admin

### Quick examples

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

### Who can run these

* Promote level **Admin**
* Single-player / listen-server hosts are typically Admin

### Player targeting

Arguments that take `player|steamid`:

1. Numeric **Steam ID** — online player, or identity resolved via Steam→identity map when available
2. **Display name** — exact case-insensitive match among **online** players
3. Multiple name matches → `Ambiguous:` list; no changes

### Command table

| Command | Description |
|---|---|
| `help` | List verbs |
| `config show` | Dump key world-config values |
| `config reload` | Reload `HireCrewConfig.xml` (does not mass-reroll desks) |
| `hire <role> <stars> [player]` | Free-hire into that player’s roster (default: you) |
| `reroll <blockEntityId>` | Force-refresh that hire desk pool |
| `reroll near` | Reroll nearest hire desk (prefers your current grid) |
| `roster <player\|steamid>` | List that owner’s crew (capped) |
| `dismiss <crewId>` | Remove one crew (any owner) |
| `clear roster <player\|steamid>` | Dismiss all crew for that owner |
| `clear pool <blockEntityId\|near>` | Reroll desk pool |
| `transfer <crewId> <player\|steamid>` | Move crew to another owner (unseats if needed) |

### Role tokens

* Gunner: `gunner`, `g`
* Reactor Tech: `engineer`, `eng`, `reactor`, `technician`, `rt`
* Helmsman: `helmsman`, `helm`
* Propulsion: `propulsion`, `prop`
* Quartermaster: `quartermaster`, `qm`
* Construction: `construction`, `construct`, `damage`, `dc`, `welder`, `damagecontrol`

### Stars

`0`–`5` (legacy aliases `recruit` / `regular` / `elite` still accepted)

### Removed debug commands

These no longer exist:

* `/chire …`
* `/crew hire …`

Use `/hirecrew hire …` (Admin) or the hire desk UI instead.
```

- [ ] **Step 2: Write `Troubleshooting.md`**

Write exact contents:

```markdown
# Troubleshooting

## Player

| Symptom | Likely cause | Fix |
|---|---|---|
| “Install Rich Hud Master…” | Missing dependency | Install Rich Hud Master workshop mod |
| `/crew` does nothing / cannot open | RichHud not ready or session not ready | Ensure both mods load; retry after world fully loaded |
| Hire desk F does nothing | Wrong aim point / no access | Aim at screen; check faction/ownership; try terminal **Open Hiring Desk** |
| Empty candidate list | Pool empty or roles disabled | Wait for refresh; ask admin to reroll; check desk role checkboxes |
| Cannot afford hire | Price × desk multiplier | Earn credits or ask admin about prices |
| `No permission` | Not allowed on that grid/crew | Use your own grids / faction permissions |
| `Ship not found` | HUD could not resolve a grid | Stand on / look at a grid, or use grid picker |
| No ambient NPC bodies | Far away, ship moving, caps, or unassigned | Assign seats; approach idle ship; wait for spawn |
| Construction never EVAs | No path / no Exit / ship moving / no damage | Paint path with `/crew path`; ensure damage exists; keep ship idle |
| `/chire` unknown | Removed | Use hire desk or ask an Admin |

## Admin

| Symptom | Likely cause | Fix |
|---|---|---|
| `Admin only` | Promote level | Promote user to Admin |
| `config reload` but desks unchanged | Expected | `/hc reroll near` or desk **Reroll pool now** |
| `Player not found` / `Ambiguous` | Name/Steam ID resolution | Use Steam ID; ensure target online for name match |
| `HireCrew not ready` | Session/store not initialized | Wait for server ready; check server log for HireCrew exceptions |
| XML changes ignored | Invalid file or not reloaded | Fix XML; `/hc config show`; check server log for normalize/fallback |
| Want to wipe a problem player | Moderation | `/hc clear roster <player>` |

### Log tip

Server/client log lines are prefixed with `[HireCrew]`.
```

- [ ] **Step 3: Commit**

```powershell
Set-Location "$env:LOCALAPPDATA\Temp\SpaceEngineers-HireCrew.wiki"
git add Admin-Commands.md Troubleshooting.md
git commit -m "docs(wiki): add Admin Commands and Troubleshooting"
```

---

### Task 8: Push wiki + push main repo

**Files:**
- None new; publish remotes

**Interfaces:**
- Consumes: Tasks 1–7 commits
- Produces: live wiki pages; main repo `origin/master` includes workshop deletion (+ optional plan/spec commits already local)

- [ ] **Step 1: Push wiki**

```powershell
Set-Location "$env:LOCALAPPDATA\Temp\SpaceEngineers-HireCrew.wiki"
git status -sb
git push -u origin HEAD
```

Expected: push succeeds to `SpaceEngineers-HireCrew.wiki.git`.

- [ ] **Step 2: Verify wiki in browser/API**

Run:

```powershell
gh api "repos/KerboNerd/SpaceEngineers-HireCrew/contents/?ref=master" 2>$null
Start-Process "https://github.com/KerboNerd/SpaceEngineers-HireCrew/wiki"
```

Manually confirm Home, Sidebar, and each linked page render.

Expected checklist:

* [ ] Home
* [ ] Getting-Started
* [ ] Hiring
* [ ] Crew-Management
* [ ] Roles-and-Effects
* [ ] Damage-Control
* [ ] Crew-Stations
* [ ] World-Config
* [ ] Admin-Commands
* [ ] Troubleshooting

- [ ] **Step 3: Push main repo**

```powershell
Set-Location "C:\Users\user\AppData\Roaming\SpaceEngineers\Mods\HireCrew"
git status -sb
git push origin master
```

Expected: `master` syncs to `origin/master` including workshop deletion and any docs commits.

- [ ] **Step 4: Final status**

```powershell
git status -sb
git ls-files workshop
```

Expected: clean tree tracking `origin/master`; `git ls-files workshop` prints nothing.

---

## Spec coverage (self-review)

| Spec requirement | Task |
|---|---|
| GitHub Wiki host | Task 2, 8 |
| Exhaustive player+admin docs | Tasks 3–7 |
| Feature-centric + Player/Admin subsections | Tasks 3–7 page bodies |
| Page map (all pages + Sidebar) | Tasks 3–7 |
| Sources: code / admin-commands / rewritten specs | Tasks 4–7 |
| No wiki duplication into main repo | Task 2 wiki clone path |
| Remove `workshop/` + gitignore update | Task 1 |
| No SteamCMD docs on wiki | Tasks 3–7 (omitted) |
| Publish via wiki git push | Task 8 |
| Removed commands documented | Home, Admin-Commands, Troubleshooting |

## Placeholder scan

* Role tokens are copied from `CrewAdminCommands.TryParseRole` (including Construction aliases).
* No TBD/TODO left in page bodies.
