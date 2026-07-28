using System;
using System.Collections.Generic;
using System.Text;
using Sandbox.Common.ObjectBuilders;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRage.Utils;

namespace HireCrew
{
    /// <summary>
    /// Store-lookalike hire desk. Terminal opens RichHud and configures per-desk pool settings.
    /// </summary>
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_TerminalBlock), false, "HC_CrewHireDesk")]
    public sealed class CrewHireBlockLogic : MyGameLogicComponent
    {
        public const string BlockSubtype = "HC_CrewHireDesk";

        private static bool _controlsRegistered;
        private IMyTerminalBlock _block;

        private static readonly string[] HireControlIds =
        {
            "HireCrew_OpenDesk",
            "HireCrew_RefreshMinutes",
            "HireCrew_PriceMultiplier",
            "HireCrew_MinCandidates",
            "HireCrew_MaxCandidates",
            "HireCrew_StarBias",
            "HireCrew_Role_Gunner",
            "HireCrew_Role_Engineer",
            "HireCrew_Role_Helmsman",
            "HireCrew_Role_Propulsion",
            "HireCrew_Role_Quartermaster",
            "HireCrew_RefillOnHire",
            "HireCrew_RerollNow"
        };

        public static bool IsHireDesk(IMyTerminalBlock block)
        {
            return block != null && block.BlockDefinition.SubtypeName == BlockSubtype;
        }

        public static void EnsureTerminalControls()
        {
            if (_controlsRegistered) return;
            _controlsRegistered = true;

            var open = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlButton, IMyTerminalBlock>("HireCrew_OpenDesk");
            open.Title = MyStringId.GetOrCompute("Open Hiring Desk");
            open.Tooltip = MyStringId.GetOrCompute("Open the crew hiring UI for this desk.");
            open.SupportsMultipleBlocks = false;
            open.Enabled = b => IsHireDesk(b);
            open.Visible = b => IsHireDesk(b);
            open.Action = b =>
            {
                var session = CrewSession.Instance;
                if (session == null || b == null) return;
                session.ClientOpenHireDesk(b.EntityId);
            };
            MyAPIGateway.TerminalControls.AddControl<IMyTerminalBlock>(open);

            AddRefreshSlider();
            AddPriceSlider();
            AddMinCandidatesSlider();
            AddMaxCandidatesSlider();
            AddStarBiasCombo();
            AddRoleCheckbox(CrewRole.Gunner, "HireCrew_Role_Gunner", "Allow Gunner");
            AddRoleCheckbox(CrewRole.Engineer, "HireCrew_Role_Engineer", "Allow Reactor Tech");
            AddRoleCheckbox(CrewRole.Helmsman, "HireCrew_Role_Helmsman", "Allow Helmsman");
            AddRoleCheckbox(CrewRole.Propulsion, "HireCrew_Role_Propulsion", "Allow Propulsion Tech");
            AddRoleCheckbox(CrewRole.Quartermaster, "HireCrew_Role_Quartermaster", "Allow Quartermaster");
            AddRoleCheckbox(CrewRole.DamageControl, "HireCrew_Role_DamageControl", "Allow Construction");
            AddRoleCheckbox(CrewRole.SalvageOps, "HireCrew_Role_SalvageOps", "Allow Salvage Ops");
            AddRefillSwitch();
            AddRerollButton();

            MyAPIGateway.TerminalControls.CustomControlGetter += FilterControls;
        }

        private static void AddRefreshSlider()
        {
            var slider = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSlider, IMyTerminalBlock>("HireCrew_RefreshMinutes");
            slider.Title = MyStringId.GetOrCompute("Pool refresh (minutes)");
            slider.Tooltip = MyStringId.GetOrCompute("How long candidates stick before this desk rerolls.");
            slider.SupportsMultipleBlocks = false;
            slider.Enabled = b => IsHireDesk(b);
            slider.Visible = b => IsHireDesk(b);
            slider.SetLimits(CrewConfig.MinRefreshMinutes, CrewConfig.MaxRefreshMinutes);
            slider.Getter = b =>
            {
                var pool = GetPool(b);
                return pool != null ? pool.RefreshMinutes : CrewConfig.DefaultRefreshMinutes;
            };
            slider.Setter = (b, value) =>
            {
                if (b == null) return;
                var session = CrewSession.Instance;
                if (session != null)
                    session.ClientRequestHireRefreshMinutes(b.EntityId, CrewConfig.ClampRefreshMinutes((int)Math.Round(value)));
            };
            slider.Writer = (b, sb) =>
            {
                int minutes = CrewConfig.DefaultRefreshMinutes;
                var pool = GetPool(b);
                if (pool != null) minutes = pool.RefreshMinutes;
                sb.Clear();
                if (minutes >= 60)
                    sb.Append(minutes / 60).Append("h ").Append(minutes % 60).Append("m");
                else
                    sb.Append(minutes).Append(" min");
            };
            MyAPIGateway.TerminalControls.AddControl<IMyTerminalBlock>(slider);
        }

        private static void AddPriceSlider()
        {
            var priceSlider = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSlider, IMyTerminalBlock>("HireCrew_PriceMultiplier");
            priceSlider.Title = MyStringId.GetOrCompute("Price multiplier");
            priceSlider.Tooltip = MyStringId.GetOrCompute("Scales candidate hire prices at this desk.");
            priceSlider.SupportsMultipleBlocks = false;
            priceSlider.Enabled = b => IsHireDesk(b);
            priceSlider.Visible = b => IsHireDesk(b);
            priceSlider.SetLimits(CrewConfig.MinPriceMultiplierPercent, CrewConfig.MaxPriceMultiplierPercent);
            priceSlider.Getter = b =>
            {
                var pool = GetPool(b);
                if (pool == null || pool.PriceMultiplierPercent <= 0)
                    return CrewConfig.DefaultPriceMultiplierPercent;
                return pool.PriceMultiplierPercent;
            };
            priceSlider.Setter = (b, value) =>
            {
                if (b == null) return;
                var session = CrewSession.Instance;
                if (session != null)
                    session.ClientRequestHirePriceMultiplier(
                        b.EntityId, CrewConfig.ClampPriceMultiplierPercent((int)Math.Round(value)));
            };
            priceSlider.Writer = (b, sb) =>
            {
                int percent = CrewConfig.DefaultPriceMultiplierPercent;
                var pool = GetPool(b);
                if (pool != null && pool.PriceMultiplierPercent > 0)
                    percent = pool.PriceMultiplierPercent;
                sb.Clear();
                sb.Append((percent / 100f).ToString("0.00")).Append("x");
            };
            MyAPIGateway.TerminalControls.AddControl<IMyTerminalBlock>(priceSlider);
        }

        private static void AddMinCandidatesSlider()
        {
            var slider = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSlider, IMyTerminalBlock>("HireCrew_MinCandidates");
            slider.Title = MyStringId.GetOrCompute("Min candidates");
            slider.Tooltip = MyStringId.GetOrCompute("Minimum candidates rolled each refresh.");
            slider.SupportsMultipleBlocks = false;
            slider.Enabled = b => IsHireDesk(b);
            slider.Visible = b => IsHireDesk(b);
            slider.SetLimits(CrewConfig.MinCandidates, CrewConfig.MaxCandidates);
            slider.Getter = b =>
            {
                var pool = GetPool(b);
                return pool != null && pool.MinCandidates > 0 ? pool.MinCandidates : CrewConfig.MinCandidates;
            };
            slider.Setter = (b, value) =>
            {
                if (b == null) return;
                var session = CrewSession.Instance;
                if (session != null)
                    session.ClientRequestHireMinCandidates(b.EntityId, (int)Math.Round(value));
            };
            slider.Writer = (b, sb) =>
            {
                var pool = GetPool(b);
                int v = pool != null && pool.MinCandidates > 0 ? pool.MinCandidates : CrewConfig.MinCandidates;
                sb.Clear();
                sb.Append(v);
            };
            MyAPIGateway.TerminalControls.AddControl<IMyTerminalBlock>(slider);
        }

        private static void AddMaxCandidatesSlider()
        {
            var slider = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSlider, IMyTerminalBlock>("HireCrew_MaxCandidates");
            slider.Title = MyStringId.GetOrCompute("Max candidates");
            slider.Tooltip = MyStringId.GetOrCompute("Maximum candidates rolled each refresh.");
            slider.SupportsMultipleBlocks = false;
            slider.Enabled = b => IsHireDesk(b);
            slider.Visible = b => IsHireDesk(b);
            slider.SetLimits(CrewConfig.MinCandidates, CrewConfig.MaxCandidates);
            slider.Getter = b =>
            {
                var pool = GetPool(b);
                return pool != null && pool.MaxCandidates > 0 ? pool.MaxCandidates : CrewConfig.MaxCandidates;
            };
            slider.Setter = (b, value) =>
            {
                if (b == null) return;
                var session = CrewSession.Instance;
                if (session != null)
                    session.ClientRequestHireMaxCandidates(b.EntityId, (int)Math.Round(value));
            };
            slider.Writer = (b, sb) =>
            {
                var pool = GetPool(b);
                int v = pool != null && pool.MaxCandidates > 0 ? pool.MaxCandidates : CrewConfig.MaxCandidates;
                sb.Clear();
                sb.Append(v);
            };
            MyAPIGateway.TerminalControls.AddControl<IMyTerminalBlock>(slider);
        }

        private static void AddStarBiasCombo()
        {
            var combo = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlCombobox, IMyTerminalBlock>("HireCrew_StarBias");
            combo.Title = MyStringId.GetOrCompute("Star bias");
            combo.Tooltip = MyStringId.GetOrCompute("Skew candidate star rolls at this desk.");
            combo.SupportsMultipleBlocks = false;
            combo.Enabled = b => IsHireDesk(b);
            combo.Visible = b => IsHireDesk(b);
            combo.ComboBoxContent = list =>
            {
                list.Add(new MyTerminalControlComboBoxItem
                {
                    Key = (long)StarBias.Low,
                    Value = MyStringId.GetOrCompute("Low")
                });
                list.Add(new MyTerminalControlComboBoxItem
                {
                    Key = (long)StarBias.Balanced,
                    Value = MyStringId.GetOrCompute("Balanced")
                });
                list.Add(new MyTerminalControlComboBoxItem
                {
                    Key = (long)StarBias.High,
                    Value = MyStringId.GetOrCompute("High")
                });
            };
            combo.Getter = b =>
            {
                var pool = GetPool(b);
                return pool != null ? pool.StarBias : (long)StarBias.Balanced;
            };
            combo.Setter = (b, key) =>
            {
                if (b == null) return;
                var session = CrewSession.Instance;
                if (session != null)
                    session.ClientRequestHireStarBias(b.EntityId, (StarBias)key);
            };
            MyAPIGateway.TerminalControls.AddControl<IMyTerminalBlock>(combo);
        }

        private static void AddRoleCheckbox(CrewRole role, string id, string title)
        {
            var box = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlCheckbox, IMyTerminalBlock>(id);
            box.Title = MyStringId.GetOrCompute(title);
            box.SupportsMultipleBlocks = false;
            box.Visible = b => IsHireDesk(b);
            box.Enabled = b =>
            {
                if (!IsHireDesk(b)) return false;
                int worldMask = HireWorldConfig.Current != null
                    ? HireWorldConfig.Current.AllowedRolesMask
                    : HireWorldConfig.AllRolesMask;
                return HireWorldConfig.RoleAllowed(worldMask, (int)role);
            };
            box.Getter = b =>
            {
                var pool = GetPool(b);
                int mask = pool != null && pool.AllowedRoles != 0
                    ? pool.AllowedRoles
                    : (HireWorldConfig.Current != null
                        ? HireWorldConfig.Current.AllowedRolesMask
                        : HireWorldConfig.AllRolesMask);
                return HireWorldConfig.RoleAllowed(mask, (int)role);
            };
            box.Setter = (b, value) =>
            {
                if (b == null) return;
                var session = CrewSession.Instance;
                if (session == null) return;
                var pool = GetPool(b);
                int mask = pool != null && pool.AllowedRoles != 0
                    ? pool.AllowedRoles
                    : (HireWorldConfig.Current != null
                        ? HireWorldConfig.Current.AllowedRolesMask
                        : HireWorldConfig.AllRolesMask);
                int bit = 1 << (int)role;
                if (value) mask |= bit;
                else mask &= ~bit;
                if (mask == 0)
                    mask = bit; // keep at least this role if user unchecked last one client-side
                session.ClientRequestHireAllowedRoles(b.EntityId, mask);
            };
            MyAPIGateway.TerminalControls.AddControl<IMyTerminalBlock>(box);
        }

        private static void AddRefillSwitch()
        {
            var sw = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlOnOffSwitch, IMyTerminalBlock>("HireCrew_RefillOnHire");
            sw.Title = MyStringId.GetOrCompute("Refill on hire");
            sw.Tooltip = MyStringId.GetOrCompute("When on, hiring replaces the taken slot with a new candidate.");
            sw.OnText = MyStringId.GetOrCompute("On");
            sw.OffText = MyStringId.GetOrCompute("Off");
            sw.SupportsMultipleBlocks = false;
            sw.Enabled = b => IsHireDesk(b);
            sw.Visible = b => IsHireDesk(b);
            sw.Getter = b =>
            {
                var pool = GetPool(b);
                return pool != null && pool.RefillOnHire;
            };
            sw.Setter = (b, value) =>
            {
                if (b == null) return;
                var session = CrewSession.Instance;
                if (session != null)
                    session.ClientRequestHireRefillOnHire(b.EntityId, value);
            };
            MyAPIGateway.TerminalControls.AddControl<IMyTerminalBlock>(sw);
        }

        private static void AddRerollButton()
        {
            var reroll = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlButton, IMyTerminalBlock>("HireCrew_RerollNow");
            reroll.Title = MyStringId.GetOrCompute("Reroll pool now");
            reroll.Tooltip = MyStringId.GetOrCompute("Immediately regenerate candidates for this desk.");
            reroll.SupportsMultipleBlocks = false;
            reroll.Enabled = b => IsHireDesk(b);
            reroll.Visible = b => IsHireDesk(b);
            reroll.Action = b =>
            {
                if (b == null) return;
                var session = CrewSession.Instance;
                if (session != null)
                    session.ClientRequestHireReroll(b.EntityId);
            };
            MyAPIGateway.TerminalControls.AddControl<IMyTerminalBlock>(reroll);
        }

        private static HireBlockPool GetPool(IMyTerminalBlock b)
        {
            var session = CrewSession.Instance;
            if (session == null || session.HirePools == null || b == null)
                return null;
            return session.HirePools.Get(b.EntityId);
        }

        private static void FilterControls(IMyTerminalBlock block, List<IMyTerminalControl> controls)
        {
            if (!IsHireDesk(block) || controls == null) return;
            for (int i = controls.Count - 1; i >= 0; i--)
            {
                var c = controls[i];
                if (c == null) continue;
                var id = c.Id ?? "";
                bool keep = false;
                for (int j = 0; j < HireControlIds.Length; j++)
                {
                    if (id == HireControlIds[j]) { keep = true; break; }
                }
                if (keep) continue;
                if (id == "OnOff" || id == "ShowInTerminal" || id == "ShowInToolbarConfig" || id == "Name"
                    || id == "CustomData" || id == "ShowOnHUD")
                    continue;
            }
        }

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            NeedsUpdate = MyEntityUpdateEnum.BEFORE_NEXT_FRAME;
        }

        public override void OnAddedToScene()
        {
            base.OnAddedToScene();
            _block = Entity as IMyTerminalBlock;
            EnsureTerminalControls();
            if (_block != null)
                _block.AppendingCustomInfo += AppendInfo;

            var session = CrewSession.Instance;
            if (session != null && MyAPIGateway.Multiplayer != null && MyAPIGateway.Multiplayer.IsServer
                && _block != null && _block.CubeGrid != null)
            {
                session.RegisterHireDesk(_block.EntityId, _block.CubeGrid.EntityId);
            }
        }

        public override void OnRemovedFromScene()
        {
            if (_block != null)
            {
                _block.AppendingCustomInfo -= AppendInfo;
                var session = CrewSession.Instance;
                if (session != null && MyAPIGateway.Multiplayer != null && MyAPIGateway.Multiplayer.IsServer)
                    session.UnregisterHireDesk(_block.EntityId);
            }
            _block = null;
            base.OnRemovedFromScene();
        }

        public override void UpdateOnceBeforeFrame()
        {
            EnsureTerminalControls();
        }

        private void AppendInfo(IMyTerminalBlock block, StringBuilder sb)
        {
            if (sb == null) return;
            int count = 0;
            int minutes = CrewConfig.DefaultRefreshMinutes;
            int mult = CrewConfig.DefaultPriceMultiplierPercent;
            int minC = CrewConfig.MinCandidates;
            int maxC = CrewConfig.MaxCandidates;
            int roles = HireWorldConfig.Current != null
                ? HireWorldConfig.Current.AllowedRolesMask
                : HireWorldConfig.AllRolesMask;
            StarBias bias = StarBias.Balanced;
            bool refill = false;

            var pool = GetPool(block);
            if (pool != null)
            {
                minutes = pool.RefreshMinutes;
                if (pool.PriceMultiplierPercent > 0)
                    mult = pool.PriceMultiplierPercent;
                count = pool.Candidates != null ? pool.Candidates.Count : 0;
                if (pool.MinCandidates > 0) minC = pool.MinCandidates;
                if (pool.MaxCandidates > 0) maxC = pool.MaxCandidates;
                if (pool.AllowedRoles != 0) roles = pool.AllowedRoles;
                bias = (StarBias)pool.StarBias;
                refill = pool.RefillOnHire;
            }

            sb.AppendLine();
            sb.AppendLine("HireCrew desk");
            sb.AppendLine("Candidates: " + count + " (" + minC + "-" + maxC + ")");
            sb.AppendLine("Refresh: " + minutes + " min");
            sb.AppendLine("Price: " + (mult / 100f).ToString("0.00") + "x");
            sb.AppendLine("Bias: " + bias);
            sb.AppendLine("Roles: " + FormatRoles(roles));
            sb.AppendLine("Refill: " + (refill ? "on" : "off"));
            sb.AppendLine("Aim at screen + F, or Open Hiring Desk.");
        }

        private static string FormatRoles(int mask)
        {
            var sb = new StringBuilder(16);
            if (HireWorldConfig.RoleAllowed(mask, (int)CrewRole.Gunner)) sb.Append('G');
            if (HireWorldConfig.RoleAllowed(mask, (int)CrewRole.Engineer)) sb.Append('E');
            if (HireWorldConfig.RoleAllowed(mask, (int)CrewRole.Helmsman)) sb.Append('H');
            if (HireWorldConfig.RoleAllowed(mask, (int)CrewRole.Propulsion)) sb.Append('P');
            if (HireWorldConfig.RoleAllowed(mask, (int)CrewRole.Quartermaster)) sb.Append('Q');
            if (HireWorldConfig.RoleAllowed(mask, (int)CrewRole.DamageControl)) sb.Append('D');
            if (HireWorldConfig.RoleAllowed(mask, (int)CrewRole.SalvageOps)) sb.Append('S');
            return sb.Length == 0 ? "-" : sb.ToString();
        }
    }
}
