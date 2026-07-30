namespace HireCrew
{
    /// <summary>
    /// Pure rules for crew UI hotkey handling (no ModAPI / RichHud).
    /// </summary>
    public static class CrewKeyBindRules
    {
        public static bool ShouldToggleOpenCrewUi(bool bindNewPressed, bool chatOpen)
        {
            return bindNewPressed && !chatOpen;
        }
    }
}
