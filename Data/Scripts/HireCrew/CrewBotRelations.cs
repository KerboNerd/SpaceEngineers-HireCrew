using System;

namespace HireCrew
{
    /// <summary>
    /// How ambient bot identities/bodies should relate to the hiring owner so
    /// ship weapons (vanilla turrets / WeaponCore) treat crew as friendly.
    /// WeaponCore compares block owner vs character ControllingIdentityId.
    /// </summary>
    public static class CrewBotRelations
    {
        /// <summary>NPC faction for factionless owners' ambient bots.</summary>
        public const string AmbientFriendlyFactionTag = "HCREW";
        public const string AmbientFriendlyFactionName = "HireCrew";
        /// <summary>Keen max friendly reputation (player ↔ faction).</summary>
        public const int FriendlyReputation = 1500;

        /// <summary>
        /// Faction the bot identity should join when the owner already has one.
        /// 0 = use the HireCrew NPC faction + reputation instead.
        /// </summary>
        public static long ResolveFriendlyFactionId(CrewRecord crew, Func<long, long> playerFactionIdOrZero)
        {
            if (crew == null)
                return 0;

            if (crew.OwnerIsFaction && crew.OwnerKey != 0)
                return crew.OwnerKey;

            if (crew.OwnerIdentityId != 0 && playerFactionIdOrZero != null)
            {
                long factionId = playerFactionIdOrZero(crew.OwnerIdentityId);
                if (factionId != 0)
                    return factionId;
            }

            return 0;
        }

        public static bool NeedsFallbackFriendlyFaction(long resolvedOwnerFactionId)
        {
            return resolvedOwnerFactionId == 0;
        }

        /// <summary>
        /// OwningPlayerIdentityId for the ambient body. Prefer the hiring player so
        /// vanilla ownership checks stay friendly even when factionless.
        /// </summary>
        public static long ResolveCharacterOwnerIdentityId(CrewRecord crew, long botIdentityId)
        {
            if (crew != null && crew.OwnerIdentityId != 0)
                return crew.OwnerIdentityId;
            return botIdentityId;
        }
    }
}
