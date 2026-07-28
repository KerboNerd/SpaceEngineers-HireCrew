using System;
using System.Collections.Generic;
using System.Text;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.Utils;
using VRageMath;

namespace HireCrew
{
    /// <summary>
    /// Server-side /hirecrew admin verb dispatch.
    /// </summary>
    public static class CrewAdminCommands
    {
        private const int RosterLineCap = 40;

        public static bool IsAdmin(ulong steamId)
        {
            try
            {
                if (MyAPIGateway.Session == null) return false;
                return MyAPIGateway.Session.GetUserPromoteLevel(steamId) >= MyPromoteLevel.Admin;
            }
            catch
            {
                return false;
            }
        }

        public static void Handle(CrewSession session, AdminCommandRequest req, long adminIdentityId, ulong adminSteamId)
        {
            if (session == null || req == null) return;
            if (!IsAdmin(adminSteamId))
            {
                session.AdminNotify(adminSteamId, "Admin only");
                return;
            }

            string verb = (req.Verb ?? "").Trim().ToLowerInvariant();
            var args = req.Args ?? new List<string>();

            try
            {
                if (verb.Length == 0 || verb == "help")
                {
                    SendHelp(session, adminSteamId);
                    return;
                }
                if (verb == "config")
                {
                    CmdConfig(session, args, adminSteamId);
                    return;
                }
                if (verb == "hire")
                {
                    CmdHire(session, args, adminIdentityId, adminSteamId);
                    return;
                }
                if (verb == "reroll")
                {
                    CmdReroll(session, args, adminSteamId);
                    return;
                }
                if (verb == "roster")
                {
                    CmdRoster(session, args, adminSteamId);
                    return;
                }
                if (verb == "dismiss")
                {
                    CmdDismiss(session, args, adminSteamId);
                    return;
                }
                if (verb == "clear")
                {
                    CmdClear(session, args, adminSteamId);
                    return;
                }
                if (verb == "transfer")
                {
                    CmdTransfer(session, args, adminSteamId);
                    return;
                }

                session.AdminNotify(adminSteamId, "Unknown command. /hirecrew help");
            }
            catch (Exception e)
            {
                session.AdminNotify(adminSteamId, "Command error: " + e.Message);
                MyLog.Default.WriteLineAndConsole("[HireCrew] admin command exception: " + e);
            }
        }

        private static void SendHelp(CrewSession session, ulong steamId)
        {
            session.AdminNotifyLines(steamId, new List<string>
            {
                "HireCrew admin (/hirecrew or /hc):",
                "help | config show|reload",
                "hire <role> <stars> [player]",
                "reroll <blockId>|near",
                "roster <player|steamid>",
                "dismiss <crewId>",
                "clear roster <player|steamid>",
                "clear pool <blockId>|near",
                "transfer <crewId> <player|steamid>"
            });
        }

        private static void CmdConfig(CrewSession session, List<string> args, ulong steamId)
        {
            if (args.Count < 1)
            {
                session.AdminNotify(steamId, "Usage: /hirecrew config show|reload");
                return;
            }
            string sub = args[0].ToLowerInvariant();
            if (sub == "show")
            {
                var cfg = HireWorldConfig.Current ?? HireWorldConfig.CreateDefaults();
                session.AdminNotifyLines(steamId, new List<string>
                {
                    "HireCrewConfig:",
                    "Refresh " + cfg.RefreshMinutesMin + "-" + cfg.RefreshMinutesMax + " def " + cfg.RefreshMinutesDefault,
                    "PriceMult% " + cfg.PriceMultiplierPercentMin + "-" + cfg.PriceMultiplierPercentMax + " def " + cfg.PriceMultiplierPercentDefault,
                    "Candidates " + cfg.MinCandidates + "-" + cfg.MaxCandidates,
                    "Variance " + cfg.PriceVarianceFraction.ToString("0.##"),
                    "RolesMask " + cfg.AllowedRolesMask,
                    "RefillDefault " + cfg.RefillOnHireDefault
                });
                return;
            }
            if (sub == "reload")
            {
                string err;
                if (!session.TryReloadHireWorldConfig(out err))
                {
                    session.AdminNotify(steamId, "Config reload failed — " + (err ?? "unknown"));
                    return;
                }
                session.AdminNotify(steamId, "Config reloaded");
                MyLog.Default.WriteLineAndConsole("[HireCrew] admin " + steamId + " config reload");
                return;
            }
            session.AdminNotify(steamId, "Usage: /hirecrew config show|reload");
        }

