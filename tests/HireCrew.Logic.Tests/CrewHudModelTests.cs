using System;
using System.Collections.Generic;
using HireCrew;
using Xunit;

namespace HireCrew.Logic.Tests
{
    public class CrewHudModelTests
    {
        [Fact]
        public void Toggle_opens_and_closes()
        {
            var m = new CrewHudModel();
            m.Toggle(42);
            Assert.True(m.IsOpen);
            Assert.Equal(42, m.GridEntityId);
            Assert.Equal(CrewHudScreen.Home, m.Screen);
            m.Toggle(42);
            Assert.False(m.IsOpen);
        }

        [Fact]
        public void FilterAvailableSeats_excludes_claimed()
        {
            var crew = new List<CrewRecord>
            {
                new CrewRecord { CrewId = "a", Status = CrewStatus.Seated, SeatEntityId = 10, WeaponEntityId = 20, GridEntityId = 1 }
            };
            var seats = new List<long> { 10, 11 };
            var free = CrewHudModel.FilterAvailableSeats(seats, crew, id => id);
            Assert.Equal(new long[] { 11 }, free.ToArray());
        }

        [Fact]
        public void TryBeginAssignFromHome_skips_crew_pick_and_keeps_selection()
        {
            var m = new CrewHudModel();
            m.Open(1);
            var crew = new CrewRecord { CrewId = "x", Status = CrewStatus.Unassigned };
            Assert.True(m.TryBeginAssignFromHome(crew));
            Assert.Equal(CrewHudScreen.AssignSeat, m.Screen);
            Assert.Equal("x", m.SelectedCrewId);
            Assert.Equal(0, m.SelectedSeatEntityId);
            Assert.Equal(0, m.SelectedWeaponEntityId);
        }

        [Fact]
        public void TryBeginAssignFromHome_rejects_seated_or_training()
        {
            var m = new CrewHudModel();
            m.Open(1);
            Assert.False(m.TryBeginAssignFromHome(new CrewRecord { CrewId = "a", Status = CrewStatus.Seated, GridEntityId = 1 }));
            Assert.Equal(CrewHudScreen.Home, m.Screen);
            Assert.False(m.TryBeginAssignFromHome(new CrewRecord
            {
                CrewId = "b",
                Status = CrewStatus.Unassigned,
                TrainingEndsUtcTicks = 9
            }));
            Assert.Equal(CrewHudScreen.Home, m.Screen);
        }

        [Fact]
        public void TryBeginQuartersFromHome_skips_crew_pick()
        {
            var m = new CrewHudModel();
            m.Open(1);
            var crew = new CrewRecord { CrewId = "q", Status = CrewStatus.Seated, GridEntityId = 5 };
            Assert.True(m.TryBeginQuartersFromHome(crew));
            Assert.Equal(CrewHudScreen.QuartersSlots, m.Screen);
            Assert.Equal("q", m.SelectedCrewId);
        }

        [Fact]
        public void TryBeginUnassignAndDismiss_keep_selection_on_confirm_screens()
        {
            var m = new CrewHudModel();
            m.Open(1);
            var seated = new CrewRecord { CrewId = "s", Status = CrewStatus.Seated, GridEntityId = 1 };
            Assert.True(m.TryBeginUnassignFromHome(seated));
            Assert.Equal(CrewHudScreen.UnassignPick, m.Screen);
            Assert.Equal("s", m.SelectedCrewId);
            m.GoHome();
            Assert.True(m.TryBeginDismissFromHome(seated));
            Assert.Equal(CrewHudScreen.DismissPick, m.Screen);
            Assert.Equal("s", m.SelectedCrewId);
        }

