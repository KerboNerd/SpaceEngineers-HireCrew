# Mission Crew Markers Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Show faction-visible screen-space HUD markers (square reticle + `Name · Xm`, edge-clamped) for crew on active repair/salvage missions.

**Architecture:** Pure math/visibility helpers in `CrewMissionMarkerRules` (unit-tested). Client draw module `CrewMissionMarkers` projects live character heads via camera `WorldToScreen`, clamps to screen edge, draws RichHud `BorderBox` + `Label` on `HudMain.Root`. Wired from `CrewHud` like ambient nameplates. No new network messages.

**Tech Stack:** Space Engineers ModAPI, RichHudFramework, xunit (`HireCrew.Logic.Tests`), C# 7.3 / net48.

## Global Constraints

- Mission-only (repair + salvage snapshots); no ambient markers
- Faction members (or unfactioned owner) only
- Square reticle + `DisplayName · {N} m`; edge clamp when off-screen/behind
- No GPS, no beacon blocks, no mission-sync payload changes
- Client-only draw; skip when character entity missing
- Ambient nameplates unchanged (may both show up close)

## File structure

| File | Responsibility |
| --- | --- |
| `Data/Scripts/HireCrew/CrewMissionMarkerRules.cs` | Pure visibility + screen clamp + label format (no ModAPI) |
| `Data/Scripts/HireCrew/CrewMissionMarkers.cs` | Client RichHud draw loop |
| `Data/Scripts/HireCrew/CrewHud.cs` | `SetReady` / `Update` / `Clear` hooks |
| `tests/HireCrew.Logic.Tests/CrewMissionMarkerRulesTests.cs` | Unit tests |
| `tests/HireCrew.Logic.Tests/HireCrew.Logic.Tests.csproj` | Compile link for rules file |

---

### Task 1: Pure marker rules + tests

**Files:**
- Create: `Data/Scripts/HireCrew/CrewMissionMarkerRules.cs`
- Create: `tests/HireCrew.Logic.Tests/CrewMissionMarkerRulesTests.cs`
- Modify: `tests/HireCrew.Logic.Tests/HireCrew.Logic.Tests.csproj`

**Interfaces:**
- Consumes: `CrewOwnership.Resolve` / `Matches` patterns (inline using same owner-key rules)
- Produces:
  - `CrewMissionMarkerRules.CanViewerSee(long viewerIdentityId, long viewerFactionIdOrZero, long crewOwnerKey, bool crewOwnerIsFaction, long crewOwnerIdentityId, long crewOwnerFactionIdOrZero) -> bool`
  - `CrewMissionMarkerRules.FormatLabel(string displayName, double distanceMeters) -> string`
  - `CrewMissionMarkerRules.ToHudOffset(float screenX01, float screenY01, float screenW, float screenH, bool behindCamera, float marginPx, out float offsetX, out float offsetY)`

- [x] **Step 1: Write the failing tests**

Create `tests/HireCrew.Logic.Tests/CrewMissionMarkerRulesTests.cs`:

