using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRageMath;

namespace HireCrew
{
    [MySessionComponentDescriptor(MyUpdateOrder.AfterSimulation)]
    public sealed class CrewSession : MySessionComponentBase
    {
        public static CrewSession Instance { get; private set; }

        public WeaponAiBridge WeaponAi { get; private set; }
        public CrewStore Store { get; private set; }
        public HirePoolStore HirePools { get; private set; }
        public RepairPathStore RepairPaths { get; private set; }
        public SalvageTargetStore SalvageTargets { get; private set; }
        public CrewPowerBuff PowerBuff { get; private set; }
        private readonly List<SalvageTargetEntry> _salvageTargetSyncBuf = new List<SalvageTargetEntry>();
        private readonly List<long> _salvageHomeIdScratch = new List<long>(16);
        private readonly List<IMyCubeGrid> _salvageGridGroupScratch = new List<IMyCubeGrid>(16);

        // Same instance required for UnregisterSecureMessageHandler.
        private Action<ushort, byte[], ulong, bool> _messageHandler;
        private int _tick;
        private readonly HashSet<ulong> _rosterSyncedSteamIds = new HashSet<ulong>();
        private readonly HashSet<long> _powerBuffGridIds = new HashSet<long>();
        private readonly Random _hireRng = new Random();
        private CrewHud _hud;
        private CrewBlockInfo _blockInfo;
        private readonly List<RepairMissionSnapshotEntry> _clientRepairMissions = new List<RepairMissionSnapshotEntry>();
        private readonly List<RepairMissionSnapshotEntry> _repairMissionSyncBuf = new List<RepairMissionSnapshotEntry>();
        private readonly List<SalvageMissionSnapshotEntry> _clientSalvageMissions = new List<SalvageMissionSnapshotEntry>();
        private readonly List<SalvageMissionSnapshotEntry> _salvageMissionSyncBuf = new List<SalvageMissionSnapshotEntry>();

        public IList<RepairMissionSnapshotEntry> ClientRepairMissions { get { return _clientRepairMissions; } }
        public IList<SalvageMissionSnapshotEntry> ClientSalvageMissions { get { return _clientSalvageMissions; } }

        public override void LoadData()
        {
            Instance = this;
            Store = new CrewStore();
            HirePools = new HirePoolStore();
            RepairPaths = new RepairPathStore();
            SalvageTargets = new SalvageTargetStore();
            PowerBuff = new CrewPowerBuff();
            WeaponAi = new WeaponAiBridge();
            WeaponAi.Load();
            _messageHandler = OnMessage;
            CrewNetworking.Register(_messageHandler);
            _hud = new CrewHud();
            _hud.Init();
            _blockInfo = new CrewBlockInfo();
            _blockInfo.Init();
            CrewHireBlockLogic.EnsureTerminalControls();
            CrewCockpitControls.EnsureTerminalControls();
        }

        public override void SaveData()
        {
            if (!MyAPIGateway.Multiplayer.IsServer || Store == null) return;
            // Ambient bots must not serialize into the sandbox save (causes duplicates on reload).
            CrewAmbientPresence.DespawnAllForSave(this);
            var bytes = Store.ToBytes() ?? new byte[0];
            var b64 = Convert.ToBase64String(bytes);
            // Dual-write: world variable + WorldStorage file (dedicated save/load resilience).
            MyAPIGateway.Utilities.SetVariable("HireCrew_Store", b64);
            try
            {
                using (var writer = MyAPIGateway.Utilities.WriteFileInWorldStorage("HireCrew.dat", typeof(CrewSession)))
                    writer.Write(b64);
            }
            catch
            {
                // Variable path remains as fallback.
            }

            if (HirePools != null)
            {
                var poolB64 = Convert.ToBase64String(HirePools.ToBytes() ?? new byte[0]);
                MyAPIGateway.Utilities.SetVariable("HireCrew_HirePools", poolB64);
                try
                {
                    using (var writer = MyAPIGateway.Utilities.WriteFileInWorldStorage("HireCrewPools.dat", typeof(CrewSession)))
                        writer.Write(poolB64);
                }
                catch { }
            }

            if (RepairPaths != null)
            {
                var pathB64 = Convert.ToBase64String(RepairPaths.ToBytes() ?? new byte[0]);
                MyAPIGateway.Utilities.SetVariable("HireCrew_RepairPaths", pathB64);
                try
                {
                    using (var writer = MyAPIGateway.Utilities.WriteFileInWorldStorage("HireCrewRepairPaths.dat", typeof(CrewSession)))
                        writer.Write(pathB64);
                }
                catch { }
            }

            if (SalvageTargets != null)
            {
                var salvageB64 = Convert.ToBase64String(SalvageTargets.ToBytes() ?? new byte[0]);
                MyAPIGateway.Utilities.SetVariable("HireCrew_SalvageTargets", salvageB64);
                try
                {
                    using (var writer = MyAPIGateway.Utilities.WriteFileInWorldStorage("HireCrewSalvageTargets.dat", typeof(CrewSession)))
                        writer.Write(salvageB64);
                }
                catch { }
            }
        }

        public override void BeforeStart()
        {
            // Utilities/chat are reliable here; LoadData can be too early on some clients.
            if (_hud != null)
                _hud.EnsureChatRegistered();
            CrewCockpitControls.EnsureTerminalControls();

            // Clients use the same clamps for terminal UI; server remains authoritative on apply.
            LoadHireWorldConfig();

            if (!MyAPIGateway.Multiplayer.IsServer) return;
            byte[] payload = TryLoadStoreBytes();
            if (payload != null)
            {
                try { Store = CrewStore.FromBytes(payload); }
                catch { Store = new CrewStore(); }
            }
            byte[] poolPayload = TryLoadHirePoolBytes();
            if (poolPayload != null)
            {
                try { HirePools = HirePoolStore.FromBytes(poolPayload); }
                catch { HirePools = new HirePoolStore(); }
            }
            byte[] pathPayload = TryLoadRepairPathBytes();
            if (pathPayload != null)
            {
                try { RepairPaths = RepairPathStore.FromBytes(pathPayload); }
                catch { RepairPaths = new RepairPathStore(); }
            }
            byte[] salvagePayload = TryLoadSalvageTargetBytes();
            if (salvagePayload != null)
            {
                try { SalvageTargets = SalvageTargetStore.FromBytes(salvagePayload); }
                catch { SalvageTargets = new SalvageTargetStore(); }
            }
            BroadcastSalvageTargetSync();
            RestoreAssignmentsFromStore();
            // Worlds saved before DespawnAllForSave may still contain leftover ambient bodies.
            CrewAmbientPresence.PurgeOrphanAmbientCharacters();
            if (_blockInfo != null)
                _blockInfo.RefreshAssigned();
            CrewStationLogic.RefreshAll();
        }

        private static byte[] TryLoadStoreBytes()
        {
            // Prefer world variable (always written); fall back to WorldStorage file (best-effort).
            string varB64;
            if (MyAPIGateway.Utilities.GetVariable("HireCrew_Store", out varB64) && !string.IsNullOrEmpty(varB64))
            {
                try { return Convert.FromBase64String(varB64); }
                catch { /* fall through to file */ }
            }

            try
            {
                if (MyAPIGateway.Utilities.FileExistsInWorldStorage("HireCrew.dat", typeof(CrewSession)))
                {
                    using (var reader = MyAPIGateway.Utilities.ReadFileInWorldStorage("HireCrew.dat", typeof(CrewSession)))
                    {
                        var b64 = reader.ReadToEnd();
                        if (!string.IsNullOrEmpty(b64))
                            return Convert.FromBase64String(b64);
                    }
                }
            }
            catch
            {
                // No usable persistence.
            }

            return null;
        }

        private static byte[] TryLoadHirePoolBytes()
        {
            string varB64;
            if (MyAPIGateway.Utilities.GetVariable("HireCrew_HirePools", out varB64) && !string.IsNullOrEmpty(varB64))
            {
                try { return Convert.FromBase64String(varB64); }
                catch { }
            }

            try
            {
                if (MyAPIGateway.Utilities.FileExistsInWorldStorage("HireCrewPools.dat", typeof(CrewSession)))
                {
                    using (var reader = MyAPIGateway.Utilities.ReadFileInWorldStorage("HireCrewPools.dat", typeof(CrewSession)))
                    {
                        var b64 = reader.ReadToEnd();
                        if (!string.IsNullOrEmpty(b64))
                            return Convert.FromBase64String(b64);
                    }
                }
            }
            catch { }

            return null;
        }

        private static byte[] TryLoadRepairPathBytes()
        {
            string varB64;
            if (MyAPIGateway.Utilities.GetVariable("HireCrew_RepairPaths", out varB64) && !string.IsNullOrEmpty(varB64))
            {
                try { return Convert.FromBase64String(varB64); }
                catch { }
            }

            try
            {
                if (MyAPIGateway.Utilities.FileExistsInWorldStorage("HireCrewRepairPaths.dat", typeof(CrewSession)))
                {
                    using (var reader = MyAPIGateway.Utilities.ReadFileInWorldStorage("HireCrewRepairPaths.dat", typeof(CrewSession)))
                    {
                        var b64 = reader.ReadToEnd();
                        if (!string.IsNullOrEmpty(b64))
                            return Convert.FromBase64String(b64);
                    }
                }
            }
            catch { }

            return null;
        }

        private static byte[] TryLoadSalvageTargetBytes()
        {
            string varB64;
            if (MyAPIGateway.Utilities.GetVariable("HireCrew_SalvageTargets", out varB64) && !string.IsNullOrEmpty(varB64))
            {
                try { return Convert.FromBase64String(varB64); }
                catch { }
            }

            try
            {
                if (MyAPIGateway.Utilities.FileExistsInWorldStorage("HireCrewSalvageTargets.dat", typeof(CrewSession)))
                {
                    using (var reader = MyAPIGateway.Utilities.ReadFileInWorldStorage("HireCrewSalvageTargets.dat", typeof(CrewSession)))
                    {
                        var b64 = reader.ReadToEnd();
                        if (!string.IsNullOrEmpty(b64))
                            return Convert.FromBase64String(b64);
                    }
                }
            }
            catch { }

            return null;
        }

        protected override void UnloadData()
        {
            if (_hud != null)
            {
                _hud.Unload();
                _hud = null;
            }
            CrewCockpitControls.Unload();
            if (_blockInfo != null)
            {
                _blockInfo.Unload();
                _blockInfo = null;
            }
            if (_messageHandler != null)
            {
                CrewNetworking.Unregister(_messageHandler);
                _messageHandler = null;
            }
            if (MyAPIGateway.Multiplayer != null && MyAPIGateway.Multiplayer.IsServer && Store != null)
                CrewAmbientPresence.DespawnAll(this);
            CrewAmbientPresence.ClearRuntime();
            CrewRepairMission.ClearAll();
            CrewSalvageMission.ClearAll();
            CrewSalvageTargetPainter.SetActive(false, 0);
            CrewSalvageTargetHighlight.ClearAll();
            if (WeaponAi != null) WeaponAi.Unload();
            WeaponAi = null;
            PowerBuff = null;
            Store = null;
            HirePools = null;
            RepairPaths = null;
            HireWorldConfig.ClearCurrent();
            if (Instance == this) Instance = null;
        }

