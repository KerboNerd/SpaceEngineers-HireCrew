using System;
using System.Collections.Generic;
using Sandbox.Definitions;
using Sandbox.ModAPI;
using VRage;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.Game.ModAPI.Interfaces;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRage.Utils;
using VRageMath;

namespace HireCrew
{
    /// <summary>
    /// Per-crew Salvage Ops EVA grind missions (no path → fly → grind → teleport home).
    /// </summary>
    public static class CrewSalvageMission
    {
        private sealed class MissionRuntime
        {
            public string CrewId;
            public long HomeGridEntityId;
            /// <summary>Grid currently being ground (updates as debris fragments change).</summary>
            public long TargetGridEntityId;
            public bool HasZone;
            public double ZoneMinX;
            public double ZoneMinY;
            public double ZoneMinZ;
            public double ZoneMaxX;
            public double ZoneMaxY;
            public double ZoneMaxZ;
            public SalvageMissionState State;
            public double StateSeconds;
            public int Hints;
            public bool NotifiedCargoFull;
            public Vector3I TargetCell;
            public bool HasTargetCell;
            public double PoseCooldown;
            public bool HasFlyVel;
            public double VelX;
            public double VelY;
            public double VelZ;
            public bool HasFlyFwd;
            public double FwdX;
            public double FwdY;
            public double FwdZ;
            public bool NotifiedCreativeRefund;
            /// <summary>Stable hover for current TargetCell (avoids chasing a moving approach point).</summary>
            public bool HasApproachHover;
            public double HoverX;
            public double HoverY;
            public double HoverZ;
            public double NoGrindProgressSeconds;
            public Vector3I SkipCell;
            public bool HasSkipCell;
        }

        private static readonly Dictionary<string, MissionRuntime> ByCrew =
            new Dictionary<string, MissionRuntime>();
        private static readonly List<string> KeyScratch = new List<string>(16);
        private static readonly List<string> RemoveScratch = new List<string>(8);
        private static readonly List<IMySlimBlock> BlockScratch = new List<IMySlimBlock>(256);
        private static readonly List<IMyInventory> InvScratch = new List<IMyInventory>(64);
        private static readonly List<IMyCubeGrid> GridGroupScratch = new List<IMyCubeGrid>(8);
        private static readonly HashSet<IMyEntity> EntityScratch = new HashSet<IMyEntity>();
        private static readonly HashSet<Vector3I> NeighborPosScratch = new HashSet<Vector3I>();
        private static readonly Vector3I[] OrthoDirs =
        {
            new Vector3I(1, 0, 0),
            new Vector3I(-1, 0, 0),
            new Vector3I(0, 1, 0),
            new Vector3I(0, -1, 0),
            new Vector3I(0, 0, 1),
            new Vector3I(0, 0, -1)
        };

        public static bool IsCrewOnMission(string crewId)
        {
            return !string.IsNullOrEmpty(crewId) && ByCrew.ContainsKey(crewId);
        }

        public static void CancelForCrew(string crewId)
        {
            if (string.IsNullOrEmpty(crewId)) return;
            ByCrew.Remove(crewId);
        }

        public static void ClearAll()
        {
            ByCrew.Clear();
        }

        public static void CollectActiveSnapshots(List<SalvageMissionSnapshotEntry> into)
        {
            if (into == null) return;
            into.Clear();
            foreach (var kv in ByCrew)
            {
                MissionRuntime m = kv.Value;
                if (m == null || m.State == SalvageMissionState.Idle) continue;
                string name = null;
                var session = CrewSession.Instance;
                if (session != null && session.Store != null)
                {
                    var crew = session.Store.Get(m.CrewId);
                    if (crew != null) name = crew.DisplayName;
                }
                into.Add(new SalvageMissionSnapshotEntry
                {
                    CrewId = m.CrewId,
                    DisplayName = name,
                    GridEntityId = m.HomeGridEntityId,
                    State = (int)m.State,
                    Hints = m.Hints
                });
            }
        }

        public static bool TryGetMissionPose(
            string crewId,
            IMyTerminalBlock seat,
            out Vector3D pos,
            out Vector3D forward,
            out Vector3D up)
        {
            pos = Vector3D.Zero;
            forward = Vector3D.Forward;
            up = Vector3D.Up;
            if (string.IsNullOrEmpty(crewId) || seat == null)
                return false;

            MissionRuntime m;
            if (!ByCrew.TryGetValue(crewId, out m) || m == null)
                return false;

            up = seat.WorldMatrix.Up;
            IMyCubeGrid targetGrid = null;
            IMyEntity tEnt;
            if (m.TargetGridEntityId != 0
                && MyAPIGateway.Entities.TryGetEntityById(m.TargetGridEntityId, out tEnt))
                targetGrid = tEnt as IMyCubeGrid;

            IMySlimBlock slim;
            if (targetGrid != null && TryResolveTarget(m, targetGrid, out slim) && slim != null)
            {
                Vector3D targetPos = GetSlimWorld(slim, targetGrid);
                Vector3D from = seat.GetPosition();
                pos = ComputeApproachHover(from, targetPos, targetGrid);
                Vector3D toBlock = targetPos - pos;
                if (toBlock.LengthSquared() < 0.01)
                    toBlock = -seat.WorldMatrix.Forward;
                toBlock.Normalize();
                forward = toBlock;
                return true;
            }

            pos = seat.WorldMatrix.Translation + seat.WorldMatrix.Right * 1.2 + up * 0.1;
            forward = seat.WorldMatrix.Forward;
            return true;
        }

        public static bool DispatchCrew(
            CrewSession session,
            string crewId,
            BoundingBoxD zone,
            long seedGridEntityId)
        {
            if (session == null || session.Store == null || string.IsNullOrEmpty(crewId))
                return false;
            if (IsCrewOnMission(crewId))
                return false;
            if (!CrewSalvageRules.IsValidZone(
                    zone.Min.X, zone.Min.Y, zone.Min.Z,
                    zone.Max.X, zone.Max.Y, zone.Max.Z))
                return false;

            var crew = session.Store.Get(crewId);
            if (crew == null || crew.Status != CrewStatus.Seated || crew.Role != CrewRole.SalvageOps)
                return false;
            if (crew.GridEntityId == 0)
                return false;

            IMyEntity homeEnt;
            if (!MyAPIGateway.Entities.TryGetEntityById(crew.GridEntityId, out homeEnt))
                return false;
            var home = homeEnt as IMyCubeGrid;
            if (home == null || !CrewAmbientPresence.IsGridIdle(home))
                return false;
            if (!CanStartAnotherOnGrid(crew.GridEntityId))
                return false;

            double radius = CrewConfig.SalvageScanRadiusMeters;
            if (Vector3D.DistanceSquared(home.WorldAABB.Center, zone.Center) > radius * radius)
                return false;

            Vector3D from = home.WorldAABB.Center;
            if (crew.SeatEntityId.HasValue)
            {
                IMyEntity seatEnt;
                if (MyAPIGateway.Entities.TryGetEntityById(crew.SeatEntityId.Value, out seatEnt)
                    && seatEnt != null)
                    from = seatEnt.GetPosition();
            }

            var m = new MissionRuntime
            {
                CrewId = crew.CrewId,
                HomeGridEntityId = crew.GridEntityId,
                TargetGridEntityId = seedGridEntityId,
                HasZone = true,
                ZoneMinX = zone.Min.X,
                ZoneMinY = zone.Min.Y,
                ZoneMinZ = zone.Min.Z,
                ZoneMaxX = zone.Max.X,
                ZoneMaxY = zone.Max.Y,
                ZoneMaxZ = zone.Max.Z,
                State = SalvageMissionState.EvaTransit,
                StateSeconds = 0
            };

            IMySlimBlock first;
            IMyCubeGrid firstGrid;
            if (!TryPickBlockInZone(m, home, crew, from, out first, out firstGrid) || first == null)
            {
                // Zone already empty — drop the mark so the highlight goes away.
                if (session != null)
                    session.ClearSalvageMarkForHome(home.EntityId);
                return false;
            }
            SetTarget(m, first);

            ByCrew[crew.CrewId] = m;
            Log("salvage dispatch crew=" + crew.CrewId
                + " home=" + m.HomeGridEntityId
                + " zoneSeed=" + seedGridEntityId
                + " target=" + m.TargetGridEntityId);
            return true;
        }

        public static bool RecallCrew(string crewId)
        {
            if (string.IsNullOrEmpty(crewId))
                return false;
            MissionRuntime m;
            if (!ByCrew.TryGetValue(crewId, out m) || m == null)
                return false;
            BeginReturn(m);
            Log("salvage recall crew=" + crewId);
            return true;
        }