```csharp
using HireCrew;
using Xunit;

public class CrewMissionMarkerRulesTests
{
    [Fact]
    public void CanViewerSee_Owner_Unfactioned()
    {
        Assert.True(CrewMissionMarkerRules.CanViewerSee(
            viewerIdentityId: 10,
            viewerFactionIdOrZero: 0,
            crewOwnerKey: 10,
            crewOwnerIsFaction: false,
            crewOwnerIdentityId: 10,
            crewOwnerFactionIdOrZero: 0));
    }

    [Fact]
    public void CanViewerSee_FactionMember_FactionOwnedCrew()
    {
        Assert.True(CrewMissionMarkerRules.CanViewerSee(
            viewerIdentityId: 11,
            viewerFactionIdOrZero: 99,
            crewOwnerKey: 99,
            crewOwnerIsFaction: true,
            crewOwnerIdentityId: 10,
            crewOwnerFactionIdOrZero: 99));
    }

    [Fact]
    public void CanViewerSee_FactionMember_PersonalCrewOfMate()
    {
        Assert.True(CrewMissionMarkerRules.CanViewerSee(
            viewerIdentityId: 11,
            viewerFactionIdOrZero: 99,
            crewOwnerKey: 10,
            crewOwnerIsFaction: false,
            crewOwnerIdentityId: 10,
            crewOwnerFactionIdOrZero: 99));
    }

    [Fact]
    public void CanViewerSee_Outsider_False()
    {
        Assert.False(CrewMissionMarkerRules.CanViewerSee(
            viewerIdentityId: 50,
            viewerFactionIdOrZero: 7,
            crewOwnerKey: 99,
            crewOwnerIsFaction: true,
            crewOwnerIdentityId: 10,
            crewOwnerFactionIdOrZero: 99));
    }

    [Fact]
    public void FormatLabel_Rounds_Meters()
    {
        Assert.Equal("Rex · 842 m", CrewMissionMarkerRules.FormatLabel("Rex", 842.4));
        Assert.Equal("Crew · 0 m", CrewMissionMarkerRules.FormatLabel(null, -3));
    }

    [Fact]
    public void ToHudOffset_Center_Is_Near_Zero()
    {
        float x, y;
        CrewMissionMarkerRules.ToHudOffset(0.5f, 0.5f, 1920f, 1080f, false, 24f, out x, out y);
        Assert.InRange(x, -1f, 1f);
        Assert.InRange(y, -1f, 1f);
    }

    [Fact]
    public void ToHudOffset_Offscreen_Clamps_To_Edge()
    {
        float x, y;
        // Far right, middle — clamp inside half-width - margin
        CrewMissionMarkerRules.ToHudOffset(1.4f, 0.5f, 1920f, 1080f, false, 24f, out x, out y);
        Assert.Equal(1920f * 0.5f - 24f, x, 1);
        Assert.InRange(y, -1f, 1f);
    }

    [Fact]
    public void ToHudOffset_Behind_Flips_Toward_Opposite_Edge()
    {
        float x, y;
        // Would be center-right if in front; behind flips direction then clamps
        CrewMissionMarkerRules.ToHudOffset(0.75f, 0.5f, 1920f, 1080f, true, 24f, out x, out y);
        Assert.True(x < 0f);
    }
}
```

- [x] **Step 2: Link the new source in the test project**

Add to `tests/HireCrew.Logic.Tests/HireCrew.Logic.Tests.csproj` ItemGroup:

```xml
    <Compile Include="..\..\Data\Scripts\HireCrew\CrewMissionMarkerRules.cs" Link="CrewMissionMarkerRules.cs" />
```

- [x] **Step 3: Run tests to verify they fail**

Skipped automated run (workspace rule). Implementer/user verify when ready.

- [x] **Step 4: Implement `CrewMissionMarkerRules.cs`**

Create `Data/Scripts/HireCrew/CrewMissionMarkerRules.cs`:

