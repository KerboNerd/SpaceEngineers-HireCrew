using VRage.Game.ModAPI;

namespace HireCrew
{
    /// <summary>
    /// Charges credits via whitelisted IMyPlayer banking helpers.
    /// </summary>
    public static class CrewEconomy
    {
        public const string ErrorInsufficientFunds = "Insufficient credits";
        public const string ErrorEconomyUnavailable = "Economy unavailable";

        public static bool TryCharge(IMyPlayer player, long amount, out string error)
        {
            error = null;
            if (player == null || amount <= 0)
            {
                error = "Invalid payment";
                return false;
            }

            long balance;
            if (!player.TryGetBalanceInfo(out balance))
            {
                error = ErrorEconomyUnavailable;
                return false;
            }

            if (balance < amount)
            {
                error = ErrorInsufficientFunds;
                return false;
            }

            player.RequestChangeBalance(-amount);
            return true;
        }
    }
}