        private static void CmdHire(CrewSession session, List<string> args, long adminIdentityId, ulong steamId)
        {
            if (args.Count < 2)
            {
                session.AdminNotify(steamId, "Usage: /hirecrew hire <role> <stars> [player]");
                return;
            }
            CrewRole role;
            if (!TryParseRole(args[0], out role))
            {
                session.AdminNotify(steamId, "Bad role. gunner|reactor|helm|prop|qm");
                return;
            }
            int stars;
            if (!TryParseStars(args[1], out stars))
            {
                session.AdminNotify(steamId, "Bad stars. 0-5");
                return;
            }

            long targetIdentity = adminIdentityId;
            if (args.Count >= 3)
            {
                string err;
                long id;
                if (!TryResolveIdentity(session, args[2], out id, out err))
                {
                    session.AdminNotify(steamId, err);
                    return;
                }
                targetIdentity = id;
            }
            if (targetIdentity == 0)
            {
                session.AdminNotify(steamId, "Player not found");
                return;
            }

            if (session.Store == null)
            {
                session.AdminNotify(steamId, "Store missing");
                return;
            }

            long ownerKey;
            bool ownerIsFaction;
            session.AdminResolveOwnerKey(targetIdentity, out ownerKey, out ownerIsFaction);

            var record = new CrewRecord
            {
                CrewId = Guid.NewGuid().ToString("N"),
                Stars = CrewConfig.ClampStars(stars),
                Role = role,
                GridEntityId = 0,
                OwnerIdentityId = targetIdentity,
                OwnerKey = ownerKey,
                OwnerIsFaction = ownerIsFaction,
                Status = CrewStatus.Unassigned,
                DisplayName = CrewNames.RollFullName(session.HireRng)
            };
            session.Store.Upsert(record);
            session.AdminBroadcastRoster();
            session.AdminNotify(steamId,
                "Hired " + record.DisplayName + " " + CrewConfig.FormatStars(record.Stars)
                + " " + CrewConfig.RoleLabel(role) + " → " + targetIdentity);
            MyLog.Default.WriteLineAndConsole("[HireCrew] admin " + steamId + " hire " + record.CrewId);
        }

        private static void CmdReroll(CrewSession session, List<string> args, ulong steamId)
        {
            if (args.Count < 1)
            {
                session.AdminNotify(steamId, "Usage: /hirecrew reroll <blockId>|near");
                return;
            }
            long blockId;
            string err;
            if (!TryResolveDesk(session, args[0], steamId, out blockId, out err))
            {
                session.AdminNotify(steamId, err);
                return;
            }
            if (!RefreshDesk(session, blockId, out err))
            {
                session.AdminNotify(steamId, err);
                return;
            }
            session.AdminNotify(steamId, "Rerolled desk " + blockId);
            MyLog.Default.WriteLineAndConsole("[HireCrew] admin " + steamId + " reroll " + blockId);
        }

