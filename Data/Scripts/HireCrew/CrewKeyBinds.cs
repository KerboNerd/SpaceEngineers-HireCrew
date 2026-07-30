using RichHudFramework.UI;
using RichHudFramework.UI.Client;
using VRage.Input;

namespace HireCrew
{
    /// <summary>
    /// Client keybinds for HireCrew via RichHud BindManager + RebindPage.
    /// </summary>
    public static class CrewKeyBinds
    {
        public const string GroupName = "HireCrew";
        public const string OpenCrewUiName = "Open Crew UI";

        private static IBindGroup _group;
        private static IBind _openCrewUi;
        private static bool _rebindPageAdded;

        public static IBind OpenCrewUi
        {
            get { return _openCrewUi; }
        }

        public static void Register()
        {
            if (_openCrewUi != null)
                return;

            _group = BindManager.GetOrCreateGroup(GroupName);

            var defaults = new BindGroupInitializer
            {
                { OpenCrewUiName, MyKeys.Home }
            };

            if (!_group.DoesBindExist(OpenCrewUiName))
                _group.RegisterBinds(defaults);

            _openCrewUi = _group[OpenCrewUiName];

            if (!_rebindPageAdded)
            {
                var page = new RebindPage
                {
                    Name = "Key Binds",
                    Enabled = true
                };
                page.Add(_group, defaults.GetBindDefinitions());
                RichHudTerminal.Root.Enabled = true;
                RichHudTerminal.Root.Add(page);
                _rebindPageAdded = true;
            }
        }

        public static void Clear()
        {
            _openCrewUi = null;
            _group = null;
            _rebindPageAdded = false;
        }
    }
}