        /// <summary>Clear / recall: <paramref name="newTargetGridEntityId"/> 0 returns all home sorties.</summary>
        public static int RetargetHomeMissions(IMyCubeGrid home, long newTargetGridEntityId)
        {
            if (newTargetGridEntityId == 0)
                return RetargetHomeMissions(home, default(BoundingBoxD), 0, clear: true);

            IMyEntity tEnt;
            if (!MyAPIGateway.Entities.TryGetEntityById(newTargetGridEntityId, out tEnt) || tEnt == null)
                return 0;
            var newTarget = tEnt as IMyCubeGrid;
            if (newTarget == null || newTarget.Closed)
                return 0;
            return RetargetHomeMissions(home, SalvageTargetStore.BuildZoneFromGrid(newTarget), newTargetGridEntityId, clear: false);
        }

        /// <summary>
        /// Active Salvage Ops from <paramref name="home"/> switch to a new zone (or return if clear).
        /// </summary>
        public static int RetargetHomeMissions(IMyCubeGrid home, BoundingBoxD zone, long seedGridEntityId)
        {
            return RetargetHomeMissions(home, zone, seedGridEntityId, clear: false);
        }

        private static int RetargetHomeMissions(
            IMyCubeGrid home,
            BoundingBoxD zone,
            long seedGridEntityId,
            bool clear)
        {
            if (home == null || ByCrew.Count == 0)
                return 0;

            int n = 0;
            CopyCrewKeys(KeyScratch);
            for (int i = 0; i < KeyScratch.Count; i++)
            {
                MissionRuntime m;
                if (!ByCrew.TryGetValue(KeyScratch[i], out m) || m == null)
                    continue;

                IMyEntity homeEnt;
                IMyCubeGrid missionHome = null;
                if (MyAPIGateway.Entities.TryGetEntityById(m.HomeGridEntityId, out homeEnt))
                    missionHome = homeEnt as IMyCubeGrid;
                if (missionHome == null || missionHome.Closed)
                    continue;
                if (missionHome.EntityId != home.EntityId && !missionHome.IsSameConstructAs(home))
                    continue;

                if (clear)
                {
                    BeginReturn(m);
                    n++;
                    continue;
                }

                Vector3D from = home.WorldAABB.Center;
                IMyCharacter character;
                CrewRecord crew = null;
                var session = CrewSession.Instance;
                if (session != null && session.Store != null)
                {
                    crew = session.Store.Get(m.CrewId);
                    if (crew != null && TryGetCharacter(crew, out character)
                        && character != null && !character.Closed)
                        from = character.GetPosition();
                }

                m.HasZone = true;
                m.ZoneMinX = zone.Min.X;
                m.ZoneMinY = zone.Min.Y;
                m.ZoneMinZ = zone.Min.Z;
                m.ZoneMaxX = zone.Max.X;
                m.ZoneMaxY = zone.Max.Y;
                m.ZoneMaxZ = zone.Max.Z;
                m.TargetGridEntityId = seedGridEntityId;

                IMySlimBlock first;
                IMyCubeGrid firstGrid;
                if (!TryPickBlockInZone(m, home, crew, from, out first, out firstGrid) || first == null)
                {
                    BeginReturnZoneDone(m);
                    n++;
                    continue;
                }

                ClearTarget(m);
                SetTarget(m, first);
                ClearFlyDynamics(m);
                m.State = SalvageMissionState.EvaTransit;
                m.StateSeconds = 0;
                m.Hints = 0;
                m.NotifiedCargoFull = false;
                n++;
                Log("salvage retarget crew=" + m.CrewId + " zoneSeed=" + seedGridEntityId);
            }
            return n;
        }

        public static int RecallMissionsOnManagedHomes(
            long identityId,
            Func<long, IMyCubeGrid, bool> canManage)
        {
            if (canManage == null || ByCrew.Count == 0)
                return 0;
            int n = 0;
            CopyCrewKeys(KeyScratch);
            for (int i = 0; i < KeyScratch.Count; i++)
            {
                MissionRuntime m;
                if (!ByCrew.TryGetValue(KeyScratch[i], out m) || m == null)
                    continue;
                IMyEntity homeEnt;
                IMyCubeGrid home = null;
                if (MyAPIGateway.Entities.TryGetEntityById(m.HomeGridEntityId, out homeEnt))
                    home = homeEnt as IMyCubeGrid;
                if (home == null || !canManage(identityId, home))
                    continue;
                BeginReturn(m);
                n++;
            }
            return n;
        }

        public static void Tick(CrewSession session)
        {
            if (session == null || session.Store == null)
                return;
            if (MyAPIGateway.Multiplayer == null || !MyAPIGateway.Multiplayer.IsServer)
                return;
            AdvanceLogical(session, 1.0);
            CleanupMissing(session);
        }

        public static void UpdateMovement(CrewSession session)
        {
            if (session == null || session.Store == null)
                return;
            if (MyAPIGateway.Multiplayer == null || !MyAPIGateway.Multiplayer.IsServer)
                return;

            float dt = 1f / 60f;
            RemoveScratch.Clear();
            CopyCrewKeys(KeyScratch);
            for (int ki = 0; ki < KeyScratch.Count; ki++)
            {
                MissionRuntime m;
                if (!ByCrew.TryGetValue(KeyScratch[ki], out m) || m == null)
                    continue;
                var crew = session.Store.Get(m.CrewId);
                if (crew == null || crew.Status != CrewStatus.Seated || crew.Role != CrewRole.SalvageOps)
                {
                    RemoveScratch.Add(m.CrewId);
                    continue;
                }

                IMyEntity homeEnt;
                IMyCubeGrid home = null;
                if (MyAPIGateway.Entities.TryGetEntityById(m.HomeGridEntityId, out homeEnt))
                    home = homeEnt as IMyCubeGrid;
                if (home == null || home.Closed || !CrewAmbientPresence.IsGridIdle(home))
                {
                    BeginReturn(m);
                    continue;
                }

                IMyTerminalBlock seat = null;
                if (crew.SeatEntityId.HasValue)
                {
                    IMyEntity seatEnt;
                    if (MyAPIGateway.Entities.TryGetEntityById(crew.SeatEntityId.Value, out seatEnt))
                        seat = seatEnt as IMyTerminalBlock;
                }

                IMyCharacter character;
                TryGetCharacter(crew, out character);

                Vector3D fromPos = character != null && !character.Closed
                    ? character.GetPosition()
                    : (seat != null ? seat.GetPosition() : home.WorldAABB.Center);

                IMyCubeGrid target = ResolveMissionTargetGrid(m);
                if (target == null || target.Closed)
                {
                    IMySlimBlock next;
                    IMyCubeGrid nextGrid;
                    if (!TryPickBlockInZone(m, home, crew, fromPos, out next, out nextGrid) || next == null)
                    {
                        BeginReturnZoneDone(m);
                        continue;
                    }
                    SetTarget(m, next);
                    target = nextGrid;
                }

                m.StateSeconds += dt;
                m.PoseCooldown = Math.Max(0, m.PoseCooldown - dt);

                switch (m.State)
                {
                    case SalvageMissionState.EvaTransit:
                        TickEvaTransit(m, crew, character, seat, home, target, dt);
                        break;
                    case SalvageMissionState.Grinding:
                        TickGrinding(session, m, crew, character, seat, home, target, dt);
                        break;
                    default:
                        RemoveScratch.Add(m.CrewId);
                        break;
                }
            }

            for (int i = 0; i < RemoveScratch.Count; i++)
                CancelForCrew(RemoveScratch[i]);
        }