        private static void CmdRoster(CrewSession session, List<string> args, ulong steamId)
        {
            if (args.Count < 1)
            {
                session.AdminNotify(steamId, "Usage: /hirecrew roster <player|steamid>");
                return;
            }
            string err;
            long identityId;
            if (!TryResolveIdentity(session, args[0], out identityId, out err))
            {
                session.AdminNotify(steamId, err);
                return;
            }

            long ownerKey;
            bool ownerIsFaction;
            session.AdminResolveOwnerKey(identityId, out ownerKey, out ownerIsFaction);
            var list = session.Store != null
                ? session.Store.GetForOwner(ownerKey, ownerIsFaction)
                : new List<CrewRecord>();

            if (list.Count == 0)
            {
                session.AdminNotify(steamId, "Roster empty for " + identityId);
                return;
            }

            var lines = new List<string>();
            lines.Add("Roster " + identityId + " (" + list.Count + "):");
            int shown = 0;
            for (int i = 0; i < list.Count; i++)
            {
                var c = list[i];
                if (c == null) continue;
                if (shown >= RosterLineCap)
                {
                    lines.Add("...and " + (list.Count - shown) + " more");
                    break;
                }
                lines.Add(ShortCrewLine(c));
                shown++;
            }
            session.AdminNotifyLines(steamId, lines);
        }

        private static void CmdDismiss(CrewSession session, List<string> args, ulong steamId)
        {
            if (args.Count < 1)
            {
                session.AdminNotify(steamId, "Usage: /hirecrew dismiss <crewId>");
                return;
            }
            string crewId = args[0];
            var crew = session.Store != null ? session.Store.Get(crewId) : null;
            if (crew == null)
            {
                session.AdminNotify(steamId, "Crew not found");
                return;
            }
            long gridId = crew.GridEntityId;
            bool wasSeated = crew.Status == CrewStatus.Seated && gridId != 0;
            if (!session.RemoveCrew(crewId))
            {
                session.AdminNotify(steamId, "Dismiss failed");
                return;
            }
            if (wasSeated)
            {
                IMyEntity ent;
                if (MyAPIGateway.Entities.TryGetEntityById(gridId, out ent))
                {
                    var grid = ent as IMyCubeGrid;
                    if (grid != null)
                        session.AdminRefreshGridBuffs(grid);
                }
            }
            session.AdminBroadcastRoster();
            session.AdminNotify(steamId, "Dismissed " + crewId);
            MyLog.Default.WriteLineAndConsole("[HireCrew] admin " + steamId + " dismiss " + crewId);
        }

        private static void CmdClear(CrewSession session, List<string> args, ulong steamId)
        {
            if (args.Count < 2)
            {
                session.AdminNotify(steamId, "Usage: /hirecrew clear roster <player>|clear pool <id|near>");
                return;
            }
            string kind = args[0].ToLowerInvariant();
            if (kind == "roster")
            {
                string err;
                long identityId;
                if (!TryResolveIdentity(session, args[1], out identityId, out err))
                {
                    session.AdminNotify(steamId, err);
                    return;
                }
                long ownerKey;
                bool ownerIsFaction;
                session.AdminResolveOwnerKey(identityId, out ownerKey, out ownerIsFaction);
                var list = session.Store != null
                    ? session.Store.GetForOwner(ownerKey, ownerIsFaction)
                    : new List<CrewRecord>();
                int n = 0;
                for (int i = 0; i < list.Count; i++)
                {
                    var c = list[i];
                    if (c == null || string.IsNullOrEmpty(c.CrewId)) continue;
                    if (session.RemoveCrew(c.CrewId)) n++;
                }
                session.AdminBroadcastRoster();
                session.AdminNotify(steamId, "Cleared " + n + " crew");
                MyLog.Default.WriteLineAndConsole("[HireCrew] admin " + steamId + " clear roster " + identityId + " n=" + n);
                return;
            }
            if (kind == "pool")
            {
                long blockId;
                string err;
                if (!TryResolveDesk(session, args[1], steamId, out blockId, out err))
                {
                    session.AdminNotify(steamId, err);
                    return;
                }
                if (!RefreshDesk(session, blockId, out err))
                {
                    session.AdminNotify(steamId, err);
                    return;
                }
                session.AdminNotify(steamId, "Cleared/rerolled pool " + blockId);
                MyLog.Default.WriteLineAndConsole("[HireCrew] admin " + steamId + " clear pool " + blockId);
                return;
            }
            session.AdminNotify(steamId, "Usage: /hirecrew clear roster|pool …");
        }

