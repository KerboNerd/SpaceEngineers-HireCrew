using System;
using System.Collections.Generic;
using RichHudFramework.Client;
using RichHudFramework.UI.Client;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.Utils;
using VRageMath;

namespace HireCrew
{
    /// <summary>
    /// Client crew UI via Rich HUD Framework.
    /// /crew — toggle management UI (assign/dismiss)
    /// /crew hud — toggle construction status sidebar
    /// Cockpit terminal button / toolbar action — same
    /// /hirecrew (/hc) — admin commands (server-authoritative)
    /// Hire desk block opens the larger hiring UI.
    /// </summary>
    public sealed class CrewHud
    {
        public const string Command = "/crew";
        public const string AdminCommand = "/hirecrew";
        public const string AdminAlias = "/hc";

        private readonly CrewHudModel _model = new CrewHudModel();
        private readonly CrewStatusHudModel _statusModel = new CrewStatusHudModel();
        private CrewHudWindow _window;
        private CrewHireWindow _hireWindow;
        private CrewStatusSidebar _statusSidebar;
        private bool _rhfReady;
        private bool _chatRegistered;
        private int _refreshCooldown;
        private long _openHireBlockId;

        public bool IsOpen { get { return _model.IsOpen; } }

        public void Init()
        {
            if (MyAPIGateway.Utilities != null && MyAPIGateway.Utilities.IsDedicated) return;
            EnsureChatRegistered();
            RichHudClient.Init("HireCrew", OnHudReady, OnHudReset);
        }

        public void EnsureChatRegistered()
        {
            if (MyAPIGateway.Utilities == null) return;
            if (MyAPIGateway.Utilities.IsDedicated) return;
            if (_chatRegistered) return;
            MyAPIGateway.Utilities.MessageEntered += OnMessageEntered;
            _chatRegistered = true;
            MyLog.Default.WriteLineAndConsole("[HireCrew] Chat commands registered (/crew, /hirecrew, /hc)");
        }

        private void OnHudReady()
        {
            _rhfReady = true;
            if (_window == null)
            {
                _window = new CrewHudWindow(HudMain.Root, _model);
                _window.CloseRequested += CloseUi;
            }
            if (_hireWindow == null)
            {
                _hireWindow = new CrewHireWindow(HudMain.Root);
                _hireWindow.CloseRequested += CloseHireUi;
            }
            if (_statusSidebar == null)
                _statusSidebar = new CrewStatusSidebar(HudMain.Root);
            CrewAmbientNameplates.SetReady(true);
            CrewMissionMarkers.SetReady(true);
        }

        private void OnHudReset()
        {
            CrewAmbientNameplates.SetReady(false);
            CrewMissionMarkers.SetReady(false);
            _rhfReady = false;
            _window = null;
            _hireWindow = null;
            _statusSidebar = null;
            _model.Close();
            _openHireBlockId = 0;
        }

        public void Unload()
        {
            CrewAmbientNameplates.SetReady(false);
            CrewMissionMarkers.SetReady(false);
            CloseUi();
            CloseHireUi();
            UnregisterChat();
            if (_statusSidebar != null)
                _statusSidebar.Apply(null, 0, false);
            _window = null;
            _hireWindow = null;
            _statusSidebar = null;
            _rhfReady = false;
            _model.Close();
        }

        public void Update()
        {
            if (MyAPIGateway.Utilities == null || MyAPIGateway.Utilities.IsDedicated) return;

            if (!_chatRegistered)
                EnsureChatRegistered();

            // Floating names above ambient bots (vanilla nametags ignore IsBot characters).
            CrewAmbientNameplates.Update(CrewSession.Instance);
            CrewMissionMarkers.Update(CrewSession.Instance);

            if (_hireWindow != null && _hireWindow.IsOpen)
            {
                if (!IsHireBlockStillValid(_openHireBlockId))
                {
                    CloseHireUi();
                }
                else
                    _hireWindow.UpdateOpen();
            }

            UpdateStatusSidebar();

            if (!_model.IsOpen) return;

            if (_model.HasManagedGrid)
            {
                IMyCubeGrid grid;
                string err;
                var session = CrewSession.Instance;
                if (session == null || !session.TryGetLocalManagedGrid(out grid, out err) || grid.EntityId != _model.GridEntityId)
                {
                    CloseUi();
                    return;
                }
            }

            _refreshCooldown++;
            if (_refreshCooldown >= 30)
            {
                _refreshCooldown = 0;
                if (_window != null)
                    _window.Refresh();
            }
        }