        private static void AdvanceLogical(CrewSession session, double dt)
        {
            CopyCrewKeys(KeyScratch);
            for (int ki = 0; ki < KeyScratch.Count; ki++)
            {
                MissionRuntime m;
                if (!ByCrew.TryGetValue(KeyScratch[ki], out m) || m == null)
                    continue;
                var crew = session.Store.Get(m.CrewId);
                if (crew == null) continue;

                IMyCharacter character;
                if (TryGetCharacter(crew, out character) && character != null && !character.Closed)
                    continue;

                if (m.State == SalvageMissionState.EvaTransit)
                {
                    m.StateSeconds += dt;
                    if (m.StateSeconds > 2.0)
                    {
                        m.State = SalvageMissionState.Grinding;
                        m.StateSeconds = 0;
                    }
                    continue;
                }

                if (m.State == SalvageMissionState.Grinding)
                {
                    IMyEntity homeEnt;
                    IMyCubeGrid home = null;
                    if (MyAPIGateway.Entities.TryGetEntityById(m.HomeGridEntityId, out homeEnt))
                        home = homeEnt as IMyCubeGrid;
                    if (home == null)
                    {
                        BeginReturn(m);
                        continue;
                    }

                    IMyCubeGrid target = ResolveMissionTargetGrid(m);
                    IMySlimBlock slim;
                    if (target == null
                        || !TryResolveTarget(m, target, out slim)
                        || slim == null
                        || slim.IsDestroyed)
                    {
                        IMyCubeGrid nextGrid;
                        if (!TryPickBlockInZone(m, home, crew, home.WorldAABB.Center, out slim, out nextGrid)
                            || slim == null)
                        {
                            BeginReturnZoneDone(m);
                            continue;
                        }
                        SetTarget(m, slim);
                        target = nextGrid;
                    }

                    float amount = CrewConfig.GetSalvageGrindIntegrityPerSecond(crew.Stars) * (float)dt;
                    GrindResult result = TryGrindTick(slim, home, null, amount, (float)dt);
                    if (result == GrindResult.CargoFull)
                    {
                        NotifyCargoFull(session, m, crew);
                        BeginReturn(m);
                        continue;
                    }
                    if (result == GrindResult.Failed)
                    {
                        ClearTarget(m);
                        IMyCubeGrid nextGrid;
                        if (!TryPickBlockInZone(m, home, crew, home.WorldAABB.Center, out slim, out nextGrid)
                            || slim == null)
                            BeginReturnZoneDone(m);
                        else
                            SetTarget(m, slim);
                        continue;
                    }
                    if (result == GrindResult.Removed
                        || slim.IsDestroyed
                        || (target != null && !BlockStillPresent(target, m.TargetCell)))
                    {
                        ClearTarget(m);
                        IMyCubeGrid nextGrid;
                        if (!TryPickBlockInZone(m, home, crew, home.WorldAABB.Center, out slim, out nextGrid)
                            || slim == null)
                            BeginReturnZoneDone(m);
                        else
                            SetTarget(m, slim);
                    }
                }
            }
        }

        private static void CleanupMissing(CrewSession session)
        {
            RemoveScratch.Clear();
            foreach (var kv in ByCrew)
            {
                if (kv.Value == null || session.Store.Get(kv.Key) == null)
                    RemoveScratch.Add(kv.Key);
            }
            for (int i = 0; i < RemoveScratch.Count; i++)
                CancelForCrew(RemoveScratch[i]);
        }

        private static void TickEvaTransit(
            MissionRuntime m,
            CrewRecord crew,
            IMyCharacter character,
            IMyTerminalBlock seat,
            IMyCubeGrid home,
            IMyCubeGrid target,
            float dt)
        {
            Vector3D fromPos = character != null && !character.Closed
                ? character.GetPosition()
                : (seat != null ? seat.GetPosition() : home.WorldAABB.Center);

            IMySlimBlock slim;
            if (!TryResolveTarget(m, target, out slim) || slim == null || slim.IsDestroyed)
            {
                IMyCubeGrid nextGrid;
                if (!TryPickNextBlock(m, home, crew, fromPos, out slim, out nextGrid) || slim == null)
                {
                    BeginReturnZoneDone(m);
                    return;
                }
                SetTarget(m, slim);
                if (nextGrid != null)
                    target = nextGrid;
            }

            Vector3D blockPos = GetSlimWorld(slim, target);
            Vector3D hover = GetOrCreateApproachHover(m, fromPos, blockPos, target);
            Vector3D flyTo = hover;
            Vector3D stage;
            // Only stage while still away from the stage point — arriving there used to idle forever.
            bool staging = NeedsSalvageStaging(target, fromPos, hover, out stage)
                && Vector3D.DistanceSquared(fromPos, stage)
                    > CrewConfig.SalvageEvaArriveMeters * CrewConfig.SalvageEvaArriveMeters;
            if (staging)
                flyTo = stage;

            IMyCubeGrid flyGrid = target != null ? target : home;
            double arrive = CrewConfig.SalvageEvaArriveMeters;
            double arriveSq = arrive * arrive;
            double grindR = CrewConfig.SalvageGrindRangeMeters;
            double grindRSq = grindR * grindR;

            if (character != null && !character.Closed)
            {
                Vector3D pos = character.GetPosition();
                bool inRange = Vector3D.DistanceSquared(pos, blockPos) <= grindRSq;
                bool atFlyTo = Vector3D.DistanceSquared(pos, flyTo) <= arriveSq;

                if (inRange)
                {
                    m.State = SalvageMissionState.Grinding;
                    m.StateSeconds = 0;
                    m.NoGrindProgressSeconds = 0;
                    return;
                }

                // Close enough to hover but still outside grind range — step in toward the block.
                if (!staging && atFlyTo)
                    flyTo = blockPos + (hover - blockPos) * 0.35;

                FlyToward(m, character, flyGrid, flyTo, CrewConfig.GetSalvageEvaSpeedMeters(crew.Stars), dt);

                pos = character.GetPosition();
                if (Vector3D.DistanceSquared(pos, blockPos) <= grindRSq)
                {
                    m.State = SalvageMissionState.Grinding;
                    m.StateSeconds = 0;
                    m.NoGrindProgressSeconds = 0;
                }
                else if (m.StateSeconds > 45.0)
                {
                    // Pathing deadlock — snap into grind range beside the block.
                    try
                    {
                        character.SetPosition(hover);
                        character.WorldMatrix = MatrixD.CreateWorld(
                            hover,
                            Vector3D.Normalize(blockPos - hover),
                            EvaUp(flyGrid, character));
                    }
                    catch { }
                    m.State = SalvageMissionState.Grinding;
                    m.StateSeconds = 0;
                    m.NoGrindProgressSeconds = 0;
                }
            }
            else if (m.StateSeconds > 2.0)
            {
                m.State = SalvageMissionState.Grinding;
                m.StateSeconds = 0;
                m.NoGrindProgressSeconds = 0;
            }
        }

        private static void TickGrinding(
            CrewSession session,
            MissionRuntime m,
            CrewRecord crew,
            IMyCharacter character,
            IMyTerminalBlock seat,
            IMyCubeGrid home,
            IMyCubeGrid target,
            float dt)
        {
            Vector3D fromPos = character != null && !character.Closed
                ? character.GetPosition()
                : (seat != null ? seat.GetPosition() : home.WorldAABB.Center);

            IMySlimBlock slim;
            if (!TryResolveTarget(m, target, out slim) || slim == null || slim.IsDestroyed)
            {
                IMyCubeGrid nextGrid;
                if (!TryPickNextBlock(m, home, crew, fromPos, out slim, out nextGrid) || slim == null)
                {
                    BeginReturnZoneDone(m);
                    return;
                }
                SetTarget(m, slim);
                if (nextGrid != null)
                    target = nextGrid;
            }

            Vector3D blockPos = GetSlimWorld(slim, target);
            double grindR = CrewConfig.SalvageGrindRangeMeters;
            if (character != null && !character.Closed)
            {
                double distSq = Vector3D.DistanceSquared(character.GetPosition(), blockPos);
                // Soft re-approach while staying in Grinding — EvaTransit flip was the bounce.
                if (distSq > grindR * grindR)
                {
                    Vector3D hover = GetOrCreateApproachHover(m, character.GetPosition(), blockPos, target);
                    FlyToward(
                        m,
                        character,
                        target ?? home,
                        hover,
                        CrewConfig.GetSalvageEvaSpeedMeters(crew.Stars) * 0.7f,
                        dt);
                    return;
                }
                HoldGrindPose(m, character, target ?? home, blockPos, dt);
            }

            float beforeIntegrity = 0f;
            try { beforeIntegrity = slim.Integrity; }
            catch { }

            float amount = CrewConfig.GetSalvageGrindIntegrityPerSecond(crew.Stars) * dt;
            GrindResult result = TryGrindTick(slim, home, character, amount, dt);
            if (result == GrindResult.CargoFull)
            {
                NotifyCargoFull(session, m, crew);
                BeginReturn(m);
                return;
            }
            if (result == GrindResult.Failed)
            {
                SkipCurrentBlock(m, home, crew, target, fromPos);
                return;
            }

            if (result == GrindResult.Removed
                || slim.IsDestroyed
                || !BlockStillPresent(target, m.TargetCell))
            {
                m.HasSkipCell = false;
                ClearTarget(m);
                IMyCubeGrid nextGrid;
                if (!TryPickNextBlock(m, home, crew, fromPos, out slim, out nextGrid) || slim == null)
                {
                    BeginReturnZoneDone(m);
                    return;
                }
                SetTarget(m, slim);
                if (nextGrid != null)
                    target = nextGrid;
                // New leaf may be across the wreck — EVA only then, not for same-cell drift.
                if (character != null && !character.Closed)
                {
                    Vector3D nextPos = GetSlimWorld(slim, target);
                    double nextDistSq = Vector3D.DistanceSquared(character.GetPosition(), nextPos);
                    if (CrewSalvageRules.NeedsEvaAfterRetarget(nextDistSq, grindR))
                    {
                        m.State = SalvageMissionState.EvaTransit;
                        m.StateSeconds = 0;
                    }
                }
                return;
            }

            float afterIntegrity = beforeIntegrity;
            try { afterIntegrity = slim.Integrity; }
            catch { }
            bool madeProgress = afterIntegrity < beforeIntegrity - 0.01f;
            if (madeProgress)
                m.NoGrindProgressSeconds = 0;
            else
            {
                m.NoGrindProgressSeconds += dt;
                if (m.NoGrindProgressSeconds > 2.5)
                    SkipCurrentBlock(m, home, crew, target, fromPos);
            }
        }

