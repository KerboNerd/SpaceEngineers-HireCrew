namespace HireCrew
{
    /// <summary>Pure helpers for /hirecrew fill (no ModAPI).</summary>
    public static class CrewAdminFillRules
    {
        public const int DefaultCount = 10;
        public const int MinCount = 1;
        public const int MaxCount = 50;
        public const int FillStars = 3;

        public static bool IsFillRole(CrewRole role)
        {
            return role == CrewRole.DamageControl || role == CrewRole.SalvageOps;
        }

        public static int ClampCount(int requested)
        {
            if (requested < MinCount) return MinCount;
            if (requested > MaxCount) return MaxCount;
            return requested;
        }

        public static string FormatResult(string roleLabel, int assigned, int requested, int noSeat, string gridName)
        {
            string name = string.IsNullOrEmpty(gridName) ? "?" : gridName;
            string label = string.IsNullOrEmpty(roleLabel) ? "?" : roleLabel;
            if (noSeat > 0)
                return "Filled " + label + ": assigned " + assigned + "/" + requested
                    + " (" + noSeat + " no seat) on " + name;
            return "Filled " + label + ": assigned " + assigned + "/" + requested + " on " + name;
        }
    }
}
