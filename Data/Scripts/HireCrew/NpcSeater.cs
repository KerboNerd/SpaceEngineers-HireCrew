using System;
using Sandbox.Game;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRageMath;

namespace HireCrew
{
    public sealed class NpcSeater
    {
        // SE bot subtype from Content/Data/Bots.sbc (not character subtype Default_Astronaut).
        private const string BotSubtype = "Astronaut";

        public bool TrySeat(IMyShipController seat, string displayName, out long characterEntityId, out string error)
        {
            characterEntityId = 0;
            error = null;
            if (seat == null)
            {
                error = "Invalid seat";
                return false;
            }

            var cockpit = seat as IMyCockpit;
            if (cockpit == null)
            {
                error = "Seat is not a cockpit/seat that can attach pilots";
                return false;
            }

            if (cockpit.IsOccupied || seat.Pilot != null)
            {
                error = "Seat occupied";
                return false;
            }

            try
            {
                var world = seat.WorldMatrix;
                var pos = world.Translation + world.Up * 0.5;
                var name = string.IsNullOrEmpty(displayName) ? "Crew" : displayName;

                // SpawnBotAbsolute does not exist on this SE build; use oriented SpawnBot that returns entity id.
                var spawnedId = MyVisualScriptLogicProvider.SpawnBot(
                    BotSubtype, pos, world.Forward, world.Up, name);

                IMyCharacter character = null;
                if (spawnedId != 0)
                {
                    IMyEntity ent;
                    if (MyAPIGateway.Entities.TryGetEntityById(spawnedId, out ent))
                        character = ent as IMyCharacter;
                }

                if (character == null)
                    character = FindNearbyCharacter(pos, 3.0);

                if (character == null)
                {
                    error = "Failed to spawn crew character";
                    return false;
                }

                if (!string.IsNullOrEmpty(displayName))
                    character.DisplayName = displayName;

                cockpit.AttachPilot(character);

                characterEntityId = character.EntityId;
                return true;
            }
            catch (Exception e)
            {
                error = e.Message;
                return false;
            }
        }

        public void Despawn(long characterEntityId)
        {
            if (characterEntityId == 0) return;
            IMyEntity ent;
            if (!MyAPIGateway.Entities.TryGetEntityById(characterEntityId, out ent) || ent == null)
                return;
            var ch = ent as IMyCharacter;
            if (ch != null)
            {
                try
                {
                    // IMyCharacter.Kill requires an object argument on this API.
                    ch.Kill(null);
                }
                catch
                {
                }
            }
            ent.Close();
        }

        public bool IsAlive(long characterEntityId)
        {
            if (characterEntityId == 0) return false;
            IMyEntity ent;
            if (!MyAPIGateway.Entities.TryGetEntityById(characterEntityId, out ent))
                return false;
            var ch = ent as IMyCharacter;
            return ch != null && ch.IsDead == false && !ch.Closed;
        }

        private static IMyCharacter FindNearbyCharacter(Vector3D pos, double radius)
        {
            IMyCharacter found = null;
            var r2 = radius * radius;
            MyAPIGateway.Entities.GetEntities(null, e =>
            {
                var ch = e as IMyCharacter;
                if (ch == null || ch.IsPlayer) return false;
                if (Vector3D.DistanceSquared(ch.GetPosition(), pos) <= r2)
                    found = ch;
                return false;
            });
            return found;
        }
    }
}