        private static void SkipCurrentBlock(
            MissionRuntime m,
            IMyCubeGrid home,
            CrewRecord crew,
            IMyCubeGrid target,
            Vector3D fromPos)
        {
            if (m == null) return;
            if (m.HasTargetCell)
            {
                m.SkipCell = m.TargetCell;
                m.HasSkipCell = true;
            }
            ClearTarget(m);
            IMySlimBlock next;
            IMyCubeGrid nextGrid;
            if (!TryPickNextBlock(m, home, crew, fromPos, out next, out nextGrid) || next == null)
                BeginReturnZoneDone(m);
            else
                SetTarget(m, next);
            m.NoGrindProgressSeconds = 0;
            m.State = SalvageMissionState.EvaTransit;
            m.StateSeconds = 0;
        }

        private enum GrindResult
        {
            Ok = 0,
            CargoFull = 1,
            Failed = 2,
            Removed = 3
        }

        private static GrindResult TryGrindTick(
            IMySlimBlock slim,
            IMyCubeGrid homeGrid,
            IMyCharacter character,
            float integrityDelta,
            float dt)
        {
            if (slim == null || homeGrid == null || integrityDelta <= 0f)
                return GrindResult.Failed;

            // Always grind into the character first — DecreaseMountLevel into remote cargo often
            // leaves comps on the block stockpile (lost on RemoveBlock). Then push to cargo.
            IMyInventory buffer = GetCharacterInventory(character);
            CollectDepositInventories(homeGrid, InvScratch);
            if (InvScratch.Count == 0 && buffer == null)
                return GrindResult.CargoFull;

            if (!HomeHasFreeVolume(homeGrid) && !HasCharacterInventorySpace(character))
                return GrindResult.CargoFull;

            // Cap so a single tick cannot erase a whole block (loses component payout).
            float maxIntegrity = 1f;
            try { maxIntegrity = Math.Max(1f, slim.MaxIntegrity); }
            catch { }
            if (dt < 0.001f) dt = 0.001f;
            float maxStep = maxIntegrity * CrewConfig.SalvageGrindMaxIntegrityFractionPerSecond * dt;
            if (integrityDelta > maxStep)
                integrityDelta = maxStep;

            // Finish stubborn last percent with a decisive dismount.
            float buildRatio = 1f;
            try { buildRatio = slim.BuildLevelRatio; }
            catch { }
            if (IsReadyToFinish(slim, buildRatio))
                return FinishDismountAndRemove(slim, homeGrid, character);

            float beforeIntegrity = 0f;
            float beforeBuild = buildRatio;
            try { beforeIntegrity = slim.Integrity; }
            catch { beforeIntegrity = 0f; }

            bool creative = IsCreativeWorld();
            bool progressed = false;
            if (buffer != null && InventoryHasSpace(buffer))
            {
                if (TryDecreaseMount(slim, buffer, integrityDelta, beforeIntegrity, beforeBuild))
                    progressed = true;
            }

            // Fallback: grind straight into a cargo inventory that accepts components.
            for (int i = 0; i < InvScratch.Count && !progressed; i++)
            {
                var inv = InvScratch[i];
                if (!InventoryHasSpace(inv) || !InventoryAcceptsComponents(inv))
                    continue;
                if (TryDecreaseMount(slim, inv, integrityDelta, beforeIntegrity, beforeBuild))
                    progressed = true;
            }

            if (buffer != null)
            {
                try { slim.MoveItemsFromConstructionStockpile(buffer); }
                catch { }
                TransferInventoryToHome(buffer, homeGrid);
            }
            FlushStockpileToBufferThenHome(slim, buffer, homeGrid);

            try { buildRatio = slim.BuildLevelRatio; }
            catch { }

            // Keen drops no grind comps in Creative — spawn a recipe-fraction refund into home cargo.
            if (creative && (progressed || beforeBuild - buildRatio > 0.0005f))
                DepositCreativeRefund(slim, homeGrid, beforeBuild - buildRatio);

            if (IsReadyToFinish(slim, buildRatio))
                return FinishDismountAndRemove(slim, homeGrid, character);

            if (!progressed)
            {
                if (!HomeHasFreeVolume(homeGrid) && !HasCharacterInventorySpace(character))
                    return GrindResult.CargoFull;
                return GrindResult.Ok;
            }

            return GrindResult.Ok;
        }

        private static bool IsReadyToFinish(IMySlimBlock slim, float buildRatio)
        {
            if (slim == null) return true;
            try
            {
                if (slim.IsDestroyed || slim.IsFullyDismounted)
                    return true;
                if (buildRatio <= 0.05f)
                    return true;
                if (slim.Integrity <= 0.05f)
                    return true;
            }
            catch
            {
                try { return slim.IsDestroyed; }
                catch { return false; }
            }
            return false;
        }

        private static bool InventoryHasSpace(IMyInventory inv)
        {
            if (inv == null) return false;
            try { return inv.CurrentVolume < inv.MaxVolume; }
            catch { return false; }
        }

        private static bool TryDecreaseMount(
            IMySlimBlock slim,
            IMyInventory inv,
            float integrityDelta,
            float beforeIntegrity,
            float beforeBuild)
        {
            if (slim == null || inv == null || integrityDelta <= 0f)
                return false;
            try
            {
                // true = vanilla deconstruct efficiency (no real grinder tool equipped).
                slim.DecreaseMountLevel(integrityDelta, inv, true);
            }
            catch { return false; }

            try { slim.MoveItemsFromConstructionStockpile(inv); }
            catch { }

            try
            {
                if (slim.Integrity < beforeIntegrity - 0.01f)
                    return true;
                if (slim.BuildLevelRatio < beforeBuild - 0.0005f)
                    return true;
            }
            catch { }
            return false;
        }

        private static GrindResult FinishDismountAndRemove(
            IMySlimBlock slim,
            IMyCubeGrid homeGrid,
            IMyCharacter character)
        {
            if (slim == null)
                return GrindResult.Removed;

            float remainBuild = 1f;
            try { remainBuild = slim.BuildLevelRatio; }
            catch { }

            IMyInventory buffer = GetCharacterInventory(character);
            CollectDepositInventories(homeGrid, InvScratch);

            // Prefer character — FullyDismount into distant cargo often drops comps.
            IMyInventory primary = null;
            if (buffer != null && InventoryHasSpace(buffer))
                primary = buffer;
            if (primary == null)
            {
                for (int i = 0; i < InvScratch.Count; i++)
                {
                    if (InventoryHasSpace(InvScratch[i]) && InventoryAcceptsComponents(InvScratch[i]))
                    {
                        primary = InvScratch[i];
                        break;
                    }
                }
            }

            if (primary != null)
            {
                try { slim.FullyDismount(primary); }
                catch { }
                try { slim.MoveItemsFromConstructionStockpile(primary); }
                catch { }
                try { slim.ClearConstructionStockpile(primary); }
                catch { }
            }

            FlushStockpileToBufferThenHome(slim, buffer, homeGrid);
            if (buffer != null)
                TransferInventoryToHome(buffer, homeGrid);

            // Creative: FullyDismount still yields nothing — refund whatever build ratio remained.
            if (IsCreativeWorld() && remainBuild > 0.001f)
                DepositCreativeRefund(slim, homeGrid, remainBuild);

            try
            {
                IMyCubeGrid g = slim.CubeGrid;
                if (g != null)
                    g.RemoveBlock(slim, true);
            }
            catch { }

            return GrindResult.Removed;
        }

        private static bool IsCreativeWorld()
        {
            try
            {
                return MyAPIGateway.Session != null && MyAPIGateway.Session.CreativeMode;
            }
            catch { return false; }
        }