```csharp
using System;

namespace HireCrew
{
    /// <summary>
    /// Pure helpers for mission HUD markers (visibility, label, screen clamp).
    /// No ModAPI — unit-tested from HireCrew.Logic.Tests.
    /// </summary>
    public static class CrewMissionMarkerRules
    {
        public static bool CanViewerSee(
            long viewerIdentityId,
            long viewerFactionIdOrZero,
            long crewOwnerKey,
            bool crewOwnerIsFaction,
            long crewOwnerIdentityId,
            long crewOwnerFactionIdOrZero)
        {
            if (viewerIdentityId == 0)
                return false;

            // Direct owner identity always sees their crew.
            if (crewOwnerIdentityId != 0 && crewOwnerIdentityId == viewerIdentityId)
                return true;
            if (!crewOwnerIsFaction && crewOwnerKey != 0 && crewOwnerKey == viewerIdentityId)
                return true;

            long viewerKey;
            bool viewerIsFaction;
            CrewOwnership.Resolve(viewerIdentityId, viewerFactionIdOrZero, out viewerKey, out viewerIsFaction);

            if (crewOwnerKey == viewerKey && crewOwnerIsFaction == viewerIsFaction)
                return true;

            if (viewerFactionIdOrZero == 0)
                return false;

            // Faction-owned roster key.
            if (crewOwnerIsFaction && crewOwnerKey == viewerFactionIdOrZero)
                return true;

            // Personal crew of a faction mate.
            if (!crewOwnerIsFaction
                && crewOwnerFactionIdOrZero != 0
                && crewOwnerFactionIdOrZero == viewerFactionIdOrZero)
                return true;

            return false;
        }

        public static string FormatLabel(string displayName, double distanceMeters)
        {
            string name = string.IsNullOrEmpty(displayName) ? "Crew" : displayName.Trim();
            int meters = (int)Math.Round(Math.Max(0.0, distanceMeters));
            return name + " · " + meters + " m";
        }

        /// <summary>
        /// Map WorldToScreen-style 0..1 coords (Y from top) to RichHud Offset
        /// (pixels from screen center, Y up). Clamps to a margin inset from edges.
        /// When behindCamera, flips through center so the pin sits on the opposite edge.
        /// </summary>
        public static void ToHudOffset(
            float screenX01,
            float screenY01,
            float screenW,
            float screenH,
            bool behindCamera,
            float marginPx,
            out float offsetX,
            out float offsetY)
        {
            if (screenW < 1f) screenW = 1f;
            if (screenH < 1f) screenH = 1f;
            if (marginPx < 0f) marginPx = 0f;

            float x = (screenX01 - 0.5f) * screenW;
            float y = (0.5f - screenY01) * screenH; // SE Y-down → RichHud Y-up

            if (behindCamera)
            {
                x = -x;
                y = -y;
            }

            float halfW = screenW * 0.5f - marginPx;
            float halfH = screenH * 0.5f - marginPx;
            if (halfW < 1f) halfW = 1f;
            if (halfH < 1f) halfH = 1f;

            // If outside the inset rect, project onto its border along the ray from center.
            float ax = Math.Abs(x);
            float ay = Math.Abs(y);
            if (ax > halfW || ay > halfH)
            {
                float scaleX = ax > 1e-4f ? halfW / ax : float.MaxValue;
                float scaleY = ay > 1e-4f ? halfH / ay : float.MaxValue;
                float scale = scaleX < scaleY ? scaleX : scaleY;
                if (scale < float.MaxValue)
                {
                    x *= scale;
                    y *= scale;
                }
            }

            offsetX = x;
            offsetY = y;
        }
    }
}
```

- [x] **Step 5: Run tests to verify they pass**

User verify:
`dotnet test tests/HireCrew.Logic.Tests/HireCrew.Logic.Tests.csproj --filter CrewMissionMarkerRulesTests -v n`

- [ ] **Step 6: Commit** (only if the user asked to commit)

```bash
git add Data/Scripts/HireCrew/CrewMissionMarkerRules.cs tests/HireCrew.Logic.Tests/CrewMissionMarkerRulesTests.cs tests/HireCrew.Logic.Tests/HireCrew.Logic.Tests.csproj
git commit -m "$(cat <<'EOF'
Add pure mission marker visibility and screen-clamp helpers.

EOF
)"
```

---

### Task 2: Client `CrewMissionMarkers` + `CrewHud` wiring

**Files:**
- Create: `Data/Scripts/HireCrew/CrewMissionMarkers.cs`
- Modify: `Data/Scripts/HireCrew/CrewHud.cs` (same call sites as `CrewAmbientNameplates`)

