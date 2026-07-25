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
        public NpcSeater Seater { get; private set; }

        // Same instance required for UnregisterSecureMessageHandler.
        private Action<ushort, byte[], ulong, bool> _messageHandler;
        private int _tick;
        private readonly HashSet<ulong> _rosterSyncedSteamIds = new HashSet<ulong>();

        public override void LoadData()
        {
            Instance = this;
            Store = new CrewStore();
            Seater = new NpcSeater();
            WeaponAi = new WeaponAiBridge();
            WeaponAi.Load();
            _messageHandler = OnMessage;
            CrewNetworking.Register(_messageHandler);
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
        }

        public override void BeforeStart()
        {
            if (!MyAPIGateway.Multiplayer.IsServer) return;
            byte[] payload = TryLoadStoreBytes();
            if (payload != null)
            {
                try { Store = CrewStore.FromBytes(payload); }
                catch { Store = new CrewStore(); }
            }
            ReseatAllFromStore();
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

        protected override void UnloadData()
        {
            if (_messageHandler != null)
            {
                CrewNetworking.Unregister(_messageHandler);
                _messageHandler = null;
            }
            if (WeaponAi != null) WeaponAi.Unload();
            WeaponAi = null;
            Seater = null;
            Store = null;
            if (Instance == this) Instance = null;
        }

        public override void UpdateAfterSimulation()
        {
            if (!MyAPIGateway.Multiplayer.IsServer || Store == null) return;
            _tick++;
            if (_tick % 60 != 0) return;
            SyncRosterToNewPlayers();
            WatchCrewIntegrity();
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
                }
                return;
            }

            if (!MyAPIGateway.Multiplayer.IsServer) return;

            var identityId = GetIdentityId(sender);
            if (id == CrewNetworking.HireMsg)
                HandleHire(CrewNetworking.Deserialize<HireRequest>(data), identityId, sender);
            else if (id == CrewNetworking.AssignMsg)
                HandleAssign(CrewNetworking.Deserialize<AssignRequest>(data), identityId, sender);
            else if (id == CrewNetworking.DismissMsg)
                HandleDismiss(CrewNetworking.Deserialize<DismissRequest>(data), identityId, sender);
        }

        public void ClientRequestHire(long gridEntityId, CrewTier tier)
        {
            var req = new HireRequest { GridEntityId = gridEntityId, Tier = (int)tier };
            var data = CrewNetworking.Serialize(req);
            if (MyAPIGateway.Multiplayer.IsServer)
                HandleHire(req, MyAPIGateway.Session.Player.IdentityId, MyAPIGateway.Multiplayer.MyId);
            else
                CrewNetworking.SendToServer(CrewNetworking.HireMsg, data);
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

        public void ClientRequestDismiss(string crewId, long gridEntityId)
        {
            var req = new DismissRequest { CrewId = crewId, GridEntityId = gridEntityId };
            var data = CrewNetworking.Serialize(req);
            if (MyAPIGateway.Multiplayer.IsServer)
                HandleDismiss(req, MyAPIGateway.Session.Player.IdentityId, MyAPIGateway.Multiplayer.MyId);
            else
                CrewNetworking.SendToServer(CrewNetworking.DismissMsg, data);
        }

        private void HandleHire(HireRequest req, long identityId, ulong steamId)
        {
            if (req == null) return;

            IMyCubeGrid grid;
            if (!TryGetGrid(req.GridEntityId, out grid)) return;
            if (!HasManagePermission(identityId, grid)) { Notify(steamId, "No permission"); return; }

            var tierInt = req.Tier;
            if (tierInt < (int)CrewTier.Recruit) tierInt = (int)CrewTier.Recruit;
            if (tierInt > (int)CrewTier.Elite) tierInt = (int)CrewTier.Elite;
            var tier = (CrewTier)tierInt;
            var price = CrewConfig.GetPrice(tier);
            string err;
            if (!CrewEconomy.TryCharge(identityId, price, out err))
            {
                Notify(steamId, err);
                return;
            }

            var record = new CrewRecord
            {
                CrewId = Guid.NewGuid().ToString("N"),
                Tier = tier,
                GridEntityId = req.GridEntityId,
                OwnerIdentityId = identityId,
                Status = CrewStatus.Unassigned,
                DisplayName = tier + " Gunner"
            };
            Store.Upsert(record);
            Notify(steamId, "Hired " + record.DisplayName);
            BroadcastRoster(req.GridEntityId);
        }

        private void HandleAssign(AssignRequest req, long identityId, ulong steamId)
        {
            if (req == null) return;

            IMyCubeGrid grid;
            if (!TryGetGrid(req.GridEntityId, out grid)) return;
            if (!HasManagePermission(identityId, grid)) { Notify(steamId, "No permission"); return; }

            var crew = Store.Get(req.CrewId);
            if (crew == null || crew.GridEntityId != req.GridEntityId)
            {
                Notify(steamId, CrewValidation.ErrorCrewMissing);
                return;
            }

            if (crew.Status == CrewStatus.Seated)
            {
                Notify(steamId, "Already assigned — dismiss first");
                return;
            }

            var err = CrewValidation.ValidateAssign(Store.GetForGrid(req.GridEntityId), req.CrewId, req.GridEntityId, req.SeatEntityId, req.WeaponEntityId);
            if (err != null) { Notify(steamId, err); return; }

            IMyEntity seatEnt, wepEnt;
            if (!MyAPIGateway.Entities.TryGetEntityById(req.SeatEntityId, out seatEnt) ||
                !MyAPIGateway.Entities.TryGetEntityById(req.WeaponEntityId, out wepEnt))
            {
                Notify(steamId, "Seat or weapon missing");
                return;
            }

            var seat = seatEnt as IMyShipController;
            var weapon = wepEnt as IMyTerminalBlock;
            if (seat == null || weapon == null || seat.CubeGrid.EntityId != grid.EntityId || weapon.CubeGrid.EntityId != grid.EntityId)
            {
                Notify(steamId, CrewValidation.ErrorWrongGrid);
                return;
            }

            if (WeaponAi == null || !WeaponAi.IsReady)
            {
                Notify(steamId, "WeaponCore not ready");
                return;
            }
            if (!WeaponAi.IsCoreWeapon(weapon))
            {
                Notify(steamId, "Not a WeaponCore weapon");
                return;
            }

            long charId;
            string seatErr;
            if (!Seater.TrySeat(seat, crew.DisplayName, CollectKnownCharacterIds(), out charId, out seatErr))
            {
                Notify(steamId, seatErr ?? "Seat failed");
                return;
            }

            crew.SeatEntityId = req.SeatEntityId;
            crew.WeaponEntityId = req.WeaponEntityId;
            crew.CharacterEntityId = charId;
            crew.Status = CrewStatus.Seated;
            Store.Upsert(crew);
            WeaponAi.SetManned(weapon, true, crew.Tier);
            Notify(steamId, "Crew assigned");
            BroadcastRoster(req.GridEntityId);
        }

        private void HandleDismiss(DismissRequest req, long identityId, ulong steamId)
        {
            if (req == null) return;

            IMyCubeGrid grid;
            if (!TryGetGrid(req.GridEntityId, out grid)) return;
            if (!HasManagePermission(identityId, grid)) { Notify(steamId, "No permission"); return; }

            var crew = Store.Get(req.CrewId);
            if (crew == null || crew.GridEntityId != req.GridEntityId)
            {
                Notify(steamId, CrewValidation.ErrorCrewMissing);
                return;
            }

            if (!InvalidateCrew(req.CrewId, "dismissed"))
                return;

            Notify(steamId, "Crew dismissed");
            BroadcastRoster(req.GridEntityId);
        }

        public bool InvalidateCrew(string crewId, string reason)
        {
            var crew = Store.Get(crewId);
            if (crew == null) return false;

            if (crew.WeaponEntityId.HasValue)
            {
                IMyEntity wepEnt;
                if (MyAPIGateway.Entities.TryGetEntityById(crew.WeaponEntityId.Value, out wepEnt))
                {
                    var weapon = wepEnt as IMyTerminalBlock;
                    if (weapon != null) WeaponAi.ForceAiOff(weapon);
                }
            }

            if (crew.CharacterEntityId.HasValue)
                Seater.Despawn(crew.CharacterEntityId.Value);

            Store.Remove(crewId);
            return true;
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
                var wepOk = crew.WeaponEntityId.HasValue && MyAPIGateway.Entities.TryGetEntityById(crew.WeaponEntityId.Value, out wepEnt) && wepEnt != null && !wepEnt.Closed;
                var alive = crew.CharacterEntityId.HasValue && Seater.IsAlive(crew.CharacterEntityId.Value);

                if (!seatOk || !wepOk || !alive)
                {
                    if (InvalidateCrew(crew.CrewId, "integrity"))
                        dirtyGrids.Add(crew.GridEntityId);
                    continue;
                }

                var seatCtrl = seatEnt as IMyShipController;
                if (seatCtrl != null && crew.CharacterEntityId.HasValue)
                {
                    var pilot = seatCtrl.Pilot;
                    if (pilot == null || pilot.EntityId != crew.CharacterEntityId.Value)
                    {
                        if (InvalidateCrew(crew.CrewId, "ejected"))
                            dirtyGrids.Add(crew.GridEntityId);
                        continue;
                    }
                }

                var seatBlock = seatEnt as IMyCubeBlock;
                var wepBlock = wepEnt as IMyCubeBlock;
                if (seatBlock == null || wepBlock == null || seatBlock.CubeGrid.EntityId != wepBlock.CubeGrid.EntityId)
                {
                    if (InvalidateCrew(crew.CrewId, "grid-split"))
                        dirtyGrids.Add(crew.GridEntityId);
                    continue;
                }

                // Grid split can reassign CubeGrid.EntityId while seat+weapon stay together.
                var seatGridId = seatBlock.CubeGrid.EntityId;
                if (crew.GridEntityId != seatGridId)
                {
                    var oldGridId = crew.GridEntityId;
                    crew.GridEntityId = seatGridId;
                    Store.Upsert(crew);
                    dirtyGrids.Add(oldGridId);
                    dirtyGrids.Add(seatGridId);
                }
            }

            foreach (var gridId in dirtyGrids)
                BroadcastRoster(gridId);
        }

        private void ReseatAllFromStore()
        {
            var snapshot = new List<CrewRecord>(Store.All);
            foreach (var crew in snapshot)
            {
                if (crew.Status != CrewStatus.Seated || !crew.SeatEntityId.HasValue || !crew.WeaponEntityId.HasValue)
                    continue;

                IMyEntity seatEnt, wepEnt;
                if (!MyAPIGateway.Entities.TryGetEntityById(crew.SeatEntityId.Value, out seatEnt) ||
                    !MyAPIGateway.Entities.TryGetEntityById(crew.WeaponEntityId.Value, out wepEnt))
                {
                    InvalidateCrew(crew.CrewId, "missing-on-load");
                    continue;
                }

                var seat = seatEnt as IMyShipController;
                var weapon = wepEnt as IMyTerminalBlock;
                if (seat == null || weapon == null)
                {
                    InvalidateCrew(crew.CrewId, "bad-refs");
                    continue;
                }

                long charId;
                string err;
                if (!Seater.TrySeat(seat, crew.DisplayName, CollectKnownCharacterIds(), out charId, out err))
                {
                    InvalidateCrew(crew.CrewId, "reseat-failed");
                    continue;
                }

                crew.CharacterEntityId = charId;
                Store.Upsert(crew);
                WeaponAi.SetManned(weapon, true, crew.Tier);
            }
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
        }

        private HashSet<long> CollectKnownCharacterIds()
        {
            var known = new HashSet<long>();
            foreach (var c in Store.All)
            {
                if (c.CharacterEntityId.HasValue && c.CharacterEntityId.Value != 0)
                    known.Add(c.CharacterEntityId.Value);
            }
            return known;
        }

        private static bool TryGetGrid(long id, out IMyCubeGrid grid)
        {
            grid = null;
            IMyEntity ent;
            if (!MyAPIGateway.Entities.TryGetEntityById(id, out ent)) return false;
            grid = ent as IMyCubeGrid;
            return grid != null;
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
            var players = new List<IMyPlayer>();
            MyAPIGateway.Players.GetPlayers(players, p => p.SteamUserId == steamId);
            return players.Count > 0 ? players[0].IdentityId : 0;
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
