using System;
using System.Collections.Generic;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Character.Components;
using Sandbox.ModAPI;
using VRage;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.Game.ModAPI.Interfaces;
using VRage.ModAPI;
using VRage.Utils;
using VRageMath;

namespace HireCrew
{
    /// <summary>
    /// Nearby-only ambient Astronaut bots for seated crew. Gameplay stays logical.
    /// HireCrew drives sit/stand (AttachPilot skips SitInSeat duration) and walks bots
    /// toward seat-neighborhood waypoints. OB characters use physics velocity (no controller);
    /// SpawnBot characters can use MoveAndRotate when available.
    /// </summary>
    public static class CrewAmbientPresence
    {
        private enum AmbientPhase
        {
            Seated = 0,
            Wandering = 1
        }

        private struct AmbientRuntime
        {
            public AmbientPhase Phase;
            public float PhaseSecondsLeft;
            public Vector3D Waypoint;
            public bool HasWaypoint;
            /// <summary>Seconds until another approach greeting is allowed.</summary>
            public float GreetCooldownLeft;
            /// <summary>Seconds left facing the player during a wave greeting.</summary>
            public float GreetFaceLeft;
            /// <summary>Seconds until the next ambient idle emote.</summary>
            public float IdleEmoteCooldownLeft;
            /// <summary>Seconds left holding still for an idle emote.</summary>
            public float IdleEmoteHoldLeft;
            /// <summary>Seconds left in the current walk/run gait segment.</summary>
            public float GaitSecondsLeft;
            /// <summary>True = brief hurry (current top speed); false = normal stroll.</summary>
            public bool GaitRunning;
        }

        private static readonly Dictionary<string, float> FarSecondsByCrewId = new Dictionary<string, float>();
        private static readonly Dictionary<string, AmbientRuntime> RuntimeByCrewId = new Dictionary<string, AmbientRuntime>();
        private static readonly Dictionary<string, float> SpawnCooldownByCrewId = new Dictionary<string, float>();
        private static readonly List<IMyPlayer> PlayerScratch = new List<IMyPlayer>();
        private static readonly HashSet<long> KnownCharacterIds = new HashSet<long>();
        private static readonly Dictionary<string, CrewBotControllers.ControlInfo> ControlByCrewId
            = new Dictionary<string, CrewBotControllers.ControlInfo>();
        private static readonly Random Rng = new Random();
        private static string _resolvedBotSubtype;
        /// <summary>Usual stroll (most of the time).</summary>
        private const float AmbientWalkSpeedMeters = 0.85f;
        private const float AmbientWalkInputScale = 0.22f;
        /// <summary>Occasional hurry — previous default pace.</summary>
        private const float AmbientRunSpeedMeters = 1.55f;
        private const float AmbientRunInputScale = 0.42f;
        private const float AmbientWalkGaitMinSeconds = 14f;
        private const float AmbientWalkGaitMaxSeconds = 34f;
        private const float AmbientRunGaitMinSeconds = 2.2f;
        private const float AmbientRunGaitMaxSeconds = 5.5f;
        /// <summary>Chance to start a short run when a walk segment ends.</summary>
        private const double AmbientRunGaitChance = 0.18;
        private const float AmbientSimStepSeconds = 1f / 60f;
        private const float SpawnFailCooldownSeconds = 15f;
        /// <summary>Inset from grid LocalAABB so bots stay on structure, not the AABB skin.</summary>
        private const float AmbientGridBoundMarginMeters = 0.85f;
        private const float GreetingTriggerMeters = 3f;
        private const float GreetingFaceSeconds = 2.8f;
        private const float GreetingCooldownSeconds = 40f;
        private const float IdleEmoteHoldSeconds = 2.4f;
        private const float IdleEmoteCooldownMinSeconds = 22f;
        private const float IdleEmoteCooldownMaxSeconds = 48f;

        /// <summary>
        /// Calm crew-appropriate emotes (vanilla / AiEnabled CrewAnimations subset).
        /// Avoids dances, taunts, and aggressive gestures.
        /// </summary>
        private static readonly string[] IdleEmoteNames =
        {
            "LookingAround",
            "Stretching",
            "CheckWrist",
            "PointForward",
            "PointLeft",
            "PointRight",
            "Thumb-Up",
        };

        private static readonly string[] GreetingEmoteNames =
        {
            "Wave",
            "Wave",
            "Wave",
            "Thumb-Up",
        };

        public static void ClearRuntime()
        {
            FarSecondsByCrewId.Clear();
            RuntimeByCrewId.Clear();
            SpawnCooldownByCrewId.Clear();
            KnownCharacterIds.Clear();
            ControlByCrewId.Clear();
            CrewBotControllers.Clear();
            _resolvedBotSubtype = null;
        }

        /// <summary>Every-frame wander steering for ambient bots in Wandering phase.</summary>
        public static void UpdateMovement(CrewSession session)
        {
            if (session == null || session.Store == null)
                return;
            if (!CrewConfig.AmbientEnabled)
                return;
            if (MyAPIGateway.Multiplayer == null || !MyAPIGateway.Multiplayer.IsServer)
                return;

            foreach (var crew in session.Store.All)
            {
                if (crew == null || string.IsNullOrEmpty(crew.CrewId))
                    continue;
                if (crew.Status != CrewStatus.Seated || !crew.CharacterEntityId.HasValue)
                    continue;
                if (CrewRepairMission.IsCrewOnMission(crew.CrewId)
                    || CrewSalvageMission.IsCrewOnMission(crew.CrewId))
                    continue;

                AmbientRuntime runtime;
                if (!RuntimeByCrewId.TryGetValue(crew.CrewId, out runtime))
                    continue;
                if (runtime.Phase != AmbientPhase.Wandering)
                    continue;

                IMyCharacter character;
                if (!TryGetLiveCharacter(crew, out character) || character == null)
                    continue;

                IMyEntity seatEnt;
                if (!crew.SeatEntityId.HasValue
                    || !MyAPIGateway.Entities.TryGetEntityById(crew.SeatEntityId.Value, out seatEnt))
                    continue;
                var seat = seatEnt as IMyTerminalBlock;
                if (seat == null)
                    continue;
                // Tick will despawn; don't keep walking while under way.
                if (!IsGridIdle(seat.CubeGrid))
                    continue;

                if (runtime.GreetCooldownLeft > 0f)
                    runtime.GreetCooldownLeft = Math.Max(0f, runtime.GreetCooldownLeft - AmbientSimStepSeconds);

                // Uncontrolled OB characters do not receive art-gravity from the game loop.
                ApplyAmbientPhysics(character, seat.CubeGrid);
                ForceStandingPose(character);

                if (UpdateGreeting(ref runtime, character, seat))
                {
                    RuntimeByCrewId[crew.CrewId] = runtime;
                    continue;
                }

                if (UpdateIdleEmote(ref runtime, character, seat))
                {
                    RuntimeByCrewId[crew.CrewId] = runtime;
                    continue;
                }

                TickGait(ref runtime);

                if (!runtime.HasWaypoint
                    || Vector3D.DistanceSquared(character.GetPosition(), runtime.Waypoint) < 0.55 * 0.55
                    || !IsInsideGridBounds(seat.CubeGrid, runtime.Waypoint, AmbientGridBoundMarginMeters))
                {
                    runtime.Waypoint = PickNeighborhoodWaypoint(seat);
                    runtime.HasWaypoint = true;
                }

                if (!SteerToward(character, runtime.Waypoint, seat.CubeGrid, seat, runtime.GaitRunning))
                {
                    // Step would leave the grid — stop and pick an in-bounds waypoint next frame.
                    runtime.HasWaypoint = false;
                }
                RuntimeByCrewId[crew.CrewId] = runtime;
            }
        }

