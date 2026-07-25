using Sandbox.Game.GameSystems.BankingAndCurrency;

namespace HireCrew
{
    public static class CrewEconomy
    {
        public const string ErrorInsufficientFunds = "Insufficient credits";

        public static bool TryCharge(long identityId, long amount, out string error)
        {
            error = null;
            if (identityId == 0 || amount <= 0)
            {
                error = "Invalid payment";
                return false;
            }

            var balance = MyBankingSystem.GetBalance(identityId);
            if (balance < amount)
            {
                error = ErrorInsufficientFunds;
                return false;
            }

            MyBankingSystem.ChangeBalance(identityId, -amount);
            return true;
        }
    }
}