        private static void CmdTransfer(CrewSession session, List<string> args, ulong steamId)
        {
            if (args.Count < 2)
            {
                session.AdminNotify(steamId, "Usage: /hirecrew transfer <crewId> <player|steamid>");
                return;
            }
            var crew = session.Store != null ? session.Store.Get(args[0]) : null;
            if (crew == null)
            {
                session.AdminNotify(steamId, "Crew not found");
                return;
            }
            string err;
            long targetIdentity;
            if (!TryResolveIdentity(session, args[1], out targetIdentity, out err))
            {
                session.AdminNotify(steamId, err);
                return;
            }

            if (crew.Status == CrewStatus.Seated)
                session.ReturnCrewToPool(crew);

            long ownerKey;
            bool ownerIsFaction;
            session.AdminResolveOwnerKey(targetIdentity, out ownerKey, out ownerIsFaction);
            crew.OwnerIdentityId = targetIdentity;
            crew.OwnerKey = ownerKey;
            crew.OwnerIsFaction = ownerIsFaction;
            crew.Status = CrewStatus.Unassigned;
            crew.GridEntityId = 0;
            session.Store.Upsert(crew);
            session.AdminBroadcastRoster();
            session.AdminNotify(steamId, "Transferred " + crew.CrewId + " → " + targetIdentity);
            MyLog.Default.WriteLineAndConsole("[HireCrew] admin " + steamId + " transfer " + crew.CrewId + " → " + targetIdentity);
        }

        private static bool RefreshDesk(CrewSession session, long blockEntityId, out string error)
        {
            error = null;
            if (session.HirePools == null)
            {
                error = "Hire pools missing";
                return false;
            }
            IMyEntity ent;
            if (!MyAPIGateway.Entities.TryGetEntityById(blockEntityId, out ent) || ent == null)
            {
                error = "Hire desk not found";
                return false;
            }
            var block = ent as IMyTerminalBlock;
            if (block == null || !CrewHireBlockLogic.IsHireDesk(block) || block.CubeGrid == null)
            {
                error = "Hire desk not found";
                return false;
            }
            var pool = session.HirePools.Ensure(block.EntityId, block.CubeGrid.EntityId, session.HireRng, DateTime.UtcNow);
            CrewHireGenerator.RefreshPool(pool, session.HireRng, DateTime.UtcNow);
            session.AdminBroadcastHirePool(pool);
            return true;
        }

        private static bool TryResolveDesk(CrewSession session, string token, ulong adminSteamId, out long blockId, out string error)
        {
            blockId = 0;
            error = null;
            if (string.Equals(token, "near", StringComparison.OrdinalIgnoreCase))
            {
                if (!TryFindNearestDesk(adminSteamId, out blockId))
                {
                    error = "No hire desk near you";
                    return false;
                }
                return true;
            }
            long id;
            if (!long.TryParse(token, out id) || id == 0)
            {
                error = "Bad block id (or use near)";
                return false;
            }
            blockId = id;
            return true;
        }

