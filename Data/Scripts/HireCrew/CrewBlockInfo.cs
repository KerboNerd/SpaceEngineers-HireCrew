using System.Collections.Generic;
using System.Text;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRage.ModAPI;

namespace HireCrew
{
    /// <summary>
    /// Appends HireCrew summary to seat/weapon terminal Custom Info when assigned.
    /// Visible only to local players who can manage the grid (owner/faction).
    /// </summary>
    public sealed class CrewBlockInfo
    {
        private readonly HashSet<long> _hooked = new HashSet<long>();
        private readonly HashSet<long> _lastAssignedBlocks = new HashSet<long>();
        private bool _active;

        public void Init()
        {
            if (_active) return;
            _active = true;
            MyAPIGateway.Entities.OnEntityAdd += OnEntityAdd;
            MyAPIGateway.Entities.OnEntityRemove += OnEntityRemove;
            ScanExisting();
        }

        public void Unload()
        {
            if (!_active) return;
            _active = false;
            MyAPIGateway.Entities.OnEntityAdd -= OnEntityAdd;
            MyAPIGateway.Entities.OnEntityRemove -= OnEntityRemove;

            var ids = new List<long>(_hooked);
            foreach (var id in ids)
            {
                IMyEntity ent;
                if (!MyAPIGateway.Entities.TryGetEntityById(id, out ent)) continue;
                var block = ent as IMyTerminalBlock;
                if (block != null)
                    block.AppendingCustomInfo -= OnAppendingCustomInfo;
            }
            _hooked.Clear();
            _lastAssignedBlocks.Clear();
        }

        /// <summary>Refresh Custom Info on assigned seats/weapons (and blocks that just lost crew).</summary>
        public void RefreshAssigned()
        {
            var session = CrewSession.Instance;
            if (session == null || session.Store == null) return;

            var next = new HashSet<long>();
            foreach (var crew in session.Store.All)
            {
                if (crew == null || crew.Status != CrewStatus.Seated) continue;
                if (crew.SeatEntityId.HasValue)
                    next.Add(crew.SeatEntityId.Value);
                if (crew.WeaponEntityId.HasValue)
                    next.Add(crew.WeaponEntityId.Value);
                if (crew.BedEntityId.HasValue)
                    next.Add(crew.BedEntityId.Value);
                if (crew.ToiletEntityId.HasValue)
                    next.Add(crew.ToiletEntityId.Value);
                if (crew.ShowerEntityId.HasValue)
                    next.Add(crew.ShowerEntityId.Value);
            }

            foreach (var id in next)
                RefreshBlock(id);

            // Clear stale HireCrew lines after dismiss/integrity cleanup.
            foreach (var id in _lastAssignedBlocks)
            {
                if (!next.Contains(id))
                    RefreshBlock(id);
            }

            _lastAssignedBlocks.Clear();
            foreach (var id in next)
                _lastAssignedBlocks.Add(id);
        }

        private void ScanExisting()
        {
            var entities = new HashSet<IMyEntity>();
            MyAPIGateway.Entities.GetEntities(entities);
            foreach (var ent in entities)
                TryHook(ent);
        }

        private void OnEntityAdd(IMyEntity ent)
        {
            TryHook(ent);
        }

        private void OnEntityRemove(IMyEntity ent)
        {
            var block = ent as IMyTerminalBlock;
            if (block == null) return;
            if (!_hooked.Remove(block.EntityId)) return;
            block.AppendingCustomInfo -= OnAppendingCustomInfo;
        }

        private void TryHook(IMyEntity ent)
        {
            var block = ent as IMyTerminalBlock;
            if (block == null || block.MarkedForClose) return;
            if (!ShouldTrack(block)) return;
            Hook(block);
        }

        private bool ShouldTrack(IMyTerminalBlock block)
        {
            if (block is IMyCockpit || CrewStationLogic.IsCrewStation(block))
                return true;

            var session = CrewSession.Instance;
            if (session != null && session.WeaponAi != null && session.WeaponAi.IsReady && session.WeaponAi.IsCoreWeapon(block))
                return true;

            // Track weapons known from roster even if WC API is not ready yet.
            if (session != null && session.Store != null)
            {
                foreach (var crew in session.Store.All)
                {
                    if (crew == null || crew.Status != CrewStatus.Seated) continue;
                    if (crew.WeaponEntityId.HasValue && crew.WeaponEntityId.Value == block.EntityId)
                        return true;
                    if (crew.SeatEntityId.HasValue && crew.SeatEntityId.Value == block.EntityId)
                        return true;
                    if (CrewValidation.AmenityClaimedBy(crew, block.EntityId))
                        return true;
                }
            }

            if (CrewAmenities.DetectKind(block).HasValue)
                return true;

            return false;
        }

