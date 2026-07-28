# HireCrew GitHub Wiki Design

Date: 2026-07-28  
Status: Approved for planning

## Problem

The mod lives on a private GitHub repo (`KerboNerd/SpaceEngineers-HireCrew`) with design/plan docs under `docs/superpowers/`, plus `docs/admin-commands.md`. Players and server admins need end-user documentation on the GitHub Wiki—not internal SDD dumps.

Workshop SteamCMD staging under `workshop/` is no longer used and should be removed from the repo.

## Goals

- Publish an exhaustive GitHub Wiki for players and admins.
- Feature-centric pages; each feature page has **Player** and **Admin** subsections (Home / Getting-Started / Troubleshooting may use a lighter split).
- Content rewritten from live code and approved specs into plain language.
- Remove the entire `workshop/` staging tree and stop documenting upload tooling.
- Publish via cloning the wiki git remote and pushing Markdown pages (including `_Sidebar`).

## Non-goals

- Duplicating wiki pages into the main repo (unless requested later).
- Dumping raw SDD plans/specs onto the wiki unchanged.
- Steam Workshop upload docs or SteamCMD tooling.
- Publicizing the private repo (wiki stays private with the repo).
- Changing mod gameplay/code except deleting unused workshop staging and related `.gitignore` entries.

## Decisions

| Topic | Choice |
|-------|--------|
| Host | GitHub Wiki on `SpaceEngineers-HireCrew` |
| Depth | Exhaustive: player + admin guides, full config, troubleshooting, short feature notes |
| Organization | Feature-centric pages with Player / Admin subsections |
| Workflow | Clone `….wiki.git`, write pages locally, push |
| Workshop | Delete `workshop/` entirely; drop obsolete ignore rules |

## Page map

| Page | Covers |
|------|--------|
| Home | What HireCrew is, dependency on Rich Hud Master, quick links for players vs admins |
| Getting-Started | Install, blocks to place, first hire → assign loop |
| Hiring | Hire desk UI, candidates, stars/prices, pool refresh, desk terminal settings |
| Crew-Management | `/crew` HUD: assign/unassign/dismiss, bulk assign, training, amenities, off-ship grid picker |
| Roles-and-Effects | Gunner, Reactor Tech, Helmsman, Propulsion, Quartermaster, Construction |
| Damage-Control | Construction/repair dispatch, EVA repair, path tool (`/crew path …`) |
| Crew-Stations | Crew station block, seating, ambient presence |
| World-Config | Full `HireCrewConfig.xml` field reference + reload behavior |
| Admin-Commands | Full `/hirecrew` / `/hc` reference (expand from `docs/admin-commands.md`) |
| Troubleshooting | Common issues (no UI, permissions, empty pool, missing RichHud, etc.) |
| _Sidebar | Navigation matching the list above |

## Content rules

- Prefer live code (`CrewConfig`, `HireWorldConfig`, `CrewAdminCommands`, hire/HUD UI) over stale prose.
- Second source: `docs/admin-commands.md`.
- Third: approved design specs for behavior notes only; rewrite for end users.
- Document current behavior only; note removed commands (`/chire`, `/crew hire`) under Admin / Troubleshooting.
- Cross-link related pages (Hiring ↔ World-Config ↔ Admin-Commands).
- No secrets, agent reports, or workshop staging content.

## Workshop cleanup

- Delete the entire `workshop/` directory (tracked scripts/VDF/dumps and ignored `content/`, `steamcmd/`, `preview.jpg`).
- Update `.gitignore`: remove workshop-specific lines (or ignore all of `workshop/` only if a leftover empty path matters; preferred: delete folder and remove those ignore rules).
- Wiki must not document SteamCMD / workshop publish flow.

## Publishing flow

1. Enable Wiki on the GitHub repo if not already enabled.
2. Clone `https://github.com/KerboNerd/SpaceEngineers-HireCrew.wiki.git` (or `gh` equivalent).
3. Write/replace `Home.md`, feature pages, `_Sidebar.md`.
4. Commit and push to the wiki remote.
5. Verify pages render on the GitHub Wiki UI.

## Success criteria

- Wiki Home and Sidebar link every page in the map.
- Every feature page has usable Player and Admin sections (where applicable).
- World-Config and Admin-Commands are complete enough for server ops without reading C#.
- `workshop/` is gone from the working tree and from git history going forward (deleted in a normal commit; no force-history rewrite required).
- Main repo still builds/runs the mod; only unused staging was removed.