        private static bool TryFindNearestDesk(ulong adminSteamId, out long blockId)
        {
            blockId = 0;
            var player = GetOnlinePlayer(adminSteamId);
            if (player == null || player.Character == null) return false;
            Vector3D origin = player.Character.GetPosition();

            // Prefer desks on the construct the admin is currently in (server-safe; no Session.Player).
            IMyCubeGrid grid = null;
            var top = player.Character.GetTopMostParent() as IMyCubeGrid;
            if (top != null)
                grid = top;

            double best = double.MaxValue;
            long bestId = 0;
            var session = CrewSession.Instance;
            if (session != null && session.HirePools != null)
            {
                foreach (var pool in session.HirePools.All)
                {
                    if (pool == null) continue;
                    IMyEntity ent;
                    if (!MyAPIGateway.Entities.TryGetEntityById(pool.BlockEntityId, out ent) || ent == null)
                        continue;
                    var block = ent as IMyTerminalBlock;
                    if (block == null || !CrewHireBlockLogic.IsHireDesk(block)) continue;
                    if (grid != null && block.CubeGrid != null && !block.CubeGrid.IsSameConstructAs(grid))
                        continue;
                    double d = Vector3D.DistanceSquared(origin, block.GetPosition());
                    if (d < best)
                    {
                        best = d;
                        bestId = block.EntityId;
                    }
                }
            }

            // If nothing on current construct, fall back to nearest desk in world from pools.
            if (bestId == 0 && session != null && session.HirePools != null && grid != null)
            {
                foreach (var pool in session.HirePools.All)
                {
                    if (pool == null) continue;
                    IMyEntity ent;
                    if (!MyAPIGateway.Entities.TryGetEntityById(pool.BlockEntityId, out ent) || ent == null)
                        continue;
                    var block = ent as IMyTerminalBlock;
                    if (block == null || !CrewHireBlockLogic.IsHireDesk(block)) continue;
                    double d = Vector3D.DistanceSquared(origin, block.GetPosition());
                    if (d < best)
                    {
                        best = d;
                        bestId = block.EntityId;
                    }
                }
            }

            if (bestId == 0) return false;
            blockId = bestId;
            return true;
        }

        /// <summary>
        /// Resolve online name/Steam ID, or offline Steam ID via stored OwnerIdentityId / Players API.
        /// </summary>
        public static bool TryResolveIdentity(CrewSession session, string token, out long identityId, out string error)
        {
            identityId = 0;
            error = null;
            if (string.IsNullOrEmpty(token))
            {
                error = "Player not found";
                return false;
            }

            ulong steamId;
            if (ulong.TryParse(token, out steamId))
            {
                var online = GetOnlinePlayer(steamId);
                if (online != null)
                {
                    identityId = online.IdentityId;
                    return true;
                }

                long mapped = MyAPIGateway.Players.TryGetIdentityId(steamId);
                if (mapped != 0)
                {
                    identityId = mapped;
                    return true;
                }

                error = "Player not found";
                return false;
            }

            var matches = new List<IMyPlayer>();
            var players = new List<IMyPlayer>();
            MyAPIGateway.Players.GetPlayers(players);
            for (int i = 0; i < players.Count; i++)
            {
                var p = players[i];
                if (p == null || string.IsNullOrEmpty(p.DisplayName)) continue;
                if (string.Equals(p.DisplayName, token, StringComparison.OrdinalIgnoreCase))
                    matches.Add(p);
            }
            if (matches.Count == 0)
            {
                error = "Player not found";
                return false;
            }
            if (matches.Count > 1)
            {
                var sb = new StringBuilder("Ambiguous:");
                for (int i = 0; i < matches.Count && i < 6; i++)
                    sb.Append(' ').Append(matches[i].DisplayName).Append('(').Append(matches[i].SteamUserId).Append(')');
                error = sb.ToString();
                return false;
            }
            identityId = matches[0].IdentityId;
            return true;
        }

        private static IMyPlayer GetOnlinePlayer(ulong steamId)
        {
            var players = new List<IMyPlayer>();
            MyAPIGateway.Players.GetPlayers(players, p => p != null && p.SteamUserId == steamId);
            if (players.Count > 0) return players[0];
            var local = MyAPIGateway.Session != null ? MyAPIGateway.Session.Player : null;
            if (local != null && (steamId == 0 || steamId == MyAPIGateway.Multiplayer.MyId))
                return local;
            return null;
        }

