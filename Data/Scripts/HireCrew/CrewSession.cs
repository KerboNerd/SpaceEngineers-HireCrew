using VRage.Game.Components;

namespace HireCrew
{
    [MySessionComponentDescriptor(MyUpdateOrder.AfterSimulation)]
    public sealed class CrewSession : MySessionComponentBase
    {
        public static CrewSession Instance { get; private set; }

        public WeaponAiBridge WeaponAi { get; private set; }
        public CrewStore Store { get; private set; }

        public override void LoadData()
        {
            Instance = this;
            Store = new CrewStore();
            WeaponAi = new WeaponAiBridge();
            WeaponAi.Load();
        }

        protected override void UnloadData()
        {
            if (WeaponAi != null)
                WeaponAi.Unload();
            WeaponAi = null;
            Store = null;
            if (Instance == this)
                Instance = null;
        }

        public override void UpdateAfterSimulation()
        {
        }
    }
}
