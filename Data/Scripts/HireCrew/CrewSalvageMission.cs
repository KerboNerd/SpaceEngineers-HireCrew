using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.Game.ModAPI.Interfaces;
using VRage.ModAPI;
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
            public long TargetGridEntityId;
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
        }

        private static readonly Dictionary<string, MissionRuntime> ByCrew =
            new Dictionary<string, MissionRuntime>();
        private static readonly List<string> KeyScratch = new List<string>(16);
        private static readonly List<string> RemoveScratch = new List<string>(8);
        private static readonly List<IMySlimBlock> BlockScratch = new List<IMySlimBlock>(256);
        private static readonly List<IMyInventory> InvScratch = new List<IMyInventory>(64);
        private static readonly List<IMyCubeGrid> GridGroupScratch = new List<IMyCubeGrid>(8);

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
                Vector3D outward = targetPos - targetGrid.WorldAABB.Center;
                if (outward.LengthSquared() < 0.01)
                    outward = seat.WorldMatrix.Forward;
                outward.Normalize();
                pos = targetPos + outward * CrewConfig.SalvageEvaStandOffMeters;
                forward = -outward;
                return true;
            }

            pos = seat.WorldMatrix.Translation + seat.WorldMatrix.Right * 1.2 + up * 0.1;
            forward = seat.WorldMatrix.Forward;
            return true;
        }

        public static bool DispatchCrew(CrewSession session, string crewId, long targetGridEntityId)
        {
            if (session == null || session.Store == null || string.IsNullOrEmpty(crewId))
                return false;
            if (IsCrewOnMission(crewId))
                return false;
            if (targetGridEntityId == 0)
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

            IMyEntity targetEnt;
            if (!MyAPIGateway.Entities.TryGetEntityById(targetGridEntityId, out targetEnt))
                return false;
            var target = targetEnt as IMyCubeGrid;
            if (target == null || target.Closed)
                return false;

            double radius = CrewConfig.SalvageScanRadiusMeters;
            if (Vector3D.DistanceSquared(home.WorldAABB.Center, target.WorldAABB.Center) > radius * radius)
                return false;

            if (!IsLegalTargetGrid(crew, target))
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
                TargetGridEntityId = targetGridEntityId,
                State = SalvageMissionState.EvaTransit,
                StateSeconds = 0
            };

            IMySlimBlock first;
            if (!TryPickNearestBlock(target, from, out first) || first == null)
                return false;
            SetTarget(m, first);

            ByCrew[crew.CrewId] = m;
            Log("salvage dispatch crew=" + crew.CrewId
                + " home=" + m.HomeGridEntityId
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

                IMyEntity targetEnt;
                IMyCubeGrid target = null;
                if (MyAPIGateway.Entities.TryGetEntityById(m.TargetGridEntityId, out targetEnt))
                    target = targetEnt as IMyCubeGrid;
                if (target == null || target.Closed)
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
                    IMyEntity targetEnt;
                    IMyCubeGrid target = null;
                    if (MyAPIGateway.Entities.TryGetEntityById(m.TargetGridEntityId, out targetEnt))
                        target = targetEnt as IMyCubeGrid;
                    if (home == null || target == null)
                    {
                        BeginReturn(m);
                        continue;
                    }

                    IMySlimBlock slim;
                    if (!TryResolveTarget(m, target, out slim) || slim == null || slim.IsDestroyed)
                    {
                        Vector3D from = target.WorldAABB.Center;
                        if (!TryPickNearestBlock(target, from, out slim) || slim == null)
                        {
                            BeginReturn(m);
                            continue;
                        }
                        SetTarget(m, slim);
                    }

                    float amount = CrewConfig.GetSalvageGrindMountPerSecond(crew.Stars) * (float)dt;
                    GrindResult result = TryGrindTick(slim, home, amount);
                    if (result == GrindResult.CargoFull)
                    {
                        NotifyCargoFull(session, m, crew);
                        BeginReturn(m);
                        continue;
                    }
                    if (result == GrindResult.Failed)
                    {
                        BeginReturn(m);
                        continue;
                    }
                    if (slim.IsDestroyed || !BlockStillPresent(target, m.TargetCell))
                    {
                        ClearTarget(m);
                        if (!TryPickNearestBlock(target, target.WorldAABB.Center, out slim) || slim == null)
                            BeginReturn(m);
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
                if (!TryPickNearestBlock(target, fromPos, out slim) || slim == null)
                {
                    BeginReturn(m);
                    return;
                }
                SetTarget(m, slim);
            }

            Vector3D blockPos = GetSlimWorld(slim, target);
            Vector3D outward = blockPos - target.WorldAABB.Center;
            if (outward.LengthSquared() < 0.01)
                outward = home.WorldMatrix.Forward;
            outward.Normalize();
            Vector3D hover = blockPos + outward * CrewConfig.SalvageEvaStandOffMeters;

            if (character != null && !character.Closed)
            {
                FlyToward(m, character, home, hover, CrewConfig.GetSalvageEvaSpeedMeters(crew.Stars), dt);
                double grindR = CrewConfig.SalvageGrindRangeMeters;
                bool inRange = Vector3D.DistanceSquared(character.GetPosition(), blockPos) <= grindR * grindR;
                bool atHover = Vector3D.DistanceSquared(character.GetPosition(), hover)
                    <= CrewConfig.SalvageEvaArriveMeters * CrewConfig.SalvageEvaArriveMeters;
                if (inRange || atHover)
                {
                    m.State = SalvageMissionState.Grinding;
                    m.StateSeconds = 0;
                }
            }
            else if (m.StateSeconds > 2.0)
            {
                m.State = SalvageMissionState.Grinding;
                m.StateSeconds = 0;
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
                if (!TryPickNearestBlock(target, fromPos, out slim) || slim == null)
                {
                    BeginReturn(m);
                    return;
                }
                SetTarget(m, slim);
            }

            Vector3D blockPos = GetSlimWorld(slim, target);
            if (character != null && !character.Closed)
            {
                double grindR = CrewConfig.SalvageGrindRangeMeters;
                if (Vector3D.DistanceSquared(character.GetPosition(), blockPos) > grindR * grindR)
                {
                    m.State = SalvageMissionState.EvaTransit;
                    m.StateSeconds = 0;
                    return;
                }
                HoldGrindPose(m, character, home, blockPos, dt);
            }

            float amount = CrewConfig.GetSalvageGrindMountPerSecond(crew.Stars) * dt;
            GrindResult result = TryGrindTick(slim, home, amount);
            if (result == GrindResult.CargoFull)
            {
                NotifyCargoFull(session, m, crew);
                BeginReturn(m);
                return;
            }
            if (result == GrindResult.Failed)
            {
                BeginReturn(m);
                return;
            }

            if (slim.IsDestroyed || !BlockStillPresent(target, m.TargetCell))
            {
                ClearTarget(m);
                if (!TryPickNearestBlock(target, fromPos, out slim) || slim == null)
                    BeginReturn(m);
                else
                    SetTarget(m, slim);
            }
        }

        private enum GrindResult
        {
            Ok = 0,
            CargoFull = 1,
            Failed = 2
        }

        private static GrindResult TryGrindTick(IMySlimBlock slim, IMyCubeGrid homeGrid, float grindSeconds)
        {
            if (slim == null || homeGrid == null || grindSeconds <= 0f)
                return GrindResult.Failed;

            IMyInventory inv = FindDepositInventory(homeGrid);
            if (inv == null)
                return GrindResult.CargoFull;

            try
            {
                slim.DecreaseMountLevel(grindSeconds, inv);
            }
            catch
            {
                return GrindResult.Failed;
            }
            return GrindResult.Ok;
        }

        private static IMyInventory FindDepositInventory(IMyCubeGrid homeGrid)
        {
            CollectHomeInventories(homeGrid, InvScratch);
            for (int i = 0; i < InvScratch.Count; i++)
            {
                var inv = InvScratch[i];
                if (inv == null) continue;
                try
                {
                    if (inv.CurrentVolume < inv.MaxVolume)
                        return inv;
                }
                catch { }
            }
            return null;
        }

        private static void CollectHomeInventories(IMyCubeGrid grid, List<IMyInventory> into)
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
                AppendGridInventories(other, into);
            }
        }

        private static void AppendGridInventories(IMyCubeGrid grid, List<IMyInventory> into)
        {
            if (grid == null) return;
            BlockScratch.Clear();
            grid.GetBlocks(BlockScratch);
            for (int i = 0; i < BlockScratch.Count; i++)
            {
                var fat = BlockScratch[i] != null ? BlockScratch[i].FatBlock : null;
                if (fat == null || fat.Closed || !fat.HasInventory)
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

        private static bool TryPickNearestBlock(IMyCubeGrid grid, Vector3D from, out IMySlimBlock best)
        {
            best = null;
            if (grid == null) return false;
            BlockScratch.Clear();
            try { grid.GetBlocks(BlockScratch); }
            catch { return false; }

            double bestDist = double.MaxValue;
            for (int i = 0; i < BlockScratch.Count; i++)
            {
                var slim = BlockScratch[i];
                if (slim == null || slim.IsDestroyed)
                    continue;
                Vector3D p = GetSlimWorld(slim, grid);
                double d = Vector3D.DistanceSquared(from, p);
                if (d < bestDist)
                {
                    bestDist = d;
                    best = slim;
                }
            }
            return best != null;
        }

        private static void SetTarget(MissionRuntime m, IMySlimBlock slim)
        {
            if (m == null || slim == null) return;
            m.TargetCell = slim.Position;
            m.HasTargetCell = true;
        }

        private static void ClearTarget(MissionRuntime m)
        {
            if (m == null) return;
            m.HasTargetCell = false;
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
