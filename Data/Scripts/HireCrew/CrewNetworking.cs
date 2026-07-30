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
        public const ushort NotifyMsg = 41735;
        public const ushort AssignAmenityMsg = 41736;
        public const ushort HireFromPoolMsg = 41737;
        public const ushort HirePoolSyncMsg = 41738;
        public const ushort HireRefreshMsg = 41739;
        public const ushort HirePoolRequestMsg = 41740;
        public const ushort UnassignMsg = 41741;
        public const ushort TrainMsg = 41742;
        public const ushort CancelTrainMsg = 41743;
        public const ushort BulkAssignMsg = 41744;
        public const ushort AdminCommandMsg = 41745;
        public const ushort PathEditMsg = 41746;
        public const ushort RepairDispatchMsg = 41747;
        public const ushort RepairMissionSyncMsg = 41748;
        public const ushort SalvageDispatchMsg = 41749;
        public const ushort SalvageMissionSyncMsg = 41750;
        public const ushort SalvageTargetEditMsg = 41751;
        public const ushort SalvageTargetSyncMsg = 41752;
        public const ushort RoleDispatchBatchMsg = 41753;

        public static void Register(Action<ushort, byte[], ulong, bool> handler)
        {
            MyAPIGateway.Multiplayer.RegisterSecureMessageHandler(HireMsg, handler);
            MyAPIGateway.Multiplayer.RegisterSecureMessageHandler(AssignMsg, handler);
            MyAPIGateway.Multiplayer.RegisterSecureMessageHandler(DismissMsg, handler);
            MyAPIGateway.Multiplayer.RegisterSecureMessageHandler(RosterMsg, handler);
            MyAPIGateway.Multiplayer.RegisterSecureMessageHandler(NotifyMsg, handler);
            MyAPIGateway.Multiplayer.RegisterSecureMessageHandler(AssignAmenityMsg, handler);
            MyAPIGateway.Multiplayer.RegisterSecureMessageHandler(HireFromPoolMsg, handler);
            MyAPIGateway.Multiplayer.RegisterSecureMessageHandler(HirePoolSyncMsg, handler);
            MyAPIGateway.Multiplayer.RegisterSecureMessageHandler(HireRefreshMsg, handler);
            MyAPIGateway.Multiplayer.RegisterSecureMessageHandler(HirePoolRequestMsg, handler);
            MyAPIGateway.Multiplayer.RegisterSecureMessageHandler(UnassignMsg, handler);
            MyAPIGateway.Multiplayer.RegisterSecureMessageHandler(TrainMsg, handler);
            MyAPIGateway.Multiplayer.RegisterSecureMessageHandler(CancelTrainMsg, handler);
            MyAPIGateway.Multiplayer.RegisterSecureMessageHandler(BulkAssignMsg, handler);
            MyAPIGateway.Multiplayer.RegisterSecureMessageHandler(AdminCommandMsg, handler);
            MyAPIGateway.Multiplayer.RegisterSecureMessageHandler(PathEditMsg, handler);
            MyAPIGateway.Multiplayer.RegisterSecureMessageHandler(RepairDispatchMsg, handler);
            MyAPIGateway.Multiplayer.RegisterSecureMessageHandler(RepairMissionSyncMsg, handler);
            MyAPIGateway.Multiplayer.RegisterSecureMessageHandler(SalvageDispatchMsg, handler);
            MyAPIGateway.Multiplayer.RegisterSecureMessageHandler(SalvageMissionSyncMsg, handler);
            MyAPIGateway.Multiplayer.RegisterSecureMessageHandler(SalvageTargetEditMsg, handler);
            MyAPIGateway.Multiplayer.RegisterSecureMessageHandler(SalvageTargetSyncMsg, handler);
            MyAPIGateway.Multiplayer.RegisterSecureMessageHandler(RoleDispatchBatchMsg, handler);
        }

        public static void Unregister(Action<ushort, byte[], ulong, bool> handler)
        {
            MyAPIGateway.Multiplayer.UnregisterSecureMessageHandler(HireMsg, handler);
            MyAPIGateway.Multiplayer.UnregisterSecureMessageHandler(AssignMsg, handler);
            MyAPIGateway.Multiplayer.UnregisterSecureMessageHandler(DismissMsg, handler);
            MyAPIGateway.Multiplayer.UnregisterSecureMessageHandler(RosterMsg, handler);
            MyAPIGateway.Multiplayer.UnregisterSecureMessageHandler(NotifyMsg, handler);
            MyAPIGateway.Multiplayer.UnregisterSecureMessageHandler(AssignAmenityMsg, handler);
            MyAPIGateway.Multiplayer.UnregisterSecureMessageHandler(HireFromPoolMsg, handler);
            MyAPIGateway.Multiplayer.UnregisterSecureMessageHandler(HirePoolSyncMsg, handler);
            MyAPIGateway.Multiplayer.UnregisterSecureMessageHandler(HireRefreshMsg, handler);
            MyAPIGateway.Multiplayer.UnregisterSecureMessageHandler(HirePoolRequestMsg, handler);
            MyAPIGateway.Multiplayer.UnregisterSecureMessageHandler(UnassignMsg, handler);
            MyAPIGateway.Multiplayer.UnregisterSecureMessageHandler(TrainMsg, handler);
            MyAPIGateway.Multiplayer.UnregisterSecureMessageHandler(CancelTrainMsg, handler);
            MyAPIGateway.Multiplayer.UnregisterSecureMessageHandler(BulkAssignMsg, handler);
            MyAPIGateway.Multiplayer.UnregisterSecureMessageHandler(AdminCommandMsg, handler);
            MyAPIGateway.Multiplayer.UnregisterSecureMessageHandler(PathEditMsg, handler);
            MyAPIGateway.Multiplayer.UnregisterSecureMessageHandler(RepairDispatchMsg, handler);
            MyAPIGateway.Multiplayer.UnregisterSecureMessageHandler(RepairMissionSyncMsg, handler);
            MyAPIGateway.Multiplayer.UnregisterSecureMessageHandler(SalvageDispatchMsg, handler);
            MyAPIGateway.Multiplayer.UnregisterSecureMessageHandler(SalvageMissionSyncMsg, handler);
            MyAPIGateway.Multiplayer.UnregisterSecureMessageHandler(SalvageTargetEditMsg, handler);
            MyAPIGateway.Multiplayer.UnregisterSecureMessageHandler(SalvageTargetSyncMsg, handler);
            MyAPIGateway.Multiplayer.UnregisterSecureMessageHandler(RoleDispatchBatchMsg, handler);
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
