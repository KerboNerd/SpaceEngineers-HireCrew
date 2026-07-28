using System.Collections.Generic;
using HireCrew;
using Xunit;

namespace HireCrew.Logic.Tests
{
    public class CrewStatusHudModelTests
    {
        private static RepairMissionSnapshotEntry Entry(string id, long grid, RepairMissionState state, string name = null, int hints = 0)
        {
            return new RepairMissionSnapshotEntry
            {
                CrewId = id,
                DisplayName = name ?? id,
                GridEntityId = grid,
                State = (int)state,
                Hints = hints
            };
        }

        [Fact]
        public void BuildRows_filters_idle_and_other_grids()
        {
            var entries = new List<RepairMissionSnapshotEntry>
            {
                Entry("a", 1, RepairMissionState.Welding),
                Entry("b", 1, RepairMissionState.Idle),
                Entry("c", 2, RepairMissionState.EvaTransit)
            };
            int overflow;
            var rows = CrewStatusHudModel.BuildRows(entries, 1, out overflow);
            Assert.Equal(1, rows.Count);
            Assert.Equal("a", rows[0].CrewId);
            Assert.Equal(0, overflow);
        }

        [Fact]
        public void StatusLabelFor_maps_states()
        {
            Assert.Equal("Welding", CrewStatusHudModel.StatusLabelFor(RepairMissionState.Welding));
            Assert.Equal("Walking out", CrewStatusHudModel.StatusLabelFor(RepairMissionState.WalkOut));
            Assert.Equal("At airlock", CrewStatusHudModel.StatusLabelFor(RepairMissionState.AtExit));
            Assert.Equal("EVA", CrewStatusHudModel.StatusLabelFor(RepairMissionState.EvaTransit));
            Assert.Equal("Returning", CrewStatusHudModel.StatusLabelFor(RepairMissionState.ReturnExit));
            Assert.Equal("Walking home", CrewStatusHudModel.StatusLabelFor(RepairMissionState.WalkHome));
            Assert.Equal("", CrewStatusHudModel.StatusLabelFor(RepairMissionState.Idle));
        }

        [Fact]
        public void HintLabelFor_out_of_comps_and_projected()
        {
            Assert.Equal("Out of comps", CrewStatusHudModel.HintLabelFor(RepairMissionHintFlags.OutOfComps));
            Assert.Equal("Projector", CrewStatusHudModel.HintLabelFor(RepairMissionHintFlags.ProjectedTarget));
            Assert.Equal("Out of comps · Projector",
                CrewStatusHudModel.HintLabelFor(RepairMissionHintFlags.OutOfComps | RepairMissionHintFlags.ProjectedTarget));
            Assert.Equal("", CrewStatusHudModel.HintLabelFor(0));
        }

        [Fact]
        public void Salvage_status_and_hint_labels()
        {
            Assert.Equal("EVA", CrewStatusHudModel.StatusLabelForSalvage(SalvageMissionState.EvaTransit));
            Assert.Equal("Grinding", CrewStatusHudModel.StatusLabelForSalvage(SalvageMissionState.Grinding));
            Assert.Equal("", CrewStatusHudModel.StatusLabelForSalvage(SalvageMissionState.Idle));
            Assert.Equal("Cargo full",
                CrewStatusHudModel.HintLabelForSalvage(SalvageMissionHintFlags.CargoFull));
        }

        [Fact]
        public void BuildRows_includes_salvage_ops()
        {
            var repair = new List<RepairMissionSnapshotEntry>();
            var salvage = new List<SalvageMissionSnapshotEntry>
            {
                new SalvageMissionSnapshotEntry
                {
                    CrewId = "s1",
                    DisplayName = "Rook",
                    GridEntityId = 42,
                    State = (int)SalvageMissionState.Grinding,
                    Hints = SalvageMissionHintFlags.CargoFull
                }
            };
            int overflow;
            var rows = CrewStatusHudModel.BuildRows(repair, salvage, 42, out overflow);
            Assert.Equal(1, rows.Count);
            Assert.Equal("Salvage Ops", rows[0].RoleLabel);
            Assert.Equal("Grinding", rows[0].StatusLabel);
            Assert.Equal("Cargo full", rows[0].HintLabel);
        }

        [Fact]
        public void BuildRows_truncates_with_overflow()
        {
            var entries = new List<RepairMissionSnapshotEntry>();
            for (int i = 0; i < 8; i++)
                entries.Add(Entry("c" + i, 5, RepairMissionState.Welding));
            int overflow;
            var rows = CrewStatusHudModel.BuildRows(entries, 5, out overflow);
            Assert.Equal(6, rows.Count);
            Assert.Equal(2, overflow);
        }

        [Fact]
        public void BuildRows_skips_null_or_empty_crew_id()
        {
            var entries = new List<RepairMissionSnapshotEntry>
            {
                Entry(null, 1, RepairMissionState.Welding),
                Entry("", 1, RepairMissionState.Welding),
                Entry("ok", 1, RepairMissionState.Welding, name: "")
            };
            int overflow;
            var rows = CrewStatusHudModel.BuildRows(entries, 1, out overflow);
            Assert.Equal(1, rows.Count);
            Assert.Equal("Crew", rows[0].DisplayName);
        }

        [Fact]
        public void ToggleSidebar_flips_default_on()
        {
            var m = new CrewStatusHudModel();
            Assert.True(m.SidebarEnabled);
            m.ToggleSidebar();
            Assert.False(m.SidebarEnabled);
            m.ToggleSidebar();
            Assert.True(m.SidebarEnabled);
        }
    }
}