        private void UpdateStatusSidebar()
        {
            if (_statusSidebar == null || !_rhfReady) return;

            if (!_statusModel.SidebarEnabled)
            {
                _statusSidebar.Apply(null, 0, false);
                return;
            }

            var session = CrewSession.Instance;
            IMyCubeGrid grid;
            string err;
            if (session == null || !session.TryGetLocalManagedGrid(out grid, out err) || grid == null)
            {
                _statusSidebar.Apply(null, 0, false);
                return;
            }

            IList<RepairMissionSnapshotEntry> repairEntries;
            IList<SalvageMissionSnapshotEntry> salvageEntries;
            if (MyAPIGateway.Multiplayer.IsServer)
            {
                var repairBuf = new List<RepairMissionSnapshotEntry>();
                var salvageBuf = new List<SalvageMissionSnapshotEntry>();
                CrewRepairMission.CollectActiveSnapshots(repairBuf);
                CrewSalvageMission.CollectActiveSnapshots(salvageBuf);
                repairEntries = repairBuf;
                salvageEntries = salvageBuf;
            }
            else
            {
                repairEntries = session.ClientRepairMissions;
                salvageEntries = session.ClientSalvageMissions;
            }

            int overflow;
            var rows = CrewStatusHudModel.BuildRows(
                repairEntries, salvageEntries, grid.EntityId, out overflow);
            _statusSidebar.Apply(rows, overflow, rows.Count > 0);
        }

        public void OpenHireDesk(long blockEntityId)
        {
            if (!_rhfReady || _hireWindow == null || !RichHudClient.Registered)
            {
                Tell("Install Rich Hud Master (workshop) to use hiring UI");
                return;
            }

            CloseUi();
            _openHireBlockId = blockEntityId;
            HireBlockPool pool = null;
            var session = CrewSession.Instance;
            if (session != null && session.HirePools != null)
                pool = session.HirePools.Get(blockEntityId);
            _hireWindow.Show(blockEntityId, pool);
            Tell("Hiring desk open");
        }

        public void OnHirePoolSynced(HireBlockPool pool)
        {
            if (pool == null) return;
            if (_hireWindow != null && _hireWindow.IsOpen)
                _hireWindow.ApplyPool(pool);
        }

        private void CloseHireUi()
        {
            _openHireBlockId = 0;
            if (_hireWindow != null)
                _hireWindow.Hide();
            if (_rhfReady && !_model.IsOpen)
                HudMain.EnableCursor = false;
        }

        private static bool IsHireBlockStillValid(long blockEntityId)
        {
            if (blockEntityId == 0) return false;
            IMyEntity ent;
            if (!MyAPIGateway.Entities.TryGetEntityById(blockEntityId, out ent) || ent == null || ent.Closed)
                return false;
            var block = ent as IMyTerminalBlock;
            if (block == null || !CrewHireBlockLogic.IsHireDesk(block)) return false;
            var session = CrewSession.Instance;
            if (session == null || block.CubeGrid == null) return false;
            return session.CanLocalPlayerManage(block.CubeGrid);
        }

        private void OnMessageEntered(string messageText, ref bool sendToOthers)
        {
            if (string.IsNullOrEmpty(messageText)) return;

            string trimmed = messageText.Trim();
            if (trimmed.Length == 0) return;

            string[] tokens = trimmed.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0) return;

            string head = tokens[0];
            bool isCrew = string.Equals(head, Command, StringComparison.OrdinalIgnoreCase);
            bool isAdmin = string.Equals(head, AdminCommand, StringComparison.OrdinalIgnoreCase)
                || string.Equals(head, AdminAlias, StringComparison.OrdinalIgnoreCase);
            if (!isCrew && !isAdmin)
                return;

            sendToOthers = false;

