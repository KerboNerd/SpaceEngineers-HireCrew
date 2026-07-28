using System.Collections.Generic;
using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.Game.ObjectBuilders.Definitions;

namespace HireCrew
{
    /// <summary>
    /// Same-grid crew buffs (no connector/subgrid walk).
    /// Power: IMyReactor.PowerOutputMultiplier + other producers via MyResourceSourceComponent.
    /// Helmsman: IMyGyro.GyroStrengthMultiplier.
    /// Propulsion: IMyThrust.ThrustMultiplier.
    /// </summary>
    public sealed class CrewPowerBuff
    {
        private static readonly MyDefinitionId ElectricityId =
            new MyDefinitionId(typeof(MyObjectBuilder_GasProperties), "Electricity");

        private readonly Dictionary<long, float> _lastSourceMultByBlock = new Dictionary<long, float>();
        private readonly List<IMySlimBlock> _blockScratch = new List<IMySlimBlock>(64);
        private readonly HashSet<long> _seenScratch = new HashSet<long>();

        public float ComputeMultiplier(IEnumerable<CrewRecord> gridCrew)
        {
            return CrewConfig.GetSeatedEngineerPowerMultiplier(gridCrew);
        }

        public void ApplyGrid(IMyCubeGrid grid, float multiplier)
        {
            if (grid == null) return;
            if (multiplier < 1f) multiplier = 1f;

            _blockScratch.Clear();
            _seenScratch.Clear();
            grid.GetBlocks(_blockScratch);

            for (int i = 0; i < _blockScratch.Count; i++)
            {
                var slim = _blockScratch[i];
                if (slim == null) continue;
                var fat = slim.FatBlock as IMyTerminalBlock;
                if (fat == null || fat.MarkedForClose) continue;

                var reactor = fat as IMyReactor;
                if (reactor != null)
                {
                    reactor.PowerOutputMultiplier = multiplier;
                    continue;
                }

                if (!(fat is Sandbox.ModAPI.IMyPowerProducer)) continue;

                var source = fat.Components.Get<MyResourceSourceComponent>();
                if (source == null) continue;

                long id = fat.EntityId;
                _seenScratch.Add(id);

                float defined = source.DefinedOutputByType(ElectricityId);
                if (defined <= 0f) continue;

                float last;
                if (!_lastSourceMultByBlock.TryGetValue(id, out last) || last <= 0f)
                    last = 1f;

                float currentMax = source.MaxOutputByType(ElectricityId);
                float raw = Approximately(currentMax, defined * last)
                    ? defined
                    : currentMax / last;
                if (raw < 0f) raw = 0f;

                source.SetMaxOutputByType(ElectricityId, raw * multiplier);
                _lastSourceMultByBlock[id] = multiplier;
            }

            if (_lastSourceMultByBlock.Count > 0)
            {
                var stale = new List<long>();
                foreach (var kv in _lastSourceMultByBlock)
                {
                    if (!_seenScratch.Contains(kv.Key))
                        stale.Add(kv.Key);
                }
                for (int i = 0; i < stale.Count; i++)
                    _lastSourceMultByBlock.Remove(stale[i]);
            }
        }

        public void ClearGrid(IMyCubeGrid grid)
        {
            ApplyGrid(grid, 1f);
            ApplyGyros(grid, 1f);
            ApplyThrust(grid, 1f);
        }

        public void ApplyGyros(IMyCubeGrid grid, float multiplier)
        {
            if (grid == null) return;
            if (multiplier < 1f) multiplier = 1f;

            _blockScratch.Clear();
            grid.GetBlocks(_blockScratch);
            for (int i = 0; i < _blockScratch.Count; i++)
            {
                var slim = _blockScratch[i];
                if (slim == null) continue;
                var gyro = slim.FatBlock as IMyGyro;
                if (gyro == null || gyro.MarkedForClose) continue;
                gyro.GyroStrengthMultiplier = multiplier;
            }
        }

        public void ApplyThrust(IMyCubeGrid grid, float multiplier)
        {
            if (grid == null) return;
            if (multiplier < 1f) multiplier = 1f;

            _blockScratch.Clear();
            grid.GetBlocks(_blockScratch);
            for (int i = 0; i < _blockScratch.Count; i++)
            {
                var slim = _blockScratch[i];
                if (slim == null) continue;
                var thrust = slim.FatBlock as IMyThrust;
                if (thrust == null || thrust.MarkedForClose) continue;
                thrust.ThrustMultiplier = multiplier;
            }
        }

        private static bool Approximately(float a, float b)
        {
            float scale = a > b ? a : b;
            if (scale < 1f) scale = 1f;
            return (a > b ? a - b : b - a) <= 0.0001f * scale;
        }
    }
}
