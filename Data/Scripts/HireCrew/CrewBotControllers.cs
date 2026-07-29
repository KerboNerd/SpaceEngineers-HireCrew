using System;
using System.Collections.Generic;
using Sandbox.Game;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.Utils;
using VRageMath;

namespace HireCrew
{
    /// <summary>
    /// AiEnabled-style bot controller pool: SpawnBot creates an IsBot player with an
    /// EntityController; we harvest that controller, delete the dummy body, then
    /// TakeControl on HireCrew ambient characters. Required for walk anim + no
    /// "disconnected player" HUD icon (plain OB characters lack a bot identity).
    ///
    /// Note: MyPlayerCollection.CreateNewIdentity/Player are not on the ModAPI and
    /// Reflection is prohibited for script mods — SpawnBot harvest is the only path.
    /// </summary>
    public static class CrewBotControllers
    {
        public sealed class ControlInfo
        {
            public IMyIdentity Identity;
            public IMyEntityController Controller;
        }

        private static readonly List<ControlInfo> Pool = new List<ControlInfo>();
        private static readonly HashSet<long> PendingHarvestEntityIds = new HashSet<long>();
        private static readonly List<IMyCharacter> DummiesToClose = new List<IMyCharacter>();
        private static readonly List<IMyPlayer> PlayerScratch = new List<IMyPlayer>();

        // Prefer HireCrew_Harvest (AnimalBot + Astronaut AI): humanoids fail SpawnBot in
        // deep space; vanilla SpaceSpider works but Spider.Teleport NREs after harvest.
        // SpaceSpider is last-resort only and is closed immediately after controller steal.
        private static readonly string[] HarvestSubtypes =
        {
            "HireCrew_Harvest",
            CrewConfig.AmbientBotSubtype,
            "Female_Astronaut",
            CrewConfig.AmbientBotSubtypeFallback,
            "SpaceSpider"
        };

        private static Vector3D _harvestPos;
        private static bool _harvestPosReady;
        private static int _spawnCooldownTicks;
        private static int _closeDelayTicks;
        private static int _logThrottle;
        private static int _harvestPosVariant;

        private const int TargetPoolSize = 4;
        private const int SpawnCooldownTicks = 90;
        // Close harvest dummies on the same tick as controller steal — leftover Spider AI
        // Teleport crashes the session if the dummy body (or orphaned agent) lingers.
        private const int DummyCloseDelayTicks = 0;

        public static int PoolCount
        {
            get { return Pool.Count; }
        }

        public static void Clear()
        {
            Pool.Clear();
            PendingHarvestEntityIds.Clear();
            DummiesToClose.Clear();
            _harvestPosReady = false;
            _spawnCooldownTicks = 0;
            _closeDelayTicks = 0;
            _harvestPosVariant = 0;
        }

        /// <summary>Call every server tick (~1 Hz is fine; more is better for harvest).</summary>
        public static void Tick()
        {
            if (MyAPIGateway.Multiplayer == null || !MyAPIGateway.Multiplayer.IsServer)
                return;

            TryHarvestPending();
            TickDummyClose();

            if (_spawnCooldownTicks > 0)
                _spawnCooldownTicks--;

            int needed = TargetPoolSize - Pool.Count - PendingHarvestEntityIds.Count;
            if (needed > 0 && _spawnCooldownTicks <= 0)
                TrySpawnHarvestDummy();
        }

        public static ControlInfo Take()
        {
            for (int i = 0; i < Pool.Count; i++)
            {
                var info = Pool[i];
                if (info == null || info.Identity == null || info.Controller == null)
                {
                    Pool.RemoveAt(i);
                    i--;
                    continue;
                }

                // Only skip identities owned by a real human player.
                // NOTE: Do NOT filter on TryGetSteamId — wildlife/bot identities can
                // report non-zero steam ids in SP, which previously left pool full
                // while ambient spawned uncontrolled (disconnected icon, no walk anim).
                IMyPlayer owner = MyAPIGateway.Players.TryGetIdentityId(info.Identity.IdentityId);
                if (owner != null && !owner.IsBot)
                    continue;

                Pool.RemoveAt(i);
                return info;
            }

            return null;
        }