        /// <summary>
        /// Creative worlds discard grind loot. Deposit <paramref name="buildFraction"/> of the
        /// block recipe into home cargo (approx. same payout as survival grinding).
        /// </summary>
        private static void DepositCreativeRefund(IMySlimBlock slim, IMyCubeGrid homeGrid, float buildFraction)
        {
            if (slim == null || homeGrid == null || buildFraction <= 0.0005f)
                return;

            MyCubeBlockDefinition def = null;
            try { def = slim.BlockDefinition as MyCubeBlockDefinition; }
            catch { }
            if (def == null || def.Components == null || def.Components.Length == 0)
                return;

            CollectDepositInventories(homeGrid, InvScratch);
            if (InvScratch.Count == 0)
                return;

            if (buildFraction > 1f) buildFraction = 1f;

            for (int c = 0; c < def.Components.Length; c++)
            {
                var comp = def.Components[c];
                if (comp == null || comp.Definition == null || comp.Count <= 0)
                    continue;

                int give = (int)Math.Floor(comp.Count * buildFraction + 0.0001);
                if (give <= 0 && buildFraction >= 0.05f && comp.Count > 0)
                    give = 1;
                if (give <= 0)
                    continue;

                MyDefinitionId id = comp.Definition.Id;
                if (!TryAddItemsToHome(homeGrid, id, give))
                    break;
            }

            TryNotifyCreativeRefund(homeGrid);
        }

        private static bool TryAddItemsToHome(IMyCubeGrid homeGrid, MyDefinitionId id, int amount)
        {
            if (homeGrid == null || amount <= 0)
                return false;
            CollectDepositInventories(homeGrid, InvScratch);
            int remaining = amount;
            for (int h = 0; h < InvScratch.Count && remaining > 0; h++)
            {
                var inv = InvScratch[h];
                if (inv == null || !InventoryHasSpace(inv))
                    continue;
                int batch = remaining;
                for (int attempt = 0; attempt < 8 && batch > 0; attempt++)
                {
                    try
                    {
                        if (!inv.CanItemsBeAdded(batch, id))
                        {
                            batch /= 2;
                            if (batch <= 0) break;
                            continue;
                        }
                        var ob = MyObjectBuilderSerializer.CreateNewObject(id) as MyObjectBuilder_PhysicalObject;
                        if (ob == null)
                            return remaining < amount;
                        inv.AddItems(batch, ob);
                        remaining -= batch;
                        break;
                    }
                    catch
                    {
                        batch /= 2;
                    }
                }
            }
            return remaining < amount;
        }

        private static void TryNotifyCreativeRefund(IMyCubeGrid homeGrid)
        {
            // One chat tip per active salvage mission that first deposits a creative refund.
            foreach (var kv in ByCrew)
            {
                var m = kv.Value;
                if (m == null || m.NotifiedCreativeRefund)
                    continue;
                if (homeGrid != null && m.HomeGridEntityId != 0)
                {
                    IMyEntity ent;
                    IMyCubeGrid home = null;
                    if (MyAPIGateway.Entities.TryGetEntityById(m.HomeGridEntityId, out ent))
                        home = ent as IMyCubeGrid;
                    if (home != null && !home.IsSameConstructAs(homeGrid))
                        continue;
                }
                m.NotifiedCreativeRefund = true;
                var session = CrewSession.Instance;
                if (session == null || session.Store == null)
                    return;
                var crew = session.Store.Get(m.CrewId);
                if (crew == null)
                    return;
                session.NotifyCrewOwners(
                    crew,
                    "Salvage: Creative world — Keen drops no grind loot; HireCrew is refunding comps to cargo");
                return;
            }
        }

        private static bool HasCharacterInventory(IMyCharacter character)
        {
            return GetCharacterInventory(character) != null;
        }

        private static bool HasCharacterInventorySpace(IMyCharacter character)
        {
            var inv = GetCharacterInventory(character);
            if (inv == null) return false;
            try { return inv.CurrentVolume < inv.MaxVolume; }
            catch { return false; }
        }

        private static IMyInventory GetCharacterInventory(IMyCharacter character)
        {
            if (character == null || character.Closed)
                return null;
            try
            {
                if (!character.HasInventory)
                    return null;
                return character.GetInventory(0) as IMyInventory;
            }
            catch { return null; }
        }

        private static bool HomeHasFreeVolume(IMyCubeGrid homeGrid)
        {
            CollectDepositInventories(homeGrid, InvScratch);
            for (int i = 0; i < InvScratch.Count; i++)
            {
                var inv = InvScratch[i];
                if (inv == null) continue;
                if (!InventoryHasSpace(inv) || !InventoryAcceptsComponents(inv))
                    continue;
                return true;
            }
            return false;
        }

        private static void TransferInventoryToHome(IMyInventory from, IMyCubeGrid homeGrid)
        {
            if (from == null || homeGrid == null)
                return;
            CollectDepositInventories(homeGrid, InvScratch);
            if (InvScratch.Count == 0)
                return;

            for (int pass = 0; pass < 12; pass++)
            {
                int count = 0;
                try { count = from.ItemCount; }
                catch { return; }
                if (count <= 0)
                    return;

                bool movedAny = false;
                for (int i = count - 1; i >= 0; i--)
                {
                    MyFixedPoint amount = 0;
                    try
                    {
                        var item = from.GetItemAt(i);
                        if (item == null)
                            continue;
                        amount = item.Value.Amount;
                    }
                    catch { continue; }
                    if (amount <= 0)
                        continue;

                    for (int h = 0; h < InvScratch.Count; h++)
                    {
                        var to = InvScratch[h];
                        if (to == null || !InventoryHasSpace(to) || !InventoryAcceptsComponents(to))
                            continue;
                        int beforeCount = count;
                        try
                        {
                            from.TransferItemTo(to, i, null, true, amount);
                        }
                        catch { continue; }

                        try { count = from.ItemCount; }
                        catch { return; }
                        if (count < beforeCount)
                        {
                            movedAny = true;
                            break;
                        }
                    }
                }
                if (!movedAny)
                    return;
            }
        }

        private static void FlushStockpileToBufferThenHome(
            IMySlimBlock slim,
            IMyInventory buffer,
            IMyCubeGrid homeGrid)
        {
            if (slim == null) return;

            if (buffer != null)
            {
                try { slim.MoveItemsFromConstructionStockpile(buffer); }
                catch { }
                try { slim.ClearConstructionStockpile(buffer); }
                catch { }
                TransferInventoryToHome(buffer, homeGrid);
            }

            CollectDepositInventories(homeGrid, InvScratch);
            for (int i = 0; i < InvScratch.Count; i++)
            {
                var inv = InvScratch[i];
                if (inv == null) continue;
                try { slim.MoveItemsFromConstructionStockpile(inv); }
                catch { }
                try { slim.ClearConstructionStockpile(inv); }
                catch { }
            }
        }

        /// <summary>Cargo containers (+ connectors) on the home physical group — not turrets/cockpits.</summary>
        private static void CollectDepositInventories(IMyCubeGrid grid, List<IMyInventory> into)
        {
            into.Clear();
            if (grid == null) return;

            GridGroupScratch.Clear();
            try
            {
                MyAPIGateway.GridGroups.GetGroup(grid, GridLinkTypeEnum.Physical, GridGroupScratch);
            }
            catch
            {
                GridGroupScratch.Clear();
            }
            if (GridGroupScratch.Count == 0)
                GridGroupScratch.Add(grid);

            for (int g = 0; g < GridGroupScratch.Count; g++)
            {
                var other = GridGroupScratch[g];
                if (other == null || other.Closed)
                    continue;
                if (other != grid && !GridsShareCargoAccess(grid, other))
                    continue;
                AppendDepositInventories(other, into);
            }
        }

        private static void AppendDepositInventories(IMyCubeGrid grid, List<IMyInventory> into)
        {
            if (grid == null) return;
            BlockScratch.Clear();
            grid.GetBlocks(BlockScratch);
            for (int i = 0; i < BlockScratch.Count; i++)
            {
                var fat = BlockScratch[i] != null ? BlockScratch[i].FatBlock : null;
                if (fat == null || fat.Closed || !fat.HasInventory)
                    continue;
                if (!(fat is IMyCargoContainer) && !(fat is IMyShipConnector))
                    continue;
                int n = 0;
                try { n = fat.InventoryCount; }
                catch { continue; }
                for (int k = 0; k < n; k++)
                {
                    try
                    {
                        var inv = fat.GetInventory(k) as IMyInventory;
                        if (inv != null)
                            into.Add(inv);
                    }
                    catch { }
                }
            }
        }

        private static readonly MyDefinitionId SteelPlateId =
            new MyDefinitionId(typeof(MyObjectBuilder_Component), "SteelPlate");

        private static bool InventoryAcceptsComponents(IMyInventory inv)
        {
            if (inv == null) return false;
            try { return inv.CanItemsBeAdded(1, SteelPlateId); }
            catch { return true; }
        }

