using System;
using System.Collections.Generic;
using Sandbox.Game;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRageMath;

namespace HireCrew
{
    public sealed class NpcSeater
    {
        // Custom bot subtype from Data/Bots.sbc (killable; not vanilla Invulnerable Astronaut).
        private const string BotSubtype = "HireCrew_Gunner";
        private const double NearbyFallbackRadius = 1.0;

        public bool TrySeat(IMyShipController seat, string displayName, out long characterEntityId, out string error)
        {
            return TrySeat(seat, displayName, null, out characterEntityId, out error);
        }

        public bool TrySeat(IMyShipController seat, string displayName, ISet<long> knownCharacterIds, out long characterEntityId, out string error)
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

            long spawnedId = 0;
            try
            {
                var world = seat.WorldMatrix;
                var pos = world.Translation + world.Up * 0.5;
                var name = string.IsNullOrEmpty(displayName) ? "Crew" : displayName;

                // SpawnBotAbsolute does not exist on this SE build; use oriented SpawnBot that returns entity id.
                spawnedId = MyVisualScriptLogicProvider.SpawnBot(
                    BotSubtype, pos, world.Forward, world.Up, name);

                IMyCharacter character = null;
                if (spawnedId != 0)
                {
                    IMyEntity ent;
                    if (MyAPIGateway.Entities.TryGetEntityById(spawnedId, out ent))
                        character = ent as IMyCharacter;

                    // Prefer failing over scanning when SpawnBot returned an id we cannot resolve.
                    if (character == null)
                    {
                        Despawn(spawnedId);
                        error = "Failed to resolve spawned crew character";
                        return false;
                    }
                }
                else
                {
                    // Last resort only: no usable spawn id. Very close + not already tracked.
                    string scanErr;
                    character = FindNearbyCharacter(pos, NearbyFallbackRadius, knownCharacterIds, out scanErr);
                    if (scanErr != null)
                    {
                        error = scanErr;
                        return false;
                    }
                }

                if (character == null)
                {
                    error = "Failed to spawn crew character";
                    return false;
                }

                characterEntityId = character.EntityId;
                spawnedId = characterEntityId;

                if (!string.IsNullOrEmpty(displayName))
                    character.DisplayName = displayName;

                cockpit.AttachPilot(character);

                if (cockpit.Pilot == null || cockpit.Pilot.EntityId != character.EntityId)
                {
                    Despawn(characterEntityId);
                    characterEntityId = 0;
                    error = "Failed to attach crew to seat";
                    return false;
                }

                return true;
            }
            catch (Exception e)
            {
                if (spawnedId != 0)
                    Despawn(spawnedId);
                characterEntityId = 0;
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

        private static IMyCharacter FindNearbyCharacter(Vector3D pos, double radius, ISet<long> knownCharacterIds, out string error)
        {
            error = null;
            IMyCharacter found = null;
            var matches = 0;
            var r2 = radius * radius;
            MyAPIGateway.Entities.GetEntities(null, e =>
            {
                var ch = e as IMyCharacter;
                if (ch == null || ch.IsPlayer) return false;
                if (knownCharacterIds != null && knownCharacterIds.Contains(ch.EntityId)) return false;
                if (Vector3D.DistanceSquared(ch.GetPosition(), pos) <= r2)
                {
                    matches++;
                    found = ch;
                }
                return false;
            });

            if (matches > 1)
            {
                error = "Ambiguous nearby crew character";
                return null;
            }

            return found;
        }
    }
}
