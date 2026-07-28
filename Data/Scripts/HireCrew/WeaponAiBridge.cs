using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces;
using VRage.Game.Entity;

namespace HireCrew
{
    /// <summary>
    /// Minimal WeaponCore/CoreSystems consumer API (whitelist-safe).
    /// Binds only HasCoreWeapon + SetBlockTrackingRange; AI on/off uses WC_ControlModes terminal property.
    /// </summary>
    public sealed class WeaponAiBridge
    {
        private const long WcApiChannel = 67549756549L;

        private bool _handlerRegistered;
        private bool _ready;
        private Func<MyEntity, bool> _hasCoreWeapon;
        private Action<MyEntity, float> _setBlockTrackingRange;

        public bool IsReady { get { return _ready; } }

        public void Load()
        {
            if (_handlerRegistered) return;
            MyAPIGateway.Utilities.RegisterMessageHandler(WcApiChannel, OnWcApiMessage);
            _handlerRegistered = true;
            MyAPIGateway.Utilities.SendModMessage(WcApiChannel, "ApiEndpointRequest");
        }

        public void Unload()
        {
            if (!_handlerRegistered) return;
            MyAPIGateway.Utilities.UnregisterMessageHandler(WcApiChannel, OnWcApiMessage);
            _handlerRegistered = false;
            _ready = false;
            _hasCoreWeapon = null;
            _setBlockTrackingRange = null;
        }

        private void OnWcApiMessage(object obj)
        {
            if (_ready || obj is string) return;

            var dict = obj as IReadOnlyDictionary<string, Delegate>;
            if (dict == null)
            {
                var mutable = obj as Dictionary<string, Delegate>;
                if (mutable == null) return;
                dict = mutable;
            }

            Delegate hasDel;
            Delegate rangeDel;
            if (!dict.TryGetValue("HasCoreWeaponBase", out hasDel))
                return;
            if (!dict.TryGetValue("SetBlockTrackingRangeBase", out rangeDel))
                return;

            _hasCoreWeapon = hasDel as Func<MyEntity, bool>;
            _setBlockTrackingRange = rangeDel as Action<MyEntity, float>;
            if (_hasCoreWeapon == null || _setBlockTrackingRange == null)
                return;

            _ready = true;
            MyAPIGateway.Utilities.ShowMessage("HireCrew", "WcApi ready");
        }

        public bool IsCoreWeapon(IMyTerminalBlock block)
        {
            if (block == null || !_ready || _hasCoreWeapon == null) return false;
            return _hasCoreWeapon((MyEntity)block);
        }

        public void ForceAiOff(IMyTerminalBlock weapon)
        {
            SetControlMode(weapon, manual: true);
        }

        public void SetManned(IMyTerminalBlock weapon, bool manned, int stars, float efficiencyMultiplier = 1f)
        {
            if (weapon == null) return;

            if (!manned)
            {
                ForceAiOff(weapon);
                return;
            }

            SetControlMode(weapon, manual: false);
            if (_ready && _setBlockTrackingRange != null)
                _setBlockTrackingRange((MyEntity)weapon, CrewConfig.GetTrackingRange(stars, efficiencyMultiplier));
        }

        private static void SetControlMode(IMyTerminalBlock weapon, bool manual)
        {
            // WC terminal id is "WC_" + "ControlModes"; Auto=0, Manual=1
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
            }

            try
            {
                weapon.SetValue("WC_ControlModes", manual ? 1L : 0L);
            }
            catch (Exception)
            {
            }
        }
    }
}