        private static bool GridsShareCargoAccess(IMyCubeGrid a, IMyCubeGrid b)
        {
            if (a == null || b == null) return false;
            try
            {
                if (a.IsSameConstructAs(b))
                    return true;
            }
            catch { }

            long ownerA = PrimaryOwner(a);
            long ownerB = PrimaryOwner(b);
            if (ownerA != 0 && ownerA == ownerB)
                return true;
            if (ownerA == 0 || ownerB == 0)
                return false;

            try
            {
                var f1 = MyAPIGateway.Session.Factions.TryGetPlayerFaction(ownerA);
                var f2 = MyAPIGateway.Session.Factions.TryGetPlayerFaction(ownerB);
                if (f1 != null && f2 != null && f1.FactionId == f2.FactionId)
                    return true;
            }
            catch { }
            return false;
        }

        private static bool IsLegalTargetGrid(CrewRecord crew, IMyCubeGrid target)
        {
            if (crew == null || target == null)
                return false;
            long viewerId = crew.OwnerIdentityId != 0 ? crew.OwnerIdentityId : crew.OwnerKey;
            long viewerFaction = 0;
            if (crew.OwnerIsFaction && crew.OwnerKey != 0)
                viewerFaction = crew.OwnerKey;
            else if (viewerId != 0)
            {
                try
                {
                    var f = MyAPIGateway.Session.Factions.TryGetPlayerFaction(viewerId);
                    if (f != null) viewerFaction = f.FactionId;
                }
                catch { }
            }

            long primary = PrimaryOwner(target);
            long gridFaction = 0;
            if (primary != 0)
            {
                try
                {
                    var f = MyAPIGateway.Session.Factions.TryGetPlayerFaction(primary);
                    if (f != null) gridFaction = f.FactionId;
                }
                catch { }
            }

            var rel = CrewSalvageRules.ClassifyTarget(viewerId, viewerFaction, primary, gridFaction);
            return CrewSalvageRules.IsLegalTarget(rel);
        }

        private static long PrimaryOwner(IMyCubeGrid grid)
        {
            try
            {
                var owners = grid.BigOwners;
                if (owners != null && owners.Count > 0)
                    return owners[0];
            }
            catch { }
            return 0;
        }

        private static bool CanStartAnotherOnGrid(long gridId)
        {
            int max = CrewConfig.SalvageMaxParallelPerGrid;
            if (max <= 0)
                return true;
            int n = 0;
            foreach (var kv in ByCrew)
            {
                if (kv.Value != null && kv.Value.HomeGridEntityId == gridId)
                    n++;
            }
            return n < max;
        }

        private static bool TryPickNearestBlock(
            IMyCubeGrid grid,
            Vector3D from,
            out IMySlimBlock best,
            MissionRuntime m = null)
        {
            best = null;
            if (grid == null) return false;
            BlockScratch.Clear();
            try { grid.GetBlocks(BlockScratch); }
            catch { return false; }

            int bestNeighbors = int.MaxValue;
            double bestDistSq = double.MaxValue;
            for (int i = 0; i < BlockScratch.Count; i++)
            {
                var slim = BlockScratch[i];
                if (slim == null || slim.IsDestroyed)
                    continue;
                if (m != null && m.HasSkipCell && slim.Position == m.SkipCell)
                    continue;
                Vector3D p = GetSlimWorld(slim, grid);
                double distSq = Vector3D.DistanceSquared(from, p);
                int neighbors = CountFaceNeighborBlocks(grid, slim);
                if (!CrewSalvageRules.PreferGrindCandidate(
                        neighbors, distSq, bestNeighbors, bestDistSq))
                    continue;
                bestNeighbors = neighbors;
                bestDistSq = distSq;
                best = slim;
            }
            return best != null;
        }

        /// <summary>
        /// Distinct orthogonal neighbor blocks touching the slim's AABB faces.
        /// Tips/edges score low; structural bridges score high.
        /// </summary>
        private static int CountFaceNeighborBlocks(IMyCubeGrid grid, IMySlimBlock slim)
        {
            if (grid == null || slim == null)
                return 0;

            NeighborPosScratch.Clear();
            Vector3I min = slim.Min;
            Vector3I max = slim.Max;

            // Fast path: 1x1x1 armor / most wreck mass.
            if (min == max)
            {
                for (int d = 0; d < OrthoDirs.Length; d++)
                    TryAddNeighborBlock(grid, slim, min + OrthoDirs[d]);
                return NeighborPosScratch.Count;
            }

            for (int y = min.Y; y <= max.Y; y++)
            for (int z = min.Z; z <= max.Z; z++)
            {
                TryAddNeighborBlock(grid, slim, new Vector3I(max.X + 1, y, z));
                TryAddNeighborBlock(grid, slim, new Vector3I(min.X - 1, y, z));
            }
            for (int x = min.X; x <= max.X; x++)
            for (int z = min.Z; z <= max.Z; z++)
            {
                TryAddNeighborBlock(grid, slim, new Vector3I(x, max.Y + 1, z));
                TryAddNeighborBlock(grid, slim, new Vector3I(x, min.Y - 1, z));
            }
            for (int x = min.X; x <= max.X; x++)
            for (int y = min.Y; y <= max.Y; y++)
            {
                TryAddNeighborBlock(grid, slim, new Vector3I(x, y, max.Z + 1));
                TryAddNeighborBlock(grid, slim, new Vector3I(x, y, min.Z - 1));
            }
            return NeighborPosScratch.Count;
        }

        private static void TryAddNeighborBlock(IMyCubeGrid grid, IMySlimBlock self, Vector3I cell)
        {
            IMySlimBlock other;
            try { other = grid.GetCubeBlock(cell); }
            catch { return; }
            if (other == null || other == self)
                return;
            NeighborPosScratch.Add(other.Position);
        }

        private static Vector3D GetOrCreateApproachHover(
            MissionRuntime m,
            Vector3D fromPos,
            Vector3D blockPos,
            IMyCubeGrid target)
        {
            if (m != null && m.HasApproachHover)
                return new Vector3D(m.HoverX, m.HoverY, m.HoverZ);

            Vector3D hover = ComputeApproachHover(fromPos, blockPos, target);
            if (m != null)
            {
                m.HoverX = hover.X;
                m.HoverY = hover.Y;
                m.HoverZ = hover.Z;
                m.HasApproachHover = true;
            }
            return hover;
        }

        private static Vector3D ComputeApproachHover(Vector3D fromPos, Vector3D blockPos, IMyCubeGrid target)
        {
            Vector3D approach = fromPos - blockPos;
            if (approach.LengthSquared() < 0.25 && target != null)
                approach = blockPos - target.WorldAABB.Center;
            if (approach.LengthSquared() < 0.01)
                approach = Vector3D.Up;
            approach.Normalize();
            return blockPos + approach * CrewConfig.SalvageEvaStandOffMeters;
        }

        /// <summary>
        /// When far and the wreck blocks the straight line to hover, skim the AABB surface first.
        /// </summary>
        private static bool NeedsSalvageStaging(
            IMyCubeGrid grid,
            Vector3D from,
            Vector3D hover,
            out Vector3D stage)
        {
            stage = hover;
            if (grid == null)
                return false;
            if (Vector3D.DistanceSquared(from, hover) < 100.0)
                return false;

            BoundingBoxD shipBox = grid.WorldAABB;
            BoundingBoxD padBox = shipBox.GetInflated(1.0);
            bool inside = padBox.Contains(from) != ContainmentType.Disjoint;

            IHitInfo hit;
            bool rayHit = MyAPIGateway.Physics.CastRay(from, hover, out hit)
                && hit != null
                && hit.HitEntity != null;
            IMyCubeGrid hitGrid = null;
            if (rayHit)
            {
                hitGrid = hit.HitEntity as IMyCubeGrid;
                if (hitGrid == null)
                {
                    var block = hit.HitEntity as IMyCubeBlock;
                    if (block != null)
                        hitGrid = block.CubeGrid;
                }
            }
            bool blockedByShip = hitGrid != null && hitGrid.EntityId == grid.EntityId;
            if (!inside && !blockedByShip)
                return false;

            // Stage near the hover on the outside of the wreck AABB.
            Vector3D surface = ClosestPointOnAabbSurface(shipBox, hover);
            Vector3D outward = surface - shipBox.Center;
            if (outward.LengthSquared() < 0.01)
                outward = hover - from;
            if (outward.LengthSquared() < 0.01)
                outward = Vector3D.Up;
            outward.Normalize();
            stage = surface + outward * 1.25;

            // Keep staging close to the grind approach.
            const double maxFromHover = 8.0;
            Vector3D away = stage - hover;
            double awayLen = away.Length();
            if (awayLen > maxFromHover)
                stage = hover + away * (maxFromHover / awayLen);

            Vector3D toHover = hover - from;
            Vector3D toStage = stage - from;
            if (toHover.LengthSquared() > 0.01 && toStage.LengthSquared() > 0.01)
            {
                toHover.Normalize();
                toStage.Normalize();
                if (Vector3D.Dot(toHover, toStage) < 0.1)
                    return false;
            }

            return Vector3D.DistanceSquared(stage, from) > 4.0;
        }

