using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.ModAPI;

namespace HireCrew
{
    [MySessionComponentDescriptor(MyUpdateOrder.AfterSimulation)]
    public sealed class CrewSession : MySessionComponentBase
    {
        public static CrewSession Instance { get; private set; }

        public WeaponAiBridge WeaponAi { get; private set; }
        public CrewStore Store { get; private set; }
        public HirePoolStore HirePools { get; private set; }
        public CrewPowerBuff PowerBuff { get; private set; }

        // Same instance required for UnregisterSecureMessageHandler.
        private Action<ushort, byte[], ulong, bool> _messageHandler;
        private int _tick;
        private readonly HashSet<ulong> _rosterSyncedSteamIds = new HashSet<ulong>();
        private readonly HashSet<long> _powerBuffGridIds = new HashSet<long>();
        private readonly Random _hireRng = new Random();
        private CrewHud _hud;
        private CrewBlockInfo _blockInfo;

        public override void LoadData()
        {
            Instance = this;
            Store = new CrewStore();
            HirePools = new HirePoolStore();
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
        }

        public override void SaveData()
        {
            if (!MyAPIGateway.Multiplayer.IsServer || Store == null) return;
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
        }

        public override void BeforeStart()
        {
            // Utilities/chat are reliable here; LoadData can be too early on some clients.
            if (_hud != null)
                _hud.EnsureChatRegistered();

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
            RestoreAssignmentsFromStore();
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

        protected override void UnloadData()
        {
            if (_hud != null)
            {
                _hud.Unload();
                _hud = null;
            }
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
            if (WeaponAi != null) WeaponAi.Unload();
            WeaponAi = null;
            PowerBuff = null;
            Store = null;
            HirePools = null;
            if (Instance == this) Instance = null;
        }

        public override void UpdateAfterSimulation()
        {
            if (_hud != null)
                _hud.Update();

            if (!MyAPIGateway.Multiplayer.IsServer || Store == null) return;
            _tick++;
            if (_tick % 60 != 0) return;
            SyncRosterToNewPlayers();
            TickTrainingCompletions();
            WatchCrewIntegrity();
            RefreshAllGridBuffs();
            if (HirePools != null && HirePools.TickRefresh(DateTime.UtcNow, _hireRng))
            {
                // Clients pull on open; no broadcast storm.
            }
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
            int mult = CrewConfig.DefaultPriceMultiplierPercent;
            if (HirePools != null)
            {
                var existing = HirePools.Get(blockEntityId);
                if (existing != null && existing.PriceMultiplierPercent > 0)
                    mult = existing.PriceMultiplierPercent;
            }
            ClientRequestHireDeskSettings(blockEntityId, refreshMinutes, mult);
        }

        public void ClientRequestHirePriceMultiplier(long blockEntityId, int priceMultiplierPercent)
        {
            int minutes = CrewConfig.DefaultRefreshMinutes;
            if (HirePools != null)
            {
                var existing = HirePools.Get(blockEntityId);
                if (existing != null)
                    minutes = existing.RefreshMinutes;
            }
            ClientRequestHireDeskSettings(blockEntityId, minutes, priceMultiplierPercent);
        }

        public void ClientRequestHireDeskSettings(long blockEntityId, int refreshMinutes, int priceMultiplierPercent)
        {
            var req = new HireRefreshRequest
            {
                BlockEntityId = blockEntityId,
                RefreshMinutes = refreshMinutes,
                PriceMultiplierPercent = priceMultiplierPercent
            };
            var data = CrewNetworking.Serialize(req);
            if (MyAPIGateway.Multiplayer.IsServer)
                HandleHireRefresh(req, MyAPIGateway.Session.Player.IdentityId, MyAPIGateway.Multiplayer.MyId);
            else
                CrewNetworking.SendToServer(CrewNetworking.HireRefreshMsg, data);
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
            pool.RefreshMinutes = CrewConfig.ClampRefreshMinutes(req.RefreshMinutes);
            // Multiplier rescales current candidate prices; refresh interval change does not reroll.
            int mult = req.PriceMultiplierPercent > 0
                ? req.PriceMultiplierPercent
                : CrewConfig.DefaultPriceMultiplierPercent;
            CrewHireGenerator.ApplyMultiplierToPool(pool, mult);
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

            // Logical crew: claim seat (+weapon for gunners). No live NPC (whitelist).
            // Still reject a seat a player is currently using.
            if (CrewStationLogic.IsSeatOccupiedByPlayer(seat))
                return "Seat occupied";

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