        [Fact]
        public void WizardBack_from_seat_and_quarters_slots_returns_home()
        {
            var m = new CrewHudModel();
            m.Open(1);
            Assert.True(m.TryBeginAssignFromHome(new CrewRecord { CrewId = "x", Status = CrewStatus.Unassigned }));
            m.WizardBack();
            Assert.Equal(CrewHudScreen.Home, m.Screen);

            Assert.True(m.TryBeginQuartersFromHome(new CrewRecord { CrewId = "q", Status = CrewStatus.Seated, GridEntityId = 1 }));
            m.WizardBack();
            Assert.Equal(CrewHudScreen.Home, m.Screen);
        }

        [Fact]
        public void FormatHomeContext_guides_player()
        {
            Assert.Equal("Select a crew member", CrewHudModel.FormatHomeContext(null));
            var unassigned = new CrewRecord
            {
                CrewId = "u",
                DisplayName = "Riven",
                Status = CrewStatus.Unassigned,
                Role = CrewRole.Gunner
            };
            var text = CrewHudModel.FormatHomeContext(unassigned);
            Assert.Contains("Riven", text);
            Assert.Contains("Unassigned", text);
        }

        [Fact]
        public void Open_without_grid_is_pool_only_and_blocks_assign()
        {
            var m = new CrewHudModel();
            m.Open(0);
            Assert.True(m.IsOpen);
            Assert.False(m.HasManagedGrid);
            Assert.False(m.TryBeginAssignFromHome(new CrewRecord { CrewId = "x", Status = CrewStatus.Unassigned }));
            Assert.Equal(CrewHudScreen.Home, m.Screen);
            var text = CrewHudModel.FormatHomeContext(
                new CrewRecord { DisplayName = "Riven", Status = CrewStatus.Unassigned, Role = CrewRole.Gunner },
                hasManagedGrid: false);
            Assert.Contains("Train or Dismiss", text);
            Assert.True(CrewHudModel.IsGridBoundScreen(CrewHudScreen.AssignSeat));
            Assert.False(CrewHudModel.IsGridBoundScreen(CrewHudScreen.TrainConfirm));
        }

        [Fact]
        public void ClampListScroll_limits_offset_to_visible_window()
        {
            var m = new CrewHudModel();
            m.ListScrollOffset = 50;
            int start = m.ClampListScroll(totalCount: 12, maxVisible: 8);
            Assert.Equal(4, start);
            Assert.Equal(4, m.ListScrollOffset);
        }

        [Fact]
        public void AdjustListScroll_pages_within_bounds()
        {
            var m = new CrewHudModel();
            m.ListScrollOffset = 0;
            m.AdjustListScroll(8, totalCount: 20, maxVisible: 8);
            Assert.Equal(8, m.ListScrollOffset);
            m.AdjustListScroll(8, totalCount: 20, maxVisible: 8);
            Assert.Equal(12, m.ListScrollOffset);
            m.AdjustListScroll(-8, totalCount: 20, maxVisible: 8);
            Assert.Equal(4, m.ListScrollOffset);
        }

        [Fact]
        public void BeginTrainConfirm_sets_screen()
        {
            var m = new CrewHudModel();
            m.Open(1);
            m.SelectedCrewId = "c1";
            m.BeginTrainConfirm();
            Assert.Equal(CrewHudScreen.TrainConfirm, m.Screen);
            Assert.Equal("c1", m.SelectedCrewId);
            m.WizardBack();
            Assert.Equal(CrewHudScreen.Home, m.Screen);
        }

        [Fact]
        public void FormatRosterDetail_shows_training_remaining()
        {
            var r = new CrewRecord
            {
                Status = CrewStatus.Unassigned,
                TrainingEndsUtcTicks = 10 * TimeSpan.TicksPerMinute
            };
            var text = CrewHudModel.FormatRosterDetail(r, 0);
            Assert.Equal("Training — 10m", text);
        }

        [Fact]
        public void CanStartTrain_false_when_max_or_training()
        {
            Assert.False(CrewHudModel.CanStartTrain(new CrewRecord { Stars = 5 }));
            Assert.False(CrewHudModel.CanStartTrain(new CrewRecord { Stars = 1, TrainingEndsUtcTicks = 9 }));
            Assert.True(CrewHudModel.CanStartTrain(new CrewRecord { Stars = 1, TrainingEndsUtcTicks = 0 }));
        }