        private static Vector3D ClosestPointOnAabbSurface(BoundingBoxD box, Vector3D p)
        {
            bool outside = p.X < box.Min.X || p.X > box.Max.X
                || p.Y < box.Min.Y || p.Y > box.Max.Y
                || p.Z < box.Min.Z || p.Z > box.Max.Z;
            if (outside)
            {
                return new Vector3D(
                    Math.Max(box.Min.X, Math.Min(box.Max.X, p.X)),
                    Math.Max(box.Min.Y, Math.Min(box.Max.Y, p.Y)),
                    Math.Max(box.Min.Z, Math.Min(box.Max.Z, p.Z)));
            }

            double dxMin = p.X - box.Min.X;
            double dxMax = box.Max.X - p.X;
            double dyMin = p.Y - box.Min.Y;
            double dyMax = box.Max.Y - p.Y;
            double dzMin = p.Z - box.Min.Z;
            double dzMax = box.Max.Z - p.Z;

            double best = dxMin;
            Vector3D result = new Vector3D(box.Min.X, p.Y, p.Z);
            if (dxMax < best) { best = dxMax; result = new Vector3D(box.Max.X, p.Y, p.Z); }
            if (dyMin < best) { best = dyMin; result = new Vector3D(p.X, box.Min.Y, p.Z); }
            if (dyMax < best) { best = dyMax; result = new Vector3D(p.X, box.Max.Y, p.Z); }
            if (dzMin < best) { best = dzMin; result = new Vector3D(p.X, p.Y, box.Min.Z); }
            if (dzMax < best) { result = new Vector3D(p.X, p.Y, box.Max.Z); }
            return result;
        }

        private static void SetTarget(MissionRuntime m, IMySlimBlock slim)
        {
            if (m == null || slim == null) return;
            m.TargetCell = slim.Position;
            m.HasTargetCell = true;
            m.HasApproachHover = false;
            m.NoGrindProgressSeconds = 0;
            try
            {
                if (slim.CubeGrid != null)
                    m.TargetGridEntityId = slim.CubeGrid.EntityId;
            }
            catch { }
        }

        private static IMyCubeGrid ResolveMissionTargetGrid(MissionRuntime m)
        {
            if (m == null || m.TargetGridEntityId == 0)
                return null;
            IMyEntity ent;
            if (!MyAPIGateway.Entities.TryGetEntityById(m.TargetGridEntityId, out ent) || ent == null)
                return null;
            var g = ent as IMyCubeGrid;
            if (g == null || g.Closed)
                return null;
            return g;
        }

        private static bool TryPickNextBlock(
            MissionRuntime m,
            IMyCubeGrid home,
            CrewRecord crew,
            Vector3D from,
            out IMySlimBlock best,
            out IMyCubeGrid bestGrid)
        {
            if (m != null && m.HasZone)
                return TryPickBlockInZone(m, home, crew, from, out best, out bestGrid);

            bestGrid = ResolveMissionTargetGrid(m);
            return TryPickNearestBlock(bestGrid, from, out best, m) && best != null;
        }

        /// <summary>
        /// Leaf-first pick among legal blocks whose world positions lie in the frozen zone.
        /// </summary>
        private static bool TryPickBlockInZone(
            MissionRuntime m,
            IMyCubeGrid home,
            CrewRecord crew,
            Vector3D from,
            out IMySlimBlock best,
            out IMyCubeGrid bestGrid)
        {
            best = null;
            bestGrid = null;
            if (m == null || !m.HasZone)
                return false;

            var zone = new BoundingBoxD(
                new Vector3D(m.ZoneMinX, m.ZoneMinY, m.ZoneMinZ),
                new Vector3D(m.ZoneMaxX, m.ZoneMaxY, m.ZoneMaxZ));

            EntityScratch.Clear();
            BoundingBoxD zoneCapture = zone;
            try
            {
                MyAPIGateway.Entities.GetEntities(EntityScratch, e =>
                {
                    var g = e as IMyCubeGrid;
                    if (g == null || g.Closed)
                        return false;
                    return zoneCapture.Intersects(g.WorldAABB);
                });
            }
            catch { return false; }

            int bestNeighbors = int.MaxValue;
            double bestDistSq = double.MaxValue;

            foreach (var ent in EntityScratch)
            {
                var grid = ent as IMyCubeGrid;
                if (grid == null || grid.Closed)
                    continue;
                if (home != null)
                {
                    try
                    {
                        if (grid.EntityId == home.EntityId || grid.IsSameConstructAs(home))
                            continue;
                    }
                    catch { continue; }
                }
                if (crew != null && !IsLegalTargetGrid(crew, grid))
                    continue;

                BlockScratch.Clear();
                try { grid.GetBlocks(BlockScratch); }
                catch { continue; }

                for (int b = 0; b < BlockScratch.Count; b++)
                {
                    var slim = BlockScratch[b];
                    if (slim == null || slim.IsDestroyed)
                        continue;
                    if (m.HasSkipCell && slim.Position == m.SkipCell && grid.EntityId == m.TargetGridEntityId)
                        continue;

                    Vector3D p = GetSlimWorld(slim, grid);
                    if (!CrewSalvageRules.IsInsideZone(
                            p.X, p.Y, p.Z,
                            zone.Min.X, zone.Min.Y, zone.Min.Z,
                            zone.Max.X, zone.Max.Y, zone.Max.Z))
                        continue;

                    double distSq = Vector3D.DistanceSquared(from, p);
                    int neighbors = CountFaceNeighborBlocks(grid, slim);
                    if (!CrewSalvageRules.PreferGrindCandidate(
                            neighbors, distSq, bestNeighbors, bestDistSq))
                        continue;
                    bestNeighbors = neighbors;
                    bestDistSq = distSq;
                    best = slim;
                    bestGrid = grid;
                }
            }

            return best != null;
        }

        private static void ClearTarget(MissionRuntime m)
        {
            if (m == null) return;
            m.HasTargetCell = false;
            m.HasApproachHover = false;
            m.NoGrindProgressSeconds = 0;
        }

        private static bool TryResolveTarget(MissionRuntime m, IMyCubeGrid grid, out IMySlimBlock slim)
        {
            slim = null;
            if (m == null || grid == null || !m.HasTargetCell)
                return false;
            try
            {
                slim = grid.GetCubeBlock(m.TargetCell);
            }
            catch { slim = null; }
            return slim != null && !slim.IsDestroyed;
        }

        private static bool BlockStillPresent(IMyCubeGrid grid, Vector3I cell)
        {
            if (grid == null) return false;
            try
            {
                var slim = grid.GetCubeBlock(cell);
                return slim != null && !slim.IsDestroyed;
            }
            catch { return false; }
        }

        private static Vector3D GetSlimWorld(IMySlimBlock slim, IMyCubeGrid grid)
        {
            if (slim == null) return Vector3D.Zero;
            if (slim.FatBlock != null)
                return slim.FatBlock.GetPosition();
            IMyCubeGrid g = slim.CubeGrid != null ? slim.CubeGrid : grid;
            if (g == null) return Vector3D.Zero;
            return g.GridIntegerToWorld(slim.Position);
        }

        /// <summary>Zone finished (no grindable blocks left) — clear mark/highlight, then go home.</summary>
        private static void BeginReturnZoneDone(MissionRuntime m)
        {
            if (m != null)
            {
                var session = CrewSession.Instance;
                if (session != null)
                    session.ClearSalvageMarkForHome(m.HomeGridEntityId);
            }
            BeginReturn(m);
        }

        private static void BeginReturn(MissionRuntime m)
        {
            if (m == null || string.IsNullOrEmpty(m.CrewId))
                return;

            ClearTarget(m);
            ClearFlyDynamics(m);

            IMyCubeGrid home = null;
            IMyEntity homeEnt;
            if (m.HomeGridEntityId != 0
                && MyAPIGateway.Entities.TryGetEntityById(m.HomeGridEntityId, out homeEnt))
                home = homeEnt as IMyCubeGrid;

            IMyTerminalBlock seat = null;
            IMyCharacter character = null;
            var session = CrewSession.Instance;
            if (session != null && session.Store != null)
            {
                var crew = session.Store.Get(m.CrewId);
                if (crew != null)
                {
                    TryGetCharacter(crew, out character);
                    if (crew.SeatEntityId.HasValue)
                    {
                        IMyEntity seatEnt;
                        if (MyAPIGateway.Entities.TryGetEntityById(crew.SeatEntityId.Value, out seatEnt))
                            seat = seatEnt as IMyTerminalBlock;
                    }
                }
            }

            // Dump any leftover grind comps before despawning the EVA body.
            if (character != null && !character.Closed && home != null)
            {
                var buffer = GetCharacterInventory(character);
                if (buffer != null)
                    TransferInventoryToHome(buffer, home);
            }

            if (character != null && !character.Closed && seat != null && !seat.Closed)
                TeleportHome(character, seat, home ?? seat.CubeGrid);

            Log("salvage home teleport crew=" + m.CrewId);
            FinishMission(m);
        }

