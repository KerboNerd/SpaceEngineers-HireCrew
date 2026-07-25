using System;
using CoreSystems.Api;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces;
using VRage.Game.Entity;
using VRage.ModAPI;

namespace HireCrew
{
    public sealed class WeaponAiBridge
    {
        private readonly WcApi _api = new WcApi();
        private bool _ready;

        public bool IsReady { get { return _ready && _api.IsReady; } }

        public void Load()
        {
            _api.Load(OnReady, true);
        }

        public void Unload()
        {
            _api.Unload();
            _ready = false;
        }

        private void OnReady()
        {
            _ready = true;
            MyAPIGateway.Utilities.ShowMessage("HireCrew", "WcApi ready");
        }

        public bool IsCoreWeapon(IMyTerminalBlock block)
        {
            if (block == null || !IsReady) return false;
            return _api.HasCoreWeapon((MyEntity)block);
        }

        public void ForceAiOff(IMyTerminalBlock weapon)
        {
            SetControlMode(weapon, manual: true);
        }

        public void SetManned(IMyTerminalBlock weapon, bool manned, CrewTier tier)
        {
            if (weapon == null) return;

            if (!manned)
            {
                ForceAiOff(weapon);
                return;
            }

            SetControlMode(weapon, manual: false);
            if (IsReady)
                _api.SetBlockTrackingRange((MyEntity)weapon, CrewConfig.GetTrackingRange(tier));
        }

        private static void SetControlMode(IMyTerminalBlock weapon, bool manual)
        {
            // WC terminal id is "WC_" + "ControlModes" => WC_ControlModes; Auto=0, Manual=1
            try
            {
                var prop = weapon.GetProperty("WC_ControlModes") as ITerminalProperty<long>;
                if (prop != null)
                {
                    prop.SetValue(weapon, manual ? 1L : 0L);
                    return;
                }
            }
            catch (Exception)
            {
                // fall through
            }

            try
            {
                weapon.SetValueLong("WC_ControlModes", manual ? 1L : 0L);
            }
            catch (Exception)
            {
                // WC not ready on this block yet
            }
        }
    }
}