        public static void Return(ControlInfo info)
        {
            if (info == null || info.Controller == null || info.Identity == null)
                return;
            // Drop any owner-faction membership before the identity re-enters the pool.
            ClearFaction(info.Identity.IdentityId);
            Pool.Add(info);
        }

        /// <summary>
        /// Join the harvested bot identity to the crew owner's faction so own-ship weapons
        /// (WeaponCore uses ControllingIdentityId) treat crew as friendly. Factionless
        /// owners get bots in the HireCrew NPC faction with max player reputation.
        /// </summary>
        public static void AlignToCrewOwner(long botIdentityId, CrewRecord crew)
        {
            if (botIdentityId == 0 || crew == null)
                return;
            if (MyAPIGateway.Session == null || MyAPIGateway.Session.Factions == null)
                return;

            long factionId = CrewBotRelations.ResolveFriendlyFactionId(
                crew,
                id =>
                {
                    var fac = MyAPIGateway.Session.Factions.TryGetPlayerFaction(id);
                    return fac != null ? fac.FactionId : 0L;
                });
            bool fallback = CrewBotRelations.NeedsFallbackFriendlyFaction(factionId);
            if (fallback)
            {
                factionId = EnsureAmbientFriendlyFactionId();
                if (factionId == 0)
                    return;
            }

            try
            {
                var current = MyAPIGateway.Session.Factions.TryGetPlayerFaction(botIdentityId);
                if (current != null && current.FactionId != factionId)
                    MyAPIGateway.Session.Factions.KickMember(current.FactionId, botIdentityId);

                var target = MyAPIGateway.Session.Factions.TryGetFactionById(factionId);
                if (target == null)
                    return;

                if (!target.IsMember(botIdentityId))
                {
                    MyAPIGateway.Session.Factions.AddPlayerToFaction(botIdentityId, factionId);
                    // Some worlds reject direct Add for bot identities — join+accept fallback.
                    if (!target.IsMember(botIdentityId))
                    {
                        MyAPIGateway.Session.Factions.SendJoinRequest(factionId, botIdentityId);
                        MyAPIGateway.Session.Factions.AcceptJoin(factionId, botIdentityId);
                    }
                }

                if (fallback && crew.OwnerIdentityId != 0)
                {
                    MyAPIGateway.Session.Factions.SetReputationBetweenPlayerAndFaction(
                        crew.OwnerIdentityId,
                        factionId,
                        CrewBotRelations.FriendlyReputation);
                }

                Log("aligned bot=" + botIdentityId
                    + " faction=" + factionId
                    + " member=" + target.IsMember(botIdentityId)
                    + " fallback=" + fallback);
            }
            catch (Exception e)
            {
                Log("AlignToCrewOwner failed id=" + botIdentityId + ": " + e.Message);
            }
        }

        private static long EnsureAmbientFriendlyFactionId()
        {
            try
            {
                var existing = MyAPIGateway.Session.Factions.TryGetFactionByTag(
                    CrewBotRelations.AmbientFriendlyFactionTag);
                if (existing != null)
                    return existing.FactionId;

                MyAPIGateway.Session.Factions.CreateNPCFaction(
                    CrewBotRelations.AmbientFriendlyFactionTag,
                    CrewBotRelations.AmbientFriendlyFactionName,
                    "HireCrew ambient crew",
                    "");

                existing = MyAPIGateway.Session.Factions.TryGetFactionByTag(
                    CrewBotRelations.AmbientFriendlyFactionTag);
                return existing != null ? existing.FactionId : 0L;
            }
            catch (Exception e)
            {
                Log("EnsureAmbientFriendlyFaction failed: " + e.Message);
                return 0;
            }
        }