        /// <summary>Server tick (~1 Hz). Spawns/despawns and soft-recovers ambient bots.</summary>
        public static void Tick(CrewSession session)
        {
            if (session == null || session.Store == null)
                return;
            if (!CrewConfig.AmbientEnabled)
                return;
            if (MyAPIGateway.Multiplayer == null || !MyAPIGateway.Multiplayer.IsServer)
                return;

            // Controllers often arrive a second after first ambient OB spawn.
            TryAttachPendingControls(session);

            var dirtyGrids = new HashSet<long>();
            RebuildKnownCharacterIds(session.Store);

            int globalLive = CountLiveBots(session.Store, 0);
            var perGridLive = new Dictionary<long, int>();

            var snapshot = new List<CrewRecord>(session.Store.All);
            foreach (var crew in snapshot)
            {
                if (crew == null || string.IsNullOrEmpty(crew.CrewId))
                    continue;
                if (crew.Status != CrewStatus.Seated || !crew.SeatEntityId.HasValue)
                {
                    if (DespawnBot(session, crew, dirtyGrids))
                        ClearCrewRuntime(crew.CrewId);
                    continue;
                }

                IMyEntity seatEnt;
                if (!MyAPIGateway.Entities.TryGetEntityById(crew.SeatEntityId.Value, out seatEnt)
                    || seatEnt == null || seatEnt.Closed)
                {
                    if (DespawnBot(session, crew, dirtyGrids))
                        ClearCrewRuntime(crew.CrewId);
                    continue;
                }

                var seat = seatEnt as IMyTerminalBlock;
                if (seat == null || seat.CubeGrid == null)
                {
                    if (DespawnBot(session, crew, dirtyGrids))
                        ClearCrewRuntime(crew.CrewId);
                    continue;
                }

                long gridId = seat.CubeGrid.EntityId;
                Vector3D seatPos = seat.WorldMatrix.Translation;
                bool playerNear = IsAnyPlayerNear(seatPos, CrewConfig.AmbientProximityMeters);
                bool gridIdle = IsGridIdle(seat.CubeGrid);

                IMyCharacter character;
                bool hasLive = TryGetLiveCharacter(crew, out character);
                bool onMission = CrewRepairMission.IsCrewOnMission(crew.CrewId)
                    || CrewSalvageMission.IsCrewOnMission(crew.CrewId);
                // Unexpected body loss (player kill / world removal) — permanent crew loss.
                // Intentional DespawnBot clears CharacterEntityId first, so it never hits this path.
                // Mid-sortie EVA clip/vanish: clear body and respawn — keep the hire.
                if (crew.CharacterEntityId.HasValue && !hasLive)
                {
                    if (CrewConfig.PermanentLossOnUnexpectedBodyGone(onMission))
                        session.HandleCrewBotKilled(crew);
                    else
                    {
                        Log("mission body gone — respawn crew=" + crew.CrewId);
                        DespawnBot(session, crew, dirtyGrids);
                    }
                    ClearCrewRuntime(crew.CrewId);
                    continue;
                }
                if (hasLive && IsBotDead(character))
                {
                    if (CrewConfig.PermanentLossOnUnexpectedBodyGone(onMission))
                    {
                        session.HandleCrewBotKilled(crew);
                        ClearCrewRuntime(crew.CrewId);
                    }
                    else
                    {
                        Log("mission body dead — despawn/respawn crew=" + crew.CrewId);
                        DespawnBot(session, crew, dirtyGrids);
                        ClearCrewRuntime(crew.CrewId);
                    }
                    continue;
                }

                // Ambient presence is station/parked only — despawn while the grid is under way.
                // Active EVA sorties keep their bodies (mission will recall/abort if needed).
                if (!gridIdle && !onMission)
                {
                    if (hasLive && DespawnBot(session, crew, dirtyGrids))
                        ClearCrewRuntime(crew.CrewId);
                    continue;
                }

                if (!playerNear && !onMission)
                {
                    if (hasLive && DespawnBot(session, crew, dirtyGrids))
                        ClearCrewRuntime(crew.CrewId);
                    continue;
                }

                if (!hasLive)
                {
                    if (CrewStationLogic.IsSeatOccupiedByPlayer(seat))
                        continue;

                    float cool;
                    if (SpawnCooldownByCrewId.TryGetValue(crew.CrewId, out cool) && cool > 0f)
                    {
                        // Damage Control sorties need bodies immediately — don't stall on ambient cooldown.
                        if (!onMission)
                        {
                            SpawnCooldownByCrewId[crew.CrewId] = cool - 1f;
                            continue;
                        }
                        SpawnCooldownByCrewId.Remove(crew.CrewId);
                    }

                    int gridLive;
                    if (!perGridLive.TryGetValue(gridId, out gridLive))
                    {
                        gridLive = CountLiveBots(session.Store, gridId);
                        perGridLive[gridId] = gridLive;
                    }

                    if (globalLive >= CrewConfig.AmbientMaxLiveBotsGlobal)
                        continue;
                    if (gridLive >= CrewConfig.AmbientMaxLiveBotsPerGrid)
                        continue;

                    if (TrySpawnAndSeat(session, crew, seat, dirtyGrids))
                    {
                        globalLive++;
                        perGridLive[gridId] = gridLive + 1;
                        FarSecondsByCrewId.Remove(crew.CrewId);
                        SpawnCooldownByCrewId.Remove(crew.CrewId);
                        // Cockpits start seated; crew stations cannot AttachPilot — wander immediately.
                        // Mission EVA ejects on first fly tick.
                        bool canSeat = seat is IMyCockpit && !onMission;
                        BeginPhase(crew.CrewId, canSeat ? AmbientPhase.Seated : AmbientPhase.Wandering, seat);
                        if (onMission)
                        {
                            IMyCharacter spawned;
                            if (TryGetLiveCharacter(crew, out spawned) && spawned != null)
                                ReleaseFromSeat(spawned, seat.CubeGrid);
                        }
                    }
                    else
                    {
                        // Short wait while controller pool fills; longer on hard spawn failures.
                        SpawnCooldownByCrewId[crew.CrewId] = CrewBotControllers.PoolCount == 0 ? 2f : SpawnFailCooldownSeconds;
                    }
                    continue;
                }

                // Mission owns pose/flight — do not re-seat, wander, or snap-home mid-EVA.
                if (onMission)
                {
                    FarSecondsByCrewId.Remove(crew.CrewId);
                    continue;
                }

                // AttachPilot bypasses SitInSeat(duration) — drive sit/stand so bots leave seats.
                TickSitWanderCycle(crew, seat, character);

                // Soft neighborhood / wrong-seat recovery while AstronautBehavior runs.
                if (NeedsRecovery(seat, character))
                {
                    float far;
                    if (!FarSecondsByCrewId.TryGetValue(crew.CrewId, out far))
                        far = 0f;
                    far += 1f;
                    FarSecondsByCrewId[crew.CrewId] = far;

                    if (far >= CrewConfig.AmbientRecoverTimeoutSeconds)
                    {
                        if (TryRecoverHome(session, crew, seat, character, dirtyGrids))
                            BeginPhase(crew.CrewId, AmbientPhase.Seated, seat);
                        else
                        {
                            DespawnBot(session, crew, dirtyGrids);
                            ClearCrewRuntime(crew.CrewId);
                        }
                        FarSecondsByCrewId.Remove(crew.CrewId);
                    }
                }
                else
                {
                    FarSecondsByCrewId.Remove(crew.CrewId);
                }
            }

            foreach (var gridId in dirtyGrids)
                session.NotifyAmbientRosterChanged(gridId);
        }

        /// <summary>Remove live bot for a crew record (dismiss / unassign / integrity).</summary>
        public static bool DespawnCrewBot(CrewSession session, CrewRecord crew, bool notify = true)
        {
            if (crew == null)
                return false;
            var dirty = new HashSet<long>();
            bool changed = DespawnBot(session, crew, dirty);
            if (changed)
            {
                ClearCrewRuntime(crew.CrewId);
                if (notify && session != null)
                {
                    foreach (var gridId in dirty)
                        session.NotifyAmbientRosterChanged(gridId);
                }
            }
            return changed;
        }

        public static void DespawnAll(CrewSession session)
        {
            if (session == null || session.Store == null)
                return;
            var dirty = new HashSet<long>();
            foreach (var crew in new List<CrewRecord>(session.Store.All))
            {
                if (crew != null)
                    DespawnBot(session, crew, dirty);
            }
            ClearRuntime();
            // No roster broadcast on unload — session is tearing down.
        }

        /// <summary>
        /// Close ambient bodies before world save so they are not written into the sandbox.
        /// Keeps the harvested controller pool for post-save respawn.
        /// </summary>
        public static void DespawnAllForSave(CrewSession session)
        {
            if (session == null || session.Store == null)
                return;
            var dirty = new HashSet<long>();
            foreach (var crew in new List<CrewRecord>(session.Store.All))
            {
                if (crew != null)
                    DespawnBot(session, crew, dirty);
            }
            FarSecondsByCrewId.Clear();
            RuntimeByCrewId.Clear();
            SpawnCooldownByCrewId.Clear();
            KnownCharacterIds.Clear();
            ControlByCrewId.Clear();
            // Intentionally keep CrewBotControllers pool.
            Log("despawned all for save");
        }

        /// <summary>
        /// Close leftover NPC_Astronaut ambient bodies that survived an older save
        /// (CharacterEntityId was cleared on load, so DespawnBot cannot find them).
        /// </summary>
        public static void PurgeOrphanAmbientCharacters()
        {
            if (MyAPIGateway.Multiplayer == null || !MyAPIGateway.Multiplayer.IsServer)
                return;

            var keep = new HashSet<long>();
            // Nothing tracked yet right after RestoreAssignmentsFromStore cleared ids.

            int closed = 0;
            try
            {
                var entities = new HashSet<IMyEntity>();
                MyAPIGateway.Entities.GetEntities(entities, e => e is IMyCharacter);
                foreach (var ent in entities)
                {
                    var character = ent as IMyCharacter;
                    if (character == null || character.Closed)
                        continue;
                    if (keep.Contains(character.EntityId))
                        continue;
                    if (!IsOrphanAmbientCharacter(character))
                        continue;

                    try
                    {
                        RemoveFromAnySeat(character, null);
                        try { MyAPIGateway.Players.RemoveControlledEntity(character); }
                        catch { }
                        character.Close();
                        closed++;
                    }
                    catch { }
                }
            }
            catch (Exception e)
            {
                Log("PurgeOrphanAmbientCharacters failed: " + e.Message);
            }

            if (closed > 0)
                Log("purged orphan ambient characters count=" + closed);
        }

        private static bool IsOrphanAmbientCharacter(IMyCharacter character)
        {
            if (character == null)
                return false;

            try
            {
                string subtype = null;
                try { subtype = character.Definition != null ? character.Definition.Id.SubtypeName : null; }
                catch { }
                if (!string.Equals(subtype, CrewConfig.AmbientCharacterSubtype, StringComparison.OrdinalIgnoreCase))
                    return false;

                // Skip characters currently seated/controlled by a real human player.
                var controlling = MyAPIGateway.Players.GetPlayerControllingEntity(character);
                if (controlling != null && !controlling.IsBot)
                    return false;

                // Prefer IsBot; also accept Save=false ambient leftovers without a live controller.
                if (character.IsBot)
                    return true;
                try
                {
                    if (!character.Save)
                        return true;
                }
                catch { }

                return false;
            }
            catch
            {
                return false;
            }
        }

        private static void ClearCrewRuntime(string crewId)
        {
            if (string.IsNullOrEmpty(crewId))
                return;
            FarSecondsByCrewId.Remove(crewId);
            RuntimeByCrewId.Remove(crewId);
        }