        private static void FinishMission(MissionRuntime m)
        {
            if (m == null || string.IsNullOrEmpty(m.CrewId)) return;
            Log("salvage idle crew=" + m.CrewId);
            ByCrew.Remove(m.CrewId);
        }

        private static void TeleportHome(IMyCharacter character, IMyTerminalBlock seat, IMyCubeGrid grid)
        {
            if (character == null || character.Closed || seat == null)
                return;
            try
            {
                MatrixD wm = seat.WorldMatrix;
                Vector3D up = wm.Up;
                if (up.LengthSquared() < 0.01)
                    up = Vector3D.Up;
                up.Normalize();
                Vector3D pos = wm.Translation + wm.Right * 1.2 + up * 0.1;
                Vector3D forward = wm.Forward;
                if (forward.LengthSquared() < 0.01)
                    forward = Vector3D.Forward;
                forward.Normalize();
                character.WorldMatrix = MatrixD.CreateWorld(pos, forward, up);
                character.SetPosition(pos);
                CrewAmbientPresence.SetCharacterJetpack(character, false);
                CrewAmbientPresence.StopCharacterMovement(character, grid);
                BindEvaPhysics(character, grid);
            }
            catch { }
        }

        private static void NotifyCargoFull(CrewSession session, MissionRuntime m, CrewRecord crew)
        {
            if (m == null || m.NotifiedCargoFull || session == null || crew == null)
                return;
            m.NotifiedCargoFull = true;
            m.Hints |= SalvageMissionHintFlags.CargoFull;
            session.NotifyCrewOwners(crew, (crew.DisplayName ?? "Salvage Ops") + ": cargo full — returning");
        }

        private static void HoldGrindPose(
            MissionRuntime m,
            IMyCharacter character,
            IMyCubeGrid grid,
            Vector3D lookAt,
            float dt)
        {
            ClearFlyDynamics(m);
            StabilizeEvaPose(m, character, grid);
            Vector3D pos = character.GetPosition();
            Vector3D toBlock = lookAt - pos;
            if (toBlock.LengthSquared() > 0.01)
            {
                Vector3D up = EvaUp(grid, character);
                Vector3D fwd = BlendFacing(m, character, grid, toBlock, dt);
                try { character.WorldMatrix = MatrixD.CreateWorld(pos, fwd, up); }
                catch { }
            }
            BindEvaPhysics(character, grid);
        }

        private static void FlyToward(
            MissionRuntime m,
            IMyCharacter character,
            IMyCubeGrid grid,
            Vector3D target,
            float speed,
            float dt)
        {
            if (character == null || character.Closed) return;
            CrewAmbientPresence.ReleaseFromSeat(character, grid);
            StabilizeEvaPose(m, character, grid);

            Vector3D pos = character.GetPosition();
            Vector3D toTarget = target - pos;
            double dist = toTarget.Length();
            if (dist < 0.08)
            {
                ClearFlyDynamics(m);
                BindEvaPhysics(character, grid);
                return;
            }
            Vector3D dir = toTarget / dist;

            double arrive = Math.Max(2.0, CrewConfig.SalvageEvaArriveMeters * 2.0);
            double ease = dist < arrive ? Math.Max(0.2, dist / arrive) : 1.0;
            Vector3D desiredVel = dir * (speed * ease);

            Vector3D vel = (m != null && m.HasFlyVel)
                ? new Vector3D(m.VelX, m.VelY, m.VelZ)
                : desiredVel * 0.35;

            Vector3D delta = desiredVel - vel;
            double maxDelta = CrewConfig.SalvageEvaAccelMeters * dt;
            double dLen = delta.Length();
            if (dLen > maxDelta && dLen > 0.0001)
                delta *= maxDelta / dLen;
            vel += delta;

            double stepLen = vel.Length() * dt;
            Vector3D next;
            if (stepLen >= dist)
            {
                next = target;
                vel = Vector3D.Zero;
            }
            else
                next = pos + vel * dt;

            if (m != null)
            {
                m.VelX = vel.X;
                m.VelY = vel.Y;
                m.VelZ = vel.Z;
                m.HasFlyVel = vel.LengthSquared() > 0.0001;
            }

            Vector3D faceDir = vel.LengthSquared() > 0.15 ? vel : dir;
            Vector3D up = EvaUp(grid, character);
            Vector3D fwd = BlendFacing(m, character, grid, faceDir, dt);
            try { character.WorldMatrix = MatrixD.CreateWorld(next, fwd, up); }
            catch
            {
                try { character.SetPosition(next); }
                catch { return; }
            }
            BindEvaPhysics(character, grid);
        }

        private static void StabilizeEvaPose(MissionRuntime m, IMyCharacter character, IMyCubeGrid grid)
        {
            if (character == null || character.Closed)
                return;
            CrewAmbientPresence.SetCharacterJetpack(character, true);
            CrewAmbientPresence.ApplyCrewInvulnerability(character);
            BindEvaPhysics(character, grid);
            if (m != null)
            {
                if (m.PoseCooldown > 0)
                    return;
                m.PoseCooldown = 0.45;
            }
            try
            {
                var ctrl = character as IMyControllableEntity;
                if (ctrl != null)
                    ctrl.MoveAndRotateStopped();
            }
            catch { }
            try
            {
                character.CurrentMovementState = MyCharacterMovementEnum.Flying;
            }
            catch { }
        }

        private static void BindEvaPhysics(IMyCharacter character, IMyCubeGrid grid)
        {
            if (character == null || character.Closed || character.Physics == null)
                return;
            try
            {
                character.Physics.Gravity = Vector3.Zero;
                character.Physics.AngularVelocity = Vector3.Zero;
                Vector3 gridVel = Vector3.Zero;
                if (grid != null && grid.Physics != null)
                    gridVel = grid.Physics.LinearVelocity;
                character.Physics.LinearVelocity = gridVel;
            }
            catch { }
        }

        private static Vector3D EvaUp(IMyCubeGrid grid, IMyCharacter character)
        {
            Vector3D up = grid != null ? grid.WorldMatrix.Up : character.WorldMatrix.Up;
            if (up.LengthSquared() < 0.01)
                up = Vector3D.Up;
            up.Normalize();
            return up;
        }

        private static Vector3D FlattenDir(Vector3D dir, Vector3D up)
        {
            Vector3D flat = dir - up * Vector3D.Dot(dir, up);
            if (flat.LengthSquared() < 0.0001)
                return Vector3D.CalculatePerpendicularVector(up);
            flat.Normalize();
            return flat;
        }

        private static Vector3D BlendFacing(
            MissionRuntime m,
            IMyCharacter character,
            IMyCubeGrid grid,
            Vector3D desiredDir,
            float dt)
        {
            Vector3D up = EvaUp(grid, character);
            Vector3D want = FlattenDir(desiredDir, up);
            Vector3D cur;
            if (m != null && m.HasFlyFwd)
                cur = FlattenDir(new Vector3D(m.FwdX, m.FwdY, m.FwdZ), up);
            else
                cur = FlattenDir(character.WorldMatrix.Forward, up);

            double t = Math.Min(1.0, CrewConfig.SalvageEvaTurnRate * dt);
            Vector3D blended = cur + (want - cur) * t;
            if (blended.LengthSquared() < 0.0001)
                blended = want;
            else
                blended.Normalize();

            if (m != null)
            {
                m.FwdX = blended.X;
                m.FwdY = blended.Y;
                m.FwdZ = blended.Z;
                m.HasFlyFwd = true;
            }
            return blended;
        }

        private static void ClearFlyDynamics(MissionRuntime m)
        {
            if (m == null) return;
            m.HasFlyVel = false;
            m.VelX = m.VelY = m.VelZ = 0;
        }

        private static bool TryGetCharacter(CrewRecord crew, out IMyCharacter character)
        {
            character = null;
            if (crew == null || !crew.CharacterEntityId.HasValue)
                return false;
            IMyEntity ent;
            if (!MyAPIGateway.Entities.TryGetEntityById(crew.CharacterEntityId.Value, out ent))
                return false;
            character = ent as IMyCharacter;
            return character != null && !character.Closed;
        }

        private static void CopyCrewKeys(List<string> into)
        {
            into.Clear();
            foreach (var kv in ByCrew)
                into.Add(kv.Key);
        }

        private static void Log(string msg)
        {
            try { MyLog.Default.WriteLineAndConsole("[HireCrew] " + msg); }
            catch { }
        }
    }
}