        public static void ClearFaction(long botIdentityId)
        {
            if (botIdentityId == 0)
                return;
            if (MyAPIGateway.Session == null || MyAPIGateway.Session.Factions == null)
                return;
            try
            {
                var fac = MyAPIGateway.Session.Factions.TryGetPlayerFaction(botIdentityId);
                if (fac != null)
                    MyAPIGateway.Session.Factions.KickMember(fac.FactionId, botIdentityId);
            }
            catch { }
        }

        private static void EnsureHarvestPosition()
        {
            // Always deep space — never near the player. Harvest dummies (animal body)
            // briefly exist; spawning overhead made the icon visible in-game.
            _harvestPosVariant++;
            var rng = new Random(_harvestPosVariant * 9973 + 17);
            double r = 8000000 + rng.NextDouble() * 4000000;
            double ang = rng.NextDouble() * Math.PI * 2.0;
            _harvestPos = new Vector3D(Math.Cos(ang) * r, (rng.NextDouble() - 0.5) * r * 0.2, Math.Sin(ang) * r);
            _harvestPosReady = true;
        }

        private static void TrySpawnHarvestDummy()
        {
            EnsureHarvestPosition();
            _spawnCooldownTicks = SpawnCooldownTicks;

            foreach (var subtype in HarvestSubtypes)
            {
                if (string.IsNullOrEmpty(subtype))
                    continue;

                long spawnId = 0;
                try
                {
                    spawnId = MyVisualScriptLogicProvider.SpawnBot(
                        subtype, _harvestPos, Vector3D.Forward, Vector3D.Up, "");
                }
                catch (Exception e)
                {
                    Log("harvest SpawnBot threw subtype=" + subtype + ": " + e.Message);
                    continue;
                }

                if (spawnId == 0)
                    continue;

                IMyEntity ent;
                if (!MyAPIGateway.Entities.TryGetEntityById(spawnId, out ent) || ent == null)
                    continue;

                var bot = ent as IMyCharacter;
                if (bot == null || bot.Closed)
                    continue;

                PendingHarvestEntityIds.Add(bot.EntityId);
                Log("harvest dummy spawned subtype=" + subtype + " id=" + bot.EntityId);
                return;
            }

            if (++_logThrottle % 10 == 1)
                Log("harvest SpawnBot failed for all subtypes (pool=" + Pool.Count + ")");
        }

        private static void TryHarvestPending()
        {
            if (PendingHarvestEntityIds.Count == 0)
                return;

            PlayerScratch.Clear();
            MyAPIGateway.Players.GetPlayers(PlayerScratch);
            for (int i = 0; i < PlayerScratch.Count; i++)
            {
                var player = PlayerScratch[i];
                if (player == null || !player.IsBot || player.Controller == null || player.Identity == null)
                    continue;
                if (player.Character == null || player.Character.Closed)
                    continue;
                if (!PendingHarvestEntityIds.Remove(player.Character.EntityId))
                    continue;

                // Kick dummy out of any auto-joined faction until assigned to a crew owner.
                ClearFaction(player.IdentityId);

                Pool.Add(new ControlInfo
                {
                    Identity = player.Identity,
                    Controller = player.Controller
                });

                var dummyBody = player.Character;
                DummiesToClose.Add(dummyBody);
                _closeDelayTicks = DummyCloseDelayTicks;
                Log("harvested bot controller id=" + player.IdentityId + " pool=" + Pool.Count);
                if (DummyCloseDelayTicks <= 0)
                    CloseDummiesNow();
                return;
            }
        }

        private static void TickDummyClose()
        {
            if (DummiesToClose.Count == 0)
                return;
            if (_closeDelayTicks > 0)
            {
                _closeDelayTicks--;
                return;
            }

            CloseDummiesNow();
        }

        private static void CloseDummiesNow()
        {
            for (int i = 0; i < DummiesToClose.Count; i++)
            {
                var bot = DummiesToClose[i];
                try
                {
                    if (bot != null && !bot.Closed)
                        bot.Close();
                }
                catch { }
            }
            DummiesToClose.Clear();
            _closeDelayTicks = 0;
        }

        private static void Log(string msg)
        {
            try { MyLog.Default.WriteLine("HireCrew bots: " + msg); }
            catch { }
        }
    }
}
