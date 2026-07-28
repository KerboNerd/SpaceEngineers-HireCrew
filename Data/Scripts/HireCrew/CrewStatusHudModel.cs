using System.Collections.Generic;

namespace HireCrew
{
    public sealed class CrewStatusHudRow
    {
        public string CrewId;
        public string DisplayName;
        public string RoleLabel;
        public string StatusLabel;
        public string HintLabel;
        public int State;
    }

    public sealed class CrewStatusHudModel
    {
        public const int MaxVisibleRows = 6;

        public bool SidebarEnabled = true;

        public void ToggleSidebar()
        {
            SidebarEnabled = !SidebarEnabled;
        }

        public static string StatusLabelFor(RepairMissionState state)
        {
            switch (state)
            {
                case RepairMissionState.WalkOut: return "Walking out";
                case RepairMissionState.AtExit: return "At airlock";
                case RepairMissionState.EvaTransit: return "EVA";
                case RepairMissionState.Welding: return "Welding";
                case RepairMissionState.ReturnExit: return "Returning";
                case RepairMissionState.WalkHome: return "Walking home";
                default: return "";
            }
        }

        public static string HintLabelFor(int hints)
        {
            bool outOfComps = (hints & RepairMissionHintFlags.OutOfComps) != 0;
            bool projected = (hints & RepairMissionHintFlags.ProjectedTarget) != 0;
            if (outOfComps && projected) return "Out of comps · Projector";
            if (outOfComps) return "Out of comps";
            if (projected) return "Projector";
            return "";
        }

        public static string StatusLabelForSalvage(SalvageMissionState state)
        {
            switch (state)
            {
                case SalvageMissionState.EvaTransit: return "EVA";
                case SalvageMissionState.Grinding: return "Grinding";
                default: return "";
            }
        }

        public static string HintLabelForSalvage(int hints)
        {
            if ((hints & SalvageMissionHintFlags.CargoFull) != 0)
                return "Cargo full";
            return "";
        }

        public static List<CrewStatusHudRow> BuildRows(
            IList<RepairMissionSnapshotEntry> entries,
            long gridEntityId,
            out int overflowCount)
        {
            return BuildRows(entries, null, gridEntityId, out overflowCount);
        }

        public static List<CrewStatusHudRow> BuildRows(
            IList<RepairMissionSnapshotEntry> repairEntries,
            IList<SalvageMissionSnapshotEntry> salvageEntries,
            long gridEntityId,
            out int overflowCount)
        {
            overflowCount = 0;
            var rows = new List<CrewStatusHudRow>();
            if (gridEntityId == 0) return rows;

            if (repairEntries != null)
            {
                for (int i = 0; i < repairEntries.Count; i++)
                {
                    var e = repairEntries[i];
                    if (e == null || string.IsNullOrEmpty(e.CrewId)) continue;
                    if (e.GridEntityId != gridEntityId) continue;
                    if (e.State == (int)RepairMissionState.Idle) continue;

                    string status = StatusLabelFor((RepairMissionState)e.State);
                    if (status.Length == 0) continue;

                    if (rows.Count >= MaxVisibleRows)
                    {
                        overflowCount++;
                        continue;
                    }

                    string name = e.DisplayName;
                    if (string.IsNullOrEmpty(name)) name = "Crew";

                    rows.Add(new CrewStatusHudRow
                    {
                        CrewId = e.CrewId,
                        DisplayName = name,
                        RoleLabel = "Construction",
                        StatusLabel = status,
                        HintLabel = HintLabelFor(e.Hints),
                        State = e.State
                    });
                }
            }

            if (salvageEntries != null)
            {
                for (int i = 0; i < salvageEntries.Count; i++)
                {
                    var e = salvageEntries[i];
                    if (e == null || string.IsNullOrEmpty(e.CrewId)) continue;
                    if (e.GridEntityId != gridEntityId) continue;
                    if (e.State == (int)SalvageMissionState.Idle) continue;

                    string status = StatusLabelForSalvage((SalvageMissionState)e.State);
                    if (status.Length == 0) continue;

                    if (rows.Count >= MaxVisibleRows)
                    {
                        overflowCount++;
                        continue;
                    }

                    string name = e.DisplayName;
                    if (string.IsNullOrEmpty(name)) name = "Crew";

                    rows.Add(new CrewStatusHudRow
                    {
                        CrewId = e.CrewId,
                        DisplayName = name,
                        RoleLabel = "Salvage Ops",
                        StatusLabel = status,
                        HintLabel = HintLabelForSalvage(e.Hints),
                        State = e.State
                    });
                }
            }

            return rows;
        }
    }
}