        [Fact]
        public void Bulk_toggle_select_and_cap()
        {
            var m = new CrewHudModel();
            m.Open(1);
            m.SetBulkMode(true);
            Assert.True(m.BulkMode);
            for (int i = 0; i < CrewHudModel.BulkSelectionCap; i++)
                Assert.True(m.TryToggleBulkSelect(new CrewRecord { CrewId = "c" + i, Status = CrewStatus.Unassigned }));
            Assert.False(m.TryToggleBulkSelect(new CrewRecord { CrewId = "overflow", Status = CrewStatus.Unassigned }));
            Assert.Equal(CrewHudModel.BulkSelectionCap, m.BulkSelectedCrewIds.Count);
            Assert.True(m.BulkSelectionCapHit);
            Assert.False(m.TryToggleBulkSelect(new CrewRecord { CrewId = "c0", Status = CrewStatus.Seated, GridEntityId = 1 }));
        }

        [Fact]
        public void Bulk_map_ready_requires_seat_and_weapon_for_gunner()
        {
            var m = new CrewHudModel();
            m.Open(1);
            m.SetBulkMode(true);
            m.TryToggleBulkSelect(new CrewRecord { CrewId = "g", Status = CrewStatus.Unassigned, Role = CrewRole.Gunner });
            Assert.True(m.TryBeginBulkMap(id => new CrewRecord { CrewId = id, Status = CrewStatus.Unassigned, Role = CrewRole.Gunner }));
            Assert.Equal(CrewHudScreen.BulkMap, m.Screen);
            Assert.False(m.IsBulkMapReady(id => new CrewRecord { CrewId = id, Role = CrewRole.Gunner, Status = CrewStatus.Unassigned }));
            m.BeginBulkPickSeat(0);
            Assert.True(m.TrySetBulkSeat(10));
            Assert.Equal(CrewHudScreen.BulkMap, m.Screen);
            Assert.False(m.IsBulkMapReady(id => new CrewRecord { CrewId = id, Role = CrewRole.Gunner, Status = CrewStatus.Unassigned }));
            m.BeginBulkPickWeapon(0);
            Assert.True(m.TrySetBulkWeapon(20));
            Assert.True(m.IsBulkMapReady(id => new CrewRecord { CrewId = id, Role = CrewRole.Gunner, Status = CrewStatus.Unassigned }));
        }

        [Fact]
        public void Bulk_map_seat_only_role_ready_without_weapon()
        {
            var m = new CrewHudModel();
            m.Open(1);
            m.SetBulkMode(true);
            m.TryToggleBulkSelect(new CrewRecord { CrewId = "e", Status = CrewStatus.Unassigned, Role = CrewRole.Engineer });
            m.TryBeginBulkMap(id => new CrewRecord { CrewId = id, Status = CrewStatus.Unassigned, Role = CrewRole.Engineer });
            m.BeginBulkPickSeat(0);
            m.TrySetBulkSeat(11);
            Assert.True(m.IsBulkMapReady(id => new CrewRecord { CrewId = id, Role = CrewRole.Engineer, Status = CrewStatus.Unassigned }));
        }

        [Fact]
        public void Bulk_back_keeps_picks_close_clears()
        {
            var m = new CrewHudModel();
            m.Open(1);
            m.SetBulkMode(true);
            m.TryToggleBulkSelect(new CrewRecord { CrewId = "g", Status = CrewStatus.Unassigned, Role = CrewRole.Gunner });
            m.TryBeginBulkMap(id => new CrewRecord { CrewId = id, Status = CrewStatus.Unassigned, Role = CrewRole.Gunner });
            m.BeginBulkPickSeat(0);
            m.TrySetBulkSeat(10);
            m.BulkMapBackToHome();
            Assert.Equal(CrewHudScreen.Home, m.Screen);
            Assert.True(m.BulkMode);
            Assert.Equal(1, m.BulkSelectedCrewIds.Count);
            Assert.Equal(10L, m.BulkMapEntries[0].SeatEntityId);
            m.Close();
            Assert.False(m.BulkMode);
            Assert.Equal(0, m.BulkSelectedCrewIds.Count);
        }

