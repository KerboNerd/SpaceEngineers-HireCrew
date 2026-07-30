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
        public const string SendRecallConstructionName = "Send/Recall Construction";
        public const string SendRecallSalvageName = "Send/Recall Salvage";

        private static IBindGroup _group;
        private static IBind _openCrewUi;
        private static IBind _sendRecallConstruction;
        private static IBind _sendRecallSalvage;
        private static bool _rebindPageAdded;

        public static IBind OpenCrewUi { get { return _openCrewUi; } }
        public static IBind SendRecallConstruction { get { return _sendRecallConstruction; } }
        public static IBind SendRecallSalvage { get { return _sendRecallSalvage; } }

        public static void Register()
        {
            if (_openCrewUi != null && _sendRecallConstruction != null && _sendRecallSalvage != null)
                return;

            _group = BindManager.GetOrCreateGroup(GroupName);

            var defaults = new BindGroupInitializer
            {
                { OpenCrewUiName, MyKeys.Home },
                { SendRecallConstructionName, MyKeys.End },
                { SendRecallSalvageName, MyKeys.Delete }
            };

            if (!_group.DoesBindExist(OpenCrewUiName)
                || !_group.DoesBindExist(SendRecallConstructionName)
                || !_group.DoesBindExist(SendRecallSalvageName))
            {
                var missing = new BindGroupInitializer();
                if (!_group.DoesBindExist(OpenCrewUiName))
                    missing.Add(OpenCrewUiName, MyKeys.Home);
                if (!_group.DoesBindExist(SendRecallConstructionName))
                    missing.Add(SendRecallConstructionName, MyKeys.End);
                if (!_group.DoesBindExist(SendRecallSalvageName))
                    missing.Add(SendRecallSalvageName, MyKeys.Delete);
                _group.RegisterBinds(missing);
            }

            _openCrewUi = _group[OpenCrewUiName];
            _sendRecallConstruction = _group[SendRecallConstructionName];
            _sendRecallSalvage = _group[SendRecallSalvageName];

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
            _sendRecallConstruction = null;
            _sendRecallSalvage = null;
            _group = null;
            _rebindPageAdded = false;
        }
    }
}
