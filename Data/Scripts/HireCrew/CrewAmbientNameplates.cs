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
    /// Client-only floating name labels above ambient crew bots.
    /// Vanilla player nametags do not render for IsBot-controlled characters.
    /// </summary>
    public static class CrewAmbientNameplates
    {
        private sealed class Plate
        {
            public CustomSpaceNode Space;
            public Label Label;
            public long CharEntityId;
            public string CachedName;
        }

        private static readonly Dictionary<string, Plate> ByCrewId = new Dictionary<string, Plate>();
        private static readonly List<string> ScratchRemove = new List<string>();
        private static readonly GlyphFormat NameFormat = new GlyphFormat(
            new Color(235, 240, 245),
            TextAlignment.Center,
            1.15f);

        private static bool _ready;
        private const float MaxDrawMeters = 20f;
        private const float HeadHeightMeters = 1.9f;

        public static void SetReady(bool ready)
        {
            _ready = ready;
            if (!ready)
                Clear();
        }

        public static void Clear()
        {
            foreach (var kv in ByCrewId)
                DestroyPlate(kv.Value);
            ByCrewId.Clear();
        }

        /// <summary>Call every client frame (or from CrewHud.Update).</summary>
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

            var cam = MyAPIGateway.Session.Camera;
            Vector3D camPos = cam.Position;
            double maxSq = MaxDrawMeters * MaxDrawMeters;

            var live = new HashSet<string>();
            foreach (var crew in session.Store.All)
            {
                if (crew == null || string.IsNullOrEmpty(crew.CrewId))
                    continue;
                if (!crew.CharacterEntityId.HasValue)
                    continue;

                IMyEntity ent;
                if (!MyAPIGateway.Entities.TryGetEntityById(crew.CharacterEntityId.Value, out ent)
                    || ent == null || ent.Closed)
                    continue;

                var character = ent as IMyCharacter;
                if (character == null)
                    continue;

                Vector3D head = character.GetPosition() + character.WorldMatrix.Up * HeadHeightMeters;
                if (Vector3D.DistanceSquared(camPos, head) > maxSq)
                    continue;
                if (Vector3D.Dot(head - camPos, cam.WorldMatrix.Forward) <= 0)
                    continue;

                string name = AmbientName(crew);
                live.Add(crew.CrewId);
                EnsurePlate(crew.CrewId, character.EntityId, name, head, cam);
            }

            ScratchRemove.Clear();
            foreach (var kv in ByCrewId)
            {
                if (!live.Contains(kv.Key))
                    ScratchRemove.Add(kv.Key);
            }
            for (int i = 0; i < ScratchRemove.Count; i++)
            {
                Plate plate;
                if (!ByCrewId.TryGetValue(ScratchRemove[i], out plate))
                    continue;
                ByCrewId.Remove(ScratchRemove[i]);
                DestroyPlate(plate);
            }
        }

        private static string AmbientName(CrewRecord crew)
        {
            if (crew != null && !string.IsNullOrEmpty(crew.DisplayName))
                return crew.DisplayName.Trim();
            return "Crew";
        }

        private static void EnsurePlate(
            string crewId,
            long charId,
            string name,
            Vector3D headPos,
            IMyCamera cam)
        {
            Plate plate;
            if (!ByCrewId.TryGetValue(crewId, out plate) || plate == null || plate.Space == null || plate.Label == null)
            {
                plate = CreatePlate(crewId, charId, name);
                ByCrewId[crewId] = plate;
            }

            plate.CharEntityId = charId;
            if (!string.Equals(plate.CachedName, name, StringComparison.Ordinal))
            {
                plate.CachedName = name;
                plate.Label.Text = name;
            }

            plate.Label.Visible = true;
            plate.Space.Visible = true;

            // Capture for matrix delegate (updated every layout).
            Vector3D pos = headPos;
            plate.Space.UpdateMatrixFunc = () => BuildBillboardMatrix(pos, cam);
        }

        private static Plate CreatePlate(string crewId, long charId, string name)
        {
            var space = new CustomSpaceNode(HudMain.Root)
            {
                Visible = true,
            };
            var label = new Label(space)
            {
                Text = name ?? "Crew",
                Format = NameFormat,
                AutoResize = true,
                Visible = true,
                ParentAlignment = ParentAlignments.Center,
            };

            return new Plate
            {
                Space = space,
                Label = label,
                CharEntityId = charId,
                CachedName = name,
            };
        }

        private static MatrixD BuildBillboardMatrix(Vector3D worldPos, IMyCamera cam)
        {
            if (cam == null)
                return MatrixD.Identity;

            Vector3D camPos = cam.Position;
            Vector3D toCam = camPos - worldPos;
            double dist = toCam.Length();
            if (dist < 0.05)
                return MatrixD.Identity;
            toCam /= dist;

            // Plane faces the camera; scale grows with distance so text stays readable.
            Vector3D up = cam.WorldMatrix.Up;
            Vector3D forward = -toCam;
            Vector3D right = Vector3D.Cross(up, forward);
            if (right.LengthSquared() < 1e-6)
                right = cam.WorldMatrix.Right;
            right.Normalize();
            up = Vector3D.Normalize(Vector3D.Cross(forward, right));

            double scale = MathHelper.Clamp(dist * 0.00135, 0.0008, 0.012);
            MatrixD orient = MatrixD.CreateWorld(worldPos, forward, up);
            return MatrixD.CreateScale(scale) * orient;
        }

        private static void DestroyPlate(Plate plate)
        {
            if (plate == null)
                return;
            try
            {
                if (plate.Label != null)
                {
                    plate.Label.Visible = false;
                    plate.Label.Unregister();
                }
            }
            catch { }
            try
            {
                if (plate.Space != null)
                {
                    plate.Space.Visible = false;
                    plate.Space.Unregister();
                }
            }
            catch { }
        }
    }
}