        [Fact]
        public void Offship_focus_toggle_and_clear_selection()
        {
            var m = new CrewHudModel();
            m.Open(0);
            Assert.False(m.HasManagedGrid);
            Assert.False(m.HasFocusedGrid);
            m.SelectedCrewId = "keep-until-change";
            m.ToggleFocusedGrid(100);
            Assert.Equal(100, m.FocusedGridId);
            Assert.True(m.HasFocusedGrid);
            Assert.Null(m.SelectedCrewId);
            m.SelectedCrewId = "a";
            m.ToggleFocusedGrid(100);
            Assert.Equal(0, m.FocusedGridId);
            Assert.Null(m.SelectedCrewId);
            m.ToggleFocusedGrid(200);
            Assert.Equal(200, m.FocusedGridId);
            m.Close();
            Assert.Equal(0, m.FocusedGridId);
        }

        [Fact]
        public void CollectCrewedGridIds_unique_seated_only()
        {
            var roster = new List<CrewRecord>
            {
                new CrewRecord { CrewId = "u", Status = CrewStatus.Unassigned, GridEntityId = 0 },
                new CrewRecord { CrewId = "a", Status = CrewStatus.Seated, GridEntityId = 10 },
                new CrewRecord { CrewId = "b", Status = CrewStatus.Seated, GridEntityId = 20 },
                new CrewRecord { CrewId = "c", Status = CrewStatus.Seated, GridEntityId = 10 },
                new CrewRecord { CrewId = "d", Status = CrewStatus.Seated, GridEntityId = 0 },
            };
            var ids = CrewHudModel.CollectCrewedGridIds(roster);
            Assert.Equal(new long[] { 10, 20 }, ids.ToArray());
        }

        [Fact]
        public void TryBeginUnassign_allows_focused_offship()
        {
            var m = new CrewHudModel();
            m.Open(0);
            var seated = new CrewRecord { CrewId = "s", Status = CrewStatus.Seated, GridEntityId = 55 };
            Assert.False(m.TryBeginUnassignFromHome(seated));
            m.ToggleFocusedGrid(55);
            Assert.True(m.CanUnassignWithFocus(seated));
            Assert.True(m.TryBeginUnassignFromHome(seated));
            Assert.Equal(CrewHudScreen.UnassignPick, m.Screen);
            Assert.Equal("s", m.SelectedCrewId);
        }

        [Fact]
        public void TryBeginUnassign_rejects_wrong_focus_grid()
        {
            var m = new CrewHudModel();
            m.Open(0);
            m.ToggleFocusedGrid(1);
            var seated = new CrewRecord { CrewId = "s", Status = CrewStatus.Seated, GridEntityId = 2 };
            Assert.False(m.CanUnassignWithFocus(seated));
            Assert.False(m.TryBeginUnassignFromHome(seated));
        }

        [Fact]
        public void IsFocusStillValid_false_when_no_seated_on_focus()
        {
            var m = new CrewHudModel();
            m.Open(0);
            m.ToggleFocusedGrid(9);
            var roster = new List<CrewRecord>
            {
                new CrewRecord { CrewId = "u", Status = CrewStatus.Unassigned },
                new CrewRecord { CrewId = "s", Status = CrewStatus.Seated, GridEntityId = 8 },
            };
            Assert.False(m.IsFocusStillValid(roster));
            roster.Add(new CrewRecord { CrewId = "t", Status = CrewStatus.Seated, GridEntityId = 9 });
            Assert.True(m.IsFocusStillValid(roster));
        }
    }
}
