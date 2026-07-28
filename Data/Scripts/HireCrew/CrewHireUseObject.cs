using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.Entity.UseObject;
using VRage.ModAPI;
using VRage.Utils;

namespace HireCrew
{
    /// <summary>
    /// Screen interaction on HC_CrewHireDesk (detector_hirecrew on the store-lookalike model).
    /// </summary>
    [MyUseObject("hirecrew")]
    public sealed class CrewHireUseObject : MyUseObjectBase
    {
        public override UseActionEnum PrimaryAction
        {
            get { return UseActionEnum.Manipulate; }
        }

        public override UseActionEnum SecondaryAction
        {
            get { return UseActionEnum.None; }
        }

        public override UseActionEnum SupportedActions
        {
            get { return UseActionEnum.Manipulate; }
        }

        // Exact constructor signature required by the UseObject factory.
        public CrewHireUseObject(IMyEntity owner, string dummyName, IMyModelDummy dummyData, uint shapeKey)
            : base(owner, dummyData)
        {
        }

        public override MyActionDescription GetActionInfo(UseActionEnum actionEnum)
        {
            if (actionEnum != UseActionEnum.Manipulate)
                return default(MyActionDescription);

            return new MyActionDescription
            {
                Text = MyStringId.GetOrCompute("Open crew hiring desk"),
                IsTextControlHint = true,
                JoystickText = MyStringId.GetOrCompute("Open crew hiring desk"),
                ShowForGamepad = true
            };
        }

        public override void Use(UseActionEnum actionEnum, IMyEntity user)
        {
            if (actionEnum != UseActionEnum.Manipulate)
                return;

            if (MyAPIGateway.Utilities != null && MyAPIGateway.Utilities.IsDedicated)
                return;

            var block = Owner as IMyTerminalBlock;
            if (block == null || !CrewHireBlockLogic.IsHireDesk(block))
                return;

            var session = CrewSession.Instance;
            if (session == null)
                return;

            if (!session.CanLocalPlayerManage(block.CubeGrid))
            {
                MyAPIGateway.Utilities.ShowMessage("HireCrew", "No permission");
                return;
            }

            session.ClientOpenHireDesk(block.EntityId);
        }
    }
}