        private void LoadHireWorldConfig()
        {
            try
            {
                if (MyAPIGateway.Utilities != null
                    && MyAPIGateway.Utilities.FileExistsInWorldStorage(HireWorldConfig.FileName, typeof(CrewSession)))
                {
                    using (var reader = MyAPIGateway.Utilities.ReadFileInWorldStorage(
                        HireWorldConfig.FileName, typeof(CrewSession)))
                    {
                        var text = reader.ReadToEnd();
                        if (!string.IsNullOrEmpty(text))
                        {
                            // SE whitelist: use ModAPI XML helpers, not System.Xml.Serialization.
                            var loaded = MyAPIGateway.Utilities.SerializeFromXML<HireWorldConfig>(text);
                            if (loaded != null)
                            {
                                HireWorldConfig.SetCurrent(loaded);
                                return;
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                try
                {
                    MyAPIGateway.Utilities.ShowMessage("HireCrew", "HireCrewConfig.xml invalid — using defaults");
                    VRage.Utils.MyLog.Default.WriteLine("HireCrew: HireCrewConfig.xml load failed: " + e.Message);
                }
                catch { }
            }

            var defaults = HireWorldConfig.CreateDefaults();
            HireWorldConfig.SetCurrent(defaults);
            if (MyAPIGateway.Multiplayer == null || !MyAPIGateway.Multiplayer.IsServer)
                return;
            try
            {
                var xml = MyAPIGateway.Utilities.SerializeToXML(defaults);
                using (var writer = MyAPIGateway.Utilities.WriteFileInWorldStorage(
                    HireWorldConfig.FileName, typeof(CrewSession)))
                    writer.Write(xml);
            }
            catch (Exception e)
            {
                try
                {
                    VRage.Utils.MyLog.Default.WriteLine("HireCrew: HireCrewConfig.xml save failed: " + e.Message);
                }
                catch { }
            }
        }

        public override void UpdateAfterSimulation()
        {
            if (_hud != null)
                _hud.Update();
            CrewPathPainter.Update(this);
            CrewSalvageTargetPainter.Update(this);
            CrewSalvageTargetHighlight.Draw();

            if (!MyAPIGateway.Multiplayer.IsServer || Store == null) return;
            // Wander steering must run every frame; spawn/lifecycle stays ~1 Hz.
            CrewAmbientPresence.UpdateMovement(this);
            CrewRepairMission.UpdateMovement(this);
            CrewSalvageMission.UpdateMovement(this);
            // Harvest dummy bot controllers often (AiEnabled pattern) so ambient NPCs can TakeControl.
            if (_tick % 5 == 0)
                CrewBotControllers.Tick();
            _tick++;
            if (_tick % 60 != 0) return;
            SyncRosterToNewPlayers();
            TickTrainingCompletions();
            WatchCrewIntegrity();
            CrewAmbientPresence.Tick(this);
            CrewRepairMission.Tick(this);
            CrewSalvageMission.Tick(this);
            TickRepairMissionSync();
            TickSalvageMissionSync();
            RefreshAllGridBuffs();
            if (HirePools != null && HirePools.TickRefresh(DateTime.UtcNow, _hireRng))
            {
                // Clients pull on open; no broadcast storm.
            }
        }

        private void TickRepairMissionSync()
        {
            if (!MyAPIGateway.Multiplayer.IsServer) return;

            CrewRepairMission.CollectActiveSnapshots(_repairMissionSyncBuf);

            if (!MyAPIGateway.Utilities.IsDedicated)
            {
                _clientRepairMissions.Clear();
                for (int i = 0; i < _repairMissionSyncBuf.Count; i++)
                    _clientRepairMissions.Add(_repairMissionSyncBuf[i]);
            }

            var sync = new RepairMissionSync
            {
                Entries = new List<RepairMissionSnapshotEntry>(_repairMissionSyncBuf)
            };
            byte[] data = CrewNetworking.Serialize(sync);

            var players = new List<IMyPlayer>();
            MyAPIGateway.Players.GetPlayers(players);
            ulong localSteam = MyAPIGateway.Multiplayer.MyId;
            for (int i = 0; i < players.Count; i++)
            {
                var p = players[i];
                if (p == null) continue;
                ulong steam = p.SteamUserId;
                if (steam == 0) continue;
                if (!MyAPIGateway.Utilities.IsDedicated && steam == localSteam)
                    continue;
                CrewNetworking.SendToPlayer(CrewNetworking.RepairMissionSyncMsg, data, steam);
            }
        }

        private void TickSalvageMissionSync()
        {
            if (!MyAPIGateway.Multiplayer.IsServer) return;

            CrewSalvageMission.CollectActiveSnapshots(_salvageMissionSyncBuf);

            if (!MyAPIGateway.Utilities.IsDedicated)
            {
                _clientSalvageMissions.Clear();
                for (int i = 0; i < _salvageMissionSyncBuf.Count; i++)
                    _clientSalvageMissions.Add(_salvageMissionSyncBuf[i]);
            }

            var sync = new SalvageMissionSync
            {
                Entries = new List<SalvageMissionSnapshotEntry>(_salvageMissionSyncBuf)
            };
            byte[] data = CrewNetworking.Serialize(sync);

            var players = new List<IMyPlayer>();
            MyAPIGateway.Players.GetPlayers(players);
            ulong localSteam = MyAPIGateway.Multiplayer.MyId;
            for (int i = 0; i < players.Count; i++)
            {
                var p = players[i];
                if (p == null) continue;
                ulong steam = p.SteamUserId;
                if (steam == 0) continue;
                if (!MyAPIGateway.Utilities.IsDedicated && steam == localSteam)
                    continue;
                CrewNetworking.SendToPlayer(CrewNetworking.SalvageMissionSyncMsg, data, steam);
            }
        }

        /// <summary>Ambient presence changed CharacterEntityId; push roster so clients hide subparts.</summary>
        public void NotifyAmbientRosterChanged(long gridEntityId)
        {
            if (!MyAPIGateway.Multiplayer.IsServer || Store == null) return;
            BroadcastRoster(gridEntityId);
        }

        /// <summary>
        /// Join-time roster push: each new steam id gets a full RosterSync once present.
        /// Throttled catch-up (no reliable PlayerJoined on all SE mod API versions).
        /// </summary>
        private void SyncRosterToNewPlayers()
        {
            var players = new List<IMyPlayer>();
            MyAPIGateway.Players.GetPlayers(players);

            var present = new HashSet<ulong>();
            foreach (var p in players)
            {
                if (p == null) continue;
                var steamId = p.SteamUserId;
                present.Add(steamId);
                if (_rosterSyncedSteamIds.Contains(steamId)) continue;

                var sync = new RosterSync
                {
                    GridEntityId = 0,
                    StoreBytes = Store.ToBytes()
                };
                CrewNetworking.SendToPlayer(CrewNetworking.RosterMsg, CrewNetworking.Serialize(sync), steamId);
                if (SalvageTargets != null)
                {
                    SalvageTargets.CopyTo(_salvageTargetSyncBuf);
                    var salvageSync = new SalvageTargetSync
                    {
                        Entries = new List<SalvageTargetEntry>(_salvageTargetSyncBuf)
                    };
                    CrewNetworking.SendToPlayer(
                        CrewNetworking.SalvageTargetSyncMsg,
                        CrewNetworking.Serialize(salvageSync),
                        steamId);
                }
                _rosterSyncedSteamIds.Add(steamId);
            }

            // Drop departed players so reconnect gets a fresh push.
            _rosterSyncedSteamIds.RemoveWhere(id => !present.Contains(id));
        }

        private void OnMessage(ushort id, byte[] data, ulong sender, bool fromServer)
        {
            if (id == CrewNetworking.NotifyMsg)
            {
                if (MyAPIGateway.Multiplayer.IsServer) return;
                var notify = CrewNetworking.Deserialize<NotifyMessage>(data);
                if (notify != null && !string.IsNullOrEmpty(notify.Text))
                    MyAPIGateway.Utilities.ShowMessage("HireCrew", notify.Text);
                return;
            }

            if (id == CrewNetworking.RosterMsg)
            {
                if (MyAPIGateway.Multiplayer.IsServer) return;
                var sync = CrewNetworking.Deserialize<RosterSync>(data);
                if (sync != null && sync.StoreBytes != null)
                {
                    try { Store = CrewStore.FromBytes(sync.StoreBytes); }
                    catch { /* keep existing client store */ }
                    if (_blockInfo != null)
                        _blockInfo.RefreshAssigned();
                    CrewStationLogic.RefreshAll();
                }
                return;
            }

            if (id == CrewNetworking.HirePoolSyncMsg)
            {
                if (MyAPIGateway.Multiplayer.IsServer) return;
                var sync = CrewNetworking.Deserialize<HirePoolSync>(data);
                if (sync == null || sync.PoolBytes == null) return;
                try
                {
                    var pool = HirePoolStore.DeserializePool(sync.PoolBytes);
                    if (pool == null) return;
                    if (HirePools == null) HirePools = new HirePoolStore();
                    HirePools.Upsert(pool);
                    if (_hud != null)
                        _hud.OnHirePoolSynced(pool);
                }
                catch { }
                return;
            }

            if (id == CrewNetworking.RepairMissionSyncMsg)
            {
                if (MyAPIGateway.Multiplayer.IsServer) return;
                var sync = CrewNetworking.Deserialize<RepairMissionSync>(data);
                _clientRepairMissions.Clear();
                if (sync != null && sync.Entries != null)
                {
                    for (int i = 0; i < sync.Entries.Count; i++)
                    {
                        if (sync.Entries[i] != null)
                            _clientRepairMissions.Add(sync.Entries[i]);
                    }
                }
                return;
            }

            if (id == CrewNetworking.SalvageMissionSyncMsg)
            {
                if (MyAPIGateway.Multiplayer.IsServer) return;
                var sync = CrewNetworking.Deserialize<SalvageMissionSync>(data);
                _clientSalvageMissions.Clear();
                if (sync != null && sync.Entries != null)
                {
                    for (int i = 0; i < sync.Entries.Count; i++)
                    {
                        if (sync.Entries[i] != null)
                            _clientSalvageMissions.Add(sync.Entries[i]);
                    }
                }
                return;
            }

            if (id == CrewNetworking.SalvageTargetSyncMsg)
            {
                if (MyAPIGateway.Multiplayer.IsServer) return;
                var sync = CrewNetworking.Deserialize<SalvageTargetSync>(data);
                CrewSalvageTargetHighlight.ApplySync(sync != null ? sync.Entries : null);
                return;
            }

            if (!MyAPIGateway.Multiplayer.IsServer) return;

            var identityId = GetIdentityId(sender);
            if (id == CrewNetworking.HireMsg)
                HandleHire(CrewNetworking.Deserialize<HireRequest>(data), identityId, sender);
            else if (id == CrewNetworking.HireFromPoolMsg)
                HandleHireFromPool(CrewNetworking.Deserialize<HireFromPoolRequest>(data), identityId, sender);
            else if (id == CrewNetworking.HireRefreshMsg)
                HandleHireRefresh(CrewNetworking.Deserialize<HireRefreshRequest>(data), identityId, sender);
            else if (id == CrewNetworking.HirePoolRequestMsg)
                HandleHirePoolRequest(CrewNetworking.Deserialize<HirePoolRequest>(data), identityId, sender);
            else if (id == CrewNetworking.AssignMsg)
                HandleAssign(CrewNetworking.Deserialize<AssignRequest>(data), identityId, sender);
            else if (id == CrewNetworking.BulkAssignMsg)
                HandleBulkAssign(CrewNetworking.Deserialize<BulkAssignRequest>(data), identityId, sender);
            else if (id == CrewNetworking.AssignAmenityMsg)
                HandleAssignAmenity(CrewNetworking.Deserialize<AssignAmenityRequest>(data), identityId, sender);
            else if (id == CrewNetworking.DismissMsg)
                HandleDismiss(CrewNetworking.Deserialize<DismissRequest>(data), identityId, sender);
            else if (id == CrewNetworking.UnassignMsg)
                HandleUnassign(CrewNetworking.Deserialize<UnassignRequest>(data), identityId, sender);
            else if (id == CrewNetworking.TrainMsg)
                HandleTrain(CrewNetworking.Deserialize<TrainRequest>(data), identityId, sender);
            else if (id == CrewNetworking.CancelTrainMsg)
                HandleCancelTrain(CrewNetworking.Deserialize<CancelTrainRequest>(data), identityId, sender);
            else if (id == CrewNetworking.AdminCommandMsg)
                HandleAdminCommand(CrewNetworking.Deserialize<AdminCommandRequest>(data), identityId, sender);
            else if (id == CrewNetworking.PathEditMsg)
                HandlePathEdit(CrewNetworking.Deserialize<PathEditRequest>(data), identityId, sender);
            else if (id == CrewNetworking.RepairDispatchMsg)
                HandleRepairDispatch(CrewNetworking.Deserialize<RepairDispatchRequest>(data), identityId, sender);
            else if (id == CrewNetworking.SalvageDispatchMsg)
                HandleSalvageDispatch(CrewNetworking.Deserialize<SalvageDispatchRequest>(data), identityId, sender);
            else if (id == CrewNetworking.SalvageTargetEditMsg)
                HandleSalvageTargetEdit(CrewNetworking.Deserialize<SalvageTargetEditRequest>(data), identityId, sender);
        }

        public void ClientRequestAdmin(AdminCommandRequest req)
        {
            if (req == null) return;

            // Stamp local controlled/managed grid so server fill/reroll-near don't rely on
            // server-side IMyPlayer.Controller (often empty for the chat sender).
            if (req.GridEntityId == 0)
            {
                IMyCubeGrid localGrid;
                string ignore;
                if (TryGetLocalManagedGrid(out localGrid, out ignore) && localGrid != null)
                    req.GridEntityId = localGrid.EntityId;
                else if (TryGetLocalControlledGrid(out localGrid) && localGrid != null)
                    req.GridEntityId = localGrid.EntityId;
            }

            var data = CrewNetworking.Serialize(req);
            if (MyAPIGateway.Multiplayer.IsServer)
            {
                var player = MyAPIGateway.Session != null ? MyAPIGateway.Session.Player : null;
                long identityId = player != null ? player.IdentityId : 0;
                HandleAdminCommand(req, identityId, MyAPIGateway.Multiplayer.MyId);
            }
            else
                CrewNetworking.SendToServer(CrewNetworking.AdminCommandMsg, data);
        }

        /// <summary>Local player's controlled ship controller grid (no ownership check).</summary>
        public bool TryGetLocalControlledGrid(out IMyCubeGrid grid)
        {
            grid = null;
            var player = MyAPIGateway.Session != null ? MyAPIGateway.Session.Player : null;
            if (player == null)
                return false;
            try
            {
                var controlled = player.Controller != null
                    ? player.Controller.ControlledEntity as IMyShipController
                    : null;
                if (controlled != null && controlled.CubeGrid != null && !controlled.CubeGrid.Closed)
                {
                    grid = controlled.CubeGrid;
                    return true;
                }
            }
            catch { }
            return false;
        }

        public void ClientRequestPathEdit(PathEditRequest req)
        {
            if (req == null) return;
            var data = CrewNetworking.Serialize(req);
            if (MyAPIGateway.Multiplayer.IsServer)
                HandlePathEdit(req, MyAPIGateway.Session.Player.IdentityId, MyAPIGateway.Multiplayer.MyId);
            else
                CrewNetworking.SendToServer(CrewNetworking.PathEditMsg, data);
        }

        public void ClientRequestRepairDispatch(string crewId, bool recall)
        {
            if (string.IsNullOrEmpty(crewId))
                return;
            var req = new RepairDispatchRequest { CrewId = crewId, Recall = recall };
            var data = CrewNetworking.Serialize(req);
            if (MyAPIGateway.Multiplayer.IsServer)
                HandleRepairDispatch(req, MyAPIGateway.Session.Player.IdentityId, MyAPIGateway.Multiplayer.MyId);
            else
                CrewNetworking.SendToServer(CrewNetworking.RepairDispatchMsg, data);
        }

        public void ClientRequestSalvageDispatch(string crewId, bool recall, long targetGridEntityId)
        {
            if (string.IsNullOrEmpty(crewId))
                return;
            var req = new SalvageDispatchRequest
            {
                CrewId = crewId,
                Recall = recall,
                TargetGridEntityId = targetGridEntityId
            };
            var data = CrewNetworking.Serialize(req);
            if (MyAPIGateway.Multiplayer.IsServer)
                HandleSalvageDispatch(req, MyAPIGateway.Session.Player.IdentityId, MyAPIGateway.Multiplayer.MyId);
            else
                CrewNetworking.SendToServer(CrewNetworking.SalvageDispatchMsg, data);
        }

        public void ClientRequestSalvageTargetEdit(long homeGridEntityId, long targetGridEntityId, bool clearAllManaged = false)
        {
            if (!clearAllManaged && homeGridEntityId == 0)
                return;
            var req = new SalvageTargetEditRequest
            {
                HomeGridEntityId = homeGridEntityId,
                TargetGridEntityId = targetGridEntityId,
                ClearAllManaged = clearAllManaged
            };
            var data = CrewNetworking.Serialize(req);
            if (MyAPIGateway.Multiplayer.IsServer)
                HandleSalvageTargetEdit(req, MyAPIGateway.Session.Player.IdentityId, MyAPIGateway.Multiplayer.MyId);
            else
                CrewNetworking.SendToServer(CrewNetworking.SalvageTargetEditMsg, data);
        }

        private void HandleSalvageTargetEdit(SalvageTargetEditRequest req, long identityId, ulong steamId)
        {
            if (req == null || SalvageTargets == null)
                return;

            if (req.TargetGridEntityId == 0 && req.ClearAllManaged)
            {
                int removed = SalvageTargets.ClearWhereHome(homeId =>
                {
                    IMyCubeGrid g;
                    return TryGetGrid(homeId, out g) && g != null && HasManagePermission(identityId, g);
                });
                int recalled = CrewSalvageMission.RecallMissionsOnManagedHomes(identityId, HasManagePermission);
                BroadcastSalvageTargetSync();
                string clearMsg = removed > 0
                    ? "Salvage: cleared " + removed + " mark(s)"
                    : "Salvage: no marks to clear";
                if (recalled > 0)
                    clearMsg += " — recalling " + recalled;
                Notify(steamId, clearMsg);
                return;
            }

            if (req.HomeGridEntityId == 0)
                return;

            IMyCubeGrid home;
            if (!TryGetGrid(req.HomeGridEntityId, out home) || home == null)
            {
                Notify(steamId, "Salvage: home grid missing");
                return;
            }
            if (!HasManagePermission(identityId, home))
            {
                Notify(steamId, "No permission");
                return;
            }

            if (req.TargetGridEntityId == 0)
            {
                CollectLinkedHomeGridIds(home, _salvageHomeIdScratch);
                SalvageTargets.ClearHomeIds(_salvageHomeIdScratch);
                SalvageTargets.ClearConstruct(home, ResolveGridById);
                int recalled = CrewSalvageMission.RetargetHomeMissions(home, 0);
                BroadcastSalvageTargetSync();
                Notify(steamId, recalled > 0
                    ? "Salvage: target cleared — recalling " + recalled
                    : "Salvage: target cleared");
                return;
            }

            IMyCubeGrid target;
            if (!TryGetGrid(req.TargetGridEntityId, out target) || target == null)
            {
                Notify(steamId, "Salvage: look at a grid");
                return;
            }

            // Ownership check uses a synthetic crew-like viewer from the requesting player.
            long viewerFaction = 0;
            try
            {
                var f = MyAPIGateway.Session.Factions.TryGetPlayerFaction(identityId);
                if (f != null) viewerFaction = f.FactionId;
            }
            catch { }

            long primary = 0;
            try
            {
                var owners = target.BigOwners;
                if (owners != null && owners.Count > 0)
                    primary = owners[0];
            }
            catch { }

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

            var rel = CrewSalvageRules.ClassifyTarget(identityId, viewerFaction, primary, gridFaction);
            if (!CrewSalvageRules.IsLegalTarget(rel))
            {
                Notify(steamId, "Salvage: illegal target (enemy)");
                return;
            }

            double radius = CrewConfig.SalvageScanRadiusMeters;
            if (Vector3D.DistanceSquared(home.WorldAABB.Center, target.WorldAABB.Center) > radius * radius)
            {
                Notify(steamId, "Salvage: target out of range");
                return;
            }

            // Frozen padded AABB — debris that stay inside remain salvageable.
            BoundingBoxD zone = SalvageTargetStore.BuildZoneFromGrid(target);
            CollectLinkedHomeGridIds(home, _salvageHomeIdScratch);
            AppendSalvageOpsHomeIds(home, _salvageHomeIdScratch);
            SalvageTargets.SetZoneForHomeIds(_salvageHomeIdScratch, req.TargetGridEntityId, zone);
            int retargeted = CrewSalvageMission.RetargetHomeMissions(home, zone, req.TargetGridEntityId);
            BroadcastSalvageTargetSync();
            string targetName = !string.IsNullOrEmpty(target.CustomName) ? target.CustomName : ("Grid " + target.EntityId);
            string homeName = !string.IsNullOrEmpty(home.CustomName) ? home.CustomName : ("Grid " + home.EntityId);
            string msg = "Salvage: " + homeName + " -> zone around " + targetName;
            if (retargeted > 0)
                msg += " (" + retargeted + " retargeted)";
            Notify(steamId, msg);
        }

        private void CollectLinkedHomeGridIds(IMyCubeGrid home, List<long> into)
        {
            into.Clear();
            if (home == null) return;
            into.Add(home.EntityId);

            _salvageGridGroupScratch.Clear();
            try
            {
                MyAPIGateway.GridGroups.GetGroup(home, GridLinkTypeEnum.Mechanical, _salvageGridGroupScratch);
            }
            catch { _salvageGridGroupScratch.Clear(); }
            AppendUniqueGridIds(_salvageGridGroupScratch, into);

            _salvageGridGroupScratch.Clear();
            try
            {
                MyAPIGateway.GridGroups.GetGroup(home, GridLinkTypeEnum.Physical, _salvageGridGroupScratch);
            }
            catch { _salvageGridGroupScratch.Clear(); }
            AppendUniqueGridIds(_salvageGridGroupScratch, into);
        }

        private void AppendSalvageOpsHomeIds(IMyCubeGrid home, List<long> into)
        {
            if (home == null || Store == null || into == null) return;
            foreach (var crew in Store.All)
            {
                if (crew == null || crew.Role != CrewRole.SalvageOps)
                    continue;
                if (crew.Status != CrewStatus.Seated || crew.GridEntityId == 0)
                    continue;
                IMyCubeGrid crewHome;
                if (!TryGetGrid(crew.GridEntityId, out crewHome) || crewHome == null)
                    continue;
                if (!HomesLinked(home, crewHome))
                    continue;
                AppendUniqueId(into, crew.GridEntityId);
            }
        }

        private bool HomesLinked(IMyCubeGrid a, IMyCubeGrid b)
        {
            if (a == null || b == null) return false;
            if (a.EntityId == b.EntityId) return true;
            try
            {
                if (a.IsSameConstructAs(b))
                    return true;
            }
            catch { }

            _salvageGridGroupScratch.Clear();
            try
            {
                MyAPIGateway.GridGroups.GetGroup(a, GridLinkTypeEnum.Mechanical, _salvageGridGroupScratch);
            }
            catch { _salvageGridGroupScratch.Clear(); }
            for (int i = 0; i < _salvageGridGroupScratch.Count; i++)
            {
                if (_salvageGridGroupScratch[i] != null
                    && _salvageGridGroupScratch[i].EntityId == b.EntityId)
                    return true;
            }

            _salvageGridGroupScratch.Clear();
            try
            {
                MyAPIGateway.GridGroups.GetGroup(a, GridLinkTypeEnum.Physical, _salvageGridGroupScratch);
            }
            catch { _salvageGridGroupScratch.Clear(); }
            for (int i = 0; i < _salvageGridGroupScratch.Count; i++)
            {
                if (_salvageGridGroupScratch[i] != null
                    && _salvageGridGroupScratch[i].EntityId == b.EntityId)
                    return true;
            }
            return false;
        }

        private bool ResolveSalvageZone(IMyCubeGrid home, out BoundingBoxD zone, out long seedGridEntityId)
        {
            zone = default(BoundingBoxD);
            seedGridEntityId = 0;
            if (home == null || SalvageTargets == null)
                return false;

            if (SalvageTargets.TryGetZoneForConstruct(home, ResolveGridById, out zone, out seedGridEntityId))
                return true;

            CollectLinkedHomeGridIds(home, _salvageHomeIdScratch);
            for (int i = 0; i < _salvageHomeIdScratch.Count; i++)
            {
                if (SalvageTargets.TryGetZone(_salvageHomeIdScratch[i], out zone))
                {
                    seedGridEntityId = SalvageTargets.GetTarget(_salvageHomeIdScratch[i]);
                    return true;
                }
            }

            return SalvageTargets.TryFindZoneWhereHome(markHomeId =>
            {
                IMyCubeGrid markHome;
                if (!TryGetGrid(markHomeId, out markHome) || markHome == null)
                    return false;
                return HomesLinked(home, markHome);
            }, out zone, out seedGridEntityId);
        }

        private long ResolveSalvageTargetId(IMyCubeGrid home)
        {
            BoundingBoxD zone;
            long seed;
            if (!ResolveSalvageZone(home, out zone, out seed))
                return 0;
            return seed != 0 ? seed : 1;
        }

        private static void AppendUniqueGridIds(List<IMyCubeGrid> grids, List<long> into)
        {
            if (grids == null || into == null) return;
            for (int i = 0; i < grids.Count; i++)
            {
                var g = grids[i];
                if (g == null || g.Closed) continue;
                AppendUniqueId(into, g.EntityId);
            }
        }

        private static void AppendUniqueId(List<long> into, long id)
        {
            if (into == null || id == 0) return;
            for (int j = 0; j < into.Count; j++)
            {
                if (into[j] == id)
                    return;
            }
            into.Add(id);
        }

        private static IMyCubeGrid ResolveGridById(long entityId)
        {
            IMyCubeGrid g;
            return TryGetGrid(entityId, out g) ? g : null;
        }

        /// <summary>Clear frozen salvage zone for a home construct and sync highlights.</summary>
        public void ClearSalvageMarkForHome(long homeGridEntityId)
        {
            if (SalvageTargets == null || homeGridEntityId == 0)
                return;

            IMyCubeGrid home;
            if (TryGetGrid(homeGridEntityId, out home) && home != null)
            {
                CollectLinkedHomeGridIds(home, _salvageHomeIdScratch);
                SalvageTargets.ClearHomeIds(_salvageHomeIdScratch);
                SalvageTargets.ClearConstruct(home, ResolveGridById);
            }
            else
            {
                SalvageTargets.Clear(homeGridEntityId);
            }
            BroadcastSalvageTargetSync();
        }

        private void BroadcastSalvageTargetSync()
        {
            if (SalvageTargets == null) return;
            SalvageTargets.CopyTo(_salvageTargetSyncBuf);

            if (!MyAPIGateway.Utilities.IsDedicated)
                CrewSalvageTargetHighlight.ApplySync(_salvageTargetSyncBuf);

            if (!MyAPIGateway.Multiplayer.IsServer) return;

            var sync = new SalvageTargetSync
            {
                Entries = new List<SalvageTargetEntry>(_salvageTargetSyncBuf)
            };
            byte[] data = CrewNetworking.Serialize(sync);

            var players = new List<IMyPlayer>();
            MyAPIGateway.Players.GetPlayers(players);
            ulong localSteam = MyAPIGateway.Multiplayer.MyId;
            for (int i = 0; i < players.Count; i++)
            {
                var p = players[i];
                if (p == null) continue;
                ulong steam = p.SteamUserId;
                if (steam == 0) continue;
                if (!MyAPIGateway.Utilities.IsDedicated && steam == localSteam)
                    continue;
                CrewNetworking.SendToPlayer(CrewNetworking.SalvageTargetSyncMsg, data, steam);
            }
        }

        private void HandleSalvageDispatch(SalvageDispatchRequest req, long identityId, ulong steamId)
        {
            if (req == null || string.IsNullOrEmpty(req.CrewId) || Store == null)
                return;

            var crew = Store.Get(req.CrewId);
            if (crew == null || crew.Role != CrewRole.SalvageOps || crew.GridEntityId == 0)
            {
                Notify(steamId, "Salvage: crew not ready");
                return;
            }

            IMyCubeGrid home;
            if (!TryGetGrid(crew.GridEntityId, out home) || home == null)
            {
                Notify(steamId, "Salvage: grid not found");
                return;
            }
            if (!HasManagePermission(identityId, home))
            {
                Notify(steamId, "No permission");
                return;
            }

            if (req.Recall)
            {
                bool ok = CrewSalvageMission.RecallCrew(crew.CrewId);
                Notify(steamId, ok
                    ? "Salvage: recalling " + (crew.DisplayName ?? "salvager")
                    : "Salvage: not out");
                return;
            }

            if (!CrewAmbientPresence.IsGridIdle(home))
            {
                Notify(steamId, "Salvage: grid moving — wait");
                return;
            }

            BoundingBoxD zone;
            long seedId;
            bool haveZone = ResolveSalvageZone(home, out zone, out seedId);
            if (!haveZone && req.TargetGridEntityId != 0)
            {
                IMyCubeGrid seedGrid;
                if (TryGetGrid(req.TargetGridEntityId, out seedGrid) && seedGrid != null)
                {
                    zone = SalvageTargetStore.BuildZoneFromGrid(seedGrid);
                    seedId = req.TargetGridEntityId;
                    haveZone = true;
                }
            }
            if (!haveZone)
            {
                Notify(steamId, "Salvage: no target — /crew salvage then LMB a wreck");
                return;
            }

            bool started = CrewSalvageMission.DispatchCrew(this, crew.CrewId, zone, seedId);
            Notify(steamId, started
                ? "Salvage: sent " + (crew.DisplayName ?? "salvager")
                : "Salvage: nothing left in zone / not ready");
        }

        private void HandleRepairDispatch(RepairDispatchRequest req, long identityId, ulong steamId)
        {
            if (req == null || string.IsNullOrEmpty(req.CrewId) || Store == null)
                return;

            var crew = Store.Get(req.CrewId);
            if (crew == null || crew.Role != CrewRole.DamageControl || crew.GridEntityId == 0)
            {
                Notify(steamId, "Construction: crew not ready");
                return;
            }

            IMyCubeGrid grid;
            if (!TryGetGrid(crew.GridEntityId, out grid) || grid == null)
            {
                Notify(steamId, "Construction: grid not found");
                return;
            }
            if (!HasManagePermission(identityId, grid))
            {
                Notify(steamId, "No permission");
                return;
            }

            if (req.Recall)
            {
                bool ok = CrewRepairMission.RecallCrew(crew.CrewId);
                Notify(steamId, ok
                    ? "Construction: recalling " + (crew.DisplayName ?? "welder")
                    : "Construction: not out");
                return;
            }

            if (!CrewAmbientPresence.IsGridIdle(grid))
            {
                Notify(steamId, "Construction: grid moving — wait");
                return;
            }

            bool started = CrewRepairMission.DispatchCrew(this, crew.CrewId);
            if (!started)
                Notify(steamId, "Construction: nothing to repair / not ready");
            else
                Notify(steamId, "Construction: sent " + (crew.DisplayName ?? "welder"));
        }

        private void HandlePathEdit(PathEditRequest req, long identityId, ulong steamId)
        {
            if (req == null || RepairPaths == null) return;

            IMyEntity gridEnt;
            if (!MyAPIGateway.Entities.TryGetEntityById(req.GridEntityId, out gridEnt) || gridEnt == null)
            {
                Notify(steamId, "Path: grid missing");
                return;
            }

            var grid = gridEnt as IMyCubeGrid;
            if (grid == null)
            {
                Notify(steamId, "Path: not a grid");
                return;
            }

            if (!HasManagePermission(identityId, grid))
            {
                Notify(steamId, "Path: no access");
                return;
            }

            var path = RepairPaths.Get(req.GridEntityId)
                ?? new RepairGridPath { GridEntityId = req.GridEntityId };
            if (path.Waypoints == null)
                path.Waypoints = new List<RepairWaypoint>();

            switch (req.Op)
            {
                case 0: // Append
                    if (path.HasExit)
                    {
                        Notify(steamId, "Path: already finished (Clear first)");
                        return;
                    }
                    path.Waypoints.Add(new RepairWaypoint
                    {
                        BlockEntityId = req.BlockEntityId,
                        LocalX = req.LocalX,
                        LocalY = req.LocalY,
                        LocalZ = req.LocalZ
                    });
                    RepairPaths.Upsert(path);
                    Notify(steamId, "Path " + path.Waypoints.Count + " wp");
                    break;

                case 1: // Undo
                    if (path.Waypoints.Count == 0)
                    {
                        Notify(steamId, "Path: empty");
                        return;
                    }
                    path.Waypoints.RemoveAt(path.Waypoints.Count - 1);
                    path.HasExit = false;
                    if (path.Waypoints.Count == 0)
                        RepairPaths.Clear(req.GridEntityId);
                    else
                        RepairPaths.Upsert(path);
                    Notify(steamId, "Path undo → " + path.Waypoints.Count + " wp");
                    break;

                case 2: // FinishExit
                    if (path.Waypoints.Count < 2)
                    {
                        Notify(steamId, "Path: need at least 2 waypoints");
                        return;
                    }
                    path.HasExit = true;
                    RepairPaths.Upsert(path);
                    Notify(steamId, "Path saved (Exit)");
                    break;

                case 3: // Clear
                    RepairPaths.Clear(req.GridEntityId);
                    Notify(steamId, "Path cleared");
                    break;

                default:
                    Notify(steamId, "Path: bad op");
                    break;
            }
        }

        private void HandleAdminCommand(AdminCommandRequest req, long identityId, ulong steamId)
        {
            CrewAdminCommands.Handle(this, req, identityId, steamId);
        }

        public void AdminNotify(ulong steamId, string message)
        {
            Notify(steamId, message);
        }

        public void AdminNotifyLines(ulong steamId, IList<string> lines)
        {
            if (lines == null) return;
            for (int i = 0; i < lines.Count; i++)
                Notify(steamId, lines[i]);
        }

        /// <summary>
        /// Notify crew owner identity and, when owned by a faction, all online faction members.
        /// Same delivery path as construction "out of components" alerts.
        /// </summary>
        public void NotifyCrewOwners(CrewRecord crew, string text)
        {
            if (crew == null || string.IsNullOrEmpty(text))
                return;
            try
            {
                var players = new List<IMyPlayer>();
                MyAPIGateway.Players.GetPlayers(players);
                var notified = new HashSet<ulong>();

                if (crew.OwnerIsFaction && crew.OwnerKey != 0
                    && MyAPIGateway.Session != null && MyAPIGateway.Session.Factions != null)
                {
                    var faction = MyAPIGateway.Session.Factions.TryGetFactionById(crew.OwnerKey);
                    if (faction != null)
                    {
                        for (int i = 0; i < players.Count; i++)
                        {
                            var p = players[i];
                            if (p == null || notified.Contains(p.SteamUserId))
                                continue;
                            if (!faction.IsMember(p.IdentityId))
                                continue;
                            Notify(p.SteamUserId, text);
                            notified.Add(p.SteamUserId);
                        }
                    }
                }

                for (int i = 0; i < players.Count; i++)
                {
                    var p = players[i];
                    if (p == null || notified.Contains(p.SteamUserId))
                        continue;
                    if (p.IdentityId == crew.OwnerIdentityId
                        || (!crew.OwnerIsFaction && p.IdentityId == crew.OwnerKey))
                    {
                        Notify(p.SteamUserId, text);
                        notified.Add(p.SteamUserId);
                    }
                }
            }
            catch { }
        }

        /// <summary>Permanent loss when a live ambient/EVA bot is killed or vanishes unexpectedly.</summary>
        public void HandleCrewBotKilled(CrewRecord crew)
        {
            if (crew == null || Store == null || string.IsNullOrEmpty(crew.CrewId))
                return;
            if (Store.Get(crew.CrewId) == null)
                return;

            long gridId = crew.GridEntityId;
            bool wasSeated = crew.Status == CrewStatus.Seated && gridId != 0;
            string name = CrewDisplayLabel(crew);
            if (string.IsNullOrEmpty(name))
                name = "Crew";

            NotifyCrewOwners(crew, name + " Got killed, Boss.");

            if (!RemoveCrew(crew.CrewId))
                return;

            if (wasSeated)
            {
                IMyCubeGrid grid;
                if (TryGetGrid(gridId, out grid))
                    RefreshGridBuffs(grid);
            }
            BroadcastRoster(gridId);
        }

        public bool TryReloadHireWorldConfig(out string error)
        {
            error = null;
            try
            {
                LoadHireWorldConfig();
                return true;
            }
            catch (Exception e)
            {
                error = e.Message;
                return false;
            }
        }

        public void AdminBroadcastRoster()
        {
            BroadcastRoster(0);
        }

        public void AdminBroadcastHirePool(HireBlockPool pool)
        {
            BroadcastHirePool(pool);
        }

        /// <summary>Admin fill/hire path: apply seat assign without notify/broadcast. Null = ok.</summary>
        public string AdminTryApplyAssign(AssignRequest req, long adminIdentityId, IMyCubeGrid grid)
        {
            return TryApplyAssign(req, adminIdentityId, grid);
        }

        public void AdminResolveOwnerKey(long identityId, out long ownerKey, out bool ownerIsFaction)
        {
            ResolveOwnerKey(identityId, out ownerKey, out ownerIsFaction);
        }

        public Random HireRng { get { return _hireRng; } }

        public void AdminRefreshGridBuffs(IMyCubeGrid grid)
        {
            RefreshGridBuffs(grid);
        }

        public void ClientRequestHire(long gridEntityId, int stars, bool skipCharge = false, CrewRole role = CrewRole.Gunner)
        {
            var req = new HireRequest
            {
                GridEntityId = gridEntityId,
                Stars = stars,
                SkipCharge = skipCharge,
                Role = (int)role
            };
            var data = CrewNetworking.Serialize(req);
            if (MyAPIGateway.Multiplayer.IsServer)
                HandleHire(req, MyAPIGateway.Session.Player.IdentityId, MyAPIGateway.Multiplayer.MyId);
            else
                CrewNetworking.SendToServer(CrewNetworking.HireMsg, data);
        }

        public void ClientRequestHireFromPool(long blockEntityId, string candidateId)
        {
            var req = new HireFromPoolRequest { BlockEntityId = blockEntityId, CandidateId = candidateId };
            var data = CrewNetworking.Serialize(req);
            if (MyAPIGateway.Multiplayer.IsServer)
                HandleHireFromPool(req, MyAPIGateway.Session.Player.IdentityId, MyAPIGateway.Multiplayer.MyId);
            else
                CrewNetworking.SendToServer(CrewNetworking.HireFromPoolMsg, data);
        }

        public void ClientRequestHireRefreshMinutes(long blockEntityId, int refreshMinutes)
        {
            var req = BuildDeskSettingsFromPool(blockEntityId);
            req.RefreshMinutes = refreshMinutes;
            ClientRequestHireDeskSettings(req);
        }

        public void ClientRequestHirePriceMultiplier(long blockEntityId, int priceMultiplierPercent)
        {
            var req = BuildDeskSettingsFromPool(blockEntityId);
            req.PriceMultiplierPercent = priceMultiplierPercent;
            ClientRequestHireDeskSettings(req);
        }

        public void ClientRequestHireMinCandidates(long blockEntityId, int minCandidates)
        {
            var req = BuildDeskSettingsFromPool(blockEntityId);
            req.MinCandidates = minCandidates;
            if (req.MaxCandidates < req.MinCandidates)
                req.MaxCandidates = req.MinCandidates;
            ClientRequestHireDeskSettings(req);
        }

        public void ClientRequestHireMaxCandidates(long blockEntityId, int maxCandidates)
        {
            var req = BuildDeskSettingsFromPool(blockEntityId);
            req.MaxCandidates = maxCandidates;
            if (req.MinCandidates > req.MaxCandidates)
                req.MinCandidates = req.MaxCandidates;
            ClientRequestHireDeskSettings(req);
        }

        public void ClientRequestHireStarBias(long blockEntityId, StarBias bias)
        {
            var req = BuildDeskSettingsFromPool(blockEntityId);
            req.StarBias = (int)bias;
            ClientRequestHireDeskSettings(req);
        }

        public void ClientRequestHireAllowedRoles(long blockEntityId, int allowedRoles)
        {
            var req = BuildDeskSettingsFromPool(blockEntityId);
            req.AllowedRoles = allowedRoles;
            ClientRequestHireDeskSettings(req);
        }

        public void ClientRequestHireRefillOnHire(long blockEntityId, bool refillOnHire)
        {
            var req = BuildDeskSettingsFromPool(blockEntityId);
            req.RefillOnHire = refillOnHire;
            ClientRequestHireDeskSettings(req);
        }

        public void ClientRequestHireReroll(long blockEntityId)
        {
            var req = BuildDeskSettingsFromPool(blockEntityId);
            req.ForceReroll = true;
            ClientRequestHireDeskSettings(req);
        }

        public void ClientRequestHireDeskSettings(HireRefreshRequest req)
        {
            if (req == null) return;
            var data = CrewNetworking.Serialize(req);
            if (MyAPIGateway.Multiplayer.IsServer)
                HandleHireRefresh(req, MyAPIGateway.Session.Player.IdentityId, MyAPIGateway.Multiplayer.MyId);
            else
                CrewNetworking.SendToServer(CrewNetworking.HireRefreshMsg, data);
        }

        public void ClientRequestHireDeskSettings(
            long blockEntityId,
            int refreshMinutes,
            int priceMultiplierPercent,
            int minCandidates,
            int maxCandidates,
            int allowedRoles,
            int starBias,
            bool refillOnHire,
            bool forceReroll)
        {
            ClientRequestHireDeskSettings(new HireRefreshRequest
            {
                BlockEntityId = blockEntityId,
                RefreshMinutes = refreshMinutes,
                PriceMultiplierPercent = priceMultiplierPercent,
                MinCandidates = minCandidates,
                MaxCandidates = maxCandidates,
                AllowedRoles = allowedRoles,
                StarBias = starBias,
                RefillOnHire = refillOnHire,
                ForceReroll = forceReroll
            });
        }

        private HireRefreshRequest BuildDeskSettingsFromPool(long blockEntityId)
        {
            var world = HireWorldConfig.Current ?? HireWorldConfig.CreateDefaults();
            var pool = HirePools != null ? HirePools.Get(blockEntityId) : null;
            if (pool != null)
                CrewHireGenerator.NormalizeDeskSettings(pool);
            return new HireRefreshRequest
            {
                BlockEntityId = blockEntityId,
                RefreshMinutes = pool != null ? pool.RefreshMinutes : world.RefreshMinutesDefault,
                PriceMultiplierPercent = pool != null && pool.PriceMultiplierPercent > 0
                    ? pool.PriceMultiplierPercent
                    : world.PriceMultiplierPercentDefault,
                MinCandidates = pool != null && pool.MinCandidates > 0 ? pool.MinCandidates : world.MinCandidates,
                MaxCandidates = pool != null && pool.MaxCandidates > 0 ? pool.MaxCandidates : world.MaxCandidates,
                AllowedRoles = pool != null && pool.AllowedRoles != 0 ? pool.AllowedRoles : world.AllowedRolesMask,
                StarBias = pool != null ? pool.StarBias : (int)StarBias.Balanced,
                RefillOnHire = pool != null && pool.RefillOnHire,
                ForceReroll = false
            };
        }

        public void ClientRequestHirePool(long blockEntityId)
        {
            var req = new HirePoolRequest { BlockEntityId = blockEntityId };
            var data = CrewNetworking.Serialize(req);
            if (MyAPIGateway.Multiplayer.IsServer)
                HandleHirePoolRequest(req, MyAPIGateway.Session.Player.IdentityId, MyAPIGateway.Multiplayer.MyId);
            else
                CrewNetworking.SendToServer(CrewNetworking.HirePoolRequestMsg, data);
        }

        public void ClientOpenHireDesk(long blockEntityId)
        {
            if (_hud != null)
                _hud.OpenHireDesk(blockEntityId);
            ClientRequestHirePool(blockEntityId);
        }

        public void ClientToggleCrewUi(long preferredGridEntityId = 0)
        {
            if (_hud != null)
                _hud.ToggleUi(preferredGridEntityId);
        }

        public void ClientRequestAssign(string crewId, long gridEntityId, long seatEntityId, long weaponEntityId)
        {
            var req = new AssignRequest
            {
                CrewId = crewId,
                GridEntityId = gridEntityId,
                SeatEntityId = seatEntityId,
                WeaponEntityId = weaponEntityId
            };
            var data = CrewNetworking.Serialize(req);
            if (MyAPIGateway.Multiplayer.IsServer)
                HandleAssign(req, MyAPIGateway.Session.Player.IdentityId, MyAPIGateway.Multiplayer.MyId);
            else
                CrewNetworking.SendToServer(CrewNetworking.AssignMsg, data);
        }

        public void ClientRequestBulkAssign(long gridEntityId, List<BulkAssignEntry> entries)
        {
            var req = new BulkAssignRequest
            {
                GridEntityId = gridEntityId,
                Entries = entries ?? new List<BulkAssignEntry>()
            };
            var data = CrewNetworking.Serialize(req);
            if (MyAPIGateway.Multiplayer.IsServer)
                HandleBulkAssign(req, MyAPIGateway.Session.Player.IdentityId, MyAPIGateway.Multiplayer.MyId);
            else
                CrewNetworking.SendToServer(CrewNetworking.BulkAssignMsg, data);
        }

        public void ClientRequestDismiss(string crewId, long gridEntityId)
        {
            var req = new DismissRequest { CrewId = crewId, GridEntityId = gridEntityId };
            var data = CrewNetworking.Serialize(req);
            if (MyAPIGateway.Multiplayer.IsServer)
                HandleDismiss(req, MyAPIGateway.Session.Player.IdentityId, MyAPIGateway.Multiplayer.MyId);
            else
                CrewNetworking.SendToServer(CrewNetworking.DismissMsg, data);
        }

        public void ClientRequestUnassign(string crewId)
        {
            var req = new UnassignRequest { CrewId = crewId };
            var data = CrewNetworking.Serialize(req);
            if (MyAPIGateway.Multiplayer.IsServer)
                HandleUnassign(req, MyAPIGateway.Session.Player.IdentityId, MyAPIGateway.Multiplayer.MyId);
            else
                CrewNetworking.SendToServer(CrewNetworking.UnassignMsg, data);
        }

        public void ClientRequestTrain(string crewId)
        {
            var req = new TrainRequest { CrewId = crewId };
            var data = CrewNetworking.Serialize(req);
            if (MyAPIGateway.Multiplayer.IsServer)
                HandleTrain(req, MyAPIGateway.Session.Player.IdentityId, MyAPIGateway.Multiplayer.MyId);
            else
                CrewNetworking.SendToServer(CrewNetworking.TrainMsg, data);
        }

        public void ClientRequestCancelTrain(string crewId)
        {
            var req = new CancelTrainRequest { CrewId = crewId };
            var data = CrewNetworking.Serialize(req);
            if (MyAPIGateway.Multiplayer.IsServer)
                HandleCancelTrain(req, MyAPIGateway.Session.Player.IdentityId, MyAPIGateway.Multiplayer.MyId);
            else
                CrewNetworking.SendToServer(CrewNetworking.CancelTrainMsg, data);
        }

        public void ClientRequestAssignAmenity(string crewId, long gridEntityId, AmenityKind kind, long blockEntityId)
        {
            var req = new AssignAmenityRequest
            {
                CrewId = crewId,
                GridEntityId = gridEntityId,
                Kind = (int)kind,
                BlockEntityId = blockEntityId
            };
            var data = CrewNetworking.Serialize(req);
            if (MyAPIGateway.Multiplayer.IsServer)
                HandleAssignAmenity(req, MyAPIGateway.Session.Player.IdentityId, MyAPIGateway.Multiplayer.MyId);
            else
                CrewNetworking.SendToServer(CrewNetworking.AssignAmenityMsg, data);
        }

        private void HandleHire(HireRequest req, long identityId, ulong steamId)
        {
            if (req == null) return;

            IMyCubeGrid grid;
            if (!TryGetGrid(req.GridEntityId, out grid)) return;
            if (!HasManagePermission(identityId, grid)) { Notify(steamId, "No permission"); return; }

            int stars = CrewConfig.ClampStars(req.Stars);

            var role = CrewConfig.ClampRole(req.Role);

            if (!req.SkipCharge)
            {
                var price = CrewConfig.GetPrice(stars);
                var player = GetPlayer(steamId);
                string err;
                if (!CrewEconomy.TryCharge(player, price, out err))
                {
                    Notify(steamId, err);
                    return;
                }
            }

            long ownerKey;
            bool ownerIsFaction;
            ResolveOwnerKey(identityId, out ownerKey, out ownerIsFaction);

            var record = new CrewRecord
            {
                CrewId = Guid.NewGuid().ToString("N"),
                Stars = stars,
                Role = role,
                GridEntityId = 0,
                OwnerIdentityId = identityId,
                OwnerKey = ownerKey,
                OwnerIsFaction = ownerIsFaction,
                Status = CrewStatus.Unassigned,
                DisplayName = CrewNames.RollFullName(_hireRng)
            };
            Store.Upsert(record);
            Notify(steamId, "Hired " + record.DisplayName + " (" + CrewConfig.FormatStars(stars) + ") — pool");
            BroadcastRoster(0);
        }

        private void HandleHireFromPool(HireFromPoolRequest req, long identityId, ulong steamId)
        {
            if (req == null || HirePools == null) return;

            IMyTerminalBlock block;
            IMyCubeGrid grid;
            if (!TryGetHireBlock(req.BlockEntityId, out block, out grid))
            {
                Notify(steamId, "Hire desk missing");
                return;
            }
            if (!HasManagePermission(identityId, grid)) { Notify(steamId, "No permission"); return; }

            var pool = HirePools.Ensure(block.EntityId, grid.EntityId, _hireRng, DateTime.UtcNow);
            var candidate = HirePools.TakeCandidate(block.EntityId, req.CandidateId);
            if (candidate == null)
            {
                Notify(steamId, "Candidate unavailable");
                SendHirePoolTo(steamId, pool);
                return;
            }

            var player = GetPlayer(steamId);
            string err;
            if (!CrewEconomy.TryCharge(player, candidate.Price, out err))
            {
                // Put candidate back if charge fails.
                if (pool.Candidates == null) pool.Candidates = new List<HireCandidate>();
                pool.Candidates.Add(candidate);
                Notify(steamId, err);
                SendHirePoolTo(steamId, pool);
                return;
            }

            var role = CrewConfig.ClampRole(candidate.Role);
            long ownerKey;
            bool ownerIsFaction;
            ResolveOwnerKey(identityId, out ownerKey, out ownerIsFaction);

            var record = new CrewRecord
            {
                CrewId = Guid.NewGuid().ToString("N"),
                Stars = CrewConfig.ClampStars(candidate.Stars),
                Role = role,
                GridEntityId = 0,
                OwnerIdentityId = identityId,
                OwnerKey = ownerKey,
                OwnerIsFaction = ownerIsFaction,
                Status = CrewStatus.Unassigned,
                DisplayName = candidate.FullName
            };
            Store.Upsert(record);
            Notify(steamId, "Hired " + record.DisplayName + " for " + candidate.Price + " sc — added to roster");
            BroadcastRoster(0);
            if (pool.RefillOnHire)
            {
                CrewHireGenerator.RefillOne(pool, _hireRng);
                BroadcastHirePool(pool);
            }
            else
                SendHirePoolTo(steamId, pool);
        }

        private void HandleHireRefresh(HireRefreshRequest req, long identityId, ulong steamId)
        {
            if (req == null || HirePools == null) return;

            IMyTerminalBlock block;
            IMyCubeGrid grid;
            if (!TryGetHireBlock(req.BlockEntityId, out block, out grid)) return;
            if (!HasManagePermission(identityId, grid)) { Notify(steamId, "No permission"); return; }

            var pool = HirePools.Ensure(block.EntityId, grid.EntityId, _hireRng, DateTime.UtcNow);
            CrewHireGenerator.NormalizeDeskSettings(pool);

            int oldMin = pool.MinCandidates;
            int oldMax = pool.MaxCandidates;
            int oldRoles = pool.AllowedRoles;
            int oldBias = pool.StarBias;
            int oldPrice = pool.PriceMultiplierPercent;

            pool.RefreshMinutes = req.RefreshMinutes;
            pool.MinCandidates = req.MinCandidates;
            pool.MaxCandidates = req.MaxCandidates;
            pool.AllowedRoles = req.AllowedRoles;
            pool.StarBias = req.StarBias;
            pool.RefillOnHire = req.RefillOnHire;
            // Keep old PriceMultiplierPercent until rescale/reroll decision.
            CrewHireGenerator.NormalizeDeskSettings(pool);

            bool shapeChanged = req.ForceReroll
                || pool.MinCandidates != oldMin
                || pool.MaxCandidates != oldMax
                || pool.AllowedRoles != oldRoles
                || pool.StarBias != oldBias;

            int newPrice = CrewConfig.ClampPriceMultiplierPercent(
                req.PriceMultiplierPercent > 0 ? req.PriceMultiplierPercent : oldPrice);
            bool priceChanged = newPrice != oldPrice;

            if (shapeChanged)
            {
                pool.PriceMultiplierPercent = newPrice;
                CrewHireGenerator.RefreshPool(pool, _hireRng, DateTime.UtcNow);
            }
            else if (priceChanged)
            {
                CrewHireGenerator.ApplyMultiplierToPool(pool, newPrice);
            }
            else
            {
                pool.PriceMultiplierPercent = newPrice;
                // refresh-only or refill-only: no candidate mutation
            }

            BroadcastHirePool(pool);
        }

        private void BroadcastHirePool(HireBlockPool pool)
        {
            if (pool == null) return;
            var players = new List<IMyPlayer>();
            MyAPIGateway.Players.GetPlayers(players);
            for (int i = 0; i < players.Count; i++)
            {
                var p = players[i];
                if (p != null)
                    SendHirePoolTo(p.SteamUserId, pool);
            }
        }

        private void HandleHirePoolRequest(HirePoolRequest req, long identityId, ulong steamId)
        {
            if (req == null || HirePools == null) return;

            IMyTerminalBlock block;
            IMyCubeGrid grid;
            if (!TryGetHireBlock(req.BlockEntityId, out block, out grid)) return;
            if (!HasManagePermission(identityId, grid)) { Notify(steamId, "No permission"); return; }

            var pool = HirePools.Ensure(block.EntityId, grid.EntityId, _hireRng, DateTime.UtcNow);
            if (DateTime.UtcNow.Ticks >= pool.NextRefreshUtcTicks)
                CrewHireGenerator.RefreshPool(pool, _hireRng, DateTime.UtcNow);
            SendHirePoolTo(steamId, pool);
        }

        private bool TryGetHireBlock(long blockEntityId, out IMyTerminalBlock block, out IMyCubeGrid grid)
        {
            block = null;
            grid = null;
            IMyEntity ent;
            if (!MyAPIGateway.Entities.TryGetEntityById(blockEntityId, out ent) || ent == null)
                return false;
            block = ent as IMyTerminalBlock;
            if (block == null || !CrewHireBlockLogic.IsHireDesk(block))
                return false;
            grid = block.CubeGrid;
            return grid != null;
        }

        private void SendHirePoolTo(ulong steamId, HireBlockPool pool)
        {
            if (pool == null) return;
            var sync = new HirePoolSync
            {
                BlockEntityId = pool.BlockEntityId,
                PoolBytes = HirePoolStore.SerializePool(pool)
            };
            var data = CrewNetworking.Serialize(sync);
            if (MyAPIGateway.Multiplayer.IsServer && steamId == MyAPIGateway.Multiplayer.MyId
                && !MyAPIGateway.Utilities.IsDedicated)
            {
                if (HirePools == null) HirePools = new HirePoolStore();
                HirePools.Upsert(pool);
                if (_hud != null)
                    _hud.OnHirePoolSynced(pool);
            }
            else
                CrewNetworking.SendToPlayer(CrewNetworking.HirePoolSyncMsg, data, steamId);
        }

        public void RegisterHireDesk(long blockEntityId, long gridEntityId)
        {
            if (!MyAPIGateway.Multiplayer.IsServer || HirePools == null) return;
            HirePools.Ensure(blockEntityId, gridEntityId, _hireRng, DateTime.UtcNow);
        }

        public void UnregisterHireDesk(long blockEntityId)
        {
            if (!MyAPIGateway.Multiplayer.IsServer || HirePools == null) return;
            HirePools.Remove(blockEntityId);
        }

        private void HandleAssign(AssignRequest req, long identityId, ulong steamId)
        {
            if (req == null) return;

            IMyCubeGrid grid;
            if (!TryGetGrid(req.GridEntityId, out grid)) return;
            if (!HasManagePermission(identityId, grid)) { Notify(steamId, "No permission"); return; }

            string err = TryApplyAssign(req, identityId, grid);
            if (err != null)
            {
                Notify(steamId, err);
                return;
            }

            Notify(steamId, "Crew assigned");
            BroadcastRoster(req.GridEntityId);
        }

        private void HandleBulkAssign(BulkAssignRequest req, long identityId, ulong steamId)
        {
            if (req == null || req.Entries == null || req.Entries.Count == 0) return;

            IMyCubeGrid grid;
            if (!TryGetGrid(req.GridEntityId, out grid)) return;
            if (!HasManagePermission(identityId, grid)) { Notify(steamId, "No permission"); return; }

            int total = req.Entries.Count;
            if (total > CrewHudModel.BulkSelectionCap)
                total = CrewHudModel.BulkSelectionCap;

            var usedSeats = new HashSet<long>();
            var usedWeapons = new HashSet<long>();
            int ok = 0;
            string firstFailName = null;
            string firstFailReason = null;

            for (int i = 0; i < total; i++)
            {
                var entry = req.Entries[i];
                if (entry == null || string.IsNullOrEmpty(entry.CrewId))
                {
                    if (firstFailReason == null)
                    {
                        firstFailName = "entry";
                        firstFailReason = CrewValidation.ErrorCrewMissing;
                    }
                    continue;
                }

                if (entry.SeatEntityId != 0 && !usedSeats.Add(entry.SeatEntityId))
                {
                    if (firstFailReason == null)
                    {
                        firstFailName = BulkFailLabel(entry.CrewId);
                        firstFailReason = "Seat already used in batch";
                    }
                    continue;
                }

                if (entry.WeaponEntityId != 0 && !usedWeapons.Add(entry.WeaponEntityId))
                {
                    if (entry.SeatEntityId != 0)
                        usedSeats.Remove(entry.SeatEntityId);
                    if (firstFailReason == null)
                    {
                        firstFailName = BulkFailLabel(entry.CrewId);
                        firstFailReason = "Weapon already used in batch";
                    }
                    continue;
                }

                var assignReq = new AssignRequest
                {
                    CrewId = entry.CrewId,
                    GridEntityId = req.GridEntityId,
                    SeatEntityId = entry.SeatEntityId,
                    WeaponEntityId = entry.WeaponEntityId
                };
                string err = TryApplyAssign(assignReq, identityId, grid);
                if (err != null)
                {
                    if (entry.SeatEntityId != 0)
                        usedSeats.Remove(entry.SeatEntityId);
                    if (entry.WeaponEntityId != 0)
                        usedWeapons.Remove(entry.WeaponEntityId);
                    if (firstFailReason == null)
                    {
                        firstFailName = BulkFailLabel(entry.CrewId);
                        firstFailReason = err;
                    }
                    continue;
                }

                ok++;
            }

            if (ok > 0)
                BroadcastRoster(req.GridEntityId);

            if (ok == total)
                Notify(steamId, "Assigned " + ok + "/" + total);
            else if (firstFailReason != null)
                Notify(steamId, "Assigned " + ok + "/" + total + ". Failed: " + firstFailName + " (" + firstFailReason + ")");
            else
                Notify(steamId, "Assigned " + ok + "/" + total);
        }

        private string BulkFailLabel(string crewId)
        {
            var crew = Store.Get(crewId);
            if (crew != null && !string.IsNullOrEmpty(crew.DisplayName))
                return crew.DisplayName;
            if (!string.IsNullOrEmpty(crewId) && crewId.Length > 8)
                return crewId.Substring(0, 8);
            return crewId ?? "?";
        }

        /// <summary>Applies one assign. Returns null on success, otherwise an error message. Does not notify or broadcast.</summary>
        private string TryApplyAssign(AssignRequest req, long identityId, IMyCubeGrid grid)
        {
            if (req == null || grid == null) return CrewValidation.ErrorCrewMissing;

            var crew = Store.Get(req.CrewId);
            if (crew == null || !OwnsCrew(identityId, crew))
                return CrewValidation.ErrorCrewMissing;

            if (CrewConfig.IsTraining(crew))
                return CrewValidation.ErrorAlreadyTraining;

            if (crew.Status == CrewStatus.Seated)
                return "Already assigned — unassign first";

            var role = crew.Role;
            var err = CrewValidation.ValidateAssign(
                GetCrewForConstruct(grid),
                req.CrewId,
                req.GridEntityId,
                req.SeatEntityId,
                req.WeaponEntityId,
                role);
            if (err != null) return err;

            IMyEntity seatEnt;
            if (!MyAPIGateway.Entities.TryGetEntityById(req.SeatEntityId, out seatEnt))
                return "Seat missing";

            var seat = seatEnt as IMyTerminalBlock;
            if (seat == null || seat.CubeGrid == null || !seat.CubeGrid.IsSameConstructAs(grid))
                return CrewValidation.ErrorWrongGrid;

            if (!CrewStationLogic.IsAssignableSeat(seat))
            {
                if (CrewAmenities.DetectKind(seat).HasValue)
                    return "Cannot assign crew to an amenity";
                return "Invalid seat";
            }

            bool needsWeapon = CrewConfig.NeedsWeapon(role);
            IMyTerminalBlock weapon = null;
            if (needsWeapon)
            {
                IMyEntity wepEnt;
                if (!MyAPIGateway.Entities.TryGetEntityById(req.WeaponEntityId, out wepEnt))
                    return "Weapon missing";

                weapon = wepEnt as IMyTerminalBlock;
                if (weapon == null || weapon.CubeGrid == null || !weapon.CubeGrid.IsSameConstructAs(grid))
                    return CrewValidation.ErrorWrongGrid;

                if (WeaponAi == null || !WeaponAi.IsReady)
                    return "WeaponCore not ready";
                if (!WeaponAi.IsCoreWeapon(weapon))
                    return "Not a WeaponCore weapon";
            }

            // Logical crew: claim seat (+weapon for gunners). Ambient bot may spawn later nearby.
            // Still reject a seat a player is currently using.
            if (CrewStationLogic.IsSeatOccupiedByPlayer(seat))
                return "Seat occupied";

            CrewAmbientPresence.DespawnCrewBot(this, crew, notify: false);
            crew.SeatEntityId = req.SeatEntityId;
            crew.WeaponEntityId = needsWeapon ? (long?)req.WeaponEntityId : null;
            crew.CharacterEntityId = null;
            crew.GridEntityId = seat.CubeGrid.EntityId;
            CrewAmenities.ClearAll(crew);
            crew.Status = CrewStatus.Seated;
            Store.Upsert(crew);
            ApplyBlockCrewName(seat, CrewDisplayLabel(crew));
            if (needsWeapon && weapon != null)
                ApplyCrewWeapon(crew, weapon);
            RefreshGridBuffs(grid);
            return null;
        }

        private void HandleAssignAmenity(AssignAmenityRequest req, long identityId, ulong steamId)
        {
            if (req == null) return;

            IMyCubeGrid grid;
            if (!TryGetGrid(req.GridEntityId, out grid)) return;
            if (!HasManagePermission(identityId, grid)) { Notify(steamId, "No permission"); return; }

            var crew = Store.Get(req.CrewId);
            if (crew == null || !OwnsCrew(identityId, crew) || !IsCrewOnConstruct(crew, grid))
            {
                Notify(steamId, CrewValidation.ErrorCrewMissing);
                return;
            }

            if (CrewConfig.IsTraining(crew))
            {
                Notify(steamId, CrewValidation.ErrorAlreadyTraining);
                return;
            }

            if (req.Kind < (int)AmenityKind.Bed || req.Kind > (int)AmenityKind.Shower)
            {
                Notify(steamId, "Invalid amenity");
                return;
            }
            var kind = (AmenityKind)req.Kind;

            var err = CrewValidation.ValidateAmenity(GetCrewForConstruct(grid), crew, req.GridEntityId, kind, req.BlockEntityId);
            if (err != null) { Notify(steamId, err); return; }

            var label = CrewDisplayLabel(crew);

            if (req.BlockEntityId == 0)
            {
                ClearBlockCrewName(CrewAmenities.GetAmenity(crew, kind), label);
                CrewAmenities.SetAmenity(crew, kind, null);
                Store.Upsert(crew);
                ReapplyCrewWeapon(crew);
                if (AffectsGridBuffs(crew.Role))
                    RefreshGridBuffs(grid);
                Notify(steamId, CrewAmenities.KindLabel(kind) + " cleared");
                BroadcastRoster(req.GridEntityId);
                return;
            }

            IMyEntity blockEnt;
            if (!MyAPIGateway.Entities.TryGetEntityById(req.BlockEntityId, out blockEnt) || blockEnt == null || blockEnt.Closed)
            {
                Notify(steamId, CrewValidation.ErrorAmenityMissing);
                return;
            }

            // Showers from Decorative Pack are plain CubeBlocks (no terminal).
            var block = blockEnt as IMyCubeBlock;
            if (block == null || block.CubeGrid == null || !block.CubeGrid.IsSameConstructAs(grid))
            {
                Notify(steamId, CrewValidation.ErrorWrongGrid);
                return;
            }

            if (!CrewAmenities.MatchesKind(block, kind))
            {
                Notify(steamId, CrewValidation.ErrorAmenityWrongType);
                return;
            }

            var previous = CrewAmenities.GetAmenity(crew, kind);
            if (previous.HasValue && previous.Value != req.BlockEntityId)
                ClearBlockCrewName(previous, label);

            CrewAmenities.SetAmenity(crew, kind, req.BlockEntityId);
            Store.Upsert(crew);
            ApplyBlockCrewName(block as IMyTerminalBlock, label);
            ReapplyCrewWeapon(crew);
            if (AffectsGridBuffs(crew.Role))
                RefreshGridBuffs(grid);
            Notify(steamId, CrewAmenities.KindLabel(kind) + " assigned (" + CrewAmenities.GetEfficiencyPercent(crew) + "% eff)");
            BroadcastRoster(req.GridEntityId);
        }

        private void HandleDismiss(DismissRequest req, long identityId, ulong steamId)
        {
            if (req == null) return;

            var crew = Store.Get(req.CrewId);
            if (crew == null || !OwnsCrew(identityId, crew))
            {
                Notify(steamId, CrewValidation.ErrorCrewMissing);
                return;
            }

            // If stationed, still need manage rights on that grid (or pool-only dismiss is always ok).
            if (crew.Status == CrewStatus.Seated && crew.GridEntityId != 0)
            {
                IMyCubeGrid grid;
                if (!TryGetGrid(crew.GridEntityId, out grid) || !HasManagePermission(identityId, grid))
                {
                    Notify(steamId, "No permission");
                    return;
                }
            }

            long gridId = crew.GridEntityId;
            bool wasSeated = crew.Status == CrewStatus.Seated && gridId != 0;

            if (!RemoveCrew(req.CrewId))
                return;

            if (wasSeated)
            {
                IMyCubeGrid grid;
                if (TryGetGrid(gridId, out grid))
                    RefreshGridBuffs(grid);
            }
            Notify(steamId, "Crew dismissed");
            BroadcastRoster(gridId);
        }

        private void HandleTrain(TrainRequest req, long identityId, ulong steamId)
        {
            if (req == null) return;

            var crew = Store.Get(req.CrewId);
            var err = CrewValidation.ValidateTrain(crew);
            if (err != null)
            {
                Notify(steamId, err);
                return;
            }
            if (!OwnsCrew(identityId, crew))
            {
                Notify(steamId, "No permission");
                return;
            }

            var player = GetPlayer(steamId);
            if (player == null)
            {
                Notify(steamId, CrewEconomy.ErrorEconomyUnavailable);
                return;
            }

            float discount = CrewConfig.GetTrainDiscountFraction(Store.All, crew.OwnerKey, crew.OwnerIsFaction);
            long cost = CrewConfig.GetTrainCost(crew.Stars, discount);
            string payErr;
            if (!CrewEconomy.TryCharge(player, cost, out payErr))
            {
                Notify(steamId, payErr);
                return;
            }

            IMyCubeGrid grid = null;
            long priorGridId = crew.GridEntityId;
            bool wasSeated = crew.Status == CrewStatus.Seated && priorGridId != 0;
            if (wasSeated)
                TryGetGrid(priorGridId, out grid);

            if (crew.Status == CrewStatus.Seated)
                ReturnCrewToPool(crew);
            else
            {
                ClearCrewStationing(crew);
                crew.Status = CrewStatus.Unassigned;
            }

            int minutes = CrewConfig.GetTrainMinutes(crew.Stars);
            crew.TrainingEndsUtcTicks = DateTime.UtcNow.AddMinutes(minutes).Ticks;
            Store.Upsert(crew);
            if (wasSeated && grid != null)
                RefreshGridBuffs(grid);
            Notify(steamId, "Training started");
            BroadcastRoster(0);
        }

        private void HandleCancelTrain(CancelTrainRequest req, long identityId, ulong steamId)
        {
            if (req == null) return;

            var crew = Store.Get(req.CrewId);
            var err = CrewValidation.ValidateCancelTrain(crew);
            if (err != null)
            {
                Notify(steamId, err);
                return;
            }
            if (!OwnsCrew(identityId, crew))
            {
                Notify(steamId, "No permission");
                return;
            }

            crew.TrainingEndsUtcTicks = 0;
            Store.Upsert(crew);
            Notify(steamId, "Training cancelled");
            BroadcastRoster(0);
        }

        private void TickTrainingCompletions()
        {
            if (Store == null) return;
            long now = DateTime.UtcNow.Ticks;
            bool any = false;
            foreach (var crew in new List<CrewRecord>(Store.All))
            {
                if (crew == null) continue;
                if (!CrewValidation.TryCompleteTraining(crew, now)) continue;
                Store.Upsert(crew);
                any = true;
            }
            if (any)
                BroadcastRoster(0);
        }

        private void HandleUnassign(UnassignRequest req, long identityId, ulong steamId)
        {
            if (req == null) return;

            var crew = Store.Get(req.CrewId);
            if (crew == null || !OwnsCrew(identityId, crew))
            {
                Notify(steamId, CrewValidation.ErrorCrewMissing);
                return;
            }

            if (crew.Status != CrewStatus.Seated)
            {
                Notify(steamId, "Already in roster pool");
                return;
            }

            IMyCubeGrid grid;
            if (crew.GridEntityId == 0 || !TryGetGrid(crew.GridEntityId, out grid) || !HasManagePermission(identityId, grid))
            {
                Notify(steamId, "No permission");
                return;
            }

            long gridId = crew.GridEntityId;
            ReturnCrewToPool(crew);
            RefreshGridBuffs(grid);
            Notify(steamId, "Crew returned to roster");
            BroadcastRoster(gridId);
        }

        /// <summary>Hard-remove (dismiss / permanent loss).</summary>
        public bool RemoveCrew(string crewId)
        {
            var crew = Store.Get(crewId);
            if (crew == null) return false;
            ClearCrewStationing(crew);
            Store.Remove(crewId);
            return true;
        }

        /// <summary>Integrity / unassign: clear seat binding, keep in owner pool.</summary>
        public bool ReturnCrewToPool(CrewRecord crew)
        {
            if (crew == null) return false;
            ClearCrewStationing(crew);
            crew.GridEntityId = 0;
            crew.Status = CrewStatus.Unassigned;
            Store.Upsert(crew);
            return true;
        }

        private void ClearCrewStationing(CrewRecord crew)
        {
            if (crew == null) return;
            CrewRepairMission.CancelForCrew(crew.CrewId);
            CrewAmbientPresence.DespawnCrewBot(this, crew, notify: false);
            var label = CrewDisplayLabel(crew);
            if (crew.Status == CrewStatus.Seated && crew.SeatEntityId.HasValue)
                ClearBlockCrewName(crew.SeatEntityId, label);
            ClearBlockCrewName(crew.BedEntityId, label);
            ClearBlockCrewName(crew.ToiletEntityId, label);
            ClearBlockCrewName(crew.ShowerEntityId, label);

            if (crew.WeaponEntityId.HasValue)
            {
                IMyEntity wepEnt;
                if (MyAPIGateway.Entities.TryGetEntityById(crew.WeaponEntityId.Value, out wepEnt))
                {
                    var weapon = wepEnt as IMyTerminalBlock;
                    if (weapon != null && WeaponAi != null) WeaponAi.ForceAiOff(weapon);
                }
            }

            crew.SeatEntityId = null;
            crew.WeaponEntityId = null;
            crew.CharacterEntityId = null;
            CrewAmenities.ClearAll(crew);
        }

        /// <summary>Legacy name — permanent remove.</summary>
        public bool InvalidateCrew(string crewId, string reason)
        {
            return RemoveCrew(crewId);
        }

        private void WatchCrewIntegrity()
        {
            var snapshot = new List<CrewRecord>(Store.All);
            var dirtyGrids = new HashSet<long>();
            foreach (var crew in snapshot)
            {
                if (crew.Status != CrewStatus.Seated) continue;

                IMyEntity seatEnt = null, wepEnt = null;
                var seatOk = crew.SeatEntityId.HasValue && MyAPIGateway.Entities.TryGetEntityById(crew.SeatEntityId.Value, out seatEnt) && seatEnt != null && !seatEnt.Closed;
                bool needsWeapon = CrewConfig.NeedsWeapon(crew.Role);
                var wepOk = !needsWeapon || (crew.WeaponEntityId.HasValue && MyAPIGateway.Entities.TryGetEntityById(crew.WeaponEntityId.Value, out wepEnt) && wepEnt != null && !wepEnt.Closed);

                if (!seatOk || !wepOk)
                {
                    long gid = crew.GridEntityId;
                    if (ReturnCrewToPool(crew))
                        dirtyGrids.Add(gid);
                    continue;
                }

                var seatBlock = seatEnt as IMyCubeBlock;
                if (seatBlock == null)
                {
                    long gid = crew.GridEntityId;
                    if (ReturnCrewToPool(crew))
                        dirtyGrids.Add(gid);
                    continue;
                }

                if (needsWeapon)
                {
                    var wepBlock = wepEnt as IMyCubeBlock;
                    if (wepBlock == null || seatBlock.CubeGrid.EntityId != wepBlock.CubeGrid.EntityId)
                    {
                        long gid = crew.GridEntityId;
                        if (ReturnCrewToPool(crew))
                            dirtyGrids.Add(gid);
                        continue;
                    }
                }

                // Grid split can reassign CubeGrid.EntityId while seat (+weapon) stay together.
                var seatGridId = seatBlock.CubeGrid.EntityId;
                if (crew.GridEntityId != seatGridId)
                {
                    var oldGridId = crew.GridEntityId;
                    crew.GridEntityId = seatGridId;
                    Store.Upsert(crew);
                    dirtyGrids.Add(oldGridId);
                    dirtyGrids.Add(seatGridId);
                }

                if (PruneMissingAmenities(crew))
                {
                    Store.Upsert(crew);
                    ReapplyCrewWeapon(crew);
                    dirtyGrids.Add(crew.GridEntityId);
                }
            }

            foreach (var gridId in dirtyGrids)
                BroadcastRoster(gridId);
        }

        private static bool PruneMissingAmenities(CrewRecord crew)
        {
            if (crew == null) return false;
            bool changed = false;
            changed |= ClearAmenityIfMissing(crew, AmenityKind.Bed);
            changed |= ClearAmenityIfMissing(crew, AmenityKind.Toilet);
            changed |= ClearAmenityIfMissing(crew, AmenityKind.Shower);
            return changed;
        }

        private static bool ClearAmenityIfMissing(CrewRecord crew, AmenityKind kind)
        {
            var id = CrewAmenities.GetAmenity(crew, kind);
            if (!id.HasValue || id.Value == 0) return false;

            IMyEntity ent;
            if (MyAPIGateway.Entities.TryGetEntityById(id.Value, out ent) && ent != null && !ent.Closed)
            {
                var block = ent as IMyCubeBlock;
                if (block != null && block.CubeGrid != null && block.CubeGrid.EntityId == crew.GridEntityId)
                    return false;
            }

            ClearBlockCrewName(id, CrewDisplayLabel(crew));
            CrewAmenities.SetAmenity(crew, kind, null);
            return true;
        }

        private void ReapplyCrewWeapon(CrewRecord crew)
        {
            if (crew == null || !crew.WeaponEntityId.HasValue) return;
            IMyEntity wepEnt;
            if (!MyAPIGateway.Entities.TryGetEntityById(crew.WeaponEntityId.Value, out wepEnt)) return;
            var weapon = wepEnt as IMyTerminalBlock;
            if (weapon == null) return;
            ApplyCrewWeapon(crew, weapon);
        }

        private void ApplyCrewWeapon(CrewRecord crew, IMyTerminalBlock weapon)
        {
            if (WeaponAi == null || weapon == null || crew == null) return;
            WeaponAi.SetManned(weapon, true, crew.Stars, CrewAmenities.GetEfficiency(crew));
        }

        private static string CrewDisplayLabel(CrewRecord crew)
        {
            if (crew == null) return "";
            if (!string.IsNullOrEmpty(crew.DisplayName))
                return crew.DisplayName;
            return CrewConfig.FormatStars(crew.Stars) + " " + CrewConfig.RoleLabel(crew.Role);
        }

        /// <summary>Appends [CrewName] to a block name, keeping the original name.</summary>
        private static void ApplyBlockCrewName(IMyTerminalBlock block, string crewName)
        {
            if (block == null || block.MarkedForClose || string.IsNullOrEmpty(crewName)) return;
            var tag = "[" + crewName + "]";
            var current = (block.CustomName ?? "").Trim();

            // Already "Original [Crew]" — leave alone.
            if (current.EndsWith(" " + tag, StringComparison.Ordinal) && current.Length > tag.Length + 1)
                return;

            // Unrenamed blocks have empty CustomName; also repair blocks that were
            // previously set to only the tag (which replaced the visible name).
            var baseName = current;
            if (string.IsNullOrEmpty(baseName) || baseName == tag)
            {
                baseName = block.DefinitionDisplayNameText ?? "";
                if (string.IsNullOrEmpty(baseName))
                    baseName = block.DisplayNameText ?? "";
                if (baseName == tag)
                    baseName = "";
            }
            else if (baseName.EndsWith(tag, StringComparison.Ordinal))
            {
                // Has tag but missing space / odd form — strip then re-append cleanly.
                baseName = baseName.Substring(0, baseName.Length - tag.Length).TrimEnd();
            }

            block.CustomName = string.IsNullOrEmpty(baseName)
                ? tag
                : baseName + " " + tag;
        }

        /// <summary>Removes [CrewName] from a block CustomName on clear/dismiss/integrity cleanup.</summary>
        private static void ClearBlockCrewName(long? entityId, string crewName)
        {
            if (!entityId.HasValue || entityId.Value == 0 || string.IsNullOrEmpty(crewName)) return;
            IMyEntity ent;
            if (!MyAPIGateway.Entities.TryGetEntityById(entityId.Value, out ent)) return;
            var block = ent as IMyTerminalBlock;
            if (block == null || block.MarkedForClose) return;

            var tag = "[" + crewName + "]";
            var current = block.CustomName ?? "";
            if (string.IsNullOrEmpty(current)) return;

            var spaced = " " + tag;
            if (current.EndsWith(spaced, StringComparison.Ordinal))
                block.CustomName = current.Substring(0, current.Length - spaced.Length);
            else if (current.EndsWith(tag, StringComparison.Ordinal))
                block.CustomName = current.Substring(0, current.Length - tag.Length).TrimEnd();
            else if (current.IndexOf(spaced, StringComparison.Ordinal) >= 0)
                block.CustomName = current.Replace(spaced, "");
            else if (current.IndexOf(tag, StringComparison.Ordinal) >= 0)
                block.CustomName = current.Replace(tag, "").Trim();
        }

        private static void ApplyAmenityCrewNames(CrewRecord crew)
        {
            if (crew == null) return;
            var label = CrewDisplayLabel(crew);
            ApplyBlockCrewNameById(crew.BedEntityId, label);
            ApplyBlockCrewNameById(crew.ToiletEntityId, label);
            ApplyBlockCrewNameById(crew.ShowerEntityId, label);
        }

        private static void ApplyBlockCrewNameById(long? entityId, string crewName)
        {
            if (!entityId.HasValue || entityId.Value == 0) return;
            IMyEntity ent;
            if (!MyAPIGateway.Entities.TryGetEntityById(entityId.Value, out ent)) return;
            ApplyBlockCrewName(ent as IMyTerminalBlock, crewName);
        }

        private void RestoreAssignmentsFromStore()
        {
            var snapshot = new List<CrewRecord>(Store.All);
            var buffGrids = new HashSet<long>();
            foreach (var crew in snapshot)
            {
                if (crew.Status != CrewStatus.Seated || !crew.SeatEntityId.HasValue)
                    continue;

                bool needsWeapon = CrewConfig.NeedsWeapon(crew.Role);
                if (needsWeapon && !crew.WeaponEntityId.HasValue)
                {
                    ReturnCrewToPool(crew);
                    continue;
                }

                IMyEntity seatEnt;
                if (!MyAPIGateway.Entities.TryGetEntityById(crew.SeatEntityId.Value, out seatEnt))
                {
                    ReturnCrewToPool(crew);
                    continue;
                }

                var seat = seatEnt as IMyTerminalBlock;
                if (seat == null || !CrewStationLogic.IsAssignableSeat(seat))
                {
                    ReturnCrewToPool(crew);
                    continue;
                }

                IMyTerminalBlock weapon = null;
                if (needsWeapon)
                {
                    IMyEntity wepEnt;
                    if (!MyAPIGateway.Entities.TryGetEntityById(crew.WeaponEntityId.Value, out wepEnt))
                    {
                        ReturnCrewToPool(crew);
                        continue;
                    }
                    weapon = wepEnt as IMyTerminalBlock;
                    if (weapon == null)
                    {
                        ReturnCrewToPool(crew);
                        continue;
                    }
                }

                // Bots do not persist across save/load — drop stale CharacterEntityId.
                if (crew.CharacterEntityId.HasValue)
                {
                    crew.CharacterEntityId = null;
                    Store.Upsert(crew);
                }

                PruneMissingAmenities(crew);
                Store.Upsert(crew);
                ApplyBlockCrewName(seat, CrewDisplayLabel(crew));
                ApplyAmenityCrewNames(crew);
                if (needsWeapon && weapon != null && WeaponAi != null && WeaponAi.IsReady)
                    ApplyCrewWeapon(crew, weapon);
                if (AffectsGridBuffs(crew.Role))
                    buffGrids.Add(crew.GridEntityId);
            }

            foreach (var gridId in buffGrids)
            {
                IMyCubeGrid grid;
                if (TryGetGrid(gridId, out grid))
                    RefreshGridBuffs(grid);
            }
        }

        private static bool AffectsGridBuffs(CrewRole role)
        {
            return role == CrewRole.Engineer
                || role == CrewRole.Helmsman
                || role == CrewRole.Propulsion;
        }

        private void RefreshAllGridBuffs()
        {
            if (PowerBuff == null || Store == null) return;

            var active = new HashSet<long>();
            foreach (var crew in Store.All)
            {
                if (crew == null || crew.Status != CrewStatus.Seated) continue;
                if (!AffectsGridBuffs(crew.Role)) continue;
                active.Add(crew.GridEntityId);
            }

            // Clear grids that lost all buffing crew (resets multipliers to 1).
            foreach (var oldId in _powerBuffGridIds)
            {
                if (active.Contains(oldId)) continue;
                IMyCubeGrid oldGrid;
                if (TryGetGrid(oldId, out oldGrid))
                    PowerBuff.ClearGrid(oldGrid);
            }

            foreach (var gridId in active)
            {
                IMyCubeGrid grid;
                if (TryGetGrid(gridId, out grid))
                    RefreshGridBuffs(grid);
            }

            _powerBuffGridIds.Clear();
            foreach (var id in active)
                _powerBuffGridIds.Add(id);
        }

        private void RefreshGridBuffs(IMyCubeGrid grid)
        {
            if (PowerBuff == null || Store == null || grid == null) return;
            var crew = Store.GetForGrid(grid.EntityId);
            float power = CrewConfig.GetSeatedRoleMultiplier(crew, CrewRole.Engineer);
            float gyro = CrewConfig.GetSeatedRoleMultiplier(crew, CrewRole.Helmsman);
            float thrust = CrewConfig.GetSeatedRoleMultiplier(crew, CrewRole.Propulsion);
            PowerBuff.ApplyGrid(grid, power);
            PowerBuff.ApplyGyros(grid, gyro);
            PowerBuff.ApplyThrust(grid, thrust);
            if (power > 1f || gyro > 1f || thrust > 1f)
                _powerBuffGridIds.Add(grid.EntityId);
        }

        private void BroadcastRoster(long gridEntityId)
        {
            var sync = new RosterSync
            {
                GridEntityId = gridEntityId,
                StoreBytes = Store.ToBytes()
            };
            var data = CrewNetworking.Serialize(sync);
            var players = new List<IMyPlayer>();
            MyAPIGateway.Players.GetPlayers(players);
            foreach (var p in players)
                CrewNetworking.SendToPlayer(CrewNetworking.RosterMsg, data, p.SteamUserId);

            // Listen/SP host: no RosterMsg on the local machine; refresh Custom Info here.
            if (_blockInfo != null)
                _blockInfo.RefreshAssigned();
            CrewStationLogic.RefreshAll();
        }

        /// <summary>
        /// Resolves the local player's managed grid from the controlled ship controller.
        /// Character IsOnShip / Parent fallback omitted: those members are not reliably available
        /// on ModAPI IMyCharacter in this SE surface; seat control is the supported path.
        /// </summary>
        public bool TryGetLocalManagedGrid(out IMyCubeGrid grid, out string error)
        {
            grid = null;
            error = null;
            var player = MyAPIGateway.Session.Player;
            if (player == null)
            {
                error = "No local player";
                return false;
            }

            IMyCubeGrid candidate = null;
            var controlled = player.Controller != null ? player.Controller.ControlledEntity as IMyShipController : null;
            if (controlled != null && controlled.CubeGrid != null)
                candidate = controlled.CubeGrid;

            if (candidate == null)
            {
                error = "Sit in a seat to manage crew";
                return false;
            }

            if (!HasManagePermission(player.IdentityId, candidate))
            {
                error = "No permission";
                return false;
            }

            grid = candidate;
            return true;
        }

        private static bool TryGetGrid(long id, out IMyCubeGrid grid)
        {
            grid = null;
            IMyEntity ent;
            if (!MyAPIGateway.Entities.TryGetEntityById(id, out ent)) return false;
            grid = ent as IMyCubeGrid;
            return grid != null;
        }

        public List<CrewRecord> GetCrewForConstruct(IMyCubeGrid grid)
        {
            var list = new List<CrewRecord>();
            if (Store == null || grid == null) return list;
            foreach (var r in Store.All)
            {
                if (r == null || r.Status != CrewStatus.Seated) continue;
                if (IsCrewOnConstruct(r, grid))
                    list.Add(r);
            }
            return list;
        }

        public List<CrewRecord> GetCrewForLocalOwner()
        {
            var player = MyAPIGateway.Session != null ? MyAPIGateway.Session.Player : null;
            if (player == null || Store == null) return new List<CrewRecord>();
            long ownerKey;
            bool ownerIsFaction;
            ResolveOwnerKey(player.IdentityId, out ownerKey, out ownerIsFaction);
            return Store.GetForOwner(ownerKey, ownerIsFaction);
        }

        private static bool IsCrewOnConstruct(CrewRecord crew, IMyCubeGrid grid)
        {
            if (crew == null || grid == null || crew.GridEntityId == 0) return false;
            if (crew.GridEntityId == grid.EntityId) return true;
            IMyCubeGrid crewGrid;
            if (!TryGetGrid(crew.GridEntityId, out crewGrid) || crewGrid == null) return false;
            return crewGrid.IsSameConstructAs(grid);
        }

        private static void ResolveOwnerKey(long identityId, out long ownerKey, out bool ownerIsFaction)
        {
            long factionId = 0;
            var faction = MyAPIGateway.Session.Factions.TryGetPlayerFaction(identityId);
            if (faction != null)
                factionId = faction.FactionId;
            CrewOwnership.Resolve(identityId, factionId, out ownerKey, out ownerIsFaction);
        }

        private static bool OwnsCrew(long identityId, CrewRecord crew)
        {
            if (crew == null) return false;
            long ownerKey;
            bool ownerIsFaction;
            ResolveOwnerKey(identityId, out ownerKey, out ownerIsFaction);
            return CrewOwnership.Matches(crew, ownerKey, ownerIsFaction);
        }

        public bool CanLocalPlayerManage(IMyCubeGrid grid)
        {
            var player = MyAPIGateway.Session.Player;
            if (player == null || grid == null) return false;
            return HasManagePermission(player.IdentityId, grid);
        }

        private static bool HasManagePermission(long identityId, IMyCubeGrid grid)
        {
            long owner = 0;
            var owners = grid.BigOwners;
            if (owners != null && owners.Count > 0) owner = owners[0];
            var sameFaction = false;
            var f1 = MyAPIGateway.Session.Factions.TryGetPlayerFaction(identityId);
            var f2 = owner != 0 ? MyAPIGateway.Session.Factions.TryGetPlayerFaction(owner) : null;
            if (f1 != null && f2 != null && f1.FactionId == f2.FactionId)
                sameFaction = true;
            return CrewValidation.CanManageGrid(identityId, owner, sameFaction);
        }

        private static long GetIdentityId(ulong steamId)
        {
            var player = GetPlayer(steamId);
            return player != null ? player.IdentityId : 0;
        }

        private static IMyPlayer GetPlayer(ulong steamId)
        {
            var players = new List<IMyPlayer>();
            MyAPIGateway.Players.GetPlayers(players, p => p.SteamUserId == steamId);
            if (players.Count > 0) return players[0];

            // SP / listen host: steam id can be 0 or briefly disagree with MyId.
            var local = MyAPIGateway.Session != null ? MyAPIGateway.Session.Player : null;
            if (local == null) return null;
            if (steamId == 0 || steamId == MyAPIGateway.Multiplayer.MyId)
                return local;
            return null;
        }

        private static void Notify(ulong steamId, string message)
        {
            if (steamId == MyAPIGateway.Multiplayer.MyId)
            {
                MyAPIGateway.Utilities.ShowMessage("HireCrew", message);
                return;
            }

            CrewNetworking.SendToPlayer(
                CrewNetworking.NotifyMsg,
                CrewNetworking.Serialize(new NotifyMessage { Text = message }),
                steamId);
        }
    }
}