            try
            {
                if (isCrew)
                {
                    if (tokens.Length == 1)
                    {
                        ToggleUi();
                        return;
                    }
                    if (string.Equals(tokens[1], "path", StringComparison.OrdinalIgnoreCase))
                    {
                        HandleCrewPath(tokens);
                        return;
                    }
                    if (string.Equals(tokens[1], "salvage", StringComparison.OrdinalIgnoreCase))
                    {
                        HandleCrewSalvage(tokens);
                        return;
                    }
                    if (string.Equals(tokens[1], "hud", StringComparison.OrdinalIgnoreCase))
                    {
                        _statusModel.ToggleSidebar();
                        Tell(_statusModel.SidebarEnabled ? "Crew status HUD on" : "Crew status HUD off");
                        return;
                    }
                    Tell("Usage: /crew | /crew hud | /crew path [start|undo|done|clear|stop] | /crew salvage [start|stop|clear|clearall]");
                    return;
                }

                var args = new System.Collections.Generic.List<string>();
                for (int i = 1; i < tokens.Length; i++)
                    args.Add(tokens[i]);
                string verb = args.Count > 0 ? args[0] : "help";
                if (args.Count > 0)
                    args.RemoveAt(0);

                var session = CrewSession.Instance;
                if (session == null)
                {
                    Tell("HireCrew not ready");
                    return;
                }
                session.ClientRequestAdmin(new AdminCommandRequest { Verb = verb, Args = args });
            }
            catch (Exception e)
            {
                Tell("Command error: " + e.Message);
                MyLog.Default.WriteLineAndConsole("[HireCrew] chat command exception: " + e);
            }
        }

        /// <summary>
        /// Toggle management UI. When <paramref name="preferredGridEntityId"/> is set (cockpit),
        /// open for that grid; otherwise use the local managed-grid heuristic (/crew).
        /// </summary>
        public void ToggleUi(long preferredGridEntityId = 0)
        {
            if (!_rhfReady || _window == null || !RichHudClient.Registered)
            {
                Tell("Install Rich Hud Master (workshop) to use crew UI");
                return;
            }

            if (_model.IsOpen)
            {
                CloseUi();
                Tell("Crew UI closed");
                return;
            }

            CloseHireUi();

            var session = CrewSession.Instance;
            if (session == null)
            {
                Tell("Cannot open crew UI");
                return;
            }

            long gridId = 0;
            if (preferredGridEntityId != 0)
            {
                IMyEntity ent;
                if (!MyAPIGateway.Entities.TryGetEntityById(preferredGridEntityId, out ent) || ent == null)
                {
                    Tell("Ship not found");
                    return;
                }
                var grid = ent as IMyCubeGrid;
                if (grid == null || !session.CanLocalPlayerManage(grid))
                {
                    Tell("No permission");
                    return;
                }
                gridId = grid.EntityId;
            }
            else
            {
                IMyCubeGrid grid;
                string err;
                if (session.TryGetLocalManagedGrid(out grid, out err) && grid != null)
                    gridId = grid.EntityId;
            }

            _model.Open(gridId);
            _refreshCooldown = 0;
            _window.Show();
            Tell(gridId != 0 ? "Crew UI open" : "Crew UI open (off ship)");
        }