        private static void BeginPhase(string crewId, AmbientPhase phase, IMyTerminalBlock seat)
        {
            if (string.IsNullOrEmpty(crewId))
                return;

            AmbientRuntime prev;
            RuntimeByCrewId.TryGetValue(crewId, out prev);

            var runtime = new AmbientRuntime
            {
                Phase = phase,
                PhaseSecondsLeft = RollPhaseDuration(phase),
                HasWaypoint = false,
                Waypoint = Vector3D.Zero,
                GreetCooldownLeft = prev.GreetCooldownLeft,
                IdleEmoteCooldownLeft = prev.IdleEmoteCooldownLeft > 0.1f
                    ? prev.IdleEmoteCooldownLeft
                    : RandomRange(8f, 18f),
                GaitRunning = false,
                GaitSecondsLeft = RandomRange(AmbientWalkGaitMinSeconds, AmbientWalkGaitMaxSeconds),
            };
            if (phase == AmbientPhase.Wandering && seat != null)
            {
                runtime.Waypoint = PickNeighborhoodWaypoint(seat);
                runtime.HasWaypoint = true;
            }
            RuntimeByCrewId[crewId] = runtime;
        }

        private static void TickGait(ref AmbientRuntime runtime)
        {
            runtime.GaitSecondsLeft -= AmbientSimStepSeconds;
            if (runtime.GaitSecondsLeft > 0f)
                return;

            if (runtime.GaitRunning)
            {
                // After a hurry, always return to stroll for a while.
                runtime.GaitRunning = false;
                runtime.GaitSecondsLeft = RandomRange(AmbientWalkGaitMinSeconds, AmbientWalkGaitMaxSeconds);
                return;
            }

            if (Rng.NextDouble() < AmbientRunGaitChance)
            {
                runtime.GaitRunning = true;
                runtime.GaitSecondsLeft = RandomRange(AmbientRunGaitMinSeconds, AmbientRunGaitMaxSeconds);
            }
            else
            {
                runtime.GaitRunning = false;
                runtime.GaitSecondsLeft = RandomRange(AmbientWalkGaitMinSeconds * 0.5f, AmbientWalkGaitMaxSeconds);
            }
        }

        private static float RollPhaseDuration(AmbientPhase phase)
        {
            if (phase == AmbientPhase.Seated)
                return RandomRange(CrewConfig.AmbientSitSecondsMin, CrewConfig.AmbientSitSecondsMax);
            return RandomRange(CrewConfig.AmbientWanderSecondsMin, CrewConfig.AmbientWanderSecondsMax);
        }

        private static float RandomRange(float min, float max)
        {
            if (max < min)
                max = min;
            if (max <= min)
                return min;
            return (float)(min + Rng.NextDouble() * (max - min));
        }

        /// <summary>
        /// Scripted AttachPilot never starts SitInSeat(duration), so vanilla StandUp never fires.
        /// HireCrew periodically RemovePilot (wander + MoveAndRotate) then re-AttachPilot.
        /// Crew stations (non-cockpit) stay in Wandering and refresh waypoints.
        /// </summary>
        private static void TickSitWanderCycle(CrewRecord crew, IMyTerminalBlock seat, IMyCharacter character)
        {
            if (crew == null || seat == null || character == null)
                return;

            var homeCockpit = seat as IMyCockpit;

            AmbientRuntime runtime;
            if (!RuntimeByCrewId.TryGetValue(crew.CrewId, out runtime))
            {
                AmbientPhase start = AmbientPhase.Wandering;
                if (homeCockpit != null && IsCharacterInSeat(character, homeCockpit))
                    start = AmbientPhase.Seated;
                BeginPhase(crew.CrewId, start, seat);
                return;
            }

            runtime.PhaseSecondsLeft -= 1f;
            if (runtime.PhaseSecondsLeft > 0f)
            {
                RuntimeByCrewId[crew.CrewId] = runtime;
                return;
            }

            if (homeCockpit == null)
            {
                // No seat attach — keep walking in the neighborhood.
                BeginPhase(crew.CrewId, AmbientPhase.Wandering, seat);
                return;
            }

            if (runtime.Phase == AmbientPhase.Seated)
            {
                RemoveFromAnySeat(character, seat.CubeGrid);
                StopMovement(character, seat.CubeGrid);
                BeginPhase(crew.CrewId, AmbientPhase.Wandering, seat);
                return;
            }

            // End wander: return home if seat free.
            StopMovement(character, seat.CubeGrid);
            if (!CrewStationLogic.IsSeatOccupiedByPlayer(seat))
            {
                try { homeCockpit.AttachPilot(character); }
                catch { }
            }
            BeginPhase(crew.CrewId, AmbientPhase.Seated, seat);
        }

        private static bool IsCharacterInSeat(IMyCharacter character, IMyCockpit seat)
        {
            return character != null && seat != null && seat.Pilot != null
                && seat.Pilot.EntityId == character.EntityId;
        }

        private static Vector3D PickNeighborhoodWaypoint(IMyTerminalBlock seat)
        {
            MatrixD wm = seat.WorldMatrix;
            float r = CrewConfig.AmbientWanderRadiusMeters;
            Vector3D origin = GetAmbientNeighborhoodOrigin(seat);
            IMyCubeGrid grid = seat != null ? seat.CubeGrid : null;

            // Uniform disk samples (angle + radius) so they don't always pick the same corridor.
            for (int attempt = 0; attempt < 14; attempt++)
            {
                double ang = Rng.NextDouble() * Math.PI * 2.0;
                double dist = Math.Sqrt(Rng.NextDouble()) * r;
                Vector3D offset = (wm.Right * Math.Cos(ang) + wm.Forward * Math.Sin(ang)) * dist;
                Vector3D candidate = origin + offset + wm.Up * 0.05;
                if (IsWalkableOnGrid(grid, seat, candidate))
                    return candidate;
            }

            // Fallback: stay near spawn, forced inside grid bounds.
            Vector3D fallback = origin;
            ClampToGridBounds(grid, ref fallback, AmbientGridBoundMarginMeters);
            return fallback;
        }

