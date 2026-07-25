using VRage.Game.Components;

namespace HireCrew
{
    [MySessionComponentDescriptor(MyUpdateOrder.AfterSimulation)]
    public sealed class CrewSession : MySessionComponentBase
    {
        public static CrewSession Instance { get; private set; }

        public override void LoadData()
        {
            Instance = this;
        }

        protected override void UnloadData()
        {
            if (Instance == this)
                Instance = null;
        }

        public override void UpdateAfterSimulation()
        {
        }
    }
}
