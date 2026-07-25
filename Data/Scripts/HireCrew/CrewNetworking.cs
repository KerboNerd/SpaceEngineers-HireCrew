using System;
using Sandbox.ModAPI;

namespace HireCrew
{
    public static class CrewNetworking
    {
        public const ushort HireMsg = 41731;
        public const ushort AssignMsg = 41732;
        public const ushort DismissMsg = 41733;
        public const ushort RosterMsg = 41734;

        public static void Register(Action<ushort, byte[], ulong, bool> handler)
        {
            MyAPIGateway.Multiplayer.RegisterSecureMessageHandler(HireMsg, handler);
            MyAPIGateway.Multiplayer.RegisterSecureMessageHandler(AssignMsg, handler);
            MyAPIGateway.Multiplayer.RegisterSecureMessageHandler(DismissMsg, handler);
            MyAPIGateway.Multiplayer.RegisterSecureMessageHandler(RosterMsg, handler);
        }

        public static void Unregister(Action<ushort, byte[], ulong, bool> handler)
        {
            MyAPIGateway.Multiplayer.UnregisterSecureMessageHandler(HireMsg, handler);
            MyAPIGateway.Multiplayer.UnregisterSecureMessageHandler(AssignMsg, handler);
            MyAPIGateway.Multiplayer.UnregisterSecureMessageHandler(DismissMsg, handler);
            MyAPIGateway.Multiplayer.UnregisterSecureMessageHandler(RosterMsg, handler);
        }

        public static byte[] Serialize<T>(T obj)
        {
            return MyAPIGateway.Utilities.SerializeToBinary(obj);
        }

        public static T Deserialize<T>(byte[] data)
        {
            return MyAPIGateway.Utilities.SerializeFromBinary<T>(data);
        }

        public static void SendToServer(ushort id, byte[] data)
        {
            MyAPIGateway.Multiplayer.SendMessageToServer(id, data);
        }

        public static void SendToPlayer(ushort id, byte[] data, ulong steamId)
        {
            MyAPIGateway.Multiplayer.SendMessageTo(id, data, steamId);
        }
    }
}