        private void HandleCrewSalvage(string[] tokens)
        {
            var session = CrewSession.Instance;
            if (session == null)
            {
                Tell("HireCrew not ready");
                return;
            }

            string sub = tokens.Length >= 3 ? tokens[2] : "start";
            if (string.Equals(sub, "stop", StringComparison.OrdinalIgnoreCase))
            {
                CrewSalvageTargetPainter.SetActive(false, 0);
                Tell("Salvage mark OFF");
                return;
            }

            if (string.Equals(sub, "clear", StringComparison.OrdinalIgnoreCase)
                || string.Equals(sub, "clearall", StringComparison.OrdinalIgnoreCase))
            {
                // Marks persist in the save — clearall wipes every mark on grids you can manage
                // (needed when an old mark was keyed to a different home grid).
                bool all = string.Equals(sub, "clearall", StringComparison.OrdinalIgnoreCase);
                if (all)
                {
                    session.ClientRequestSalvageTargetEdit(0, 0, clearAllManaged: true);
                    return;
                }

                long homeId = CrewSalvageTargetPainter.ActiveHomeGridEntityId;
                if (homeId == 0)
                {
                    IMyCubeGrid clearHome;
                    if (!TryResolveSalvageHomeGrid(session, out clearHome) || clearHome == null)
                    {
                        Tell("Salvage: look at your home ship, or use /crew salvage clearall");
                        return;
                    }
                    homeId = clearHome.EntityId;
                }
                session.ClientRequestSalvageTargetEdit(homeId, 0);
                return;
            }

            // start — resolve home ship (seat / look-at managed / Salvage Ops construct); LMB picks wreck
            IMyCubeGrid homeGrid;
            if (!TryResolveSalvageHomeGrid(session, out homeGrid) || homeGrid == null)
            {
                Tell("Salvage: look at your home ship (or sit in it) first");
                return;
            }
            CrewPathPainter.SetActive(false, 0);
            CrewSalvageTargetPainter.SetActive(true, homeGrid.EntityId);
            Tell("Salvage mark ON — home=" + GridLabel(homeGrid) + " — LMB a different wreck");
        }

        private void HandleCrewPath(string[] tokens)
        {
            var session = CrewSession.Instance;
            if (session == null)
            {
                Tell("HireCrew not ready");
                return;
            }

            string sub = tokens.Length >= 3 ? tokens[2] : "start";
            if (string.Equals(sub, "stop", StringComparison.OrdinalIgnoreCase))
            {
                CrewPathPainter.SetActive(false, 0);
                Tell("Path tool OFF");
                return;
            }

            if (string.Equals(sub, "undo", StringComparison.OrdinalIgnoreCase)
                || string.Equals(sub, "done", StringComparison.OrdinalIgnoreCase)
                || string.Equals(sub, "clear", StringComparison.OrdinalIgnoreCase))
            {
                long gridId = CrewPathPainter.ActiveGridEntityId;
                if (gridId == 0 && !TryResolvePathGrid(session, out gridId))
                {
                    Tell("Path: look at a managed grid first");
                    return;
                }

                int op = string.Equals(sub, "undo", StringComparison.OrdinalIgnoreCase) ? 1
                    : string.Equals(sub, "done", StringComparison.OrdinalIgnoreCase) ? 2
                    : 3;
                session.ClientRequestPathEdit(new PathEditRequest { GridEntityId = gridId, Op = op });
                return;
            }

            // start (default) — disable salvage mark so modes don't fight over LMB
            long startGridId;
            if (!TryResolvePathGrid(session, out startGridId))
            {
                Tell("Path: look at a managed grid (or stand on one)");
                return;
            }
            CrewSalvageTargetPainter.SetActive(false, 0);
            CrewPathPainter.SetActive(true, startGridId);
            Tell("Path tool ON — LMB append, RMB done");
        }

