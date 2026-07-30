namespace HireCrew
{
    /// <summary>
    /// Pure rules for crew hotkey handling (no ModAPI / RichHud).
    /// </summary>
    public static class CrewKeyBindRules
    {
        public static bool ShouldToggleOpenCrewUi(bool bindNewPressed, bool chatOpen)
        {
            return ShouldHandleBind(bindNewPressed, chatOpen);
        }

        public static bool ShouldHandleBind(bool bindNewPressed, bool chatOpen)
        {
            return bindNewPressed && !chatOpen;
        }

        public static bool ShouldRecallRole(bool anyOfRoleOnMission)
        {
            return anyOfRoleOnMission;
        }

        public static string FormatRoleDispatchSummary(string roleLabel, bool recall, int count)
        {
            if (string.IsNullOrEmpty(roleLabel))
                roleLabel = "Crew";
            if (count <= 0)
                return roleLabel + ": none ready";
            if (recall)
                return roleLabel + ": recalling " + count;
            return roleLabel + ": sent " + count;
        }
    }
}
