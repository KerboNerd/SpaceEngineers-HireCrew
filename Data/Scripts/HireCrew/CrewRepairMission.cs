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
    /// Per-grid Damage Control EVA repair missions (waypoint walk → exit → weld → return).
    /// </summary>
    public static class CrewRepairMission
    {
        private sealed class MissionRuntime
        {
            public string CrewId;
            public long GridEntityId;
            public RepairMissionState State;
            public int WaypointIndex;
            public long TargetBlockEntityId;
            public Vector3I TargetCell;
            public bool HasTargetCell;
            /// <summary>True when welding a projector hologram (ProjectedGrid), not a real damaged block.</summary>
            public bool TargetIsProjected;
            public long ProjectorEntityId;
            public double StateSeconds;
            public double NoCompSeconds;
            /// <summary>Seconds with no pickable work — grace before returning (projector CanBuild lags).</summary>
            public double NoWorkSeconds;
            /// <summary>Countdown before the next expensive work-target scan.</summary>
            public double AcquireCooldown;
            public double LogicalProgressMeters;
            public DateTime RescanAfterUtc;
            public bool NotifiedOutOfComps;
            /// <summary>True when a finished station→airlock path exists; false = local/station repair.</summary>
            public bool UsesPath;
            public bool HasHover;
            public double HoverX;
            public double HoverY;
            public double HoverZ;
            public bool HasStaging;
            public double StageX;
            public double StageY;
            public double StageZ;
            public double StuckSeconds;
            public double StuckSampleSeconds;
            public double LastPosX;
            public double LastPosY;
            public double LastPosZ;
            public bool HasLastPos;
            public int UnstuckCount;
            /// <summary>Seconds until next WorldMatrix face snap while holding weld pose.</summary>
            public double FaceCooldown;
            /// <summary>Seconds until next MoveAndRotateStopped / Standing force.</summary>
            public double PoseCooldown;
            public double NudgeCooldown;
            public bool HasFlyVel;
            public double VelX;
            public double VelY;
            public double VelZ;
            public bool HasFlyFwd;
            public double FwdX;
            public double FwdY;
            public double FwdZ;
            /// <summary>Cells skipped this sortie after a stuck weld (ghost / unweldable).</summary>
            public readonly HashSet<string> SkippedTargets = new HashSet<string>();
        }

        /// <summary>Active missions keyed by crew id (parallel EVAs on one grid allowed).</summary>
        private static readonly Dictionary<string, MissionRuntime> ByCrew = new Dictionary<string, MissionRuntime>();
        /// <summary>Per-welder cooldown after a finished sortie.</summary>
        private static readonly Dictionary<string, DateTime> CrewCooldownUntil = new Dictionary<string, DateTime>();
        private static readonly List<IMySlimBlock> BlockScratch = new List<IMySlimBlock>(256);
        private static readonly List<IMySlimBlock> ProjectedScratch = new List<IMySlimBlock>(256);
        private static readonly List<string> RemoveScratch = new List<string>(8);
        private static readonly List<string> KeyScratch = new List<string>(16);
        private static readonly Dictionary<string, MyParticleEffect> WeldFxByCrew =
            new Dictionary<string, MyParticleEffect>();
        private static readonly Dictionary<string, int> MissingCompScratch =
            new Dictionary<string, int>();
        private static readonly List<IMyInventory> InvScratch = new List<IMyInventory>(64);
        private static readonly List<IMyCubeGrid> GridGroupScratch = new List<IMyCubeGrid>(8);
        private struct CachedWork
        {
            public Vector3I Cell;
            public long ProjectorEntityId;
            public bool Projected;
            public Vector3D World;
        }
        private static readonly List<CachedWork> WorkCache = new List<CachedWork>(256);
        private static long WorkCacheGridId;
        private static int WorkCacheFrame = -9999;
        private const string WeldParticleSubtype = "WelderContactPoint";

        public static bool IsCrewOnMission(string crewId)
        {
            return !string.IsNullOrEmpty(crewId) && ByCrew.ContainsKey(crewId);
        }

        public static bool IsAnyMissionOnGrid(long gridEntityId)
        {
            if (gridEntityId == 0) return false;
            foreach (var kv in ByCrew)
            {
                if (kv.Value != null && kv.Value.GridEntityId == gridEntityId)
                    return true;
            }
            return false;
        }

        public static void CancelForCrew(string crewId)
        {
            if (string.IsNullOrEmpty(crewId)) return;
            ClearMissionForCrew(crewId, cooldown: false);
        }

        public static void ClearAll()
        {
            KeyScratch.Clear();
            foreach (var kv in WeldFxByCrew)
                KeyScratch.Add(kv.Key);
            for (int i = 0; i < KeyScratch.Count; i++)
                StopWeldParticles(KeyScratch[i]);
            WeldFxByCrew.Clear();
            ByCrew.Clear();
            CrewCooldownUntil.Clear();
        }

        /// <summary>
        /// Fills <paramref name="into"/> with non-Idle mission snapshots for HUD sync.
        /// </summary>
        public static void CollectActiveSnapshots(List<RepairMissionSnapshotEntry> into)
        {
            if (into == null) return;
            into.Clear();
            foreach (var kv in ByCrew)
            {
                MissionRuntime m = kv.Value;
                if (m == null || m.State == RepairMissionState.Idle) continue;
                if (string.IsNullOrEmpty(m.CrewId)) continue;

                int hints = RepairMissionHintFlags.None;
                if (m.NotifiedOutOfComps) hints |= RepairMissionHintFlags.OutOfComps;
                if (m.TargetIsProjected) hints |= RepairMissionHintFlags.ProjectedTarget;

                string name = m.CrewId;
                var session = CrewSession.Instance;
                if (session != null && session.Store != null)
                {
                    CrewRecord crew = session.Store.Get(m.CrewId);
                    if (crew != null && !string.IsNullOrEmpty(crew.DisplayName))
                        name = crew.DisplayName;
                }

                into.Add(new RepairMissionSnapshotEntry
                {
                    CrewId = m.CrewId,
                    DisplayName = name,
                    GridEntityId = m.GridEntityId,
                    State = (int)m.State,
                    Hints = hints
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
            if (string.IsNullOrEmpty(crewId) || seat == null || seat.CubeGrid == null)
                return false;

            MissionRuntime m;
            if (!ByCrew.TryGetValue(crewId, out m) || m == null)
                return false;

            var grid = seat.CubeGrid;
            long gridId = m.GridEntityId;
            up = seat.WorldMatrix.Up;

            // If somehow still marked returning, prefer station pose for ambient snap.
            if (m.State == RepairMissionState.ReturnExit || m.State == RepairMissionState.WalkHome)
            {
                pos = seat.WorldMatrix.Translation + seat.WorldMatrix.Right * 1.2 + up * 0.1;
                forward = seat.WorldMatrix.Forward;
                return true;
            }

            var paths = CrewSession.Instance != null ? CrewSession.Instance.RepairPaths : null;
            var path = paths != null ? paths.Get(gridId) : null;

            if (m.UsesPath && path != null && path.Waypoints != null && path.Waypoints.Count > 0)
            {
                int idx = m.WaypointIndex;
                if (idx < 0) idx = 0;
                if (idx >= path.Waypoints.Count) idx = path.Waypoints.Count - 1;

                Vector3D wp;
                if (!RepairPathStore.TryResolveWorldPos(grid, path.Waypoints[idx], out wp))
                    return false;

                if (m.State == RepairMissionState.EvaTransit
                    || m.State == RepairMissionState.Welding)
                {
                    Vector3D outward = wp - grid.WorldAABB.Center;
                    if (outward.LengthSquared() < 0.01)
                        outward = seat.WorldMatrix.Forward;
                    outward.Normalize();
                    pos = wp + outward * CrewConfig.RepairEvaStandOffMeters;
                    forward = -outward;
                }
                else
                {
                    pos = wp + up * 0.1;
                    Vector3D toSeat = seat.WorldMatrix.Translation - pos;
                    forward = toSeat - up * Vector3D.Dot(toSeat, up);
                    if (forward.LengthSquared() < 0.01)
                        forward = seat.WorldMatrix.Forward;
                    else
                        forward.Normalize();
                }
                return true;
            }

            // Local / station repair — pose near damage or seat.
            IMySlimBlock slim;
            if (TryResolveTarget(m, grid, out slim) && slim != null)
            {
                Vector3D targetPos = GetSlimWorld(slim, grid);
                Vector3D outward = targetPos - grid.WorldAABB.Center;
                if (outward.LengthSquared() < 0.01)
                    outward = seat.WorldMatrix.Forward;
                outward.Normalize();
                pos = targetPos + outward * CrewConfig.RepairEvaStandOffMeters;
                forward = -outward;
                return true;
            }

            pos = seat.WorldMatrix.Translation + seat.WorldMatrix.Right * 1.2 + up * 0.1;
            forward = seat.WorldMatrix.Forward;
            return true;
        }

        public static void Tick(CrewSession session)
        {
            if (session == null || session.Store == null || session.RepairPaths == null)
                return;
            if (MyAPIGateway.Multiplayer == null || !MyAPIGateway.Multiplayer.IsServer)
                return;

            // Lifecycle ~1 Hz; damage scans run from UpdateMovement (faster cadence).
            AdvanceLogical(session, 1.0);
            CleanupMissing(session);
        }

        public static void UpdateMovement(CrewSession session)
        {
            if (session == null || session.Store == null || session.RepairPaths == null)
                return;
            if (MyAPIGateway.Multiplayer == null || !MyAPIGateway.Multiplayer.IsServer)
                return;

            const float dt = 1f / 60f;
            RemoveScratch.Clear();
            CopyCrewKeys(KeyScratch);
            for (int ki = 0; ki < KeyScratch.Count; ki++)
            {
                string crewId = KeyScratch[ki];
                MissionRuntime m;
                if (!ByCrew.TryGetValue(crewId, out m) || m == null || string.IsNullOrEmpty(m.CrewId))
                {
                    RemoveScratch.Add(crewId);
                    continue;
                }

                IMyEntity gridEnt;
                if (!MyAPIGateway.Entities.TryGetEntityById(m.GridEntityId, out gridEnt) || gridEnt == null)
                {
                    RemoveScratch.Add(crewId);
                    continue;
                }
                var grid = gridEnt as IMyCubeGrid;
                if (grid == null)
                {
                    RemoveScratch.Add(crewId);
                    continue;
                }

                var crew = session.Store.Get(m.CrewId);
                if (crew == null || crew.Status != CrewStatus.Seated || crew.Role != CrewRole.DamageControl)
                {
                    RemoveScratch.Add(crewId);
                    continue;
                }

                if (!CrewAmbientPresence.IsGridIdle(grid)
                    && (m.State == RepairMissionState.EvaTransit
                        || m.State == RepairMissionState.Welding
                        || m.State == RepairMissionState.AtExit))
                {
                    Log("repair abort moving crew=" + m.CrewId);
                    BeginReturn(m);
                }

                IMyCharacter character;
                TryGetCharacter(crew, out character);
                IMyTerminalBlock seat = null;
                if (crew.SeatEntityId.HasValue)
                {
                    IMyEntity seatEnt;
                    if (MyAPIGateway.Entities.TryGetEntityById(crew.SeatEntityId.Value, out seatEnt))
                        seat = seatEnt as IMyTerminalBlock;
                }

                m.StateSeconds += dt;
                switch (m.State)
                {
                    case RepairMissionState.WalkOut:
                        TickWalk(session, m, crew, character, seat, grid, true, dt);
                        break;
                    case RepairMissionState.AtExit:
                        TickAtExit(m, crew, character, seat, grid);
                        break;
                    case RepairMissionState.EvaTransit:
                        TickEvaTransit(session, m, crew, character, seat, grid, dt);
                        break;
                    case RepairMissionState.Welding:
                        TickWelding(session, m, crew, character, seat, grid, dt);
                        break;
                    case RepairMissionState.ReturnExit:
                    case RepairMissionState.WalkHome:
                        FinishMission(m);
                        break;
                }
            }

            for (int i = 0; i < RemoveScratch.Count; i++)
                ClearMissionForCrew(RemoveScratch[i], cooldown: false);
        }

        /// <summary>Manual Send for one Damage Control crew. Returns false if not started.</summary>
        public static bool DispatchCrew(CrewSession session, string crewId)
        {
            if (session == null || session.Store == null || string.IsNullOrEmpty(crewId))
                return false;
            if (IsCrewOnMission(crewId))
                return false;

            var crew = session.Store.Get(crewId);
            if (crew == null || crew.Status != CrewStatus.Seated || crew.Role != CrewRole.DamageControl)
                return false;
            if (crew.GridEntityId == 0)
                return false;

            IMyEntity gridEnt;
            if (!MyAPIGateway.Entities.TryGetEntityById(crew.GridEntityId, out gridEnt))
                return false;
            var grid = gridEnt as IMyCubeGrid;
            if (grid == null || !CrewAmbientPresence.IsGridIdle(grid))
                return false;
            if (!CanStartAnotherOnGrid(crew.GridEntityId))
                return false;

            InvalidateWorkCache();
            EnsureWorkCache(grid);

            // Manual Send ignores post-sortie cooldown.
            CrewCooldownUntil.Remove(crew.CrewId);

            Vector3D from = grid.WorldAABB.Center;
            if (crew.SeatEntityId.HasValue)
            {
                IMyEntity seatEnt;
                if (MyAPIGateway.Entities.TryGetEntityById(crew.SeatEntityId.Value, out seatEnt)
                    && seatEnt != null)
                    from = seatEnt.GetPosition();
            }

            bool usesPath = session.RepairPaths != null && session.RepairPaths.IsReady(crew.GridEntityId);
            var m = new MissionRuntime
            {
                CrewId = crew.CrewId,
                GridEntityId = crew.GridEntityId,
                State = usesPath ? RepairMissionState.WalkOut : RepairMissionState.EvaTransit,
                WaypointIndex = 0,
                StateSeconds = 0,
                UsesPath = usesPath,
                AcquireCooldown = 0
            };

            IMyProjector projector;
            bool isProjected;
            IMySlimBlock target;
            string kind = "idle";
            if (TryPickWorkTarget(grid, from, crew.CrewId, out target, out projector, out isProjected)
                && target != null)
            {
                SetMissionTarget(m, target, projector, isProjected);
                kind = isProjected ? "project" : "repair";
            }

            ByCrew[crew.CrewId] = m;
            Log("repair dispatch crew=" + crew.CrewId + " grid=" + crew.GridEntityId
                + (usesPath ? " via=path" : " via=local")
                + " kind=" + kind);
            return true;
        }

        /// <summary>Manual Recall for one Damage Control crew. Returns false if not on mission.</summary>
        public static bool RecallCrew(string crewId)
        {
            if (string.IsNullOrEmpty(crewId))
                return false;
            MissionRuntime m;
            if (!ByCrew.TryGetValue(crewId, out m) || m == null)
                return false;
            BeginReturn(m);
            Log("repair recall crew=" + crewId);
            return true;
        }

        private static bool CanStartAnotherOnGrid(long gridId)
        {
            int max = CrewConfig.RepairMaxParallelPerGrid;
            if (max <= 0)
                return true;
            int n = 0;
            foreach (var kv in ByCrew)
            {
                if (kv.Value != null && kv.Value.GridEntityId == gridId)
                    n++;
            }
            return n < max;
        }

        private static void AdvanceLogical(CrewSession session, double dt)
        {
            CopyCrewKeys(KeyScratch);
            for (int ki = 0; ki < KeyScratch.Count; ki++)
            {
                MissionRuntime m;
                if (!ByCrew.TryGetValue(KeyScratch[ki], out m) || m == null || string.IsNullOrEmpty(m.CrewId))
                    continue;
                var crew = session.Store.Get(m.CrewId);
                if (crew == null) continue;
                IMyCharacter character;
                if (TryGetCharacter(crew, out character) && character != null)
                    continue;

                // Skip theater states without a body — snap resume handles pose on respawn.
                if (m.State == RepairMissionState.AtExit)
                {
                    if (m.StateSeconds > 1.5) { m.State = RepairMissionState.EvaTransit; m.StateSeconds = 0; }
                    else m.StateSeconds += dt;
                    continue;
                }
                if (m.State == RepairMissionState.EvaTransit || m.State == RepairMissionState.ReturnExit)
                {
                    if (m.State == RepairMissionState.ReturnExit)
                    {
                        FinishMission(m);
                        continue;
                    }
                    m.StateSeconds += dt;
                    if (m.StateSeconds > 2.0)
                    {
                        m.State = RepairMissionState.Welding;
                        m.StateSeconds = 0;
                    }
                    continue;
                }

                if (m.State == RepairMissionState.Welding)
                {
                    // Projector builds are visible theater — wait for a live body; do not place holograms remotely.
                    if (m.TargetIsProjected)
                        continue;

                    // While a body exists, UpdateMovement owns weld ticks (avoid double-billing comps).
                    IMyCharacter liveBody;
                    if (TryGetCharacter(crew, out liveBody) && liveBody != null && !liveBody.Closed)
                        continue;

                    IMyEntity gridEnt;
                    if (!MyAPIGateway.Entities.TryGetEntityById(m.GridEntityId, out gridEnt))
                        continue;
                    var grid = gridEnt as IMyCubeGrid;
                    if (grid == null) continue;
                    IMySlimBlock slim;
                    if (!TryResolveTarget(m, grid, out slim) || slim == null || !NeedsRepair(slim))
                    {
                        ClearCurrentTarget(m, grid);
                        TryAcquireNextTarget(m, grid, grid.WorldAABB.Center, (float)dt);
                        continue;
                    }
                    long welderId = crew.OwnerIdentityId != 0 ? crew.OwnerIdentityId : crew.OwnerKey;
                    float amount = CrewConfig.GetRepairWeldMountPerSecond(crew.Stars) * (float)dt;
                    if (!TryWeldTick(slim, grid, welderId, amount))
                    {
                        // Same as EVA: one failed cell must not end the sortie.
                        bool missing = IsBlockedByMissingComponents(slim, grid, false);
                        if (!missing && m.HasTargetCell)
                            m.SkippedTargets.Add(
                                SkipTargetKey(m.TargetCell, m.ProjectorEntityId, m.TargetIsProjected));
                        ClearCurrentTarget(m, grid);
                        if (!TryAcquireNextTarget(m, grid, grid.WorldAABB.Center, 0f) && missing)
                            TryFinishIfOnlyUnaffordableWork(session, m, crew, grid);
                        continue;
                    }
                    if (!NeedsRepair(slim))
                    {
                        ClearCurrentTarget(m, grid);
                        TryAcquireNextTarget(m, grid, grid.WorldAABB.Center, 0f);
                    }
                    continue;
                }

                // Far-away: advance interior waypoints on a timer.
                if (m.State != RepairMissionState.WalkOut && m.State != RepairMissionState.WalkHome)
                    continue;

                var path = session.RepairPaths.Get(m.GridEntityId);
                if (path == null || path.Waypoints == null || path.Waypoints.Count == 0)
                    continue;

                m.LogicalProgressMeters += CrewConfig.RepairLogicalWalkSpeedMeters * dt;
                if (m.LogicalProgressMeters < 3.0)
                    continue;
                m.LogicalProgressMeters = 0;

                if (m.State == RepairMissionState.WalkOut)
                {
                    if (m.WaypointIndex < path.Waypoints.Count - 1)
                        m.WaypointIndex++;
                    if (m.WaypointIndex >= path.Waypoints.Count - 1)
                    {
                        m.State = RepairMissionState.AtExit;
                        m.StateSeconds = 0;
                    }
                }
                else if (m.State == RepairMissionState.WalkHome)
                {
                    FinishMission(m);
                }
            }
        }

        private static void TickWalk(
            CrewSession session,
            MissionRuntime m,
            CrewRecord crew,
            IMyCharacter character,
            IMyTerminalBlock seat,
            IMyCubeGrid grid,
            bool outward,
            float dt)
        {
            var path = session.RepairPaths.Get(m.GridEntityId);
            if (path == null || path.Waypoints == null || path.Waypoints.Count == 0)
            {
                FinishMission(m);
                return;
            }

            int idx = m.WaypointIndex;
            if (idx < 0) idx = 0;
            if (idx >= path.Waypoints.Count) idx = path.Waypoints.Count - 1;

            Vector3D wp;
            if (!RepairPathStore.TryResolveWorldPos(grid, path.Waypoints[idx], out wp))
            {
                FinishMission(m);
                return;
            }
            wp = wp + grid.WorldMatrix.Up * 0.1;

            if (character != null && !character.Closed)
            {
                CrewAmbientPresence.SetCharacterJetpack(character, false);
                CrewAmbientPresence.SteerCharacterToward(character, wp, grid, seat, false);
                if (Vector3D.DistanceSquared(character.GetPosition(), wp)
                    <= CrewConfig.RepairWaypointArriveMeters * CrewConfig.RepairWaypointArriveMeters)
                {
                    CrewAmbientPresence.StopCharacterMovement(character, grid);
                    AdvanceWalkIndex(m, path, outward);
                }
            }
        }

        private static void AdvanceWalkIndex(MissionRuntime m, RepairGridPath path, bool outward)
        {
            if (outward)
            {
                if (m.WaypointIndex < path.Waypoints.Count - 1)
                    m.WaypointIndex++;
                if (m.WaypointIndex >= path.Waypoints.Count - 1)
                {
                    m.State = RepairMissionState.AtExit;
                    m.StateSeconds = 0;
                }
            }
            else
            {
                if (m.WaypointIndex > 0)
                    m.WaypointIndex--;
                if (m.WaypointIndex <= 0)
                    FinishMission(m);
            }
        }

        private static void TickAtExit(
            MissionRuntime m,
            CrewRecord crew,
            IMyCharacter character,
            IMyTerminalBlock seat,
            IMyCubeGrid grid)
        {
            TryOpenNearbyDoor(grid, character != null ? character.GetPosition() : grid.WorldAABB.Center);

            Vector3D exitPos;
            if (!TryGetExitWorld(m, grid, out exitPos))
            {
                BeginReturn(m);
                return;
            }

            Vector3D outward = exitPos - grid.WorldAABB.Center;
            if (outward.LengthSquared() < 0.01)
                outward = grid.WorldMatrix.Forward;
            outward.Normalize();
            Vector3D exterior = exitPos + outward * 2.5;

            if (character != null && !character.Closed)
            {
                float evaSpeed = CrewConfig.GetRepairEvaSpeedMeters(crew != null ? crew.Stars : 0);
                FlyToward(m, character, grid, exterior, evaSpeed, 1f / 60f);
                if (Vector3D.DistanceSquared(character.GetPosition(), exterior) < 1.0)
                {
                    m.State = RepairMissionState.EvaTransit;
                    m.StateSeconds = 0;
                }
            }
            else if (m.StateSeconds > 1.5)
            {
                m.State = RepairMissionState.EvaTransit;
                m.StateSeconds = 0;
            }
        }

        private static void TickEvaTransit(
            CrewSession session,
            MissionRuntime m,
            CrewRecord crew,
            IMyCharacter character,
            IMyTerminalBlock seat,
            IMyCubeGrid grid,
            float dt)
        {
            Vector3D fromPos = character != null && !character.Closed
                ? character.GetPosition()
                : (seat != null ? seat.GetPosition() : grid.WorldAABB.Center);

            IMySlimBlock slim;
            if (!TryResolveTarget(m, grid, out slim) || slim == null)
            {
                // Release stale claims (grinded block / projector cell) before rescanning.
                if (m.HasTargetCell)
                    ClearCurrentTarget(m, grid);
                TryAcquireNextTarget(m, grid, fromPos, dt);
                if (!TryResolveTarget(m, grid, out slim) || slim == null)
                {
                    // No personal CanBuild/repair claim yet — still EVA to the work rally
                    // so the whole batch flies, not just the first claimant.
                    if (character != null && !character.Closed)
                    {
                        Vector3D rally = GetDispatchRally(m, grid, fromPos);
                        FlyToward(m, character, grid, rally, CrewConfig.GetRepairEvaSpeedMeters(crew.Stars), dt);
                    }
                    return;
                }
            }
            else
                m.NoWorkSeconds = 0;

            EnsureWeldApproach(m, grid, slim, fromPos);

            Vector3D hover = GetHover(m);
            Vector3D flyTo = hover;
            if (m.HasStaging)
            {
                Vector3D stage = new Vector3D(m.StageX, m.StageY, m.StageZ);
                if (Vector3D.DistanceSquared(fromPos, stage) > 2.25)
                    flyTo = stage;
                else
                    m.HasStaging = false;
            }

            if (character != null && !character.Closed)
            {
                UpdateStuckWatch(m, character, grid, GetSlimWorld(slim, grid), dt);
                FlyToward(m, character, grid, flyTo, CrewConfig.GetRepairEvaSpeedMeters(crew.Stars), dt);

                bool inWeldRange = IsInWeldRangeOfSlim(character.GetPosition(), slim, grid);
                bool atHover = !m.HasStaging
                    && Vector3D.DistanceSquared(character.GetPosition(), hover)
                        <= CrewConfig.RepairEvaArriveMeters * CrewConfig.RepairEvaArriveMeters;
                // Only enter Welding when in AABB weld range — atHover alone left large
                // blocks "welding" while still outside the 5 m center sphere.
                if (inWeldRange || (atHover && IsInWeldRangeOfSlim(hover, slim, grid)))
                {
                    m.State = RepairMissionState.Welding;
                    m.StateSeconds = 0;
                    m.NoCompSeconds = 0;
                    m.NotifiedOutOfComps = false;
                    m.StuckSeconds = 0;
                }
            }
            else if (m.StateSeconds > 2.0)
            {
                // Projector theater needs a live EVA body — do not skip ahead to remote Build.
                if (!m.TargetIsProjected)
                {
                    m.State = RepairMissionState.Welding;
                    m.StateSeconds = 0;
                }
            }
        }

        /// <summary>
        /// Shared EVA rally near active work / projector / hull so idle welders still fly out.
        /// </summary>
        private static Vector3D GetDispatchRally(MissionRuntime m, IMyCubeGrid grid, Vector3D from)
        {
            Vector3D anchor = grid.WorldAABB.Center;
            bool haveAnchor = false;

            foreach (var kv in ByCrew)
            {
                if (kv.Value == null || m == null)
                    continue;
                if (!string.IsNullOrEmpty(m.CrewId)
                    && string.Equals(kv.Key, m.CrewId, StringComparison.Ordinal))
                    continue;
                MissionRuntime other = kv.Value;
                if (other.GridEntityId != grid.EntityId || !other.HasTargetCell)
                    continue;

                IMySlimBlock slim;
                if (!TryResolveTarget(other, grid, out slim) || slim == null)
                    continue;
                anchor = GetSlimWorld(slim, grid);
                haveAnchor = true;
                break;
            }

            if (!haveAnchor)
            {
                EnsureWorkCache(grid);
                for (int i = 0; i < WorkCache.Count; i++)
                {
                    if (!WorkCache[i].Projected)
                        continue;
                    anchor = WorkCache[i].World;
                    haveAnchor = true;
                    break;
                }
            }

            if (!haveAnchor)
                anchor = grid.WorldAABB.Center;

            Vector3D outward = anchor - grid.WorldAABB.Center;
            if (outward.LengthSquared() < 0.01)
                outward = from - grid.WorldAABB.Center;
            if (outward.LengthSquared() < 0.01)
                outward = grid.WorldMatrix.Forward;
            outward.Normalize();

            Vector3D side = Vector3D.CalculatePerpendicularVector(outward);
            int slot = 0;
            if (m != null && !string.IsNullOrEmpty(m.CrewId))
                slot = Math.Abs(m.CrewId.GetHashCode()) % 7;

            return anchor + outward * 5.0 + side * ((slot - 3) * 2.2);
        }

        private static void TickWelding(
            CrewSession session,
            MissionRuntime m,
            CrewRecord crew,
            IMyCharacter character,
            IMyTerminalBlock seat,
            IMyCubeGrid grid,
            float dt)
        {
            IMySlimBlock slim;
            if (!TryResolveTarget(m, grid, out slim) || slim == null
                || (!m.TargetIsProjected && !NeedsRepair(slim)))
            {
                // Drop ghost claims (missing cell / CanBuild no longer OK) so we do not
                // idle in Welding forever while AcquireCooldown suppresses rescans.
                Vector3D from = character != null && !character.Closed
                    ? character.GetPosition()
                    : grid.WorldAABB.Center;
                ClearCurrentTarget(m, grid);
                if (!TryAcquireNextTarget(m, grid, from, dt)
                    || !TryResolveTarget(m, grid, out slim)
                    || slim == null)
                {
                    if (!m.HasTargetCell)
                    {
                        m.State = RepairMissionState.EvaTransit;
                        m.StateSeconds = 0;
                    }
                    return;
                }
                m.State = RepairMissionState.EvaTransit;
                m.StateSeconds = 0;
                return;
            }
            else
                m.NoWorkSeconds = 0;

            // Projector holograms only place when the welder body is on-site.
            if (m.TargetIsProjected
                && (character == null || character.Closed))
                return;

            if (character != null && !character.Closed)
            {
                Vector3D pos = character.GetPosition();
                Vector3D blockPos = GetSlimWorld(slim, grid);
                bool inWeldRange = IsInWeldRangeOfSlim(pos, slim, grid);

                // Hold outside the AABB and weld — do not dive into the block.
                if (!inWeldRange)
                {
                    StopWeldParticles(m.CrewId);
                    EnsureWeldApproach(m, grid, slim, pos);
                    Vector3D hover = GetHover(m);
                    // Keep hover within weld reach of the AABB surface (not the center).
                    if (!IsInWeldRangeOfSlim(hover, slim, grid))
                    {
                        hover = ComputeSlimSurfaceHover(
                            slim, grid, pos, Math.Max(1.25f, CrewConfig.RepairEvaStandOffMeters * 0.65f));
                        m.HoverX = hover.X;
                        m.HoverY = hover.Y;
                        m.HoverZ = hover.Z;
                    }
                    UpdateStuckWatch(m, character, grid, blockPos, dt);
                    FlyToward(m, character, grid, hover, CrewConfig.GetRepairEvaSpeedMeters(crew.Stars) * 0.7f, dt);
                }
                else
                {
                    m.StuckSeconds = 0;
                    HoldEvaWeldPose(m, character, grid, blockPos, dt);
                }
            }

            // Weld / Build only in proximity. No remote hologram placement.
            if (character == null || character.Closed)
                return;
            Vector3D weldAt = GetSlimWorld(slim, grid);
            if (!IsInWeldRangeOfSlim(character.GetPosition(), slim, grid))
            {
                StopWeldParticles(m.CrewId);
                return;
            }

            UpdateWeldParticles(m, weldAt, character.GetPosition());

            long welderId = crew.OwnerIdentityId != 0 ? crew.OwnerIdentityId : crew.OwnerKey;

            // Projector hologram: pay first component from grid cargo, place, then weld physical.
            if (m.TargetIsProjected)
            {
                IMyProjector projector = GetMissionProjector(m);
                IMySlimBlock placed;
                if (projector == null
                    || !TryBuildProjected(projector, slim, grid, welderId, out placed)
                    || placed == null)
                {
                    HandleWeldStall(session, m, crew, grid, character, slim, dt, projectedPlaceFail: true);
                    return;
                }

                m.NoCompSeconds = 0;
                Log("repair project place crew=" + m.CrewId);
                SetMissionTarget(m, placed, null, false);
                slim = placed;
                if (!NeedsRepair(slim))
                {
                    ClearCurrentTarget(m, grid);
                    m.State = RepairMissionState.EvaTransit;
                    m.StateSeconds = 0;
                    TryAcquireNextTarget(m, grid, character.GetPosition(), 0f);
                    return;
                }
                // Fall through into normal weld tick for remaining components.
            }

            float amount = CrewConfig.GetRepairWeldMountPerSecond(crew.Stars) * dt;
            float maxIntegrity = 0f;
            try { maxIntegrity = slim.MaxIntegrity; }
            catch { }
            if (CrewRepairShareRules.IsLargeBlock(maxIntegrity, CrewConfig.RepairShareMaxIntegrity))
                amount *= CrewConfig.RepairLargeWeldMultiplier;

            float before = slim.Integrity;
            if (!TryWeldTick(slim, grid, welderId, amount))
            {
                HandleWeldStall(session, m, crew, grid, character, slim, dt, projectedPlaceFail: false);
                return;
            }

            m.NoCompSeconds = 0;
            if (slim.Integrity > before + 0.01f)
                Log("repair weld crew=" + m.CrewId + " +" + (slim.Integrity - before).ToString("0.0"));

            if (!NeedsRepair(slim))
            {
                ClearCurrentTarget(m, grid);
                m.State = RepairMissionState.EvaTransit;
                m.StateSeconds = 0;
                TryAcquireNextTarget(m, grid, character.GetPosition(), 0f);
            }
        }

        /// <summary>
        /// Skip the current cell after a stall. Missing comps for one block must not end the
        /// sortie — other affordable work may remain. Home only via TryAcquireNextTarget.
        /// </summary>
        private static void HandleWeldStall(
            CrewSession session,
            MissionRuntime m,
            CrewRecord crew,
            IMyCubeGrid grid,
            IMyCharacter character,
            IMySlimBlock slim,
            float dt,
            bool projectedPlaceFail)
        {
            if (m == null)
                return;

            m.NoCompSeconds += dt;
            float limit = projectedPlaceFail
                ? CrewConfig.RepairNoCompAbortSeconds
                : CrewConfig.RepairNoProgressSkipSeconds;
            if (m.NoCompSeconds < limit)
                return;

            bool missingComps = IsBlockedByMissingComponents(
                slim, grid, m.TargetIsProjected || projectedPlaceFail);
            // Stockpile still holding the next comps is not "out of components".
            if (missingComps && !m.TargetIsProjected && !projectedPlaceFail
                && StockpileHasAnyMissing(slim, MissingCompScratch))
                missingComps = false;

            float maxIntegrity = 0f;
            try { if (slim != null) maxIntegrity = slim.MaxIntegrity; }
            catch { }
            bool large = !m.TargetIsProjected && !projectedPlaceFail
                && CrewRepairShareRules.IsLargeBlock(
                    maxIntegrity, CrewConfig.RepairShareMaxIntegrity);

            // Large incomplete blocks: stay on target and re-feed. Abandoning mid-plate forced
            // players to re-Send repeatedly to finish Epstein-class drives.
            if (large && !missingComps && NeedsRepair(slim))
            {
                m.NoCompSeconds = 0;
                m.HasHover = false;
                FeedConstructionStockpile(slim, grid);
                Log("repair large retry crew=" + m.CrewId);
                return;
            }

            // Permanent skip only for unweldable/ghost cells. Never blacklist large blocks.
            if (!missingComps && !large && m.HasTargetCell)
                m.SkippedTargets.Add(SkipTargetKey(m.TargetCell, m.ProjectorEntityId, m.TargetIsProjected));

            Log(missingComps
                ? ("repair skip unaffordable crew=" + m.CrewId)
                : ("repair skip stuck target crew=" + m.CrewId
                    + (m.HasTargetCell ? (" cell=" + m.TargetCell) : string.Empty)));

            ClearCurrentTarget(m, grid);
            m.NoCompSeconds = 0;
            m.State = RepairMissionState.EvaTransit;
            m.StateSeconds = 0;
            Vector3D from = character != null && !character.Closed
                ? character.GetPosition()
                : grid.WorldAABB.Center;
            if (!TryAcquireNextTarget(m, grid, from, 0f) && missingComps)
            {
                // No other pickable work right now — if cache is only unaffordable, go home.
                if (TryFinishIfOnlyUnaffordableWork(session, m, crew, grid))
                    return;
            }
        }

        private static string SkipTargetKey(Vector3I cell, long projectorEntityId, bool projected)
        {
            return (projected ? "p" : "r") + ":" + projectorEntityId + ":" + cell.X + "," + cell.Y + "," + cell.Z;
        }

        private static bool IsBlockedByMissingComponents(
            IMySlimBlock slim,
            IMyCubeGrid grid,
            bool projectedFirstComp)
        {
            if (slim == null || grid == null)
                return false;

            bool creative = false;
            try { creative = MyAPIGateway.Session != null && MyAPIGateway.Session.CreativeMode; }
            catch { }
            if (creative)
                return false;

            if (projectedFirstComp)
            {
                MyDefinitionId firstCompId;
                if (!TryGetFirstComponentId(slim, out firstCompId))
                    return false;
                return CountComponentOnGrid(grid, firstCompId) < 1;
            }

            MissingCompScratch.Clear();
            try { slim.GetMissingComponents(MissingCompScratch); }
            catch { return false; }
            if (MissingCompScratch.Count == 0)
                return false;
            // Large blocks list hundreds of plates as "missing" — require any stock to continue,
            // not the entire remaining recipe in cargo before the first spark.
            // Comps already on the block stockpile also count (shared welds / mid-feed).
            if (HasAnyMissingComponentInCargo(grid, MissingCompScratch))
                return false;
            if (StockpileHasAnyMissing(slim, MissingCompScratch))
                return false;
            return true;
        }

        /// <summary>
        /// True when cargo has at least one unit of any still-missing component type.
        /// </summary>
        private static bool HasAnyMissingComponentInCargo(
            IMyCubeGrid grid,
            Dictionary<string, int> missing)
        {
            if (grid == null || missing == null || missing.Count == 0)
                return false;
            foreach (var kv in missing)
            {
                if (kv.Value <= 0 || string.IsNullOrEmpty(kv.Key))
                    continue;
                var id = new MyDefinitionId(typeof(MyObjectBuilder_Component), kv.Key);
                if (CountComponentOnGrid(grid, id) > 0)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Pull missing comps into the block stockpile (Keen mounts from stockpile, including plates).
        /// </summary>
        private static void FeedConstructionStockpile(IMySlimBlock slim, IMyCubeGrid grid)
        {
            if (slim == null || grid == null)
                return;
            CollectGridInventories(grid, InvScratch);
            for (int i = 0; i < InvScratch.Count; i++)
            {
                var inv = InvScratch[i];
                if (inv == null)
                    continue;
                try { slim.MoveItemsToConstructionStockpile(inv); }
                catch { }
            }
        }

        private static bool StockpileHasAnyMissing(IMySlimBlock slim, Dictionary<string, int> missing)
        {
            if (slim == null || missing == null || missing.Count == 0)
                return false;
            foreach (var kv in missing)
            {
                if (kv.Value <= 0 || string.IsNullOrEmpty(kv.Key))
                    continue;
                var id = new MyDefinitionId(typeof(MyObjectBuilder_Component), kv.Key);
                try
                {
                    if (slim.GetConstructionStockpileItemAmount(id) > 0)
                        return true;
                }
                catch { }
            }
            return false;
        }

        private static void ClearCurrentTarget(MissionRuntime m, IMyCubeGrid grid = null)
        {
            if (m == null) return;
            if (grid != null && !m.TargetIsProjected && m.HasTargetCell)
            {
                // Shared welders still need the stockpile — only the last one to leave refunds.
                int others = CountTargetClaimants(
                    grid.EntityId, m.CrewId, m.TargetCell, m.ProjectorEntityId, false);
                if (others == 0)
                {
                    IMySlimBlock abandon;
                    if (TryResolveTarget(m, grid, out abandon) && abandon != null)
                        RefundBlockStockpile(abandon, grid);
                }
            }
            StopWeldParticles(m.CrewId);
            m.TargetBlockEntityId = 0;
            m.HasTargetCell = false;
            m.TargetIsProjected = false;
            m.ProjectorEntityId = 0;
            m.HasHover = false;
            m.HasStaging = false;
            ClearFlyDynamics(m);
            // Next CanBuild cells appear after a place/finish — refresh shared cache.
            InvalidateWorkCache();
        }

        /// <summary>
        /// Pick another unclaimed target. Returns false when returning home (no work after grace).
        /// </summary>
        private static bool TryAcquireNextTarget(
            MissionRuntime m,
            IMyCubeGrid grid,
            Vector3D from,
            float dt)
        {
            if (m == null || grid == null)
                return false;

            // Throttle full work scans — idle welders were nuking FPS on large projections.
            if (dt > 0f)
            {
                if (m.AcquireCooldown > 0)
                {
                    m.AcquireCooldown = Math.Max(0, m.AcquireCooldown - dt);
                    return false;
                }
                m.AcquireCooldown = CrewConfig.RepairAcquireThrottleSeconds;
            }

            IMySlimBlock slim;
            IMyProjector proj;
            bool projected;
            if (TryPickWorkTarget(grid, from, m.CrewId, out slim, out proj, out projected)
                && slim != null)
            {
                SetMissionTarget(m, slim, proj, projected);
                m.NoWorkSeconds = 0;
                m.AcquireCooldown = 0;
                return true;
            }

            // Projector CanBuild often exposes one cell at a time — wait while siblings work.
            if (SiblingHasClaimedTarget(m.GridEntityId, m.CrewId))
            {
                m.NoWorkSeconds = 0;
                return false;
            }

            // Fresh cache before deciding the sortie is done (ClearTarget invalidates often).
            InvalidateWorkCache();
            EnsureWorkCache(grid);

            bool projectorPending = HasPendingProjectorHolograms(grid);
            float returnAfter = projectorPending
                ? CrewConfig.RepairProjectorIdleReturnSeconds
                : CrewConfig.RepairNoWorkReturnSeconds;

            // Unclaimed cache entries we cannot pick = unaffordable or locally skipped.
            // Keep waiting for projector frontier; otherwise OOC-home if cargo is the blocker.
            if (WorkCache.Count > 0 && !projectorPending)
            {
                var session = CrewSession.Instance;
                var crew = session != null && session.Store != null
                    ? session.Store.Get(m.CrewId)
                    : null;
                if (TryFinishIfOnlyUnaffordableWork(session, m, crew, grid))
                    return false;
            }

            m.NoWorkSeconds += Math.Max(dt, CrewConfig.RepairAcquireThrottleSeconds);
            if (m.NoWorkSeconds >= returnAfter)
            {
                Log("repair no work crew=" + m.CrewId
                    + (projectorPending ? " (projector idle)" : string.Empty));
                BeginReturn(m);
                return false;
            }
            return false;
        }

        /// <summary>
        /// True when a projector still has hologram blocks (even if none are CanBuild yet).
        /// </summary>
        private static bool HasPendingProjectorHolograms(IMyCubeGrid grid)
        {
            if (grid == null)
                return false;

            BlockScratch.Clear();
            grid.GetBlocks(BlockScratch);
            for (int i = 0; i < BlockScratch.Count; i++)
            {
                var fat = BlockScratch[i] != null ? BlockScratch[i].FatBlock : null;
                var proj = fat as IMyProjector;
                if (proj == null || proj.Closed || !proj.IsWorking || !proj.IsProjecting)
                    continue;
                IMyCubeGrid pGrid = proj.ProjectedGrid;
                if (pGrid == null)
                    continue;
                ProjectedScratch.Clear();
                pGrid.GetBlocks(ProjectedScratch);
                if (ProjectedScratch.Count > 0)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// If every unclaimed cache entry fails affordability, notify and go home.
        /// Returns true when the mission was ended.
        /// </summary>
        private static bool TryFinishIfOnlyUnaffordableWork(
            CrewSession session,
            MissionRuntime m,
            CrewRecord crew,
            IMyCubeGrid grid)
        {
            if (m == null || grid == null)
                return false;

            EnsureWorkCache(grid);
            bool sawUnaffordable = false;
            bool sawOther = false;

            for (int i = 0; i < WorkCache.Count; i++)
            {
                CachedWork w = WorkCache[i];
                if (m.SkippedTargets.Contains(SkipTargetKey(w.Cell, w.ProjectorEntityId, w.Projected)))
                {
                    sawOther = true;
                    continue;
                }

                IMySlimBlock slim;
                if (!TryResolveCachedWork(grid, w, out slim) || slim == null)
                    continue;

                if (IsTargetClaimFull(grid, m.CrewId, w, slim))
                    continue;

                if (!CanAffordSlimWork(grid, slim, w.Projected))
                {
                    sawUnaffordable = true;
                    continue;
                }

                // Affordable work with a free claim slot exists — stay out.
                return false;
            }

            if (!sawUnaffordable || sawOther)
                return false;

            NotifyOutOfComponents(session, m, crew);
            Log("repair out of comps crew=" + m.CrewId);
            BeginReturn(m);
            return true;
        }

        private static bool TryResolveCachedWork(
            IMyCubeGrid grid,
            CachedWork w,
            out IMySlimBlock slim)
        {
            slim = null;
            if (grid == null)
                return false;
            if (!w.Projected)
            {
                slim = grid.GetCubeBlock(w.Cell);
                return slim != null;
            }

            IMyEntity pent;
            if (!MyAPIGateway.Entities.TryGetEntityById(w.ProjectorEntityId, out pent) || pent == null)
                return false;
            var projector = pent as IMyProjector;
            if (projector == null || projector.ProjectedGrid == null)
                return false;
            slim = projector.ProjectedGrid.GetCubeBlock(w.Cell);
            return slim != null;
        }

        private static bool CanAffordSlimWork(IMyCubeGrid grid, IMySlimBlock slim, bool projected)
        {
            return !IsBlockedByMissingComponents(slim, grid, projected);
        }

        private static bool SiblingHasClaimedTarget(long gridId, string selfCrewId)
        {
            foreach (var kv in ByCrew)
            {
                if (kv.Value == null)
                    continue;
                if (!string.IsNullOrEmpty(selfCrewId)
                    && string.Equals(kv.Key, selfCrewId, StringComparison.Ordinal))
                    continue;
                MissionRuntime other = kv.Value;
                if (other.GridEntityId == gridId && other.HasTargetCell)
                    return true;
            }
            return false;
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

        private static void BeginReturn(MissionRuntime m)
        {
            if (m == null || string.IsNullOrEmpty(m.CrewId))
                return;

            IMyCubeGrid grid = null;
            IMyEntity gridEnt;
            if (m.GridEntityId != 0
                && MyAPIGateway.Entities.TryGetEntityById(m.GridEntityId, out gridEnt))
                grid = gridEnt as IMyCubeGrid;

            if (grid != null)
                ClearCurrentTarget(m, grid);
            else
                StopWeldParticles(m.CrewId);
            ClearFlyDynamics(m);

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
                TeleportHome(character, seat, grid ?? seat.CubeGrid);

            Log("repair home teleport crew=" + m.CrewId);
            FinishMission(m);
        }

        private static void FinishMission(MissionRuntime m)
        {
            if (m == null || string.IsNullOrEmpty(m.CrewId)) return;
            Log("repair idle crew=" + m.CrewId);
            ClearMissionForCrew(m.CrewId, cooldown: true);
        }

        private static void ClearMissionForCrew(string crewId, bool cooldown)
        {
            if (string.IsNullOrEmpty(crewId))
                return;
            StopWeldParticles(crewId);
            ByCrew.Remove(crewId);
            if (cooldown)
                CrewCooldownUntil[crewId] = DateTime.UtcNow.AddSeconds(CrewConfig.RepairRescanSeconds);
            else
                CrewCooldownUntil.Remove(crewId);
        }

        private static void CleanupMissing(CrewSession session)
        {
            RemoveScratch.Clear();
            CopyCrewKeys(KeyScratch);
            for (int ki = 0; ki < KeyScratch.Count; ki++)
            {
                string crewId = KeyScratch[ki];
                MissionRuntime m;
                if (!ByCrew.TryGetValue(crewId, out m) || m == null)
                {
                    RemoveScratch.Add(crewId);
                    continue;
                }
                IMyEntity ent;
                if (!MyAPIGateway.Entities.TryGetEntityById(m.GridEntityId, out ent)
                    || ent == null
                    || ent.Closed)
                {
                    RemoveScratch.Add(crewId);
                    continue;
                }
                if (session != null && session.Store != null
                    && session.Store.Get(crewId) == null)
                    RemoveScratch.Add(crewId);
            }
            for (int i = 0; i < RemoveScratch.Count; i++)
                ClearMissionForCrew(RemoveScratch[i], cooldown: false);

            // Drop expired cooldowns.
            RemoveScratch.Clear();
            foreach (var kv in CrewCooldownUntil)
            {
                if (kv.Value <= DateTime.UtcNow)
                    RemoveScratch.Add(kv.Key);
            }
            for (int i = 0; i < RemoveScratch.Count; i++)
                CrewCooldownUntil.Remove(RemoveScratch[i]);
        }

        private static void CopyCrewKeys(List<string> into)
        {
            into.Clear();
            foreach (var key in ByCrew.Keys)
                into.Add(key);
        }

        private static bool TryGetExitWorld(MissionRuntime m, IMyCubeGrid grid, out Vector3D exitPos)
        {
            exitPos = Vector3D.Zero;
            var paths = CrewSession.Instance != null ? CrewSession.Instance.RepairPaths : null;
            if (paths == null) return false;
            var path = paths.Get(m.GridEntityId);
            if (path == null || path.Waypoints == null || path.Waypoints.Count == 0)
                return false;
            return RepairPathStore.TryResolveWorldPos(grid, path.Waypoints[path.Waypoints.Count - 1], out exitPos);
        }

        private static void TryOpenNearbyDoor(IMyCubeGrid grid, Vector3D near)
        {
            if (grid == null) return;
            BlockScratch.Clear();
            grid.GetBlocks(BlockScratch);
            for (int i = 0; i < BlockScratch.Count; i++)
            {
                var fat = BlockScratch[i] != null ? BlockScratch[i].FatBlock : null;
                var door = fat as IMyDoor;
                if (door == null || door.Closed) continue;
                if (Vector3D.DistanceSquared(door.GetPosition(), near) > 16.0)
                    continue;
                try { door.OpenDoor(); }
                catch { }
            }
        }

        private static void SetMissionTarget(
            MissionRuntime m,
            IMySlimBlock slim,
            IMyProjector projector,
            bool isProjected)
        {
            if (m == null || slim == null) return;
            m.TargetBlockEntityId = slim.FatBlock != null ? slim.FatBlock.EntityId : 0;
            m.TargetCell = slim.Position;
            m.HasTargetCell = true;
            m.TargetIsProjected = isProjected;
            m.ProjectorEntityId = projector != null ? projector.EntityId : 0;
            m.HasHover = false;
            m.HasStaging = false;
            ClearFlyDynamics(m);
        }

        private static IMyProjector GetMissionProjector(MissionRuntime m)
        {
            if (m == null || m.ProjectorEntityId == 0)
                return null;
            IMyEntity ent;
            if (!MyAPIGateway.Entities.TryGetEntityById(m.ProjectorEntityId, out ent) || ent == null)
                return null;
            return ent as IMyProjector;
        }

        private static Vector3D GetHover(MissionRuntime m)
        {
            return new Vector3D(m.HoverX, m.HoverY, m.HoverZ);
        }

        private static void EnsureWeldApproach(
            MissionRuntime m,
            IMyCubeGrid grid,
            IMySlimBlock slim,
            Vector3D fromPos)
        {
            if (m == null || grid == null || slim == null)
                return;

            if (!m.HasHover)
            {
                Vector3D hover;
                if (!TryComputeWeldHover(grid, slim, fromPos, out hover))
                    hover = ComputeSlimSurfaceHover(slim, grid, fromPos, CrewConfig.RepairEvaStandOffMeters);
                m.HoverX = hover.X;
                m.HoverY = hover.Y;
                m.HoverZ = hover.Z;
                m.HasHover = true;

                // Spread shared welders along the surface (AABB-based), not around the center.
                int shareSlot = 0;
                if (!string.IsNullOrEmpty(m.CrewId))
                    shareSlot = Math.Abs(m.CrewId.GetHashCode()) % 7;
                BoundingBoxD slimBox;
                Vector3D hoverBlock = GetSlimWorld(slim, grid);
                if (TryGetSlimWorldBox(slim, grid, out slimBox))
                    hoverBlock = slimBox.Center;
                Vector3D hoverOut = new Vector3D(m.HoverX, m.HoverY, m.HoverZ) - hoverBlock;
                if (hoverOut.LengthSquared() < 0.01)
                    hoverOut = grid.WorldMatrix.Forward;
                hoverOut.Normalize();
                Vector3D hoverSide = Vector3D.CalculatePerpendicularVector(hoverOut);
                double lateral;
                double outwardBias;
                CrewRepairShareRules.SharedHoverOffsets(shareSlot, out lateral, out outwardBias);
                Vector3D off = hoverSide * lateral + hoverOut * outwardBias;
                m.HoverX += off.X;
                m.HoverY += off.Y;
                m.HoverZ += off.Z;
                // Re-snap to a valid surface hover if the offset left weld reach.
                hover = new Vector3D(m.HoverX, m.HoverY, m.HoverZ);
                if (!IsInWeldRangeOfSlim(hover, slim, grid))
                {
                    hover = ComputeSlimSurfaceHover(
                        slim, grid, fromPos, Math.Max(1.25f, CrewConfig.RepairEvaStandOffMeters * 0.65f));
                    hover = hover + hoverSide * lateral;
                    m.HoverX = hover.X;
                    m.HoverY = hover.Y;
                    m.HoverZ = hover.Z;
                }
                hover = new Vector3D(m.HoverX, m.HoverY, m.HoverZ);

                // If the straight path is blocked by the hull, stage outside the AABB first.
                Vector3D stage;
                if (NeedsExteriorStaging(grid, fromPos, hover, out stage))
                {
                    m.StageX = stage.X;
                    m.StageY = stage.Y;
                    m.StageZ = stage.Z;
                    m.HasStaging = true;
                }
                else
                    m.HasStaging = false;
            }
        }

        private static bool TryComputeWeldHover(
            IMyCubeGrid grid,
            IMySlimBlock slim,
            Vector3D preferFrom,
            out Vector3D hover)
        {
            hover = Vector3D.Zero;
            if (grid == null || slim == null)
                return false;

            // AABB surface hover — center + stand-off buries bots inside large fat blocks.
            hover = ComputeSlimSurfaceHover(slim, grid, preferFrom, CrewConfig.RepairEvaStandOffMeters);
            try
            {
                Vector3I cell = grid.WorldToGridInteger(hover);
                if (grid.CubeExists(cell) || grid.GetCubeBlock(cell) != null)
                {
                    BoundingBoxD box;
                    if (TryGetSlimWorldBox(slim, grid, out box))
                    {
                        Vector3D dir = hover - box.Center;
                        if (dir.LengthSquared() < 0.01)
                            dir = preferFrom - box.Center;
                        if (dir.LengthSquared() < 0.01)
                            dir = grid.WorldMatrix.Forward;
                        dir.Normalize();
                        hover = hover + dir * 1.5;
                    }
                }
            }
            catch { }

            return true;
        }

        private static bool NeedsExteriorStaging(
            IMyCubeGrid grid,
            Vector3D from,
            Vector3D hover,
            out Vector3D stage)
        {
            stage = hover;
            if (grid == null)
                return false;

            // Close enough — a staging detour just looks like flying away first.
            if (Vector3D.DistanceSquared(from, hover) < 64.0)
                return false;

            BoundingBoxD shipBox = grid.WorldAABB;
            // Character-sized pad only — do not inflate by weld standoff or staging flies far off-hull.
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

            // Only AABB-stage when the weld hover is already outside the world box. If hover is
            // still inside (common — standoff is only a few meters), projecting onto the AABB
            // surface flings the bot to a distant empty face on large/loose bounds.
            if (shipBox.Contains(hover) != ContainmentType.Disjoint)
                return false;

            const double clearance = 0.75;
            Vector3D surface;
            if (!TryAabbExitToward(shipBox, from, hover, out surface))
                surface = ClosestPointOnAabbSurface(shipBox, hover);
            stage = surface + AabbFaceOutwardNormal(shipBox, surface) * clearance;

            // Hard cap: staging must stay near the weld approach, never across the whole AABB.
            const double maxFromHover = 3.0;
            Vector3D away = stage - hover;
            double awayLen = away.Length();
            if (awayLen > maxFromHover)
                stage = hover + away * (maxFromHover / awayLen);

            // Abort staging if it points away from the weld approach.
            Vector3D toHover = hover - from;
            Vector3D toStage = stage - from;
            if (toHover.LengthSquared() > 0.01 && toStage.LengthSquared() > 0.01)
            {
                toHover.Normalize();
                toStage.Normalize();
                if (Vector3D.Dot(toHover, toStage) < 0.15)
                    return false;
            }

            return Vector3D.DistanceSquared(stage, from) > 4.0;
        }

        /// <summary>
        /// Where the ray from→toward leaves the AABB. Requires <paramref name="toward"/> to lie
        /// outside the box; otherwise callers would snap interior points onto a far face.
        /// </summary>
        private static bool TryAabbExitToward(BoundingBoxD box, Vector3D from, Vector3D toward, out Vector3D exit)
        {
            exit = toward;
            if (box.Contains(toward) != ContainmentType.Disjoint)
                return false;

            Vector3D dir = toward - from;
            double len = dir.Length();
            if (len < 0.01)
                return false;
            dir /= len;

            double tMin = double.NegativeInfinity;
            double tMax = double.PositiveInfinity;
            if (!ClipRaySlab(from.X, dir.X, box.Min.X, box.Max.X, ref tMin, ref tMax)
                || !ClipRaySlab(from.Y, dir.Y, box.Min.Y, box.Max.Y, ref tMin, ref tMax)
                || !ClipRaySlab(from.Z, dir.Z, box.Min.Z, box.Max.Z, ref tMin, ref tMax)
                || tMax < 0.05)
                return false;

            // Exterior target: leave at the AABB exit on the way there.
            double tExit = tMax;
            if (tExit > len)
                tExit = len;
            exit = from + dir * tExit;
            return true;
        }

        private static bool ClipRaySlab(
            double origin, double dir, double min, double max, ref double tMin, ref double tMax)
        {
            if (Math.Abs(dir) < 1e-9)
                return origin >= min && origin <= max;

            double t1 = (min - origin) / dir;
            double t2 = (max - origin) / dir;
            if (t1 > t2)
            {
                double tmp = t1;
                t1 = t2;
                t2 = tmp;
            }
            if (t1 > tMin) tMin = t1;
            if (t2 < tMax) tMax = t2;
            return tMin <= tMax;
        }

        /// <summary>
        /// Nearest point on the AABB surface. Interior points use true nearest-face distance
        /// (not dominant half-extent ratio — that sends long ships to a far end-cap).
        /// </summary>
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

        private static Vector3D AabbFaceOutwardNormal(BoundingBoxD box, Vector3D onSurface)
        {
            const double eps = 0.05;
            if (Math.Abs(onSurface.X - box.Min.X) <= eps) return new Vector3D(-1, 0, 0);
            if (Math.Abs(onSurface.X - box.Max.X) <= eps) return new Vector3D(1, 0, 0);
            if (Math.Abs(onSurface.Y - box.Min.Y) <= eps) return new Vector3D(0, -1, 0);
            if (Math.Abs(onSurface.Y - box.Max.Y) <= eps) return new Vector3D(0, 1, 0);
            if (Math.Abs(onSurface.Z - box.Min.Z) <= eps) return new Vector3D(0, 0, -1);
            if (Math.Abs(onSurface.Z - box.Max.Z) <= eps) return new Vector3D(0, 0, 1);

            Vector3D o = onSurface - box.Center;
            if (o.LengthSquared() < 0.01)
                return Vector3D.Up;
            o.Normalize();
            return o;
        }

        private static void UpdateStuckWatch(
            MissionRuntime m,
            IMyCharacter character,
            IMyCubeGrid grid,
            Vector3D preferToward,
            float dt)
        {
            if (m == null || character == null || character.Closed || grid == null)
                return;

            Vector3D pos = character.GetPosition();
            if (!m.HasLastPos)
            {
                m.LastPosX = pos.X;
                m.LastPosY = pos.Y;
                m.LastPosZ = pos.Z;
                m.HasLastPos = true;
                m.StuckSeconds = 0;
                return;
            }

            // Sample progress every ~0.5s so frame-to-frame noise does not false-trigger.
            m.StuckSampleSeconds += dt;
            try
            {
                Vector3I cell = grid.WorldToGridInteger(pos);
                if (grid.CubeExists(cell) || grid.GetCubeBlock(cell) != null)
                    m.StuckSeconds += dt;
            }
            catch { }

            if (m.StuckSampleSeconds >= 0.5)
            {
                Vector3D last = new Vector3D(m.LastPosX, m.LastPosY, m.LastPosZ);
                double moved = Vector3D.Distance(pos, last);
                m.LastPosX = pos.X;
                m.LastPosY = pos.Y;
                m.LastPosZ = pos.Z;
                m.StuckSampleSeconds = 0;
                if (moved < CrewConfig.RepairStuckMoveMeters)
                    m.StuckSeconds += 0.5;
                else if (m.StuckSeconds > 0)
                    m.StuckSeconds = Math.Max(0, m.StuckSeconds - 0.5);
            }

            if (m.StuckSeconds < CrewConfig.RepairStuckSeconds)
                return;

            Vector3D toward = preferToward;
            if (m.HasHover)
                toward = GetHover(m);
            m.StuckSeconds = 0;
            m.StuckSampleSeconds = 0;
            m.HasHover = false;
            m.HasStaging = false;
            m.HasLastPos = false;
            ClearFlyDynamics(m);
            m.UnstuckCount++;
            TeleportOutsideGrid(character, grid, toward);
            Log("repair unstuck crew=" + m.CrewId + " n=" + m.UnstuckCount);
            if (m.UnstuckCount > 6)
            {
                m.UnstuckCount = 0;
                // Do not abort a large incomplete weld — hover thrash was ending sorties early.
                if (m.State == RepairMissionState.Welding
                    && m.HasTargetCell
                    && !m.TargetIsProjected)
                {
                    IMySlimBlock stuckSlim;
                    if (TryResolveTarget(m, grid, out stuckSlim) && stuckSlim != null && NeedsRepair(stuckSlim))
                    {
                        float mi = 0f;
                        try { mi = stuckSlim.MaxIntegrity; }
                        catch { }
                        if (CrewRepairShareRules.IsLargeBlock(mi, CrewConfig.RepairShareMaxIntegrity))
                        {
                            m.HasHover = false;
                            m.HasStaging = false;
                            Log("repair unstuck keep large crew=" + m.CrewId);
                            return;
                        }
                    }
                }
                BeginReturn(m);
            }
        }

        private static void TeleportOutsideGrid(IMyCharacter character, IMyCubeGrid grid, Vector3D preferToward)
        {
            if (character == null || character.Closed || grid == null)
                return;

            BoundingBoxD box = grid.WorldAABB;
            Vector3D from = character.GetPosition();
            Vector3D anchor = preferToward.LengthSquared() > 0.01 ? preferToward : from;
            Vector3D stage;

            Vector3D surface;
            if (TryAabbExitToward(box, from, anchor, out surface))
            {
                stage = surface + AabbFaceOutwardNormal(box, surface) * 1.25;
            }
            else
            {
                // Anchor still inside the world AABB — do not project onto a distant box face.
                // Nudge a short distance from the work point along a local outward.
                Vector3D push = anchor - box.Center;
                if (push.LengthSquared() < 0.01)
                    push = anchor - from;
                if (push.LengthSquared() < 0.01)
                    push = grid.WorldMatrix.Forward;
                push.Normalize();
                stage = anchor + push * 1.25;
            }

            // Never unstuck across the whole ship bounds — stay by the work point.
            const double maxFromAnchor = 3.0;
            Vector3D delta = stage - anchor;
            double dLen = delta.Length();
            if (dLen > maxFromAnchor)
                stage = anchor + delta * (maxFromAnchor / dLen);

            try
            {
                character.SetPosition(stage);
                BindEvaPhysics(character, grid);
                if (preferToward.LengthSquared() > 0.01)
                    FaceToward(character, grid, preferToward);
            }
            catch { }
        }

        private static void TryNudgeOutOfSolid(IMyCharacter character, IMyCubeGrid grid, Vector3D awayFrom)
        {
            if (character == null || grid == null) return;
            try
            {
                Vector3D pos = character.GetPosition();
                Vector3I cell = grid.WorldToGridInteger(pos);
                if (!grid.CubeExists(cell) && grid.GetCubeBlock(cell) == null)
                    return;
                Vector3D outDir = pos - grid.WorldAABB.Center;
                if (outDir.LengthSquared() < 0.01)
                    outDir = pos - awayFrom;
                if (outDir.LengthSquared() < 0.01)
                    outDir = grid.WorldMatrix.Forward;
                outDir.Normalize();
                MatrixD wm = character.WorldMatrix;
                wm.Translation = pos + outDir * 1.25;
                character.WorldMatrix = wm;
                BindEvaPhysics(character, grid);
            }
            catch { }
        }

        /// <summary>
        /// Scripted EVA puppet: kill gravity (ambient tick is skipped on mission) and ride the grid.
        /// Zeroing world velocity while art-grav pulls = fall/snap jitter every frame.
        /// </summary>
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

        private static void FaceToward(IMyCharacter character, IMyCubeGrid grid, Vector3D worldPoint)
        {
            if (character == null || character.Closed) return;
            Vector3D pos = character.GetPosition();
            Vector3D to = worldPoint - pos;
            if (to.LengthSquared() < 0.0001) return;
            to.Normalize();
            try
            {
                Vector3D up = EvaUp(grid, character);
                Vector3D fwd = FlattenDir(to, up);
                character.WorldMatrix = MatrixD.CreateWorld(pos, fwd, up);
            }
            catch { }
        }

        /// <summary>Blend facing toward a world direction (flight heading) without hard snaps.</summary>
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

            double t = Math.Min(1.0, CrewConfig.RepairEvaTurnRate * dt);
            // Spherical-ish blend: normalize lerp (stable for moderate turn rates).
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

        private static void StabilizeEvaPose(
            MissionRuntime m,
            IMyCharacter character,
            IMyCubeGrid grid,
            bool forcePose)
        {
            if (character == null || character.Closed)
                return;

            // Jetpack + Flying pose for EVA theater (position is still scripted).
            CrewAmbientPresence.SetCharacterJetpack(character, true);
            CrewAmbientPresence.ApplyCrewInvulnerability(character);
            BindEvaPhysics(character, grid);

            if (m != null)
            {
                if (!forcePose && m.PoseCooldown > 0)
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

        private static void UpdateWeldParticles(MissionRuntime m, Vector3D weldPos, Vector3D fromPos)
        {
            if (m == null || string.IsNullOrEmpty(m.CrewId))
                return;

            Vector3D to = weldPos - fromPos;
            if (to.LengthSquared() < 0.0001)
                to = Vector3D.Forward;
            else
                to.Normalize();
            Vector3D up = Vector3D.Up;
            if (Math.Abs(Vector3D.Dot(to, up)) > 0.95)
                up = Vector3D.Right;
            Vector3D right = Vector3D.Normalize(Vector3D.Cross(to, up));
            up = Vector3D.Normalize(Vector3D.Cross(right, to));
            MatrixD matrix = MatrixD.CreateWorld(weldPos, to, up);

            MyParticleEffect fx;
            if (!WeldFxByCrew.TryGetValue(m.CrewId, out fx) || fx == null)
            {
                try
                {
                    if (!MyParticlesManager.TryCreateParticleEffect(
                            WeldParticleSubtype,
                            ref matrix,
                            ref weldPos,
                            uint.MaxValue,
                            out fx)
                        || fx == null)
                    {
                        // Fallback ship-welder arc if contact point subtype missing.
                        if (!MyParticlesManager.TryCreateParticleEffect(
                                "ShipWelderArc",
                                ref matrix,
                                ref weldPos,
                                uint.MaxValue,
                                out fx)
                            || fx == null)
                            return;
                    }
                    try { fx.Autodelete = false; }
                    catch { }
                    try { fx.UserScale = 0.9f; }
                    catch { }
                    WeldFxByCrew[m.CrewId] = fx;
                }
                catch { return; }
            }

            try
            {
                fx.WorldMatrix = matrix;
                fx.Play();
            }
            catch { }
        }

        private static void StopWeldParticles(string crewId)
        {
            if (string.IsNullOrEmpty(crewId))
                return;
            MyParticleEffect fx;
            if (!WeldFxByCrew.TryGetValue(crewId, out fx))
                return;
            WeldFxByCrew.Remove(crewId);
            if (fx == null)
                return;
            try { fx.Stop(false); }
            catch { }
            try { MyParticlesManager.RemoveParticleEffect(fx); }
            catch { }
        }

        private static void HoldEvaWeldPose(
            MissionRuntime m,
            IMyCharacter character,
            IMyCubeGrid grid,
            Vector3D lookAt,
            float dt)
        {
            if (m != null)
            {
                m.PoseCooldown = Math.Max(0, m.PoseCooldown - dt);
                m.NudgeCooldown = Math.Max(0, m.NudgeCooldown - dt);
            }

            ClearFlyDynamics(m);
            StabilizeEvaPose(m, character, grid, false);
            BindEvaPhysics(character, grid);

            Vector3D pos = character.GetPosition();
            Vector3D toBlock = lookAt - pos;
            if (toBlock.LengthSquared() > 0.01)
            {
                Vector3D up = EvaUp(grid, character);
                Vector3D fwd = BlendFacing(m, character, grid, toBlock, dt);
                try
                {
                    character.WorldMatrix = MatrixD.CreateWorld(pos, fwd, up);
                }
                catch { }
            }

            if (m != null && m.NudgeCooldown <= 0)
            {
                TryNudgeOutOfSolid(character, grid, lookAt);
                m.NudgeCooldown = 1.0;
            }
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

            // Seated pilots cannot EVA — ambient sit-cycle used to re-AttachPilot mid-sortie.
            CrewAmbientPresence.ReleaseFromSeat(character, grid);

            if (m != null)
                m.PoseCooldown = Math.Max(0, m.PoseCooldown - dt);

            StabilizeEvaPose(m, character, grid, false);

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

            // Ease cruise near the point; accelerate velocity so turns aren't sharp.
            double arrive = Math.Max(2.0, CrewConfig.RepairEvaArriveMeters * 2.0);
            double ease = dist < arrive ? Math.Max(0.2, dist / arrive) : 1.0;
            Vector3D desiredVel = dir * (speed * ease);

            Vector3D vel = (m != null && m.HasFlyVel)
                ? new Vector3D(m.VelX, m.VelY, m.VelZ)
                : desiredVel * 0.35;

            Vector3D delta = desiredVel - vel;
            double maxDelta = CrewConfig.RepairEvaAccelMeters * dt;
            double dLen = delta.Length();
            if (dLen > maxDelta && dLen > 0.0001)
                delta *= maxDelta / dLen;
            vel += delta;

            // Don't overshoot the waypoint.
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

            // Face along travel (or toward target when nearly stopped).
            Vector3D faceDir = vel.LengthSquared() > 0.15 ? vel : dir;
            Vector3D up = EvaUp(grid, character);
            Vector3D fwd = BlendFacing(m, character, grid, faceDir, dt);

            try
            {
                character.WorldMatrix = MatrixD.CreateWorld(next, fwd, up);
            }
            catch
            {
                try { character.SetPosition(next); }
                catch { return; }
            }

            BindEvaPhysics(character, grid);
        }

        private static void InvalidateWorkCache()
        {
            WorkCacheFrame = -9999;
            WorkCacheGridId = 0;
            WorkCache.Clear();
        }

        private static void EnsureWorkCache(IMyCubeGrid grid)
        {
            if (grid == null) return;
            int frame = 0;
            try
            {
                if (MyAPIGateway.Session != null)
                    frame = MyAPIGateway.Session.GameplayFrameCounter;
            }
            catch { }

            if (WorkCacheGridId == grid.EntityId
                && frame - WorkCacheFrame >= 0
                && frame - WorkCacheFrame < CrewConfig.RepairWorkCacheFrames)
                return;

            WorkCacheGridId = grid.EntityId;
            WorkCacheFrame = frame;
            WorkCache.Clear();

            // 1) Real-grid damage / incomplete / deformation.
            BlockScratch.Clear();
            grid.GetBlocks(BlockScratch);
            for (int i = 0; i < BlockScratch.Count; i++)
            {
                var s = BlockScratch[i];
                if (s == null || s.IsDestroyed || !NeedsRepair(s))
                    continue;
                WorkCache.Add(new CachedWork
                {
                    Cell = s.Position,
                    ProjectorEntityId = 0,
                    Projected = false,
                    World = GetSlimWorld(s, grid)
                });
            }

            // 2) Projector holograms currently legal to build (CanBuild).
            for (int i = 0; i < BlockScratch.Count; i++)
            {
                var fat = BlockScratch[i] != null ? BlockScratch[i].FatBlock : null;
                var proj = fat as IMyProjector;
                if (proj == null || proj.Closed || !proj.IsWorking || !proj.IsProjecting)
                    continue;
                IMyCubeGrid pGrid = proj.ProjectedGrid;
                if (pGrid == null)
                    continue;

                ProjectedScratch.Clear();
                pGrid.GetBlocks(ProjectedScratch);
                for (int j = 0; j < ProjectedScratch.Count; j++)
                {
                    var s = ProjectedScratch[j];
                    if (s == null || s.IsDestroyed)
                        continue;
                    if (!ProjectorCanBuild(proj, s))
                        continue;

                    WorkCache.Add(new CachedWork
                    {
                        Cell = s.Position,
                        ProjectorEntityId = proj.EntityId,
                        Projected = true,
                        World = GetSlimWorld(s, pGrid)
                    });
                }
            }
        }

        private static bool TryPickWorkTarget(
            IMyCubeGrid grid,
            Vector3D from,
            string selfCrewId,
            out IMySlimBlock best,
            out IMyProjector projector,
            out bool isProjected)
        {
            best = null;
            projector = null;
            isProjected = false;
            if (grid == null) return false;

            EnsureWorkCache(grid);
            long gridId = grid.EntityId;
            double bestScore = double.MaxValue;
            int bestIndex = -1;
            Vector3D center = grid.WorldAABB.Center;

            HashSet<string> selfSkips = null;
            MissionRuntime selfMission;
            if (!string.IsNullOrEmpty(selfCrewId) && ByCrew.TryGetValue(selfCrewId, out selfMission)
                && selfMission != null && selfMission.SkippedTargets.Count > 0)
                selfSkips = selfMission.SkippedTargets;

            // Prefer joining an under-full large real target already claimed by a sibling.
            double bestJoin = double.MaxValue;
            int bestJoinIndex = -1;
            for (int i = 0; i < WorkCache.Count; i++)
            {
                CachedWork w = WorkCache[i];
                if (w.Projected)
                    continue;
                if (selfSkips != null
                    && selfSkips.Contains(SkipTargetKey(w.Cell, w.ProjectorEntityId, w.Projected)))
                    continue;

                IMySlimBlock joinSlim;
                if (!TryResolveCachedWork(grid, w, out joinSlim) || joinSlim == null)
                    continue;
                float maxIntegrity = 0f;
                try { maxIntegrity = joinSlim.MaxIntegrity; }
                catch { }
                if (!CrewRepairShareRules.IsLargeBlock(maxIntegrity, CrewConfig.RepairShareMaxIntegrity))
                    continue;
                if (!CanAffordSlimWork(grid, joinSlim, false))
                    continue;
                if (CountTargetClaimants(gridId, selfCrewId, w.Cell, w.ProjectorEntityId, w.Projected) <= 0)
                    continue;
                if (IsTargetClaimFull(grid, selfCrewId, w, joinSlim))
                    continue;

                double jd = Vector3D.DistanceSquared(w.World, from);
                if (jd < bestJoin)
                {
                    bestJoin = jd;
                    bestJoinIndex = i;
                }
            }

            if (bestJoinIndex >= 0)
            {
                CachedWork join = WorkCache[bestJoinIndex];
                isProjected = false;
                return TryResolveCachedWork(grid, join, out best) && best != null;
            }

            for (int i = 0; i < WorkCache.Count; i++)
            {
                CachedWork w = WorkCache[i];
                if (selfSkips != null
                    && selfSkips.Contains(SkipTargetKey(w.Cell, w.ProjectorEntityId, w.Projected)))
                    continue;

                IMySlimBlock candidate;
                if (!TryResolveCachedWork(grid, w, out candidate) || candidate == null)
                    continue;
                if (IsTargetClaimFull(grid, selfCrewId, w, candidate))
                    continue;
                // Skip cells we cannot pay for — other affordable work should be preferred.
                if (!CanAffordSlimWork(grid, candidate, w.Projected))
                    continue;

                double d = Vector3D.DistanceSquared(w.World, from);
                double hull = Vector3D.DistanceSquared(w.World, center);
                double score = d - hull * 0.05;
                // Prefer real repairs slightly over holograms when scores are close.
                if (!w.Projected)
                    score -= 2.0;
                if (score < bestScore)
                {
                    bestScore = score;
                    bestIndex = i;
                }
            }

            if (bestIndex < 0)
                return false;

            CachedWork pick = WorkCache[bestIndex];
            isProjected = pick.Projected;
            if (!TryResolveCachedWork(grid, pick, out best) || best == null)
                return false;
            if (!pick.Projected)
                return true;
            return TryGetProjector(pick.ProjectorEntityId, out projector) && projector != null;
        }

        private static bool TryGetProjector(long projectorEntityId, out IMyProjector projector)
        {
            projector = null;
            if (projectorEntityId == 0)
                return false;
            IMyEntity pent;
            if (!MyAPIGateway.Entities.TryGetEntityById(projectorEntityId, out pent) || pent == null)
                return false;
            projector = pent as IMyProjector;
            return projector != null;
        }

        private static int CountTargetClaimants(
            long gridId,
            string selfCrewId,
            Vector3I cell,
            long projectorEntityId,
            bool projected)
        {
            int n = 0;
            foreach (var kv in ByCrew)
            {
                if (kv.Value == null)
                    continue;
                if (!string.IsNullOrEmpty(selfCrewId)
                    && string.Equals(kv.Key, selfCrewId, StringComparison.Ordinal))
                    continue;
                MissionRuntime m = kv.Value;
                if (m.GridEntityId != gridId || !m.HasTargetCell)
                    continue;
                if (m.TargetIsProjected != projected)
                    continue;
                if (projected && m.ProjectorEntityId != projectorEntityId)
                    continue;
                if (m.TargetCell == cell)
                    n++;
            }
            return n;
        }

        /// <summary>
        /// True when other work exists on the cache that this crew could consider instead of exclude.
        /// </summary>
        private static bool WorkCacheHasOtherPickable(
            IMyCubeGrid grid,
            string selfCrewId,
            Vector3I excludeCell,
            long excludeProjectorId,
            bool excludeProjected)
        {
            if (grid == null)
                return false;
            EnsureWorkCache(grid);
            HashSet<string> selfSkips = null;
            MissionRuntime selfMission;
            if (!string.IsNullOrEmpty(selfCrewId) && ByCrew.TryGetValue(selfCrewId, out selfMission)
                && selfMission != null && selfMission.SkippedTargets.Count > 0)
                selfSkips = selfMission.SkippedTargets;

            for (int i = 0; i < WorkCache.Count; i++)
            {
                CachedWork w = WorkCache[i];
                if (w.Projected == excludeProjected
                    && w.Cell == excludeCell
                    && w.ProjectorEntityId == excludeProjectorId)
                    continue;
                if (selfSkips != null
                    && selfSkips.Contains(SkipTargetKey(w.Cell, w.ProjectorEntityId, w.Projected)))
                    continue;
                IMySlimBlock slim;
                if (!TryResolveCachedWork(grid, w, out slim) || slim == null)
                    continue;
                if (!CanAffordSlimWork(grid, slim, w.Projected))
                    continue;
                return true;
            }
            return false;
        }

        private static int MaxSlotsForCachedWork(
            IMyCubeGrid grid,
            string selfCrewId,
            CachedWork w,
            IMySlimBlock slim)
        {
            float maxIntegrity = 0f;
            try { if (slim != null) maxIntegrity = slim.MaxIntegrity; }
            catch { }
            bool onlyRemaining = !WorkCacheHasOtherPickable(
                grid, selfCrewId, w.Cell, w.ProjectorEntityId, w.Projected);
            return CrewRepairShareRules.MaxClaimSlots(
                w.Projected,
                maxIntegrity,
                CrewConfig.RepairShareMaxWelders,
                CrewConfig.RepairShareMaxIntegrity,
                onlyRemaining);
        }

        private static bool IsTargetClaimFull(
            IMyCubeGrid grid,
            string selfCrewId,
            CachedWork w,
            IMySlimBlock slim)
        {
            if (grid == null)
                return true;
            int claimants = CountTargetClaimants(
                grid.EntityId, selfCrewId, w.Cell, w.ProjectorEntityId, w.Projected);
            int max = MaxSlotsForCachedWork(grid, selfCrewId, w, slim);
            return CrewRepairShareRules.IsClaimFull(claimants, max);
        }

        /// <summary>
        /// Prefer strict (havok) CanBuild; fall back to non-havok so claim / resolve / Build agree.
        /// </summary>
        private static bool ProjectorCanBuild(IMyProjector projector, IMySlimBlock projected)
        {
            if (projector == null || projected == null || projector.Closed)
                return false;
            try
            {
                if (!projector.IsProjecting)
                    return false;
                var check = projector.CanBuild(projected, true);
                if (check != BuildCheckResult.OK)
                    check = projector.CanBuild(projected, false);
                return check == BuildCheckResult.OK;
            }
            catch { return false; }
        }

        private static bool TryBuildProjected(
            IMyProjector projector,
            IMySlimBlock projected,
            IMyCubeGrid hostGrid,
            long ownerId,
            out IMySlimBlock placed)
        {
            placed = null;
            if (projector == null || projected == null || projector.Closed || hostGrid == null)
                return false;
            if (!ProjectorCanBuild(projector, projected))
                return false;

            MyDefinitionId firstCompId;
            if (!TryGetFirstComponentId(projected, out firstCompId))
                return false;

            bool creative = false;
            try { creative = MyAPIGateway.Session != null && MyAPIGateway.Session.CreativeMode; }
            catch { }

            // Survival: pay only the first component, place an incomplete block (no Instant Build),
            // then weld the rest over time at star-scaled speed.
            if (!creative && CountComponentOnGrid(hostGrid, firstCompId) < 1)
                return false;

            try
            {
                projector.Build(projected, ownerId, projector.EntityId, false, ownerId);
            }
            catch { return false; }

            try
            {
                var projGrid = projected.CubeGrid;
                if (projGrid == null)
                    return false;
                Vector3D world = projGrid.GridIntegerToWorld(projected.Position);
                Vector3I cell = hostGrid.WorldToGridInteger(world);
                placed = hostGrid.GetCubeBlock(cell);
            }
            catch { return false; }

            if (placed == null)
                return false;

            // Charge only after a real block exists (failed remap must not eat comps).
            if (!creative && !TryRemoveComponentFromGrid(hostGrid, firstCompId, 1))
            {
                Log("repair project place unpaid first-comp=" + firstCompId.SubtypeName);
                return false;
            }

            return true;
        }

        private static bool TryGetFirstComponentId(IMySlimBlock slim, out MyDefinitionId id)
        {
            id = default(MyDefinitionId);
            if (slim == null) return false;
            try
            {
                var def = slim.BlockDefinition as MyCubeBlockDefinition;
                if (def != null && def.Components != null && def.Components.Length > 0
                    && def.Components[0].Definition != null)
                {
                    id = def.Components[0].Definition.Id;
                    return true;
                }
            }
            catch { }

            MissingCompScratch.Clear();
            try { slim.GetMissingComponents(MissingCompScratch); }
            catch { return false; }
            foreach (var kv in MissingCompScratch)
            {
                if (string.IsNullOrEmpty(kv.Key) || kv.Value <= 0)
                    continue;
                id = new MyDefinitionId(typeof(MyObjectBuilder_Component), kv.Key);
                return true;
            }
            return false;
        }

        /// <summary>
        /// Inventories on the host grid plus physically connected grids that share ownership
        /// (connectors / landing gear / mechanical links) so docked ships can use base cargo.
        /// </summary>
        private static void CollectGridInventories(IMyCubeGrid grid, List<IMyInventory> into)
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

        private static IMyInventory FindFeederInventory(
            List<IMyInventory> inventories,
            Dictionary<string, int> missing)
        {
            if (inventories == null || missing == null || missing.Count == 0)
                return null;
            for (int i = 0; i < inventories.Count; i++)
            {
                var inv = inventories[i];
                if (inv == null) continue;
                foreach (var kv in missing)
                {
                    if (kv.Value <= 0 || string.IsNullOrEmpty(kv.Key))
                        continue;
                    var id = new MyDefinitionId(typeof(MyObjectBuilder_Component), kv.Key);
                    try
                    {
                        if (inv.GetItemAmount(id) > 0)
                            return inv;
                    }
                    catch { }
                }
            }
            return null;
        }

        /// <summary>
        /// Return unused construction-stockpile items to grid cargo (prevents vacuumed comps being lost).
        /// </summary>
        private static void RefundBlockStockpile(IMySlimBlock slim, IMyCubeGrid grid)
        {
            if (slim == null || grid == null)
                return;
            CollectGridInventories(grid, InvScratch);
            for (int i = 0; i < InvScratch.Count; i++)
            {
                var inv = InvScratch[i];
                if (inv == null) continue;
                try { slim.MoveItemsFromConstructionStockpile(inv); }
                catch { }
            }
        }

        private static int CountComponentOnGrid(IMyCubeGrid grid, MyDefinitionId id)
        {
            CollectGridInventories(grid, InvScratch);
            MyFixedPoint total = 0;
            for (int i = 0; i < InvScratch.Count; i++)
            {
                try { total += InvScratch[i].GetItemAmount(id); }
                catch { }
            }
            return (int)total;
        }

        private static bool TryRemoveComponentFromGrid(IMyCubeGrid grid, MyDefinitionId id, int amount)
        {
            if (amount <= 0) return true;
            if (CountComponentOnGrid(grid, id) < amount)
                return false;

            int left = amount;
            CollectGridInventories(grid, InvScratch);
            for (int i = 0; i < InvScratch.Count && left > 0; i++)
            {
                var inv = InvScratch[i];
                MyFixedPoint have;
                try { have = inv.GetItemAmount(id); }
                catch { continue; }
                if (have <= 0) continue;

                int take = have < left ? (int)have : left;
                try
                {
                    var ob = MyObjectBuilderSerializer.CreateNewObject(id) as MyObjectBuilder_PhysicalObject;
                    if (ob == null)
                        continue;
                    // ModAPI RemoveItemsOfType is void — success measured by amount delta.
                    inv.RemoveItemsOfType((MyFixedPoint)take, ob, false);
                }
                catch { continue; }

                MyFixedPoint after;
                try { after = inv.GetItemAmount(id); }
                catch { continue; }
                int got = (int)(have - after);
                if (got > 0)
                    left -= got;
            }
            return left <= 0;
        }

        private static bool TryResolveTarget(MissionRuntime m, IMyCubeGrid grid, out IMySlimBlock slim)
        {
            slim = null;
            if (m == null || grid == null) return false;

            if (m.TargetIsProjected)
            {
                IMyProjector projector = GetMissionProjector(m);
                if (projector == null || projector.ProjectedGrid == null)
                    return false;
                if (m.HasTargetCell)
                {
                    slim = projector.ProjectedGrid.GetCubeBlock(m.TargetCell);
                    if (slim == null) return false;
                    return ProjectorCanBuild(projector, slim);
                }
                return false;
            }

            if (m.TargetBlockEntityId != 0)
            {
                IMyEntity ent;
                if (MyAPIGateway.Entities.TryGetEntityById(m.TargetBlockEntityId, out ent) && ent != null)
                {
                    var block = ent as IMyCubeBlock;
                    if (block != null && block.CubeGrid != null && block.CubeGrid.EntityId == grid.EntityId)
                    {
                        slim = block.SlimBlock;
                        return slim != null;
                    }
                }
            }
            if (m.HasTargetCell)
            {
                slim = grid.GetCubeBlock(m.TargetCell);
                return slim != null;
            }
            return false;
        }

        private static bool NeedsRepair(IMySlimBlock slim)
        {
            if (slim == null || slim.IsDestroyed) return false;
            try
            {
                // Tight thresholds — 0.999 ratio / 0.1 integrity slack left blocks visually at ~99%.
                if (slim.Integrity < slim.MaxIntegrity - 0.01f)
                    return true;
                if (slim.BuildLevelRatio < 0.9999f)
                    return true;
            }
            catch { }

            // Still-missing recipe comps count even when integrity/ratio look "done".
            try
            {
                MissingCompScratch.Clear();
                slim.GetMissingComponents(MissingCompScratch);
                if (MissingCompScratch.Count > 0)
                    return true;
            }
            catch { }

            // Via IMySlimBlock (Ingame) — casting to MySlimBlock is sandbox-prohibited.
            return BlockHasDeformation(slim);
        }

        /// <summary>
        /// Armor projections / bone damage. Must use the interface property, not MySlimBlock.
        /// </summary>
        private static bool BlockHasDeformation(IMySlimBlock slim)
        {
            if (slim == null) return false;
            try
            {
                if (slim.HasDeformation)
                    return true;
            }
            catch { }
            try
            {
                // Nanobot: HasDeformation can be false while MaxDeformation still reports dents.
                if (slim.MaxDeformation > 0.01f)
                    return true;
            }
            catch { }
            return false;
        }

        /// <summary>
        /// Manufacturing &amp; Maintenance Projector pattern: FixBones(0, 10).
        /// oldDamage=0 forces Keen's ResetBlockSkeleton path for armor projections.
        /// </summary>
        private static void ApplyProjectionFix(IMySlimBlock slim)
        {
            if (slim == null)
                return;
            try
            {
                if (!slim.HasDeformation)
                    return;
            }
            catch
            {
                try
                {
                    if (slim.MaxDeformation <= 0.01f)
                        return;
                }
                catch { return; }
            }

            try { slim.FixBones(0f, 10f); }
            catch { }

            try { slim.UpdateVisual(); }
            catch { }
        }

        private static Vector3D GetSlimWorld(IMySlimBlock slim, IMyCubeGrid grid)
        {
            if (slim == null) return Vector3D.Zero;
            if (slim.FatBlock != null)
                return slim.FatBlock.GetPosition();
            // Prefer the slim's own grid (projected holograms live on ProjectedGrid).
            IMyCubeGrid g = slim.CubeGrid != null ? slim.CubeGrid : grid;
            if (g == null) return Vector3D.Zero;
            return g.GridIntegerToWorld(slim.Position);
        }

        /// <summary>
        /// World AABB of the slim (block cubes when available). Large thrusters dwarf a
        /// center-point + 5 m sphere — range/hover must use the real bounds.
        /// </summary>
        private static bool TryGetSlimWorldBox(IMySlimBlock slim, IMyCubeGrid grid, out BoundingBoxD box)
        {
            box = default(BoundingBoxD);
            if (slim == null)
                return false;
            try
            {
                slim.GetWorldBoundingBox(out box, true);
                if (box.Max.X >= box.Min.X && box.Max.Y >= box.Min.Y && box.Max.Z >= box.Min.Z)
                    return true;
            }
            catch { }
            try
            {
                if (slim.FatBlock != null)
                {
                    box = slim.FatBlock.WorldAABB;
                    return true;
                }
            }
            catch { }

            Vector3D c = GetSlimWorld(slim, grid);
            double half = 1.25;
            try
            {
                if (grid != null && grid.GridSizeEnum == MyCubeSize.Small)
                    half = 0.25;
            }
            catch { }
            box = new BoundingBoxD(c - new Vector3D(half), c + new Vector3D(half));
            return true;
        }

        private static bool IsInWeldRangeOfSlim(Vector3D pos, IMySlimBlock slim, IMyCubeGrid grid)
        {
            BoundingBoxD box;
            if (!TryGetSlimWorldBox(slim, grid, out box))
                return false;
            // Inside the fat volume → clipping/glitching; force a surface hover instead.
            try
            {
                if (box.Contains(pos) != ContainmentType.Disjoint)
                    return false;
            }
            catch { }
            return CrewRepairShareRules.IsWithinDistanceOfAabb(
                pos.X, pos.Y, pos.Z,
                box.Min.X, box.Min.Y, box.Min.Z,
                box.Max.X, box.Max.Y, box.Max.Z,
                CrewConfig.RepairWeldRangeMeters);
        }

        /// <summary>Hover just outside the block AABB facing the approach direction.</summary>
        private static Vector3D ComputeSlimSurfaceHover(
            IMySlimBlock slim,
            IMyCubeGrid grid,
            Vector3D fromPos,
            float standOff)
        {
            BoundingBoxD box;
            if (!TryGetSlimWorldBox(slim, grid, out box))
            {
                Vector3D c = GetSlimWorld(slim, grid);
                Vector3D o = fromPos - c;
                if (o.LengthSquared() < 0.01)
                    o = grid != null ? grid.WorldMatrix.Forward : Vector3D.Forward;
                o.Normalize();
                return c + o * standOff;
            }

            Vector3D center = box.Center;
            Vector3D dir = fromPos - center;
            if (dir.LengthSquared() < 0.01)
                dir = grid != null ? (center - grid.WorldAABB.Center) : Vector3D.Forward;
            if (dir.LengthSquared() < 0.01)
                dir = grid != null ? grid.WorldMatrix.Forward : Vector3D.Forward;
            dir.Normalize();

            // Ray from center through approach dir → AABB surface, then stand off.
            Vector3D halfExt = (box.Max - box.Min) * 0.5;
            Vector3D far = center + dir * (halfExt.Length() * 2.0 + standOff + 2.0);
            Vector3D surface;
            if (!TryAabbExitToward(box, center, far, out surface))
                surface = ClosestPointOnAabbSurface(box, fromPos);
            Vector3D outward = AabbFaceOutwardNormal(box, surface);
            if (Vector3D.Dot(outward, dir) < 0.2)
                outward = dir;
            outward.Normalize();
            return surface + outward * standOff;
        }

        private static bool TryWeldTick(IMySlimBlock slim, IMyCubeGrid grid, long welderOwnerId, float weldSeconds)
        {
            if (slim == null || grid == null || weldSeconds <= 0f)
                return false;

            bool hadDeform = BlockHasDeformation(slim);
            bool fullIntegrity = false;
            float before = 0f;
            float beforeRatio = 0f;
            float beforeDeform = 0f;
            try
            {
                before = slim.Integrity;
                beforeRatio = slim.BuildLevelRatio;
                beforeDeform = slim.MaxDeformation;
                fullIntegrity = slim.Integrity >= slim.MaxIntegrity - 0.1f;
            }
            catch { }

            // M&M: fix projections first (works at full integrity; no components required).
            if (hadDeform)
                ApplyProjectionFix(slim);

            bool deformProgressed = false;
            try
            {
                if (hadDeform && !BlockHasDeformation(slim))
                    deformProgressed = true;
                else if (beforeDeform > 0.01f && slim.MaxDeformation < beforeDeform - 0.001f)
                    deformProgressed = true;
            }
            catch { }

            // Full-integrity dents: only count real FixBones progress (never fake-success).
            if (hadDeform && fullIntegrity)
                return deformProgressed;

            // Feed the construction stockpile then let Keen mount from it (same as a hand welder).
            // Passing null with no stockpile skips steel plates / late comps on large blocks.
            MissingCompScratch.Clear();
            bool needsComps = false;
            try
            {
                slim.GetMissingComponents(MissingCompScratch);
                needsComps = MissingCompScratch.Count > 0;
            }
            catch { needsComps = true; }

            bool creative = false;
            try { creative = MyAPIGateway.Session != null && MyAPIGateway.Session.CreativeMode; }
            catch { }

            IMyInventory weldInv = null;
            if (needsComps && !creative)
            {
                bool cargoHas = HasAnyMissingComponentInCargo(grid, MissingCompScratch);
                if (cargoHas)
                    FeedConstructionStockpile(slim, grid);

                bool stockHas = StockpileHasAnyMissing(slim, MissingCompScratch);
                // Do not abort when cargo looks empty — plates may already sit on the stockpile.
                if (!cargoHas && !stockHas)
                    return false;

                // Keen's "outputInventory" arg is also a component SOURCE. After Feed empties
                // cargo into the stockpile, passing that emptied inv stalls late plate stages
                // on large blocks. Prefer null so mounts come from the construction stockpile;
                // only pass an inv that still holds missing comps (other containers).
                weldInv = FindInventoryWithMissingComps(grid, MissingCompScratch);
            }

            bool progressed = deformProgressed;
            try
            {
                // weldSeconds is Keen "welder seconds" (multiplied by IntegrityPointsPerSec internally).
                slim.IncreaseMountLevel(
                    weldSeconds,
                    welderOwnerId,
                    weldInv,
                    0f,
                    false,
                    MyOwnershipShareModeEnum.Faction);
            }
            catch { }

            // After integrity weld, clear any remaining dent the same way M&M does.
            if (BlockHasDeformation(slim))
                ApplyProjectionFix(slim);

            try
            {
                // Huge MaxIntegrity blocks move ratio/absolute integrity very slowly per tick.
                if (slim.Integrity > before + 0.0001f || slim.BuildLevelRatio > beforeRatio)
                    progressed = true;
                if (beforeDeform > 0.01f && slim.MaxDeformation < beforeDeform - 0.001f)
                    progressed = true;
                if (hadDeform && !BlockHasDeformation(slim))
                    progressed = true;
            }
            catch { }

            return progressed;
        }

        private static IMyInventory FindInventoryWithMissingComps(
            IMyCubeGrid grid,
            Dictionary<string, int> missing)
        {
            if (grid == null || missing == null || missing.Count == 0)
                return null;
            CollectGridInventories(grid, InvScratch);
            for (int i = 0; i < InvScratch.Count; i++)
            {
                var inv = InvScratch[i];
                if (inv == null)
                    continue;
                foreach (var kv in missing)
                {
                    if (kv.Value <= 0 || string.IsNullOrEmpty(kv.Key))
                        continue;
                    var id = new MyDefinitionId(typeof(MyObjectBuilder_Component), kv.Key);
                    try
                    {
                        if (inv.GetItemAmount(id) > 0)
                            return inv;
                    }
                    catch { }
                }
            }
            // null = mount from construction stockpile only (do not pass an emptied cargo inv).
            return null;
        }

        private static bool TryGetCharacter(CrewRecord crew, out IMyCharacter character)
        {
            character = null;
            if (crew == null || !crew.CharacterEntityId.HasValue)
                return false;
            IMyEntity ent;
            if (!MyAPIGateway.Entities.TryGetEntityById(crew.CharacterEntityId.Value, out ent)
                || ent == null
                || ent.Closed)
                return false;
            character = ent as IMyCharacter;
            return character != null;
        }

        private static void NotifyOutOfComponents(CrewSession session, MissionRuntime m, CrewRecord crew)
        {
            if (m != null && m.NotifiedOutOfComps)
                return;
            if (m != null)
                m.NotifiedOutOfComps = true;
            string name = CrewDisplayName(crew);
            NotifyOwner(session, crew, name + " Ran out of components, Boss.");
        }

        private static string CrewDisplayName(CrewRecord crew)
        {
            if (crew == null)
                return "Construction";
            if (!string.IsNullOrEmpty(crew.DisplayName))
                return crew.DisplayName;
            return CrewConfig.RoleLabel(crew.Role);
        }

        private static void NotifyOwner(CrewSession session, CrewRecord crew, string text)
        {
            if (session == null)
                return;
            session.NotifyCrewOwners(crew, text);
        }

        private static void Log(string msg)
        {
            try { MyLog.Default.WriteLineAndConsole("[HireCrew] " + msg); }
            catch { }
        }
    }
}
