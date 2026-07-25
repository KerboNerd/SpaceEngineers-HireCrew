using System;
using VRage.Game.Components;

namespace HireCrew
{
    [MySessionComponentDescriptor(MyUpdateOrder.AfterSimulation)]
    public sealed class CrewSession : MySessionComponentBase
    {
        public static CrewSession Instance { get; private set; }

        public WeaponAiBridge WeaponAi { get; private set; }
        public CrewStore Store { get; private set; }

        // Same instance required for UnregisterSecureMessageHandler.
        private Action<ushort, byte[], ulong, bool> _messageHandler;

        public override void LoadData()
        {
            Instance = this;
            Store = new CrewStore();
            WeaponAi = new WeaponAiBridge();
            WeaponAi.Load();
            _messageHandler = OnMessage;
            CrewNetworking.Register(_messageHandler);
        }

        protected override void UnloadData()
        {
            if (_messageHandler != null)
            {
                CrewNetworking.Unregister(_messageHandler);
                _messageHandler = null;
            }
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

        private void OnMessage(ushort id, byte[] data, ulong sender, bool fromServer)
        {
            // filled in Task 5
        }
    }
}
