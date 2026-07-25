using Sandbox.Common.ObjectBuilders;
using Sandbox.ModAPI;
using VRage.Game.Components;
using VRage.ObjectBuilders;

namespace HireCrew
{
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_TextPanel), false,
        "HireCrew_Terminal", "HireCrew_Terminal_Small")]
    public sealed class CrewTerminalLogic : MyGameLogicComponent
    {
        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            NeedsUpdate = MyEntityUpdateEnum.NONE;
        }

        public static bool IsCrewTerminal(IMyTerminalBlock block)
        {
            if (block == null) return false;
            var sub = block.BlockDefinition.SubtypeName;
            return sub == "HireCrew_Terminal" || sub == "HireCrew_Terminal_Small";
        }
    }
}