        private static bool TryResolvePathGrid(CrewSession session, out long gridId)
        {
            gridId = 0;
            IMyCubeGrid rayGrid;
            if (CrewPathPainter.TryRayGridUnderCrosshair(out rayGrid)
                && rayGrid != null
                && session.CanLocalPlayerManage(rayGrid))
            {
                gridId = rayGrid.EntityId;
                return true;
            }

            IMyCubeGrid local;
            string err;
            if (session.TryGetLocalManagedGrid(out local, out err) && local != null)
            {
                gridId = local.EntityId;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Home ship for salvage marks. Never use an arbitrary look-at grid as home — that
        /// made owned wrecks become "home" so LMB on them was rejected as self-target.
        /// </summary>
        private static bool TryResolveSalvageHomeGrid(CrewSession session, out IMyCubeGrid grid)
        {
            grid = null;
            if (session == null) return false;

            // 1) Seated / controlling a managed ship
            string err;
            IMyCubeGrid seated;
            if (session.TryGetLocalManagedGrid(out seated, out err) && seated != null)
            {
                grid = seated;
                return true;
            }

            // 2) Nearest ship that already has your Salvage Ops (EVA / freefly)
            if (TryResolveHomeFromSalvageOps(session, out grid) && grid != null)
                return true;

            // 3) Look-at only if that grid itself hosts Salvage Ops (not any owned wreck)
            double lookRange = CrewConfig.SalvageScanRadiusMeters;
            if (lookRange < 80.0) lookRange = 80.0;
            IMyCubeGrid rayGrid;
            if (CrewLookRay.TryGridUnderCrosshair(lookRange, out rayGrid)
                && rayGrid != null
                && session.CanLocalPlayerManage(rayGrid)
                && GridHasSeatedSalvageOps(session, rayGrid))
            {
                grid = rayGrid;
                return true;
            }

            return false;
        }

        private static bool GridHasSeatedSalvageOps(CrewSession session, IMyCubeGrid grid)
        {
            if (session == null || session.Store == null || grid == null)
                return false;
            var roster = session.GetCrewForLocalOwner();
            if (roster == null) return false;
            for (int i = 0; i < roster.Count; i++)
            {
                var crew = roster[i];
                if (crew == null || crew.Role != CrewRole.SalvageOps)
                    continue;
                if (crew.Status != CrewStatus.Seated || crew.GridEntityId == 0)
                    continue;
                if (crew.GridEntityId == grid.EntityId)
                    return true;
                IMyEntity ent;
                if (!MyAPIGateway.Entities.TryGetEntityById(crew.GridEntityId, out ent) || ent == null)
                    continue;
                var g = ent as IMyCubeGrid;
                if (g != null && !g.Closed && g.IsSameConstructAs(grid))
                    return true;
            }
            return false;
        }

        private static bool TryResolveHomeFromSalvageOps(CrewSession session, out IMyCubeGrid grid)
        {
            grid = null;
            if (session == null || session.Store == null)
                return false;

            Vector3D origin = Vector3D.Zero;
            try
            {
                var cam = MyAPIGateway.Session != null ? MyAPIGateway.Session.Camera : null;
                if (cam != null)
                    origin = cam.WorldMatrix.Translation;
            }
            catch { }

            double bestDist = double.MaxValue;
            IMyCubeGrid best = null;
            var roster = session.GetCrewForLocalOwner();
            if (roster == null)
                return false;

            for (int i = 0; i < roster.Count; i++)
            {
                var crew = roster[i];
                if (crew == null || crew.Role != CrewRole.SalvageOps)
                    continue;
                if (crew.Status != CrewStatus.Seated || crew.GridEntityId == 0)
                    continue;

                IMyEntity ent;
                if (!MyAPIGateway.Entities.TryGetEntityById(crew.GridEntityId, out ent) || ent == null)
                    continue;
                var g = ent as IMyCubeGrid;
                if (g == null || g.Closed || !session.CanLocalPlayerManage(g))
                    continue;

                double d = Vector3D.DistanceSquared(origin, g.WorldAABB.Center);
                if (d < bestDist)
                {
                    bestDist = d;
                    best = g;
                }
            }

            grid = best;
            return grid != null;
        }

        private static string GridLabel(IMyCubeGrid grid)
        {
            if (grid == null) return "grid";
            return !string.IsNullOrEmpty(grid.CustomName) ? grid.CustomName : ("Grid " + grid.EntityId);
        }

        private static void Tell(string message)
        {
            try
            {
                MyAPIGateway.Utilities.ShowMessage("HireCrew", message);
                MyAPIGateway.Utilities.ShowNotification("HireCrew: " + message, 4000);
            }
            catch
            {
                MyLog.Default.WriteLineAndConsole("[HireCrew] " + message);
            }
        }

        private void CloseUi()
        {
            _model.Close();
            if (_window != null)
                _window.Hide();
            if (_rhfReady && (_hireWindow == null || !_hireWindow.IsOpen))
                HudMain.EnableCursor = false;
        }

        private void UnregisterChat()
        {
            if (!_chatRegistered) return;
            try
            {
                if (MyAPIGateway.Utilities != null)
                    MyAPIGateway.Utilities.MessageEntered -= OnMessageEntered;
            }
            catch { }
            _chatRegistered = false;
        }
    }
}