**Interfaces:**
- Consumes: `CrewMissionMarkerRules.*`; `CrewSession.ClientRepairMissions` / `ClientSalvageMissions` / `Store`; `HudMain.Root`; RichHud `BorderBox`, `Label`, `HudElementBase`
- Produces: `CrewMissionMarkers.SetReady(bool)`, `Update(CrewSession)`, `Clear()`

- [x] **Step 1: Implement `CrewMissionMarkers.cs`**

Create `Data/Scripts/HireCrew/CrewMissionMarkers.cs` with this structure (full file):

```csharp
using System;
using System.Collections.Generic;
using RichHudFramework.Client;
using RichHudFramework.UI;
using RichHudFramework.UI.Client;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRageMath;

namespace HireCrew
{
    /// <summary>
    /// Client screen-space markers for crew on repair/salvage missions.
    /// </summary>
    public static class CrewMissionMarkers
    {
        private sealed class Marker
        {
            public HudElementBase Root;
            public BorderBox Reticle;
            public Label Label;
            public string CachedText;
        }

        private static readonly Dictionary<string, Marker> ByCrewId = new Dictionary<string, Marker>();
        private static readonly List<string> ScratchRemove = new List<string>();
        private static readonly HashSet<string> Live = new HashSet<string>();
        private static readonly GlyphFormat LabelFormat = new GlyphFormat(
            new Color(220, 235, 245),
            TextAlignment.Center,
            1.05f);
        private static readonly Color ReticleColor = new Color(200, 230, 245, 220);

        private const float ReticleSize = 28f;
        private const float ReticleThickness = 2f;
        private const float EdgeMarginPx = 28f;
        private const float HeadHeightMeters = 1.9f;

        private static bool _ready;

        public static void SetReady(bool ready)
        {
            _ready = ready;
            if (!ready)
                Clear();
        }

        public static void Clear()
        {
            foreach (var kv in ByCrewId)
                DestroyMarker(kv.Value);
            ByCrewId.Clear();
        }

        public static void Update(CrewSession session)
        {
            if (!_ready || !RichHudClient.Registered)
                return;
            if (MyAPIGateway.Utilities != null && MyAPIGateway.Utilities.IsDedicated)
                return;
            if (session == null || session.Store == null)
                return;
            if (MyAPIGateway.Session == null || MyAPIGateway.Session.Camera == null)
                return;
            var local = MyAPIGateway.Session.Player;
            if (local == null)
            {
                Clear();
                return;
            }

            long viewerId = local.IdentityId;
            long viewerFactionId = 0;
            var viewerFac = MyAPIGateway.Session.Factions.TryGetPlayerFaction(viewerId);
            if (viewerFac != null)
                viewerFactionId = viewerFac.FactionId;

            var cam = MyAPIGateway.Session.Camera;
            Vector3D camPos = cam.Position;
            Vector3D camFwd = cam.WorldMatrix.Forward;
            Vector2 screenSize = HudMain.ScreenSize;
            float screenW = screenSize.X;
            float screenH = screenSize.Y;
            if (screenW < 1f || screenH < 1f)
                return;

            Live.Clear();
            Collect(session, session.ClientRepairMissions, viewerId, viewerFactionId, camPos, camFwd, screenW, screenH, Live);
            Collect(session, session.ClientSalvageMissions, viewerId, viewerFactionId, camPos, camFwd, screenW, screenH, Live);

            ScratchRemove.Clear();
            foreach (var kv in ByCrewId)
            {
                if (!Live.Contains(kv.Key))
                    ScratchRemove.Add(kv.Key);
            }
            for (int i = 0; i < ScratchRemove.Count; i++)
            {
                Marker m;
                if (!ByCrewId.TryGetValue(ScratchRemove[i], out m))
                    continue;
                ByCrewId.Remove(ScratchRemove[i]);
                DestroyMarker(m);
            }
        }

        private static void Collect(
            CrewSession session,
            System.Collections.IList entries,
            long viewerId,
            long viewerFactionId,
            Vector3D camPos,
            Vector3D camFwd,
            float screenW,
            float screenH,
            HashSet<string> live)
        {
            if (entries == null)
                return;
            for (int i = 0; i < entries.Count; i++)
            {
                string crewId;
                string displayName;
                if (!TryReadEntry(entries[i], out crewId, out displayName))
                    continue;
                if (string.IsNullOrEmpty(crewId))
                    continue;

                var crew = session.Store.Get(crewId);
                if (crew == null || !crew.CharacterEntityId.HasValue)
                    continue;

                long ownerFac = 0;
                if (!crew.OwnerIsFaction)
                {
                    long ownerId = crew.OwnerIdentityId != 0 ? crew.OwnerIdentityId : crew.OwnerKey;
                    var of = MyAPIGateway.Session.Factions.TryGetPlayerFaction(ownerId);
                    if (of != null)
                        ownerFac = of.FactionId;
                }
                else
                    ownerFac = crew.OwnerKey;

                if (!CrewMissionMarkerRules.CanViewerSee(
                    viewerId,
                    viewerFactionId,
                    crew.OwnerKey,
                    crew.OwnerIsFaction,
                    crew.OwnerIdentityId,
                    ownerFac))
                    continue;

                IMyEntity ent;
                if (!MyAPIGateway.Entities.TryGetEntityById(crew.CharacterEntityId.Value, out ent)
                    || ent == null || ent.Closed)
                    continue;
                var character = ent as IMyCharacter;
                if (character == null)
                    continue;

                Vector3D head = character.GetPosition() + character.WorldMatrix.Up * HeadHeightMeters;
                double dist = Vector3D.Distance(camPos, head);
                bool behind = Vector3D.Dot(head - camPos, camFwd) <= 0;

                Vector3 screen = MyAPIGateway.Session.Camera.WorldToScreen(ref head);
                // WorldToScreen: X/Y typically 0..1; Z is depth (engine-dependent). Prefer Dot for behind.
                float ox, oy;
                CrewMissionMarkerRules.ToHudOffset(
                    screen.X,
                    screen.Y,
                    screenW,
                    screenH,
                    behind,
                    EdgeMarginPx,
                    out ox,
                    out oy);

                string text = CrewMissionMarkerRules.FormatLabel(
                    !string.IsNullOrEmpty(displayName) ? displayName : crew.DisplayName,
                    dist);
                EnsureMarker(crewId, text, ox, oy);
                live.Add(crewId);
            }
        }

        private static bool TryReadEntry(object entry, out string crewId, out string displayName)
        {
            crewId = null;
            displayName = null;
            var r = entry as RepairMissionSnapshotEntry;
            if (r != null)
            {
                crewId = r.CrewId;
                displayName = r.DisplayName;
                return true;
            }
            var s = entry as SalvageMissionSnapshotEntry;
            if (s != null)
            {
                crewId = s.CrewId;
                displayName = s.DisplayName;
                return true;
            }
            return false;
        }

        private static void EnsureMarker(string crewId, string text, float offsetX, float offsetY)
        {
            Marker m;
            if (!ByCrewId.TryGetValue(crewId, out m) || m == null || m.Root == null)
            {
                m = CreateMarker();
                ByCrewId[crewId] = m;
            }

            m.Root.Visible = true;
            m.Root.Offset = new Vector2(offsetX, offsetY);

            if (!string.Equals(m.CachedText, text, StringComparison.Ordinal))
            {
                m.CachedText = text;
                m.Label.Text = text;
            }
        }

        private static Marker CreateMarker()
        {
            var root = new HudElementBase(HudMain.Root)
            {
                Size = new Vector2(ReticleSize + 8f, ReticleSize + 28f),
                ParentAlignment = ParentAlignments.Center,
                Visible = true,
                ZOffset = -2
            };

            var reticle = new BorderBox(root)
            {
                Size = new Vector2(ReticleSize, ReticleSize),
                Thickness = ReticleThickness,
                Color = ReticleColor,
                ParentAlignment = ParentAlignments.Center | ParentAlignments.Top,
                Offset = new Vector2(0f, -4f),
                Visible = true
            };

            var label = new Label(root)
            {
                Format = LabelFormat,
                AutoResize = true,
                ParentAlignment = ParentAlignments.Center | ParentAlignments.Bottom,
                Offset = new Vector2(0f, 2f),
                Visible = true,
                Text = "Crew · 0 m"
            };

            return new Marker
            {
                Root = root,
                Reticle = reticle,
                Label = label,
                CachedText = null
            };
        }

        private static void DestroyMarker(Marker m)
        {
            if (m == null)
                return;
            try
            {
                if (m.Label != null)
                {
                    m.Label.Visible = false;
                    m.Label.Unregister();
                }
            }
            catch { }
            try
            {
                if (m.Reticle != null)
                {
                    m.Reticle.Visible = false;
                    m.Reticle.Unregister();
                }
            }
            catch { }
            try
            {
                if (m.Root != null)
                {
                    m.Root.Visible = false;
                    m.Root.Unregister();
                }
            }
            catch { }
        }
    }
}
```