        private void Hook(IMyTerminalBlock block)
        {
            if (!_hooked.Add(block.EntityId)) return;
            block.AppendingCustomInfo += OnAppendingCustomInfo;
            block.RefreshCustomInfo();
        }

        private void RefreshBlock(long entityId)
        {
            IMyEntity ent;
            if (!MyAPIGateway.Entities.TryGetEntityById(entityId, out ent)) return;
            var block = ent as IMyTerminalBlock;
            if (block == null || block.MarkedForClose) return;
            Hook(block);
            block.RefreshCustomInfo();
        }

        private void OnAppendingCustomInfo(IMyTerminalBlock block, StringBuilder sb)
        {
            if (block == null || sb == null) return;

            var session = CrewSession.Instance;
            if (session == null || session.Store == null) return;

            var player = MyAPIGateway.Session.Player;
            if (player == null) return;

            var grid = block.CubeGrid;
            if (grid == null || !session.CanLocalPlayerManage(grid)) return;

            var crew = FindAssigned(session.Store, block.EntityId);
            if (crew == null) return;

            string seatName = ResolveBlockName(crew.SeatEntityId);
            string weaponName = ResolveBlockName(crew.WeaponEntityId);
            var name = string.IsNullOrEmpty(crew.DisplayName)
                ? (CrewConfig.FormatStars(crew.Stars) + " " + CrewConfig.RoleLabel(crew.Role))
                : crew.DisplayName;
            var marks = CrewAmenities.FormatAmenityMarks(crew);
            if (string.IsNullOrEmpty(marks)) marks = "—";

            sb.AppendLine();
            sb.AppendLine("HireCrew");
            sb.AppendLine("Name: " + name);
            sb.AppendLine("Role: " + CrewConfig.RoleLabel(crew.Role));
            sb.AppendLine("Stars: " + CrewConfig.FormatStars(crew.Stars) + " (" + crew.Stars + ")");
            sb.AppendLine("Seat: " + seatName);
            if (crew.Role == CrewRole.Engineer)
            {
                var powerPct = (int)System.Math.Round(
                    CrewConfig.GetPowerBonus(crew.Stars, CrewAmenities.GetEfficiency(crew)) * 100f);
                sb.AppendLine("Power: +" + powerPct + "%");
            }
            else if (crew.Role == CrewRole.DamageControl)
            {
                sb.AppendLine("Job: EVA weld / project — Send from HUD");
            }
            else
            {
                sb.AppendLine("Weapon: " + weaponName);
            }
            sb.AppendLine("Quarters: " + marks);
            sb.AppendLine("Efficiency: " + CrewAmenities.GetEfficiencyPercent(crew) + "%");
            sb.AppendLine("Bed: " + ResolveBlockName(crew.BedEntityId));
            sb.AppendLine("Toilet: " + ResolveBlockName(crew.ToiletEntityId));
            sb.AppendLine("Shower: " + ResolveBlockName(crew.ShowerEntityId));
            sb.AppendLine("Status: " + crew.Status);
        }

        private static CrewRecord FindAssigned(CrewStore store, long blockEntityId)
        {
            foreach (var crew in store.All)
            {
                if (crew == null || crew.Status != CrewStatus.Seated) continue;
                if (crew.SeatEntityId.HasValue && crew.SeatEntityId.Value == blockEntityId)
                    return crew;
                if (crew.WeaponEntityId.HasValue && crew.WeaponEntityId.Value == blockEntityId)
                    return crew;
                if (CrewValidation.AmenityClaimedBy(crew, blockEntityId))
                    return crew;
            }
            return null;
        }

        private static string ResolveBlockName(long? entityId)
        {
            if (!entityId.HasValue) return "—";
            IMyEntity ent;
            if (!MyAPIGateway.Entities.TryGetEntityById(entityId.Value, out ent) || ent == null)
                return "#" + entityId.Value;
            // Showers (Decorative Pack) are plain CubeBlocks, not terminal blocks.
            var cube = ent as IMyCubeBlock;
            if (cube != null)
            {
                var name = CrewAmenities.BlockLabel(cube);
                return string.IsNullOrEmpty(name) ? "#" + entityId.Value : name;
            }
            return "#" + entityId.Value;
        }
    }
}
