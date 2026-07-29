# Mission Crew Markers Design

Date: 2026-07-29  
Status: Approved for planning

## Problem

Construction (Damage Control) and Salvage Ops crew leave the ship on EVA missions. Ambient nameplates only draw within ~20 m, so players lose track of active sortie crew at range.

## Goals

- Show a **custom HUD marker** for each crew on an active **repair or salvage** mission.
- Marker: **square reticle** + label `DisplayName · {meters}` (whole meters).
- **Screen-edge clamp** when the crew is off-screen or behind the camera.
- Visible to players in the **same faction** as the crew owner (owner alone when unfactioned).
- Client-only draw; no GPS entries, no beacon blocks.

## Non-goals

- Real Beacon / Antenna blocks on bots.
- Temporary GPS pins.
- Markers for ambient / station wander (mission-only).
- Role-colored icons or repair-vs-salvage chrome (v1 same look).
- Max-range fade or anti-overlap layout for stacked edge markers.
- Changing mission sync payloads or server mission logic.
- Hiding ambient nameplates when a mission marker is present (both may show up close in v1).

## Approach

**Screen-space RichHud markers (Approach A).**  
New client module `CrewMissionMarkers`, lifecycle parallel to `CrewAmbientNameplates` (`SetReady` / `Update` / `Clear` from `CrewHud`). Each client frame: read existing `ClientRepairMissions` + `ClientSalvageMissions`, resolve roster `CharacterEntityId` → live character head position, faction-gate the local player, project world→screen (clamp to edge), draw reticle + label on `HudMain.Root`.

## Player loop

1. Dispatch Construction or Salvage crew as today.
2. Faction members see a square reticle + `Name · Xm` tracking that crew while the mission is active and the character body is present on their client.
3. Marker clears on recall, mission complete, or when the character entity is unavailable locally.

## Components

| Piece | Role |
| --- | --- |
| `CrewMissionMarkers` | Client draw + projection/clamp + faction filter |
| `CrewHud` | Call `SetReady` / `Update` / `Clear` alongside nameplates |
| Existing mission sync | Source of active `CrewId` / `DisplayName` lists |
| Roster `CrewRecord` | `CharacterEntityId`, owner/faction fields for resolve + gate |

## Data / networking

No new messages. Markers consume:

- `IList<RepairMissionSnapshotEntry>` / `IList<SalvageMissionSnapshotEntry>` already synced to clients.
- Local roster lookup for `CharacterEntityId` and ownership.

If the character entity is missing on a client (streamed out), skip that marker for that frame; do not invent a server-reported position for v1.

## Faction visibility

Show marker when local player identity:

- owns the crew (same rules as existing ownership helpers), or
- shares a faction with the crew owner / faction-owned `OwnerKey`.

Non-faction outsiders never see markers.

## Visuals (v1)

- Hollow square reticle, ~24–32 px, light cyan/white.
- Label under reticle: `{DisplayName} · {N} m`.
- Same style for repair and salvage.
- No max draw distance beyond entity availability.
- Dedicated server: no draw.

## Edge cases

| Case | Behavior |
| --- | --- |
| Mission ends / recall | Marker removed |
| Body despawned / not synced | No marker until body exists again |
| Multiple markers at same edge | Slight overlap OK |
| Close range | Ambient nameplate may also show; leave both |
| Unfactioned owner | Only that owner sees markers |

## Testing

- Solo: send salvage + construction; confirm reticle + name/distance; confirm edge clamp when looking away; confirm clear on recall/finish.
- Faction MP: second faction member sees markers; outsider does not.
- Far from bot (entity gone): marker absent; returns when entity streams back in.
- Ambient-only crew (no mission): no marker.

## Out of scope follow-ups

- Hide ambient nameplate while mission marker active.
- Distinct repair/salvage colors.
- Server-synced fallback position when character is despawned.
- Edge-stack separation.