Notes for implementer:
- If `HudElementBase` cannot be constructed directly as a plain container in this RichHud build, use a transparent `TexturedBox` (`Color = new Color(0,0,0,0)`) as `Root` instead.
- If `WorldToScreen` signature differs (`WorldToScreen(Vector3D)` without `ref`), match the local ModAPI overload.
- Prefer two typed `Collect` overloads (`IList<RepairMissionSnapshotEntry>` / `IList<SalvageMissionSnapshotEntry>`) if the `IList` + cast approach feels brittle — behavior must stay identical.

- [x] **Step 2: Wire into `CrewHud.cs`**

In `OnHudReady` (where `CrewAmbientNameplates.SetReady(true)` is called), also call:

```csharp
            CrewMissionMarkers.SetReady(true);
```

In `OnHudReset` and `Unload` (where nameplates are set false), also call:

```csharp
            CrewMissionMarkers.SetReady(false);
```

In `Update`, immediately after `CrewAmbientNameplates.Update(...)`:

```csharp
            CrewMissionMarkers.Update(CrewSession.Instance);
```

- [ ] **Step 3: Manual smoke (in-game — do not automate)** — awaiting user

1. Hire Damage Control + Salvage Ops; paint path / mark salvage zone as needed.
2. Send each on mission — confirm square reticle + `Name · Xm` tracks the bot.
3. Look away — marker clamps to screen edge.
4. Recall / finish — marker disappears.
5. Ambient-only crew — no marker.
6. (If available) second faction client sees markers; outsider does not.

- [ ] **Step 4: Commit** (only if the user asked to commit)

```bash
git add Data/Scripts/HireCrew/CrewMissionMarkers.cs Data/Scripts/HireCrew/CrewHud.cs
git commit -m "$(cat <<'EOF'
Show screen-space HUD markers for on-mission construction and salvage crew.

EOF
)"
```

---

## Spec coverage check

| Spec requirement | Task |
| --- | --- |
| Custom HUD marker on mission | Task 2 |
| Square reticle + name/distance | Task 1 FormatLabel + Task 2 draw |
| Edge clamp / behind camera | Task 1 ToHudOffset + Task 2 |
| Faction visibility | Task 1 CanViewerSee + Task 2 |
| No GPS / beacons / sync changes | All tasks (client-only) |
| Skip missing character | Task 2 Collect continue |
| Ambient nameplates unchanged | Task 2 does not touch nameplates |
| Dedicated server no draw | Task 2 early return |

## Placeholder / consistency self-review

- No TBD steps; APIs named consistently (`CanViewerSee`, `FormatLabel`, `ToHudOffset`, `SetReady`/`Update`/`Clear`).
- Test project link added in Task 1 before implementation.
- Commits gated on user request (workspace commit rule).