        private static string ShortCrewLine(CrewRecord c)
        {
            string status = c.Status == CrewStatus.Seated ? "seated" : "pool";
            return c.CrewId.Substring(0, Math.Min(8, c.CrewId.Length))
                + " " + (c.DisplayName ?? "?")
                + " " + CrewConfig.RoleLabel(c.Role)
                + " " + CrewConfig.FormatStars(c.Stars)
                + " " + status;
        }

        public static bool TryParseRole(string roleName, out CrewRole role)
        {
            role = CrewRole.Gunner;
            if (string.IsNullOrEmpty(roleName)) return false;
            if (string.Equals(roleName, "gunner", StringComparison.OrdinalIgnoreCase)
                || string.Equals(roleName, "g", StringComparison.OrdinalIgnoreCase))
            {
                role = CrewRole.Gunner;
                return true;
            }
            if (string.Equals(roleName, "engineer", StringComparison.OrdinalIgnoreCase)
                || string.Equals(roleName, "eng", StringComparison.OrdinalIgnoreCase)
                || string.Equals(roleName, "reactor", StringComparison.OrdinalIgnoreCase)
                || string.Equals(roleName, "technician", StringComparison.OrdinalIgnoreCase)
                || string.Equals(roleName, "rt", StringComparison.OrdinalIgnoreCase))
            {
                role = CrewRole.Engineer;
                return true;
            }
            if (string.Equals(roleName, "helmsman", StringComparison.OrdinalIgnoreCase)
                || string.Equals(roleName, "helm", StringComparison.OrdinalIgnoreCase))
            {
                role = CrewRole.Helmsman;
                return true;
            }
            if (string.Equals(roleName, "propulsion", StringComparison.OrdinalIgnoreCase)
                || string.Equals(roleName, "prop", StringComparison.OrdinalIgnoreCase))
            {
                role = CrewRole.Propulsion;
                return true;
            }
            if (string.Equals(roleName, "quartermaster", StringComparison.OrdinalIgnoreCase)
                || string.Equals(roleName, "qm", StringComparison.OrdinalIgnoreCase))
            {
                role = CrewRole.Quartermaster;
                return true;
            }
            if (string.Equals(roleName, "construction", StringComparison.OrdinalIgnoreCase)
                || string.Equals(roleName, "construct", StringComparison.OrdinalIgnoreCase)
                || string.Equals(roleName, "damage", StringComparison.OrdinalIgnoreCase)
                || string.Equals(roleName, "dc", StringComparison.OrdinalIgnoreCase)
                || string.Equals(roleName, "welder", StringComparison.OrdinalIgnoreCase)
                || string.Equals(roleName, "damagecontrol", StringComparison.OrdinalIgnoreCase))
            {
                role = CrewRole.DamageControl;
                return true;
            }
            return false;
        }

        public static bool TryParseStars(string starToken, out int stars)
        {
            stars = 1;
            if (string.IsNullOrEmpty(starToken)) return false;
            if (string.Equals(starToken, "recruit", StringComparison.OrdinalIgnoreCase)
                || string.Equals(starToken, "r", StringComparison.OrdinalIgnoreCase))
            {
                stars = 1;
                return true;
            }
            if (string.Equals(starToken, "regular", StringComparison.OrdinalIgnoreCase)
                || string.Equals(starToken, "reg", StringComparison.OrdinalIgnoreCase))
            {
                stars = 3;
                return true;
            }
            if (string.Equals(starToken, "elite", StringComparison.OrdinalIgnoreCase)
                || string.Equals(starToken, "e", StringComparison.OrdinalIgnoreCase))
            {
                stars = 5;
                return true;
            }
            int parsed;
            if (!int.TryParse(starToken, out parsed)) return false;
            if (parsed < CrewConfig.MinStars || parsed > CrewConfig.MaxStars) return false;
            stars = parsed;
            return true;
        }
    }
}