        private static bool IsInsideGridBounds(IMyCubeGrid grid, Vector3D worldPos, float marginMeters)
        {
            if (grid == null)
                return false;
            try
            {
                Vector3D local = Vector3D.Transform(worldPos, grid.WorldMatrixNormalizedInv);
                BoundingBoxD box = grid.LocalAABB;
                Vector3D min = box.Min + new Vector3D(marginMeters);
                Vector3D max = box.Max - new Vector3D(marginMeters);
                if (min.X > max.X) { double c = box.Center.X; min.X = c; max.X = c; }
                if (min.Y > max.Y) { double c = box.Center.Y; min.Y = c; max.Y = c; }
                if (min.Z > max.Z) { double c = box.Center.Z; min.Z = c; max.Z = c; }
                return local.X >= min.X && local.X <= max.X
                    && local.Y >= min.Y && local.Y <= max.Y
                    && local.Z >= min.Z && local.Z <= max.Z;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Clamp world position into the grid LocalAABB (with margin). Returns true if changed.</summary>
        private static bool ClampToGridBounds(IMyCubeGrid grid, ref Vector3D worldPos, float marginMeters)
        {
            if (grid == null)
                return false;
            try
            {
                Vector3D local = Vector3D.Transform(worldPos, grid.WorldMatrixNormalizedInv);
                BoundingBoxD box = grid.LocalAABB;
                Vector3D min = box.Min + new Vector3D(marginMeters);
                Vector3D max = box.Max - new Vector3D(marginMeters);
                if (min.X > max.X) { double c = box.Center.X; min.X = c; max.X = c; }
                if (min.Y > max.Y) { double c = box.Center.Y; min.Y = c; max.Y = c; }
                if (min.Z > max.Z) { double c = box.Center.Z; min.Z = c; max.Z = c; }

                Vector3D clamped = Vector3D.Clamp(local, min, max);
                if (Vector3D.DistanceSquared(clamped, local) < 1e-6)
                    return false;
                worldPos = Vector3D.Transform(clamped, grid.WorldMatrix);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Inside grid AABB and standing above this grid's blocks (down-ray hit).</summary>
        private static bool IsWalkableOnGrid(IMyCubeGrid grid, IMyTerminalBlock seat, Vector3D worldPos)
        {
            if (grid == null || !IsInsideGridBounds(grid, worldPos, AmbientGridBoundMarginMeters))
                return false;

            Vector3D down;
            if (!TryGetGravityDown(worldPos, seat, out down))
            {
                if (seat != null)
                    down = -seat.WorldMatrix.Up;
                else
                    return true;
            }

            try
            {
                IHitInfo hit;
                if (!MyAPIGateway.Physics.CastRay(worldPos - down * 0.05, worldPos + down * 4.0, out hit)
                    || hit == null
                    || hit.HitEntity == null)
                    return false;

                IMyCubeGrid hitGrid = hit.HitEntity as IMyCubeGrid;
                if (hitGrid == null)
                {
                    var block = hit.HitEntity as IMyCubeBlock;
                    if (block != null)
                        hitGrid = block.CubeGrid;
                }
                return hitGrid != null && hitGrid.EntityId == grid.EntityId;
            }
            catch
            {
                return IsInsideGridBounds(grid, worldPos, AmbientGridBoundMarginMeters);
            }
        }

        /// <summary>
        /// Pick a nearby empty standing spot beside the seat (walkable deck + clear body volume).
        /// </summary>
        private static void GetAmbientSpawnPose(
            IMyTerminalBlock seat,
            out Vector3D pos,
            out Vector3D forward,
            out Vector3D up)
        {
            MatrixD wm = seat.WorldMatrix;
            up = wm.Up;
            Vector3D down;
            if (TryGetGravityDown(wm.Translation, seat, out down) && down.LengthSquared() > 0.01)
                up = -down;
            up.Normalize();

            float gridSize = 2.5f;
            IMyCubeGrid grid = seat != null ? seat.CubeGrid : null;
            if (grid != null)
                gridSize = grid.GridSize;

            Vector3I cells = Vector3I.One;
            var cube = seat as IMyCubeBlock;
            if (cube != null)
                cells = cube.Max - cube.Min + Vector3I.One;

            float halfX = cells.X * gridSize * 0.5f;
            float halfZ = cells.Z * gridSize * 0.5f;
            const float footUp = 0.08f;

            // Preferred offsets first (beside station / in front of cockpit), then a ring search.
            var candidates = new List<Vector3D>(24);
            if (seat is IMyCockpit)
            {
                candidates.Add(wm.Translation + wm.Forward * (halfZ + 0.85f) + up * footUp);
                candidates.Add(wm.Translation + wm.Forward * (halfZ + 1.4f) + up * footUp);
                candidates.Add(wm.Translation + wm.Forward * (halfZ + 0.85f) + wm.Right * 1.1f + up * footUp);
                candidates.Add(wm.Translation + wm.Forward * (halfZ + 0.85f) - wm.Right * 1.1f + up * footUp);
            }
            else
            {
                candidates.Add(wm.Translation + wm.Right * (halfX + 0.9f) + wm.Forward * (halfZ * 0.15f) + up * footUp);
                candidates.Add(wm.Translation - wm.Right * (halfX + 0.9f) + wm.Forward * (halfZ * 0.15f) + up * footUp);
                candidates.Add(wm.Translation + wm.Forward * (halfZ + 0.9f) + up * footUp);
                candidates.Add(wm.Translation - wm.Forward * (halfZ + 0.9f) + up * footUp);
            }

            // Expanding ring around the seat for a free pad.
            float[] radii = { 1.4f, 2.0f, 2.7f, 3.5f, 4.5f };
            int sectors = 8;
            for (int ri = 0; ri < radii.Length; ri++)
            {
                float rad = radii[ri];
                for (int s = 0; s < sectors; s++)
                {
                    double ang = (Math.PI * 2.0 * s) / sectors + ri * 0.2;
                    Vector3D flat = wm.Right * Math.Cos(ang) + wm.Forward * Math.Sin(ang);
                    candidates.Add(wm.Translation + flat * rad + up * footUp);
                }
            }

            Vector3I? seatMin = null;
            Vector3I? seatMax = null;
            if (cube != null)
            {
                seatMin = cube.Min;
                seatMax = cube.Max;
            }

            for (int i = 0; i < candidates.Count; i++)
            {
                Vector3D candidate = candidates[i];
                if (grid != null)
                    ClampToGridBounds(grid, ref candidate, AmbientGridBoundMarginMeters);

                if (!IsWalkableOnGrid(grid, seat, candidate))
                    continue;
                if (!IsEmptySpawnVolume(grid, candidate, up, seatMin, seatMax))
                    continue;

                pos = candidate;
                Vector3D toSeat = wm.Translation - pos;
                forward = toSeat - up * Vector3D.Dot(toSeat, up);
                if (forward.LengthSquared() < 0.01)
                    forward = -FlatDirOr(wm, up);
                else
                    forward.Normalize();
                return;
            }

            // Soft fallback: empty volume even if walkability ray failed (odd gravity / gaps).
            for (int i = 0; i < candidates.Count; i++)
            {
                Vector3D candidate = candidates[i];
                if (grid != null)
                    ClampToGridBounds(grid, ref candidate, AmbientGridBoundMarginMeters);
                if (!IsEmptySpawnVolume(grid, candidate, up, seatMin, seatMax))
                    continue;

                pos = candidate;
                Vector3D toSeat = wm.Translation - pos;
                forward = toSeat - up * Vector3D.Dot(toSeat, up);
                if (forward.LengthSquared() < 0.01)
                    forward = -FlatDirOr(wm, up);
                else
                    forward.Normalize();
                return;
            }

            // Last resort: offset beside the seat (SnapFeetToDeck runs after spawn).
            if (seat is IMyCockpit)
                pos = wm.Translation + wm.Forward * (halfZ + 0.9f) + up * footUp;
            else
                pos = wm.Translation + wm.Right * (halfX + 0.9f) + up * footUp;
            if (grid != null)
                ClampToGridBounds(grid, ref pos, AmbientGridBoundMarginMeters);
            forward = -FlatDirOr(wm, up);
        }

        private static Vector3D FlatDirOr(MatrixD wm, Vector3D up)
        {
            Vector3D f = wm.Forward - up * Vector3D.Dot(wm.Forward, up);
            if (f.LengthSquared() < 0.01)
                f = wm.Right - up * Vector3D.Dot(wm.Right, up);
            if (f.LengthSquared() < 0.01)
                f = Vector3D.CalculatePerpendicularVector(up);
            f.Normalize();
            return f;
        }

        /// <summary>True when feet/torso/head are clear air beside the home seat (not inside blocks).</summary>
        private static bool IsEmptySpawnVolume(
            IMyCubeGrid grid,
            Vector3D feetPos,
            Vector3D up,
            Vector3I? seatMin,
            Vector3I? seatMax)
        {
            if (grid == null)
                return true;

            try
            {
                // Body samples — reject solid cubes and the home seat's own cells.
                Vector3D[] samples =
                {
                    feetPos,
                    feetPos + up * 0.55,
                    feetPos + up * 1.15,
                    feetPos + up * 1.65,
                };
                for (int i = 0; i < samples.Length; i++)
                {
                    Vector3I cell = grid.WorldToGridInteger(samples[i]);
                    if (seatMin.HasValue && seatMax.HasValue
                        && cell.X >= seatMin.Value.X && cell.X <= seatMax.Value.X
                        && cell.Y >= seatMin.Value.Y && cell.Y <= seatMax.Value.Y
                        && cell.Z >= seatMin.Value.Z && cell.Z <= seatMax.Value.Z)
                        return false;
                    if (grid.CubeExists(cell) || grid.GetCubeBlock(cell) != null)
                        return false;
                }

                // Lateral probes — reject overlapping station mesh / tight alcoves.
                Vector3D right = Vector3D.CalculatePerpendicularVector(up);
                Vector3D forward = Vector3D.Cross(up, right);
                if (forward.LengthSquared() > 0.01)
                    forward.Normalize();
                right.Normalize();

                Vector3D chest = feetPos + up * 1.0;
                Vector3D[] dirs = { right, -right, forward, -forward };
                for (int i = 0; i < dirs.Length; i++)
                {
                    IHitInfo hit;
                    if (MyAPIGateway.Physics.CastRay(chest, chest + dirs[i] * 0.45, out hit)
                        && hit != null
                        && hit.HitEntity != null)
                    {
                        var hitGrid = hit.HitEntity as IMyCubeGrid;
                        if (hitGrid == null)
                        {
                            var block = hit.HitEntity as IMyCubeBlock;
                            if (block != null)
                                hitGrid = block.CubeGrid;
                        }
                        if (hitGrid != null && hitGrid.EntityId == grid.EntityId)
                            return false;
                    }
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        private static Vector3D GetAmbientNeighborhoodOrigin(IMyTerminalBlock seat)
        {
            Vector3D pos, forward, up;
            GetAmbientSpawnPose(seat, out pos, out forward, out up);
            return pos;
        }

        /// <returns>False if the step would leave the grid (caller should repick waypoint).</returns>
        private static bool SteerToward(
            IMyCharacter character,
            Vector3D worldTarget,
            IMyCubeGrid grid,
            IMyTerminalBlock seat,
            bool running)
        {
            if (character == null || character.Closed || character.Physics == null)
                return true;

            float speed = running ? AmbientRunSpeedMeters : AmbientWalkSpeedMeters;
            float inputScale = running ? AmbientRunInputScale : AmbientWalkInputScale;

            var ctrl = character as VRage.Game.ModAPI.Interfaces.IMyControllableEntity;
            try
            {
                if (ctrl != null && ctrl.EnabledThrusts)
                    ctrl.SwitchThrusts();
            }
            catch { }

            Vector3D down;
            Vector3D up = character.WorldMatrix.Up;
            if (TryGetGravityDown(character.GetPosition(), seat, out down))
                up = -down;

            Vector3D pos = character.GetPosition();
            // Soft recovery if somehow already outside.
            if (grid != null && ClampToGridBounds(grid, ref pos, AmbientGridBoundMarginMeters))
            {
                try { character.SetPosition(pos); }
                catch { }
            }

            Vector3D to = worldTarget - pos;
            Vector3D flatTo = to - up * Vector3D.Dot(to, up);
            if (flatTo.LengthSquared() < 0.16)
            {
                StopMovement(character, grid);
                return true;
            }
            flatTo.Normalize();

            Vector3D stepProbe = pos + flatTo * (speed * AmbientSimStepSeconds);
            if (grid != null && !IsInsideGridBounds(grid, stepProbe, AmbientGridBoundMarginMeters))
            {
                StopMovement(character, grid);
                return false;
            }

            bool controlled = HasBotControl(character);

            // With AiEnabled-style TakeControl, MoveAndRotate drives walk anim + locomotion.
            // MoveAndRotate yaw alone is too weak on harvested controllers — face the waypoint
            // with WorldMatrix when the heading error is large, then walk mostly forward.
            if (ctrl != null && controlled)
            {
                try
                {
                    Vector3D flatFwd = character.WorldMatrix.Forward - up * Vector3D.Dot(character.WorldMatrix.Forward, up);
                    double align = 0.0;
                    if (flatFwd.LengthSquared() > 0.01)
                    {
                        flatFwd.Normalize();
                        align = Vector3D.Dot(flatFwd, flatTo);
                    }

                    if (align < 0.92)
                    {
                        try
                        {
                            character.WorldMatrix = MatrixD.CreateWorld(pos, flatTo, up);
                        }
                        catch { }
                        flatFwd = flatTo;
                        align = 1.0;
                    }

                    // VRage character local forward is -Z; never force +Z (that walks backward).
                    // After facing the waypoint, drive mostly along character forward.
                    Vector3 move = new Vector3(0f, 0f, -inputScale);
                    Vector2 rot = Vector2.Zero;
                    double cross = Vector3D.Dot(up, Vector3D.Cross(flatFwd, flatTo));
                    rot = new Vector2(0f, MathHelper.Clamp((float)cross * 4f, -1f, 1f));

                    ctrl.MoveAndRotate(move, rot, 0f);
                    ApplyLocomotionAnimation(character, speed, moving: true, running: running);
                }
                catch { }
                return true;
            }

            // Fallback (no controller yet): physics step — idle pose, but stays usable.
            try
            {
                Vector3 gridVel = Vector3.Zero;
                if (grid != null && grid.Physics != null)
                    gridVel = grid.Physics.LinearVelocity;

                Vector3 relative = character.Physics.LinearVelocity - gridVel;
                float vertical = (float)Vector3D.Dot(relative, down.LengthSquared() > 0.01 ? down : -up);
                Vector3 verticalKeep = (Vector3)((down.LengthSquared() > 0.01 ? down : -up) * vertical);

                Vector3D newPos = stepProbe;
                Vector3D fwd = character.WorldMatrix.Forward;
                fwd = fwd - up * Vector3D.Dot(fwd, up);
                bool needTurn = true;
                if (fwd.LengthSquared() > 0.01)
                {
                    fwd.Normalize();
                    needTurn = Vector3D.Dot(fwd, flatTo) < 0.93;
                }
                if (needTurn)
                    character.WorldMatrix = MatrixD.CreateWorld(newPos, flatTo, up);
                else
                    character.SetPosition(newPos);

                character.Physics.LinearVelocity =
                    (Vector3)(flatTo * speed) + gridVel + verticalKeep;
                ApplyLocomotionAnimation(character, speed, moving: true, running: running);
            }
            catch { }
            return true;
        }

        /// <summary>
        /// AC Variables.SetValue and MyCharacter.PlayCharacterAnimation are not whitelisted.
        /// MovementState is the only allowed lever without a bot EntityController.
        /// </summary>
        private static void ApplyLocomotionAnimation(IMyCharacter character, float forwardSpeed, bool moving, bool running = false)
        {
            if (character == null || character.Closed)
                return;

            try
            {
                if (!moving)
                    character.CurrentMovementState = MyCharacterMovementEnum.Standing;
                else if (running)
                    character.CurrentMovementState = MyCharacterMovementEnum.Running;
                else
                    character.CurrentMovementState = MyCharacterMovementEnum.Walking;
            }
            catch { }
        }

        /// <summary>
        /// When a player walks up, face them and play the vanilla Wave emote (cooldown).
        /// Returns true if wander should be skipped this frame.
        /// </summary>
        private static bool UpdateGreeting(ref AmbientRuntime runtime, IMyCharacter character, IMyTerminalBlock seat)
        {
            if (character == null || character.Closed || character.Physics == null)
                return false;

            IMyCharacter nearestPlayer;
            double distSq;
            if (!TryGetNearestPlayerCharacter(character.GetPosition(), out nearestPlayer, out distSq)
                || nearestPlayer == null)
            {
                if (runtime.GreetFaceLeft > 0f)
                {
                    runtime.GreetFaceLeft = Math.Max(0f, runtime.GreetFaceLeft - AmbientSimStepSeconds);
                    StopMovement(character, seat != null ? seat.CubeGrid : null);
                    return true;
                }
                return false;
            }

            if (runtime.GreetFaceLeft > 0f)
            {
                runtime.GreetFaceLeft = Math.Max(0f, runtime.GreetFaceLeft - AmbientSimStepSeconds);
                FaceToward(character, nearestPlayer.GetPosition(), seat);
                StopMovement(character, seat != null ? seat.CubeGrid : null);
                return true;
            }

            if (runtime.GreetCooldownLeft > 0f)
                return false;
            if (distSq > GreetingTriggerMeters * GreetingTriggerMeters)
                return false;

            FaceToward(character, nearestPlayer.GetPosition(), seat);
            StopMovement(character, seat != null ? seat.CubeGrid : null);
            string greet = PickRandom(GreetingEmoteNames);
            PlayCharacterEmote(character, greet);

            runtime.GreetFaceLeft = GreetingFaceSeconds;
            runtime.GreetCooldownLeft = GreetingCooldownSeconds;
            runtime.IdleEmoteCooldownLeft = Math.Max(runtime.IdleEmoteCooldownLeft, 12f);
            runtime.HasWaypoint = false;
            return true;
        }

        /// <summary>
        /// Occasional calm idle emotes while wandering (looking around, stretch, wrist check, etc.).
        /// Returns true if wander should be skipped this frame.
        /// </summary>
        private static bool UpdateIdleEmote(ref AmbientRuntime runtime, IMyCharacter character, IMyTerminalBlock seat)
        {
            if (character == null || character.Closed)
                return false;

            if (runtime.IdleEmoteHoldLeft > 0f)
            {
                runtime.IdleEmoteHoldLeft = Math.Max(0f, runtime.IdleEmoteHoldLeft - AmbientSimStepSeconds);
                StopMovement(character, seat != null ? seat.CubeGrid : null);
                return true;
            }

            if (runtime.IdleEmoteCooldownLeft > 0f)
            {
                runtime.IdleEmoteCooldownLeft = Math.Max(0f, runtime.IdleEmoteCooldownLeft - AmbientSimStepSeconds);
                return false;
            }

            // Prefer pausing at a waypoint so they don't freeze mid-stride as often.
            bool atRest = !runtime.HasWaypoint
                || Vector3D.DistanceSquared(character.GetPosition(), runtime.Waypoint) < 1.2 * 1.2;
            if (!atRest && Rng.NextDouble() > 0.12)
            {
                runtime.IdleEmoteCooldownLeft = 4f + (float)Rng.NextDouble() * 6f;
                return false;
            }

            string emote = PickRandom(IdleEmoteNames);
            StopMovement(character, seat != null ? seat.CubeGrid : null);
            PlayCharacterEmote(character, emote);

            // Idle pauses: glance a new random heading so they don't stay glued to one facing.
            NudgeFacingYaw(character, seat, (float)(Rng.NextDouble() * 140.0 - 70.0));

            runtime.IdleEmoteHoldLeft = IdleEmoteHoldSeconds;
            runtime.IdleEmoteCooldownLeft = IdleEmoteCooldownMinSeconds
                + (float)Rng.NextDouble() * (IdleEmoteCooldownMaxSeconds - IdleEmoteCooldownMinSeconds);
            return true;
        }

        private static string PickRandom(string[] options)
        {
            if (options == null || options.Length == 0)
                return "Wave";
            return options[Rng.Next(0, options.Length)];
        }

        private static void PlayCharacterEmote(IMyCharacter character, string emoteName)
        {
            if (character == null || character.Closed || string.IsNullOrEmpty(emoteName))
                return;
            try
            {
                // AiEnabled pattern: "emote" gate then animation subtype.
                character.TriggerCharacterAnimationEvent("emote", true);
                character.TriggerCharacterAnimationEvent(emoteName, true);
            }
            catch (Exception e)
            {
                Log("emote failed " + emoteName + ": " + e.Message);
            }
        }

        private static void NudgeFacingYaw(IMyCharacter character, IMyTerminalBlock seat, float degrees)
        {
            if (character == null || character.Closed || Math.Abs(degrees) < 0.5f)
                return;
            try
            {
                Vector3D pos = character.GetPosition();
                Vector3D up = character.WorldMatrix.Up;
                Vector3D down;
                if (TryGetGravityDown(pos, seat, out down) && down.LengthSquared() > 0.01)
                    up = -down;
                if (up.LengthSquared() < 0.01)
                    up = Vector3D.Up;
                up.Normalize();

                Vector3D fwd = character.WorldMatrix.Forward;
                fwd = fwd - up * Vector3D.Dot(fwd, up);
                if (fwd.LengthSquared() < 0.01)
                    return;
                fwd.Normalize();
                MatrixD rot = MatrixD.CreateFromAxisAngle(up, MathHelper.ToRadians(degrees));
                Vector3D newFwd = Vector3D.TransformNormal(fwd, rot);
                character.WorldMatrix = MatrixD.CreateWorld(pos, newFwd, up);
            }
            catch { }
        }

        private static bool TryGetNearestPlayerCharacter(Vector3D from, out IMyCharacter nearest, out double distSq)
        {
            nearest = null;
            distSq = double.MaxValue;
            try
            {
                PlayerScratch.Clear();
                MyAPIGateway.Players.GetPlayers(PlayerScratch);
                for (int i = 0; i < PlayerScratch.Count; i++)
                {
                    var p = PlayerScratch[i];
                    if (p == null || p.IsBot || p.Character == null || p.Character.Closed)
                        continue;
                    double d = Vector3D.DistanceSquared(from, p.Character.GetPosition());
                    if (d < distSq)
                    {
                        distSq = d;
                        nearest = p.Character;
                    }
                }
            }
            catch { }
            return nearest != null;
        }

        private static void FaceToward(IMyCharacter character, Vector3D worldTarget, IMyTerminalBlock seat)
        {
            if (character == null || character.Closed)
                return;

            Vector3D pos = character.GetPosition();
            Vector3D up = character.WorldMatrix.Up;
            Vector3D down;
            if (TryGetGravityDown(pos, seat, out down) && down.LengthSquared() > 0.01)
                up = -down;
            if (up.LengthSquared() < 0.01)
                up = Vector3D.Up;
            up.Normalize();

            Vector3D to = worldTarget - pos;
            Vector3D flat = to - up * Vector3D.Dot(to, up);
            if (flat.LengthSquared() < 0.01)
                return;
            flat.Normalize();

            try
            {
                character.WorldMatrix = MatrixD.CreateWorld(pos, flat, up);
            }
            catch { }
        }

        private static void StopMovement(IMyCharacter character, IMyCubeGrid grid = null)
        {
            if (character == null)
                return;

            var ctrl = character as VRage.Game.ModAPI.Interfaces.IMyControllableEntity;
            try
            {
                if (ctrl != null)
                    ctrl.MoveAndRotateStopped();
            }
            catch { }

            try
            {
                if (character.Physics != null)
                {
                    Vector3 gridVel = Vector3.Zero;
                    if (grid != null && grid.Physics != null)
                        gridVel = grid.Physics.LinearVelocity;
                    character.Physics.LinearVelocity = gridVel;
                }
                ApplyLocomotionAnimation(character, 0f, moving: false);
            }
            catch { }
        }

        private static bool DespawnBot(CrewSession session, CrewRecord crew, HashSet<long> dirtyGrids)
        {
            if (crew == null || !crew.CharacterEntityId.HasValue)
                return false;

            long id = crew.CharacterEntityId.Value;
            long gridId = crew.GridEntityId;
            try
            {
                IMyEntity ent;
                if (MyAPIGateway.Entities.TryGetEntityById(id, out ent) && ent != null && !ent.Closed)
                {
                    var character = ent as IMyCharacter;
                    IMyCubeGrid gridHint = null;
                    if (crew.SeatEntityId.HasValue)
                    {
                        IMyEntity seatEnt;
                        if (MyAPIGateway.Entities.TryGetEntityById(crew.SeatEntityId.Value, out seatEnt))
                        {
                            var seatBlock = seatEnt as IMyCubeBlock;
                            if (seatBlock != null)
                                gridHint = seatBlock.CubeGrid;
                        }
                    }
                    if (character != null)
                    {
                        RemoveFromAnySeat(character, gridHint);
                        try { MyAPIGateway.Players.RemoveControlledEntity(character); }
                        catch { }
                    }
                    ent.Close();
                }
                else
                {
                    try { MyVisualScriptLogicProvider.RemoveEntity(id, false); }
                    catch { }
                }
            }
            catch { }

            ReleaseCrewControl(crew.CrewId);
            crew.CharacterEntityId = null;
            if (session != null && session.Store != null)
                session.Store.Upsert(crew);
            KnownCharacterIds.Remove(id);
            if (gridId != 0)
                dirtyGrids.Add(gridId);
            return true;
        }

        private static bool TrySpawnAndSeat(
            CrewSession session,
            CrewRecord crew,
            IMyTerminalBlock seat,
            HashSet<long> dirtyGrids)
        {
            Vector3D pos, forward, up;
            if (!CrewRepairMission.TryGetMissionPose(crew.CrewId, seat, out pos, out forward, out up)
                && !CrewSalvageMission.TryGetMissionPose(crew.CrewId, seat, out pos, out forward, out up))
                GetAmbientSpawnPose(seat, out pos, out forward, out up);
            string botName = AmbientDisplayName(crew);

            string subtype = ResolveBotSubtype();
            long spawnId = 0;
            try
            {
                spawnId = MyVisualScriptLogicProvider.SpawnBot(subtype, pos, forward, up, botName);
            }
            catch (Exception e)
            {
                Log("spawn threw subtype=" + subtype + " for " + crew.CrewId + ": " + e.Message);
                return false;
            }

            IMyCharacter character = ResolveSpawnedCharacter(spawnId, pos);
            if (character == null && subtype != CrewConfig.AmbientBotSubtypeFallback)
            {
                // Custom def missing/broken — try vanilla Astronaut once.
                _resolvedBotSubtype = CrewConfig.AmbientBotSubtypeFallback;
                subtype = _resolvedBotSubtype;
                Log("retrying with fallback subtype=" + subtype);
                try
                {
                    spawnId = MyVisualScriptLogicProvider.SpawnBot(subtype, pos, forward, up, botName);
                }
                catch (Exception e)
                {
                    Log("fallback spawn threw for " + crew.CrewId + ": " + e.Message);
                    return false;
                }
                character = ResolveSpawnedCharacter(spawnId, pos);
            }

            string spawnPath = "SpawnBot/" + subtype;
            if (character == null)
            {
                // AiEnabled pattern: OB character + harvested IsBot EntityController (TakeControl).
                // Plain OB without controller = disconnected HUD icon + no walk anim.
                character = TrySpawnCharacterEntity(pos, forward, up, botName, seat, crew);
                spawnPath = "Controlled/" + CrewConfig.AmbientCharacterSubtype;
            }

            if (character == null)
            {
                Log("spawn unresolved for " + crew.CrewId + " subtype=" + subtype
                    + " spawnId=" + spawnId + " ctrlPool=" + CrewBotControllers.PoolCount);
                return false;
            }

            ApplyAmbientPhysics(character, seat.CubeGrid);
            ApplyCrewInvulnerability(character);
            AlignCrewBotToOwner(character, crew);
            AlignCharacterToLocalDown(character, seat);
            SnapFeetToDeck(character, seat);
            ForceStandingPose(character);
            ApplyAmbientNameplate(character, botName);

            var homeCockpit = seat as IMyCockpit;
            if (homeCockpit != null)
            {
                try
                {
                    homeCockpit.AttachPilot(character);
                }
                catch (Exception e)
                {
                    Log("AttachPilot failed for " + crew.CrewId + ": " + e.Message);
                }
            }

            crew.CharacterEntityId = character.EntityId;
            session.Store.Upsert(crew);
            KnownCharacterIds.Add(character.EntityId);
            if (crew.GridEntityId != 0)
                dirtyGrids.Add(crew.GridEntityId);
            Log("spawn ok " + crew.CrewId + " via=" + spawnPath
                + " char=" + character.EntityId
                + " seat=" + (homeCockpit != null ? "cockpit" : "station"));
            return true;
        }

        /// <summary>
        /// AiEnabled CreateBotObject pattern: character OB owned by a harvested bot identity,
        /// then EntityController.TakeControl so MoveAndRotate drives walk anim / HUD.
        /// </summary>
        private static IMyCharacter TrySpawnCharacterEntity(
            Vector3D pos,
            Vector3D forward,
            Vector3D up,
            string displayName,
            IMyTerminalBlock seat,
            CrewRecord crew)
        {
            string crewId = crew != null ? crew.CrewId : null;
            // Require a harvested IsBot controller before the body exists — uncontrolled OB
            // characters briefly show the disconnected-player HUD icon.
            var control = CrewBotControllers.Take();
            if (control == null || control.Controller == null || control.Identity == null)
            {
                Log("waiting for bot controller (pool=" + CrewBotControllers.PoolCount + ")");
                return null;
            }

            // Prefer gravity Up so the character is not spawned "sideways" in art-grav.
            Vector3D down;
            if (TryGetGravityDown(pos, seat, out down))
            {
                up = -down;
                forward = forward - up * Vector3D.Dot(forward, up);
            }
            if (forward.LengthSquared() < 0.01)
                forward = seat != null ? seat.WorldMatrix.Forward : Vector3D.Forward;
            if (up.LengthSquared() < 0.01)
                up = Vector3D.Up;
            forward.Normalize();
            up.Normalize();
            forward = Vector3D.Normalize(forward - up * Vector3D.Dot(forward, up));
            if (forward.LengthSquared() < 0.01)
                forward = Vector3D.CalculatePerpendicularVector(up);

            long ownerIdentityId = CrewBotRelations.ResolveCharacterOwnerIdentityId(
                crew, control.Identity.IdentityId);
            CrewBotControllers.AlignToCrewOwner(control.Identity.IdentityId, crew);

            var matrix = MatrixD.CreateWorld(pos, forward, up);
            var po = new MyPositionAndOrientation(ref matrix);
            var ob = new MyObjectBuilder_Character
            {
                // Name + DisplayName show the floating nametag; TakeControl before Add
                // avoids the disconnected-player icon that empty/uncontrolled bodies get.
                Name = displayName ?? "",
                DisplayName = displayName,
                SubtypeName = CrewConfig.AmbientCharacterSubtype,
                CharacterModel = CrewConfig.AmbientCharacterSubtype,
                EntityId = 0,
                AIMode = false,
                JetpackEnabled = false,
                EnableBroadcasting = false,
                EnableBroadcastingPlayerToggle = false,
                IsPersistenceCharacter = false,
                NeedsOxygenFromSuit = false,
                OxygenLevel = 1f,
                MovementState = MyCharacterMovementEnum.Standing,
                PersistentFlags = MyPersistentEntityFlags2.InScene | MyPersistentEntityFlags2.Enabled,
                PositionAndOrientation = po,
                Health = 1000,
                CharacterGeneralDamageModifier = 0f,
                LightEnabled = false,
                // Hire player ownership so factionless owners' weapons stay friendly.
                OwningPlayerIdentityId = ownerIdentityId,
            };

            if (seat != null && seat.CubeGrid != null)
                ob.RelativeDampeningEntity = seat.CubeGrid.EntityId;

            try
            {
                IMyEntity ent = MyAPIGateway.Entities.CreateFromObjectBuilder(ob);
                var character = ent as IMyCharacter;
                if (character == null)
                {
                    if (ent != null && !ent.Closed)
                        ent.Close();
                    CrewBotControllers.Return(control);
                    Log("character OB spawn produced non-character for " + displayName);
                    return null;
                }

                if (Vector3D.DistanceSquared(character.GetPosition(), Vector3D.Zero) < 0.0001)
                    character.SetPosition(pos);

                try
                {
                    control.Controller.TakeControl(character);
                }
                catch (Exception e)
                {
                    Log("TakeControl failed for " + displayName + ": " + e.Message);
                    try { character.Close(); } catch { }
                    CrewBotControllers.Return(control);
                    return null;
                }

                try
                {
                    // Prevent sandbox serialization — duplicates on reload if Save stays true.
                    character.Save = false;
                }
                catch { }

                try
                {
                    MyAPIGateway.Entities.AddEntity(character, true);
                }
                catch (Exception e)
                {
                    Log("AddEntity failed for " + displayName + ": " + e.Message);
                    try { character.Close(); } catch { }
                    CrewBotControllers.Return(control);
                    return null;
                }

                try { character.Save = false; }
                catch { }

                if (!string.IsNullOrEmpty(crewId))
                    ControlByCrewId[crewId] = control;

                if (seat != null && seat.CubeGrid != null)
                    ApplyAmbientPhysics(character, seat.CubeGrid);

                ApplyCrewInvulnerability(character);
                ApplyAmbientNameplate(character, displayName);

                Log("character OB+control ok " + displayName + " char=" + character.EntityId
                    + " ident=" + control.Identity.IdentityId
                    + " owner=" + ownerIdentityId
                    + " pool=" + CrewBotControllers.PoolCount);
                return character;
            }
            catch (Exception e)
            {
                CrewBotControllers.Return(control);
                Log("character OB spawn threw for " + displayName + ": " + e.Message);
                return null;
            }
        }

        /// <summary>
        /// Align SpawnBot-path identities (and any controlling identity) to the hiring owner.
        /// Controlled OB path also calls this via AlignToCrewOwner before TakeControl.
        /// </summary>
        private static void AlignCrewBotToOwner(IMyCharacter character, CrewRecord crew)
        {
            if (character == null || character.Closed || crew == null)
                return;

            long botIdentityId = 0;
            try
            {
                if (character.ControllerInfo != null && character.ControllerInfo.ControllingIdentityId != 0)
                    botIdentityId = character.ControllerInfo.ControllingIdentityId;
            }
            catch { }

            if (botIdentityId == 0)
            {
                try
                {
                    PlayerScratch.Clear();
                    MyAPIGateway.Players.GetPlayers(PlayerScratch);
                    for (int i = 0; i < PlayerScratch.Count; i++)
                    {
                        var p = PlayerScratch[i];
                        if (p == null || p.Character == null || p.Character.EntityId != character.EntityId)
                            continue;
                        botIdentityId = p.IdentityId;
                        break;
                    }
                }
                catch { }
            }

            if (botIdentityId != 0)
                CrewBotControllers.AlignToCrewOwner(botIdentityId, crew);
        }

        private static string AmbientDisplayName(CrewRecord crew)
        {
            string name = null;
            if (crew != null && !string.IsNullOrEmpty(crew.DisplayName))
                name = crew.DisplayName.Trim();
            if (string.IsNullOrEmpty(name))
                name = "Crew";
            if (name.Length > 48)
                name = name.Substring(0, 48);
            return name;
        }

        private static void ApplyAmbientNameplate(IMyCharacter character, string displayName)
        {
            if (character == null || character.Closed || string.IsNullOrEmpty(displayName))
                return;
            try { character.Name = displayName; }
            catch { }
            try { character.DisplayName = displayName; }
            catch { }
        }

        private static void ReleaseCrewControl(string crewId)
        {
            if (string.IsNullOrEmpty(crewId))
                return;
            CrewBotControllers.ControlInfo info;
            if (!ControlByCrewId.TryGetValue(crewId, out info))
                return;
            ControlByCrewId.Remove(crewId);
            CrewBotControllers.Return(info);
        }

        /// <summary>
        /// Late-bind harvested controllers onto already-spawned uncontrolled OB characters.
        /// </summary>
        private static void TryAttachPendingControls(CrewSession session)
        {
            if (session == null || session.Store == null || CrewBotControllers.PoolCount <= 0)
                return;

            foreach (var crew in session.Store.All)
            {
                if (crew == null || string.IsNullOrEmpty(crew.CrewId))
                    continue;
                if (ControlByCrewId.ContainsKey(crew.CrewId))
                    continue;

                IMyCharacter character;
                if (!TryGetLiveCharacter(crew, out character) || character == null)
                    continue;
                if (HasBotControl(character))
                    continue;

                var control = CrewBotControllers.Take();
                if (control == null)
                    return;

                try
                {
                    CrewBotControllers.AlignToCrewOwner(control.Identity.IdentityId, crew);
                    control.Controller.TakeControl(character);
                    ControlByCrewId[crew.CrewId] = control;
                    Log("late TakeControl ok " + crew.CrewId
                        + " char=" + character.EntityId
                        + " ident=" + control.Identity.IdentityId);
                }
                catch (Exception e)
                {
                    CrewBotControllers.Return(control);
                    Log("late TakeControl failed " + crew.CrewId + ": " + e.Message);
                }
            }
        }

        private static bool HasBotControl(IMyCharacter character)
        {
            try
            {
                return character != null
                    && character.ControllerInfo != null
                    && character.ControllerInfo.Controller != null;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// OB-spawned characters are not bot-controlled, so Keen does not apply artificial
        /// gravity / relative dampening the same way. Mirror AiEnabled: set Physics.Gravity
        /// from CalculateArtificialGravityAt and keep jetpack off.
        /// </summary>
        private static void ApplyAmbientPhysics(IMyCharacter character, IMyCubeGrid grid)
        {
            if (character == null || character.Closed || character.Physics == null)
                return;

            try
            {
                float interference;
                Vector3 natural = MyAPIGateway.Physics.CalculateNaturalGravityAt(
                    character.WorldAABB.Center, out interference);
                Vector3 gravity = natural;
                if (gravity.LengthSquared() < 1e-6f)
                    gravity = MyAPIGateway.Physics.CalculateArtificialGravityAt(
                        character.WorldAABB.Center, interference);

                character.Physics.Gravity = gravity;

                if (grid != null && grid.Physics != null
                    && character.Physics.LinearVelocity.LengthSquared() < 0.01f)
                {
                    character.Physics.LinearVelocity = grid.Physics.LinearVelocity;
                }
            }
            catch { }

            ForceStandingPose(character);
        }

        /// <summary>
        /// Freefall/jetpack float pose sticks on uncontrolled characters even under art-grav.
        /// Force jetpack off and Standing unless already in a walk state from MoveAndRotate.
        /// </summary>
        private static void ForceStandingPose(IMyCharacter character)
        {
            if (character == null || character.Closed)
                return;

            try
            {
                var jet = character.Components != null
                    ? character.Components.Get<MyCharacterJetpackComponent>()
                    : null;
                if (jet != null && jet.TurnedOn)
                    jet.TurnOnJetpack(false);
            }
            catch { }

            try
            {
                var ctrl = character as VRage.Game.ModAPI.Interfaces.IMyControllableEntity;
                if (ctrl != null && ctrl.EnabledThrusts)
                    ctrl.SwitchThrusts();
            }
            catch { }

            try
            {
                MyCharacterMovementEnum state = character.CurrentMovementState;
                if (state == MyCharacterMovementEnum.Flying
                    || state == MyCharacterMovementEnum.Falling
                    || state == MyCharacterMovementEnum.Jump)
                {
                    character.CurrentMovementState = MyCharacterMovementEnum.Standing;
                }
            }
            catch { }
        }

        /// <summary>Place feet on the deck so the character leaves freefall support checks.</summary>
        private static void SnapFeetToDeck(IMyCharacter character, IMyTerminalBlock seat)
        {
            if (character == null || character.Closed)
                return;

            Vector3D down;
            if (!TryGetGravityDown(character.GetPosition(), seat, out down))
                return;

            Vector3D up = -down;
            Vector3D pos = character.GetPosition();
            try
            {
                IHitInfo hit;
                if (!MyAPIGateway.Physics.CastRay(pos + up * 1.25, pos + down * 4.0, out hit)
                    || hit == null
                    || hit.HitEntity == null
                    || hit.HitEntity.EntityId == character.EntityId)
                    return;

                character.SetPosition(hit.Position + up * 0.95);
                AlignCharacterToLocalDown(character, seat);
            }
            catch { }
        }

        private static void AlignCharacterToLocalDown(IMyCharacter character, IMyTerminalBlock seat)
        {
            if (character == null || character.Closed)
                return;

            Vector3D down;
            if (!TryGetGravityDown(character.GetPosition(), seat, out down))
                return;

            Vector3D up = -down;
            Vector3D forward = character.WorldMatrix.Forward;
            forward = forward - up * Vector3D.Dot(forward, up);
            if (forward.LengthSquared() < 0.01 && seat != null)
                forward = seat.WorldMatrix.Forward - up * Vector3D.Dot(seat.WorldMatrix.Forward, up);
            if (forward.LengthSquared() < 0.01)
                forward = Vector3D.CalculatePerpendicularVector(up);
            forward.Normalize();

            try
            {
                character.WorldMatrix = MatrixD.CreateWorld(character.GetPosition(), forward, up);
            }
            catch { }
        }

        private static bool TryGetGravityDown(Vector3D worldPos, IMyTerminalBlock seat, out Vector3D down)
        {
            down = Vector3D.Zero;
            try
            {
                float interference;
                Vector3 natural = MyAPIGateway.Physics.CalculateNaturalGravityAt(worldPos, out interference);
                Vector3 gravity = natural;
                if (gravity.LengthSquared() < 1e-6f)
                    gravity = MyAPIGateway.Physics.CalculateArtificialGravityAt(worldPos, interference);
                if (gravity.LengthSquared() > 1e-6f)
                {
                    down = Vector3D.Normalize(gravity);
                    return true;
                }
            }
            catch { }

            if (seat != null)
            {
                down = -seat.WorldMatrix.Up;
                return down.LengthSquared() > 0.01;
            }
            return false;
        }

        private static string ResolveBotSubtype()
        {
            if (!string.IsNullOrEmpty(_resolvedBotSubtype))
                return _resolvedBotSubtype;
            return CrewConfig.AmbientBotSubtype;
        }

        private static void Log(string msg)
        {
            try { MyLog.Default.WriteLine("HireCrew ambient: " + msg); }
            catch { }
        }

        private static IMyCharacter ResolveSpawnedCharacter(long spawnId, Vector3D pos)
        {
            if (spawnId != 0)
            {
                IMyEntity ent;
                if (MyAPIGateway.Entities.TryGetEntityById(spawnId, out ent))
                {
                    var ch = ent as IMyCharacter;
                    if (ch != null && !ch.Closed)
                        return ch;
                }

                try { MyVisualScriptLogicProvider.RemoveEntity(spawnId, false); }
                catch { }
                return null;
            }

            // Last resort: tight scan when API returns 0.
            const double radius = 0.95;
            double r2 = radius * radius;
            IMyCharacter found = null;
            var entities = new HashSet<IMyEntity>();
            MyAPIGateway.Entities.GetEntities(entities);
            foreach (var e in entities)
            {
                var ch = e as IMyCharacter;
                if (ch == null || ch.Closed)
                    continue;
                if (KnownCharacterIds.Contains(ch.EntityId))
                    continue;
                if (Vector3D.DistanceSquared(ch.GetPosition(), pos) > r2)
                    continue;
                if (found != null)
                    return null; // ambiguous
                found = ch;
            }
            return found;
        }

        private static bool TryGetLiveCharacter(CrewRecord crew, out IMyCharacter character)
        {
            character = null;
            if (crew == null || !crew.CharacterEntityId.HasValue)
                return false;
            IMyEntity ent;
            if (!MyAPIGateway.Entities.TryGetEntityById(crew.CharacterEntityId.Value, out ent)
                || ent == null || ent.Closed)
                return false;
            character = ent as IMyCharacter;
            return character != null;
        }

        private static bool IsBotDead(IMyCharacter character)
        {
            if (character == null || character.Closed)
                return true;
            try
            {
                var destroyable = character as IMyDestroyableObject;
                if (destroyable != null && destroyable.Integrity <= 0f)
                    return true;
            }
            catch { }
            return false;
        }

        private static bool NeedsRecovery(IMyTerminalBlock homeSeat, IMyCharacter character)
        {
            if (character == null || homeSeat == null)
                return true;

            double far2 = CrewConfig.AmbientFarFromSeatMeters * CrewConfig.AmbientFarFromSeatMeters;
            if (Vector3D.DistanceSquared(character.GetPosition(), homeSeat.WorldMatrix.Translation) > far2)
                return true;

            IMyCockpit occupied;
            if (TryFindOccupiedCockpit(character, homeSeat.CubeGrid, out occupied) && occupied != null)
            {
                // Wrong seat (including another HireCrew seat or random cockpit).
                if (homeSeat.EntityId != occupied.EntityId)
                    return true;
            }

            return false;
        }

        private static bool TryRecoverHome(
            CrewSession session,
            CrewRecord crew,
            IMyTerminalBlock seat,
            IMyCharacter character,
            HashSet<long> dirtyGrids)
        {
            if (character == null || seat == null)
                return false;

            RemoveFromAnySeat(character, seat.CubeGrid);

            var homeCockpit = seat as IMyCockpit;
            if (homeCockpit == null)
            {
                // Crew stations are TerminalBlocks — cannot AttachPilot; despawn instead.
                return false;
            }

            if (CrewStationLogic.IsSeatOccupiedByPlayer(seat))
                return false;

            try
            {
                homeCockpit.AttachPilot(character);
                if (crew.GridEntityId != 0)
                    dirtyGrids.Add(crew.GridEntityId);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Eject ambient/mission bot from any cockpit so EVA can move the body.</summary>
        public static void ReleaseFromSeat(IMyCharacter character, IMyCubeGrid preferredGrid = null)
        {
            RemoveFromAnySeat(character, preferredGrid);
        }

        private static void RemoveFromAnySeat(IMyCharacter character, IMyCubeGrid preferredGrid = null)
        {
            if (character == null)
                return;

            IMyCockpit cockpit;
            if (TryFindOccupiedCockpit(character, preferredGrid, out cockpit) && cockpit != null)
            {
                try { cockpit.RemovePilot(); }
                catch { }
            }
        }

        private static bool TryFindOccupiedCockpit(IMyCharacter character, IMyCubeGrid preferredGrid, out IMyCockpit cockpit)
        {
            cockpit = null;
            if (character == null)
                return false;

            // Parent is often the seat while piloting.
            var parentCockpit = character.Parent as IMyCockpit;
            if (parentCockpit != null && parentCockpit.Pilot != null
                && parentCockpit.Pilot.EntityId == character.EntityId)
            {
                cockpit = parentCockpit;
                return true;
            }

            IMyCubeGrid grid = preferredGrid;
            if (grid == null)
            {
                var parentBlock = character.Parent as IMyCubeBlock;
                if (parentBlock != null)
                    grid = parentBlock.CubeGrid;
            }
            if (grid == null)
                return false;

            foreach (var c in grid.GetFatBlocks<IMyCockpit>())
            {
                if (c == null || c.Pilot == null)
                    continue;
                if (c.Pilot.EntityId == character.EntityId)
                {
                    cockpit = c;
                    return true;
                }
            }
            return false;
        }

        private static bool IsAnyPlayerNear(Vector3D pos, float meters)
        {
            PlayerScratch.Clear();
            MyAPIGateway.Players.GetPlayers(PlayerScratch);
            double r2 = (double)meters * meters;
            foreach (var p in PlayerScratch)
            {
                if (p == null || p.Character == null || p.Character.Closed)
                    continue;
                if (Vector3D.DistanceSquared(p.Character.GetPosition(), pos) <= r2)
                    return true;
            }
            return false;
        }

        /// <summary>True when the grid is parked / drifting below ambient idle thresholds.</summary>
        public static bool IsGridIdle(IMyCubeGrid grid)
        {
            if (grid == null || grid.Physics == null || !grid.Physics.Enabled)
                return true;

            try
            {
                float lin = grid.Physics.LinearVelocity.Length();
                float ang = grid.Physics.AngularVelocity.Length();
                return lin <= CrewConfig.AmbientGridIdleLinearSpeedMeters
                    && ang <= CrewConfig.AmbientGridIdleAngularSpeedRad;
            }
            catch
            {
                return true;
            }
        }

        /// <summary>Steer an ambient-style character toward a world point (walk).</summary>
        public static bool SteerCharacterToward(
            IMyCharacter character,
            Vector3D worldTarget,
            IMyCubeGrid grid,
            IMyTerminalBlock seat,
            bool running)
        {
            return SteerToward(character, worldTarget, grid, seat, running);
        }

        public static void StopCharacterMovement(IMyCharacter character, IMyCubeGrid grid = null)
        {
            StopMovement(character, grid);
        }

        public static void SetCharacterJetpack(IMyCharacter character, bool enabled)
        {
            if (character == null || character.Closed)
                return;
            try
            {
                var jet = character.Components != null
                    ? character.Components.Get<MyCharacterJetpackComponent>()
                    : null;
                if (jet != null && jet.TurnedOn != enabled)
                    jet.TurnOnJetpack(enabled);
            }
            catch { }

            try
            {
                var ctrl = character as VRage.Game.ModAPI.Interfaces.IMyControllableEntity;
                if (ctrl != null && ctrl.EnabledThrusts != enabled)
                    ctrl.SwitchThrusts();
            }
            catch { }
        }

        /// <summary>
        /// WeaponCore omits entities with this private bit from its target database
        /// (AiDatabase / WeaponTracking). Not a named Keen EntityFlags value.
        /// </summary>
        private const EntityFlags WeaponCoreIgnoreTargetFlag = (EntityFlags)0x20000000;

        /// <summary>
        /// Zero damage modifier so OB/fallback astronauts survive EVA hull clips,
        /// and mark the body ignored by WeaponCore targeting (faction alone is not enough:
        /// WC uses MyIDModule.Owner / ControllingIdentityId, which stay on the bot identity).
        /// HireCrew_Crew SpawnBot also sets Invulnerable in Bots.sbc.
        /// </summary>
        public static void ApplyCrewInvulnerability(IMyCharacter character)
        {
            if (character == null || character.Closed)
                return;
            try { character.CharacterGeneralDamageModifier = 0f; }
            catch { }
            try { character.Flags |= WeaponCoreIgnoreTargetFlag; }
            catch { }
        }

        private static int CountLiveBots(CrewStore store, long gridEntityIdOrZero)
        {
            int n = 0;
            foreach (var crew in store.All)
            {
                if (crew == null || !crew.CharacterEntityId.HasValue)
                    continue;
                if (gridEntityIdOrZero != 0 && crew.GridEntityId != gridEntityIdOrZero)
                    continue;
                IMyEntity ent;
                if (MyAPIGateway.Entities.TryGetEntityById(crew.CharacterEntityId.Value, out ent)
                    && ent != null && !ent.Closed)
                    n++;
            }
            return n;
        }

        private static void RebuildKnownCharacterIds(CrewStore store)
        {
            KnownCharacterIds.Clear();
            foreach (var crew in store.All)
            {
                if (crew != null && crew.CharacterEntityId.HasValue)
                    KnownCharacterIds.Add(crew.CharacterEntityId.Value);
            }
        }
    }
}
