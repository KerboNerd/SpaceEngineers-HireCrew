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
            if (!MyAPIGateway.Multiplayer.IsServer) return;
            var bytes = Store.ToBytes();
            MyAPIGateway.Utilities.SetVariable("HireCrew_Store", Convert.ToBase64String(bytes ?? new byte[0]));
        }

        public override void BeforeStart()
        {
            if (!MyAPIGateway.Multiplayer.IsServer) return;
            string b64;
            if (MyAPIGateway.Utilities.GetVariable("HireCrew_Store", out b64) && !string.IsNullOrEmpty(b64))
            {
                try { Store = CrewStore.FromBytes(Convert.FromBase64String(b64)); }
                catch { Store = new CrewStore(); }
            }
            ReseatAllFromStore();
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
            WatchCrewIntegrity();
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
                CrewNetworking.Deserialize<RosterSync>(data);
                // Client cache optional in v1; UI can request refresh via terminal read from block custom data later
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
            IMyCubeGrid grid;
            if (!TryGetGrid(req.GridEntityId, out grid)) return;
            if (!HasManagePermission(identityId, grid)) { Notify(steamId, "No permission"); return; }

            var tier = (CrewTier)req.Tier;
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
            IMyCubeGrid grid;
            if (!TryGetGrid(req.GridEntityId, out grid)) return;
            if (!HasManagePermission(identityId, grid)) { Notify(steamId, "No permission"); return; }

            var crew = Store.Get(req.CrewId);
            if (crew == null || crew.GridEntityId != req.GridEntityId)
            {
                Notify(steamId, CrewValidation.ErrorCrewMissing);
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

            if (WeaponAi != null && WeaponAi.IsReady && !WeaponAi.IsCoreWeapon(weapon))
            {
                Notify(steamId, "Not a WeaponCore weapon");
                return;
            }

            long charId;
            string seatErr;
            if (!Seater.TrySeat(seat, crew.DisplayName, out charId, out seatErr))
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
            IMyCubeGrid grid;
            if (!TryGetGrid(req.GridEntityId, out grid)) return;
            if (!HasManagePermission(identityId, grid)) { Notify(steamId, "No permission"); return; }
            InvalidateCrew(req.CrewId, "dismissed");
            Notify(steamId, "Crew dismissed");
            BroadcastRoster(req.GridEntityId);
        }

        public void InvalidateCrew(string crewId, string reason)
        {
            var crew = Store.Get(crewId);
            if (crew == null) return;

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
        }

        private void WatchCrewIntegrity()
        {
            var snapshot = new List<CrewRecord>(Store.All);
            foreach (var crew in snapshot)
            {
                if (crew.Status != CrewStatus.Seated) continue;

                IMyEntity seatEnt = null, wepEnt = null, gridEnt = null;
                var seatOk = crew.SeatEntityId.HasValue && MyAPIGateway.Entities.TryGetEntityById(crew.SeatEntityId.Value, out seatEnt) && seatEnt != null && !seatEnt.Closed;
                var wepOk = crew.WeaponEntityId.HasValue && MyAPIGateway.Entities.TryGetEntityById(crew.WeaponEntityId.Value, out wepEnt) && wepEnt != null && !wepEnt.Closed;
                var gridOk = MyAPIGateway.Entities.TryGetEntityById(crew.GridEntityId, out gridEnt) && gridEnt != null && !gridEnt.Closed;
                var alive = crew.CharacterEntityId.HasValue && Seater.IsAlive(crew.CharacterEntityId.Value);

                if (!seatOk || !wepOk || !gridOk || !alive)
                {
                    InvalidateCrew(crew.CrewId, "integrity");
                    continue;
                }

                var seatBlock = seatEnt as IMyCubeBlock;
                var wepBlock = wepEnt as IMyCubeBlock;
                if (seatBlock == null || wepBlock == null || seatBlock.CubeGrid.EntityId != wepBlock.CubeGrid.EntityId)
                    InvalidateCrew(crew.CrewId, "grid-split");
            }
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
                if (!Seater.TrySeat(seat, crew.DisplayName, out charId, out err))
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
