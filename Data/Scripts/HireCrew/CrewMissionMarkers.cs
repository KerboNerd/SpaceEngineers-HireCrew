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
    /// Always HUD overlay (visible through blocks). Position via RichHud PixelToWorld plane.
    /// </summary>
    public static class CrewMissionMarkers
    {
        private enum MarkerKind
        {
            Construction = 0,
            Salvage = 1
        }

        private sealed class Marker
        {
            public TexturedBox Root;
            public BorderBox Reticle;
            public Label SpecLabel;
            public Label Label;
            public string CachedText;
            public string CachedSpec;
            public MarkerKind CachedKind;
            public bool KindSet;
        }

        private static readonly Dictionary<string, Marker> ByCrewId = new Dictionary<string, Marker>();
        private static readonly List<string> ScratchRemove = new List<string>();
        private static readonly HashSet<string> Live = new HashSet<string>();

        private static readonly Color ConstructionColor = new Color(90, 170, 255, 230);
        private static readonly Color SalvageColor = new Color(255, 160, 40, 230);

        private const float ReticleSize = 28f;
        private const float ReticleThickness = 2f;
        private const float EdgeMarginPx = 28f;
        private const float HeadHeightMeters = 1.9f;
        private const float BehindDotEps = 0.02f;
        private const string SpecConstruction = "CONSTRUCTION";
        private const string SpecSalvage = "SALVAGE";

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
            MatrixD camWorld = cam.WorldMatrix;
            Vector3D camFwd = camWorld.Forward;
            float screenW = HudMain.ScreenWidth;
            float screenH = HudMain.ScreenHeight;
            if (screenW < 1f || screenH < 1f)
                return;

            MatrixD pixelToWorld = HudMain.PixelToWorld;
            MatrixD worldToPixel = MatrixD.Invert(pixelToWorld);

            Live.Clear();
            CollectRepair(session, session.ClientRepairMissions, viewerId, viewerFactionId, camPos, camWorld, camFwd, screenW, screenH, pixelToWorld, worldToPixel, Live);
            CollectSalvage(session, session.ClientSalvageMissions, viewerId, viewerFactionId, camPos, camWorld, camFwd, screenW, screenH, pixelToWorld, worldToPixel, Live);

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

        private static void CollectRepair(
            CrewSession session,
            IList<RepairMissionSnapshotEntry> entries,
            long viewerId,
            long viewerFactionId,
            Vector3D camPos,
            MatrixD camWorld,
            Vector3D camFwd,
            float screenW,
            float screenH,
            MatrixD pixelToWorld,
            MatrixD worldToPixel,
            HashSet<string> live)
        {
            if (entries == null)
                return;
            for (int i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                if (e == null)
                    continue;
                TryAddMarker(session, e.CrewId, e.DisplayName, MarkerKind.Construction, viewerId, viewerFactionId, camPos, camWorld, camFwd, screenW, screenH, pixelToWorld, worldToPixel, live);
            }
        }

        private static void CollectSalvage(
            CrewSession session,
            IList<SalvageMissionSnapshotEntry> entries,
            long viewerId,
            long viewerFactionId,
            Vector3D camPos,
            MatrixD camWorld,
            Vector3D camFwd,
            float screenW,
            float screenH,
            MatrixD pixelToWorld,
            MatrixD worldToPixel,
            HashSet<string> live)
        {
            if (entries == null)
                return;
            for (int i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                if (e == null)
                    continue;
                TryAddMarker(session, e.CrewId, e.DisplayName, MarkerKind.Salvage, viewerId, viewerFactionId, camPos, camWorld, camFwd, screenW, screenH, pixelToWorld, worldToPixel, live);
            }
        }

        private static void TryAddMarker(
            CrewSession session,
            string crewId,
            string displayName,
            MarkerKind kind,
            long viewerId,
            long viewerFactionId,
            Vector3D camPos,
            MatrixD camWorld,
            Vector3D camFwd,
            float screenW,
            float screenH,
            MatrixD pixelToWorld,
            MatrixD worldToPixel,
            HashSet<string> live)
        {
            if (string.IsNullOrEmpty(crewId))
                return;

            var crew = session.Store.Get(crewId);
            if (crew == null || !crew.CharacterEntityId.HasValue)
                return;

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
                return;

            IMyEntity ent;
            if (!MyAPIGateway.Entities.TryGetEntityById(crew.CharacterEntityId.Value, out ent)
                || ent == null || ent.Closed)
                return;
            var character = ent as IMyCharacter;
            if (character == null)
                return;

            Vector3D head = character.GetPosition() + character.WorldMatrix.Up * HeadHeightMeters;
            double dist = Vector3D.Distance(camPos, head);
            Vector3D toHead = head - camPos;
            bool behind = Vector3D.Dot(toHead, camFwd) <= BehindDotEps;

            float ox, oy;
            if (!behind && TryProjectToHud(camPos, head, pixelToWorld, worldToPixel, out ox, out oy))
            {
                CrewMissionMarkerRules.ClampHudOffset(ref ox, ref oy, screenW, screenH, EdgeMarginPx);
            }
            else
            {
                float rx = (float)Vector3D.Dot(toHead, camWorld.Right);
                float ry = (float)Vector3D.Dot(toHead, camWorld.Up);
                CrewMissionMarkerRules.ClampDirToScreenEdge(rx, ry, screenW, screenH, EdgeMarginPx, out ox, out oy);
            }

            string text = CrewMissionMarkerRules.FormatLabel(
                !string.IsNullOrEmpty(displayName) ? displayName : crew.DisplayName,
                dist);
            string spec = kind == MarkerKind.Salvage ? SpecSalvage : SpecConstruction;
            EnsureMarker(crewId, kind, spec, text, ox, oy);
            live.Add(crewId);
        }

        /// <summary>
        /// Ray from camera through worldPos, hit RichHud PixelToWorld plane, invert to pixel Offset.
        /// Matches CamSpaceNode / HudMain screen space (includes FOV + ResScale).
        /// </summary>
        private static bool TryProjectToHud(
            Vector3D camPos,
            Vector3D worldPos,
            MatrixD pixelToWorld,
            MatrixD worldToPixel,
            out float offsetX,
            out float offsetY)
        {
            offsetX = 0f;
            offsetY = 0f;

            Vector3D planePoint = pixelToWorld.Translation;
            // Plane faces the camera; use camera→plane as normal reference.
            Vector3D planeNormal = camPos - planePoint;
            double nLen = planeNormal.Length();
            if (nLen < 1e-6)
                return false;
            planeNormal /= nLen;

            Vector3D dir = worldPos - camPos;
            double dirLen = dir.Length();
            if (dirLen < 1e-6)
                return false;
            dir /= dirLen;

            double denom = Vector3D.Dot(dir, planeNormal);
            if (Math.Abs(denom) < 1e-8)
                return false;

            double t = Vector3D.Dot(planePoint - camPos, planeNormal) / denom;
            if (t <= 1e-4)
                return false;

            Vector3D hit = camPos + dir * t;
            Vector3D local = Vector3D.Transform(hit, worldToPixel);
            offsetX = (float)local.X;
            offsetY = (float)local.Y;
            return true;
        }

        private static void EnsureMarker(
            string crewId,
            MarkerKind kind,
            string spec,
            string text,
            float offsetX,
            float offsetY)
        {
            Marker m;
            if (!ByCrewId.TryGetValue(crewId, out m) || m == null || m.Root == null)
            {
                m = CreateMarker();
                ByCrewId[crewId] = m;
            }

            m.Root.Visible = true;
            m.Root.Offset = new Vector2(offsetX, offsetY);

            if (!m.KindSet || m.CachedKind != kind)
            {
                m.KindSet = true;
                m.CachedKind = kind;
                Color c = kind == MarkerKind.Salvage ? SalvageColor : ConstructionColor;
                m.Reticle.Color = c;
                var fmt = new GlyphFormat(c, TextAlignment.Center, 1.05f);
                m.SpecLabel.Format = fmt;
                m.Label.Format = fmt;
            }

            if (!string.Equals(m.CachedSpec, spec, StringComparison.Ordinal))
            {
                m.CachedSpec = spec;
                m.SpecLabel.Text = spec;
            }

            if (!string.Equals(m.CachedText, text, StringComparison.Ordinal))
            {
                m.CachedText = text;
                m.Label.Text = text;
            }
        }

        private static Marker CreateMarker()
        {
            // Root center = projected head. Spec above, reticle center, name below.
            var root = new TexturedBox(HudMain.Root)
            {
                Size = new Vector2(160f, ReticleSize + 48f),
                Color = new Color(0, 0, 0, 0),
                ParentAlignment = ParentAlignments.Center,
                Visible = true,
                ZOffset = sbyte.MaxValue,
                UseCursor = false,
                ShareCursor = false
            };

            var spec = new Label(root)
            {
                Format = new GlyphFormat(ConstructionColor, TextAlignment.Center, 1.0f),
                AutoResize = true,
                ParentAlignment = ParentAlignments.Center,
                Offset = new Vector2(0f, ReticleSize * 0.5f + 12f),
                Visible = true,
                Text = SpecConstruction
            };

            var reticle = new BorderBox(root)
            {
                Size = new Vector2(ReticleSize, ReticleSize),
                Thickness = ReticleThickness,
                Color = ConstructionColor,
                ParentAlignment = ParentAlignments.Center,
                Offset = Vector2.Zero,
                Visible = true
            };

            var label = new Label(root)
            {
                Format = new GlyphFormat(ConstructionColor, TextAlignment.Center, 1.05f),
                AutoResize = true,
                ParentAlignment = ParentAlignments.Center,
                Offset = new Vector2(0f, -(ReticleSize * 0.5f + 12f)),
                Visible = true,
                Text = "Crew · 0 m"
            };

            return new Marker
            {
                Root = root,
                Reticle = reticle,
                SpecLabel = spec,
                Label = label,
                CachedText = null,
                CachedSpec = null,
                CachedKind = MarkerKind.Construction,
                KindSet = false
            };
        }

        private static void DestroyMarker(Marker m)
        {
            if (m == null)
                return;
            try
            {
                if (m.SpecLabel != null)
                {
                    m.SpecLabel.Visible = false;
                    m.SpecLabel.Unregister();
                }
            }
            catch { }
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
