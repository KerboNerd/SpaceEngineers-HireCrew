using System;
using System.Collections.Generic;

namespace HireCrew
{
    public enum CrewHudScreen
    {
        Home = 0,
        AssignCrew = 1,
        AssignSeat = 2,
        AssignWeapon = 3,
        DismissPick = 4,
        QuartersCrew = 5,
        QuartersSlots = 6,
        QuartersPickBlock = 7,
        UnassignPick = 8,
        TrainConfirm = 9,
        CancelTrainConfirm = 10,
        BulkMap = 11,
        BulkPickSeat = 12,
        BulkPickWeapon = 13
    }

    public sealed class BulkMapEntry
    {
        public string CrewId;
        public long SeatEntityId;
        public long WeaponEntityId;
    }

    public sealed class CrewHudModel
    {
        public const int BulkSelectionCap = 20;

        public bool IsOpen { get; private set; }
        public CrewHudScreen Screen { get; private set; }
        public long GridEntityId { get; private set; }
        /// <summary>Off-seat grid focus for viewing/unassign; never used as local manage target.</summary>
        public long FocusedGridId { get; private set; }
        public string SelectedCrewId { get; set; }
        public long SelectedSeatEntityId { get; set; }
        public long SelectedWeaponEntityId { get; set; }
        public AmenityKind SelectedAmenityKind { get; set; }
        public int ListScrollOffset { get; set; }

        public bool BulkMode { get; private set; }
        public int BulkEditIndex { get; private set; }
        public bool BulkSelectionCapHit { get; private set; }

        private readonly List<string> _bulkSelectedCrewIds = new List<string>();
        private readonly List<BulkMapEntry> _bulkMapEntries = new List<BulkMapEntry>();

        public List<string> BulkSelectedCrewIds { get { return _bulkSelectedCrewIds; } }
        public List<BulkMapEntry> BulkMapEntries { get { return _bulkMapEntries; } }

        /// <summary>True when opened from a managed seat; false for off-ship (grid picker / pool).</summary>
        public bool HasManagedGrid { get { return GridEntityId != 0; } }

        public bool HasFocusedGrid { get { return FocusedGridId != 0; } }

        public void Open(long gridEntityId)
        {
            IsOpen = true;
            GridEntityId = gridEntityId;
            FocusedGridId = 0;
            Screen = CrewHudScreen.Home;
            ListScrollOffset = 0;
            ClearBulkState();
        }

        public void Close()
        {
            IsOpen = false;
            Screen = CrewHudScreen.Home;
            FocusedGridId = 0;
            ListScrollOffset = 0;
            ClearBulkState();
        }

        public void ClearFocusedGrid()
        {
            if (FocusedGridId == 0) return;
            FocusedGridId = 0;
            SelectedCrewId = null;
        }

        public void ToggleFocusedGrid(long gridEntityId)
        {
            if (gridEntityId == 0) return;
            long next = FocusedGridId == gridEntityId ? 0L : gridEntityId;
            if (next == FocusedGridId) return;
            FocusedGridId = next;
            SelectedCrewId = null;
        }

        public static List<long> CollectCrewedGridIds(IList<CrewRecord> roster)
        {
            var ids = new List<long>();
            if (roster == null) return ids;
            for (int i = 0; i < roster.Count; i++)
            {
                var r = roster[i];
                if (r == null || r.Status != CrewStatus.Seated || r.GridEntityId == 0) continue;
                bool seen = false;
                for (int j = 0; j < ids.Count; j++)
                {
                    if (ids[j] == r.GridEntityId)
                    {
                        seen = true;
                        break;
                    }
                }
                if (!seen) ids.Add(r.GridEntityId);
            }
            return ids;
        }

        public bool IsFocusStillValid(IList<CrewRecord> roster)
        {
            if (FocusedGridId == 0) return true;
            if (roster == null) return false;
            for (int i = 0; i < roster.Count; i++)
            {
                var r = roster[i];
                if (r != null && r.Status == CrewStatus.Seated && r.GridEntityId == FocusedGridId)
                    return true;
            }
            return false;
        }

        public bool CanUnassignWithFocus(CrewRecord r)
        {
            return CanUnassignHome(r) && HasFocusedGrid && r != null && r.GridEntityId == FocusedGridId;
        }

        public void Toggle(long gridEntityId)
        {
            if (IsOpen) Close();
            else Open(gridEntityId);
        }

        public void GoHome()
        {
            Screen = CrewHudScreen.Home;
            ListScrollOffset = 0;
        }

        public static bool CanAssignHome(CrewRecord r)
        {
            return r != null && !CrewConfig.IsTraining(r) && r.Status == CrewStatus.Unassigned;
        }

        public static bool CanUnassignHome(CrewRecord r)
        {
            return r != null && !CrewConfig.IsTraining(r) && r.Status == CrewStatus.Seated;
        }

        public static bool CanQuartersHome(CrewRecord r)
        {
            return r != null && !CrewConfig.IsTraining(r) && r.Status == CrewStatus.Seated && r.GridEntityId != 0;
        }

        public static bool CanDismissHome(CrewRecord r)
        {
            return r != null;
        }

        public static string FormatHomeContext(CrewRecord r)
        {
            return FormatHomeContext(r, true);
        }

        public static string FormatHomeContext(CrewRecord r, bool hasManagedGrid)
        {
            if (r == null) return "Select a crew member";
            string name = string.IsNullOrEmpty(r.DisplayName) ? CrewConfig.RoleLabel(r.Role) : r.DisplayName;
            if (CrewConfig.IsTraining(r))
                return "Selected: " + name + " — Training (actions locked)";
            if (!hasManagedGrid)
            {
                if (r.Status == CrewStatus.Unassigned)
                    return "Selected: " + name + " — Unassigned · Train or Dismiss";
                if (r.Status == CrewStatus.Seated)
                    return "Selected: " + name + " — Stationed · Train or Dismiss";
                return "Selected: " + name + " · Train or Dismiss";
            }
            if (r.Status == CrewStatus.Unassigned)
                return "Selected: " + name + " — Unassigned · Assign or Train";
            if (r.Status == CrewStatus.Seated)
                return "Selected: " + name + " — Stationed · Unassign, Quarters, or Dismiss";
            return "Selected: " + name;
        }

        public static bool IsGridBoundScreen(CrewHudScreen screen)
        {
            return screen == CrewHudScreen.AssignCrew
                || screen == CrewHudScreen.AssignSeat
                || screen == CrewHudScreen.AssignWeapon
                || screen == CrewHudScreen.UnassignPick
                || screen == CrewHudScreen.QuartersCrew
                || screen == CrewHudScreen.QuartersSlots
                || screen == CrewHudScreen.QuartersPickBlock
                || screen == CrewHudScreen.BulkMap
                || screen == CrewHudScreen.BulkPickSeat
                || screen == CrewHudScreen.BulkPickWeapon;
        }

        public void SetBulkMode(bool on)
        {
            if (on)
            {
                if (!HasManagedGrid) return;
                BulkMode = true;
                BulkSelectionCapHit = false;
                return;
            }
            ClearBulkState();
            if (Screen == CrewHudScreen.BulkMap
                || Screen == CrewHudScreen.BulkPickSeat
                || Screen == CrewHudScreen.BulkPickWeapon)
            {
                Screen = CrewHudScreen.Home;
                ListScrollOffset = 0;
            }
        }

        public bool IsBulkSelected(string crewId)
        {
            if (string.IsNullOrEmpty(crewId)) return false;
            for (int i = 0; i < _bulkSelectedCrewIds.Count; i++)
                if (string.Equals(_bulkSelectedCrewIds[i], crewId, StringComparison.Ordinal))
                    return true;
            return false;
        }

        public bool TryToggleBulkSelect(CrewRecord r)
        {
            BulkSelectionCapHit = false;
            if (!BulkMode || !HasManagedGrid || !CanAssignHome(r) || string.IsNullOrEmpty(r.CrewId))
                return false;

            for (int i = 0; i < _bulkSelectedCrewIds.Count; i++)
            {
                if (string.Equals(_bulkSelectedCrewIds[i], r.CrewId, StringComparison.Ordinal))
                {
                    _bulkSelectedCrewIds.RemoveAt(i);
                    RemoveBulkMapEntry(r.CrewId);
                    return true;
                }
            }

            if (_bulkSelectedCrewIds.Count >= BulkSelectionCap)
            {
                BulkSelectionCapHit = true;
                return false;
            }

            _bulkSelectedCrewIds.Add(r.CrewId);
            return true;
        }

        public void ClearBulkSelection()
        {
            _bulkSelectedCrewIds.Clear();
            _bulkMapEntries.Clear();
            BulkEditIndex = 0;
            BulkSelectionCapHit = false;
        }

        public bool TryBeginBulkMap(Func<string, CrewRecord> resolve)
        {
            if (!BulkMode || !HasManagedGrid || _bulkSelectedCrewIds.Count == 0)
                return false;

            PruneBulkSelection(resolve);

            if (_bulkSelectedCrewIds.Count == 0)
                return false;

            // Preserve picks for crew still selected; drop others; append missing.
            var keep = new List<BulkMapEntry>();
            for (int i = 0; i < _bulkSelectedCrewIds.Count; i++)
            {
                string id = _bulkSelectedCrewIds[i];
                BulkMapEntry existing = FindBulkMapEntry(id);
                if (existing != null)
                    keep.Add(existing);
                else
                    keep.Add(new BulkMapEntry { CrewId = id, SeatEntityId = 0, WeaponEntityId = 0 });
            }
            _bulkMapEntries.Clear();
            _bulkMapEntries.AddRange(keep);
            BulkEditIndex = 0;
            Screen = CrewHudScreen.BulkMap;
            ListScrollOffset = 0;
            return true;
        }

        public void SelectBulkMapRow(int mapIndex)
        {
            if (mapIndex < 0 || mapIndex >= _bulkMapEntries.Count) return;
            BulkEditIndex = mapIndex;
        }

        public void BeginBulkPickSeat(int mapIndex)
        {
            if (mapIndex < 0 || mapIndex >= _bulkMapEntries.Count) return;
            BulkEditIndex = mapIndex;
            Screen = CrewHudScreen.BulkPickSeat;
            ListScrollOffset = 0;
        }

        public void BeginBulkPickWeapon(int mapIndex)
        {
            if (mapIndex < 0 || mapIndex >= _bulkMapEntries.Count) return;
            BulkEditIndex = mapIndex;
            Screen = CrewHudScreen.BulkPickWeapon;
            ListScrollOffset = 0;
        }

        public bool TrySetBulkSeat(long seatId)
        {
            if (Screen != CrewHudScreen.BulkPickSeat) return false;
            if (BulkEditIndex < 0 || BulkEditIndex >= _bulkMapEntries.Count) return false;
            if (seatId != 0)
            {
                for (int i = 0; i < _bulkMapEntries.Count; i++)
                {
                    if (i == BulkEditIndex) continue;
                    if (_bulkMapEntries[i].SeatEntityId == seatId)
                        _bulkMapEntries[i].SeatEntityId = 0;
                }
            }
            _bulkMapEntries[BulkEditIndex].SeatEntityId = seatId;
            Screen = CrewHudScreen.BulkMap;
            ListScrollOffset = 0;
            return true;
        }

        public bool TrySetBulkWeapon(long weaponId)
        {
            if (Screen != CrewHudScreen.BulkPickWeapon) return false;
            if (BulkEditIndex < 0 || BulkEditIndex >= _bulkMapEntries.Count) return false;
            if (weaponId != 0)
            {
                for (int i = 0; i < _bulkMapEntries.Count; i++)
                {
                    if (i == BulkEditIndex) continue;
                    if (_bulkMapEntries[i].WeaponEntityId == weaponId)
                        _bulkMapEntries[i].WeaponEntityId = 0;
                }
            }
            _bulkMapEntries[BulkEditIndex].WeaponEntityId = weaponId;
            Screen = CrewHudScreen.BulkMap;
            ListScrollOffset = 0;
            return true;
        }

        public void ReturnToBulkMap()
        {
            Screen = CrewHudScreen.BulkMap;
            ListScrollOffset = 0;
        }

        public void BulkMapBackToHome()
        {
            Screen = CrewHudScreen.Home;
            ListScrollOffset = 0;
        }

        public bool IsBulkMapReady(Func<string, CrewRecord> resolve)
        {
            if (_bulkMapEntries.Count == 0) return false;
            var seats = new HashSet<long>();
            var weapons = new HashSet<long>();
            for (int i = 0; i < _bulkMapEntries.Count; i++)
            {
                var e = _bulkMapEntries[i];
                if (e == null || string.IsNullOrEmpty(e.CrewId)) return false;
                if (e.SeatEntityId == 0) return false;
                if (!seats.Add(e.SeatEntityId)) return false;

                CrewRecord crew = resolve != null ? resolve(e.CrewId) : null;
                bool needsWeapon = crew != null && CrewConfig.NeedsWeapon(crew.Role);
                if (needsWeapon)
                {
                    if (e.WeaponEntityId == 0) return false;
                    if (!weapons.Add(e.WeaponEntityId)) return false;
                }
            }
            return true;
        }

        public HashSet<long> GetBulkReservedSeats(int exceptIndex)
        {
            var set = new HashSet<long>();
            for (int i = 0; i < _bulkMapEntries.Count; i++)
            {
                if (i == exceptIndex) continue;
                long id = _bulkMapEntries[i].SeatEntityId;
                if (id != 0) set.Add(id);
            }
            return set;
        }

        public HashSet<long> GetBulkReservedWeapons(int exceptIndex)
        {
            var set = new HashSet<long>();
            for (int i = 0; i < _bulkMapEntries.Count; i++)
            {
                if (i == exceptIndex) continue;
                long id = _bulkMapEntries[i].WeaponEntityId;
                if (id != 0) set.Add(id);
            }
            return set;
        }

        public void PruneBulkSelection(Func<string, CrewRecord> resolve)
        {
            if (_bulkSelectedCrewIds.Count == 0) return;
            for (int i = _bulkSelectedCrewIds.Count - 1; i >= 0; i--)
            {
                string id = _bulkSelectedCrewIds[i];
                CrewRecord r = resolve != null ? resolve(id) : null;
                if (!CanAssignHome(r))
                {
                    _bulkSelectedCrewIds.RemoveAt(i);
                    RemoveBulkMapEntry(id);
                }
            }
        }

        private void ClearBulkState()
        {
            BulkMode = false;
            BulkEditIndex = 0;
            BulkSelectionCapHit = false;
            _bulkSelectedCrewIds.Clear();
            _bulkMapEntries.Clear();
        }

        private BulkMapEntry FindBulkMapEntry(string crewId)
        {
            for (int i = 0; i < _bulkMapEntries.Count; i++)
                if (_bulkMapEntries[i] != null &&
                    string.Equals(_bulkMapEntries[i].CrewId, crewId, StringComparison.Ordinal))
                    return _bulkMapEntries[i];
            return null;
        }

        private void RemoveBulkMapEntry(string crewId)
        {
            for (int i = _bulkMapEntries.Count - 1; i >= 0; i--)
                if (_bulkMapEntries[i] != null &&
                    string.Equals(_bulkMapEntries[i].CrewId, crewId, StringComparison.Ordinal))
                    _bulkMapEntries.RemoveAt(i);
        }

        public bool TryBeginAssignFromHome(CrewRecord selected)
        {
            if (!HasManagedGrid || !CanAssignHome(selected)) return false;
            SelectedCrewId = selected.CrewId;
            SelectedSeatEntityId = 0;
            SelectedWeaponEntityId = 0;
            Screen = CrewHudScreen.AssignSeat;
            ListScrollOffset = 0;
            return true;
        }

        public bool TryBeginQuartersFromHome(CrewRecord selected)
        {
            if (!HasManagedGrid || !CanQuartersHome(selected)) return false;
            SelectedCrewId = selected.CrewId;
            Screen = CrewHudScreen.QuartersSlots;
            ListScrollOffset = 0;
            return true;
        }

        public bool TryBeginUnassignFromHome(CrewRecord selected)
        {
            bool ok = (HasManagedGrid && CanUnassignHome(selected)) || CanUnassignWithFocus(selected);
            if (!ok) return false;
            SelectedCrewId = selected.CrewId;
            Screen = CrewHudScreen.UnassignPick;
            ListScrollOffset = 0;
            return true;
        }

        public bool TryBeginDismissFromHome(CrewRecord selected)
        {
            if (!CanDismissHome(selected)) return false;
            SelectedCrewId = selected.CrewId;
            Screen = CrewHudScreen.DismissPick;
            ListScrollOffset = 0;
            return true;
        }

        public void BeginTrainConfirm()
        {
            Screen = CrewHudScreen.TrainConfirm;
            ListScrollOffset = 0;
        }

        public void BeginCancelTrainConfirm()
        {
            Screen = CrewHudScreen.CancelTrainConfirm;
            ListScrollOffset = 0;
        }

        public void WizardNextFromCrew() { Screen = CrewHudScreen.AssignSeat; ListScrollOffset = 0; }
        public void WizardNextFromSeat() { Screen = CrewHudScreen.AssignWeapon; ListScrollOffset = 0; }
        public void QuartersNextFromCrew() { Screen = CrewHudScreen.QuartersSlots; ListScrollOffset = 0; }

        public void OpenAmenityPicker(AmenityKind kind)
        {
            SelectedAmenityKind = kind;
            Screen = CrewHudScreen.QuartersPickBlock;
            ListScrollOffset = 0;
        }

        public void ReturnToQuartersSlots()
        {
            Screen = CrewHudScreen.QuartersSlots;
            ListScrollOffset = 0;
        }

        public void WizardBack()
        {
            if (Screen == CrewHudScreen.AssignWeapon) { Screen = CrewHudScreen.AssignSeat; ListScrollOffset = 0; return; }
            if (Screen == CrewHudScreen.AssignSeat) { GoHome(); return; }
            if (Screen == CrewHudScreen.AssignCrew) { GoHome(); return; }
            if (Screen == CrewHudScreen.DismissPick) { GoHome(); return; }
            if (Screen == CrewHudScreen.UnassignPick) { GoHome(); return; }
            if (Screen == CrewHudScreen.TrainConfirm) { GoHome(); return; }
            if (Screen == CrewHudScreen.CancelTrainConfirm) { GoHome(); return; }
            if (Screen == CrewHudScreen.QuartersPickBlock) { Screen = CrewHudScreen.QuartersSlots; ListScrollOffset = 0; return; }
            if (Screen == CrewHudScreen.QuartersSlots) { GoHome(); return; }
            if (Screen == CrewHudScreen.QuartersCrew) { GoHome(); return; }
            if (Screen == CrewHudScreen.BulkPickSeat || Screen == CrewHudScreen.BulkPickWeapon)
            {
                ReturnToBulkMap();
                return;
            }
            if (Screen == CrewHudScreen.BulkMap)
            {
                BulkMapBackToHome();
                return;
            }
        }

        public bool TrySelectListIndex(int indexZeroBased, int listCount)
        {
            if (indexZeroBased < 0 || indexZeroBased >= listCount) return false;
            return true;
        }

        /// <summary>Clamps <see cref="ListScrollOffset"/> into [0, max(0, totalCount - maxVisible)].</summary>
        public int ClampListScroll(int totalCount, int maxVisible)
        {
            int maxOffset = totalCount > maxVisible ? totalCount - maxVisible : 0;
            int start = ListScrollOffset;
            if (start < 0) start = 0;
            if (start > maxOffset) start = maxOffset;
            ListScrollOffset = start;
            return start;
        }

        public void AdjustListScroll(int deltaRows, int totalCount, int maxVisible)
        {
            if (deltaRows == 0) return;
            int maxOffset = totalCount > maxVisible ? totalCount - maxVisible : 0;
            int next = ListScrollOffset + deltaRows;
            if (next < 0) next = 0;
            if (next > maxOffset) next = maxOffset;
            ListScrollOffset = next;
        }

        public static List<CrewRecord> RosterForGrid(IEnumerable<CrewRecord> all, long gridId)
        {
            var list = new List<CrewRecord>();
            if (all == null) return list;
            foreach (var r in all)
                if (r != null && r.GridEntityId == gridId)
                    list.Add(r);
            return list;
        }

        public static List<T> FilterAvailableSeats<T>(IEnumerable<T> seats, IEnumerable<CrewRecord> gridCrew, Func<T, long> idOf)
        {
            var claimed = new HashSet<long>();
            if (gridCrew != null)
                foreach (var c in gridCrew)
                    if (c != null && c.Status == CrewStatus.Seated && c.SeatEntityId.HasValue)
                        claimed.Add(c.SeatEntityId.Value);
            var result = new List<T>();
            if (seats == null) return result;
            foreach (var s in seats)
                if (!claimed.Contains(idOf(s)))
                    result.Add(s);
            return result;
        }

        public static List<T> FilterAvailableWeapons<T>(IEnumerable<T> weapons, IEnumerable<CrewRecord> gridCrew, Func<T, long> idOf)
        {
            var claimed = new HashSet<long>();
            if (gridCrew != null)
                foreach (var c in gridCrew)
                    if (c != null && c.Status == CrewStatus.Seated && c.WeaponEntityId.HasValue)
                        claimed.Add(c.WeaponEntityId.Value);
            var result = new List<T>();
            if (weapons == null) return result;
            foreach (var w in weapons)
                if (!claimed.Contains(idOf(w)))
                    result.Add(w);
            return result;
        }

        public static List<T> FilterAvailableAmenities<T>(
            IEnumerable<T> blocks,
            IEnumerable<CrewRecord> gridCrew,
            string currentCrewId,
            AmenityKind kind,
            Func<T, long> idOf,
            Func<T, AmenityKind, bool> matchesKind)
        {
            var claimed = new HashSet<long>();
            if (gridCrew != null)
            {
                foreach (var c in gridCrew)
                {
                    if (c == null || c.Status != CrewStatus.Seated) continue;
                    if (!string.IsNullOrEmpty(currentCrewId) &&
                        string.Equals(c.CrewId, currentCrewId, StringComparison.Ordinal))
                        continue;
                    if (c.BedEntityId.HasValue) claimed.Add(c.BedEntityId.Value);
                    if (c.ToiletEntityId.HasValue) claimed.Add(c.ToiletEntityId.Value);
                    if (c.ShowerEntityId.HasValue) claimed.Add(c.ShowerEntityId.Value);
                }
            }

            var result = new List<T>();
            if (blocks == null) return result;
            foreach (var b in blocks)
            {
                if (!matchesKind(b, kind)) continue;
                long id = idOf(b);
                if (claimed.Contains(id)) continue;
                result.Add(b);
            }
            return result;
        }

        public static string FormatRosterLine(CrewRecord r)
        {
            if (r == null) return "";
            var name = string.IsNullOrEmpty(r.DisplayName)
                ? CrewConfig.RoleLabel(r.Role)
                : r.DisplayName;
            var detail = FormatRosterDetail(r);
            if (string.IsNullOrEmpty(detail))
                return name;
            return name + " \u2014 " + detail;
        }

        public static bool CanStartTrain(CrewRecord r)
        {
            return r != null && !CrewConfig.IsTraining(r) && r.Stars < CrewConfig.MaxStars;
        }

        public static bool CanCancelTrain(CrewRecord r)
        {
            return CrewConfig.IsTraining(r);
        }

        public static string FormatTrainRemaining(long endsUtcTicks, long utcNowTicks)
        {
            long left = endsUtcTicks - utcNowTicks;
            if (left <= 0) return "0m";
            int totalMins = (int)(left / TimeSpan.TicksPerMinute);
            if (totalMins < 1) totalMins = 1;
            if (totalMins >= 60)
            {
                int h = totalMins / 60;
                int m = totalMins % 60;
                if (m == 0) return h + "h";
                return h + "h " + m + "m";
            }
            return totalMins + "m";
        }

        public static string FormatRosterDetail(CrewRecord r)
        {
            return FormatRosterDetail(r, DateTime.UtcNow.Ticks);
        }

        public static string FormatRosterDetail(CrewRecord r, long utcNowTicks)
        {
            if (r == null) return "";
            if (CrewConfig.IsTraining(r))
                return "Training \u2014 " + FormatTrainRemaining(r.TrainingEndsUtcTicks, utcNowTicks);
            if (r.Status != CrewStatus.Seated || r.GridEntityId == 0)
                return "Unassigned";

            var marks = CrewAmenities.FormatAmenityMarks(r);
            var eff = CrewAmenities.GetEfficiencyPercent(r);
            if (r.Role == CrewRole.Engineer)
            {
                var powerPct = (int)Math.Round(CrewConfig.GetPowerBonus(r.Stars, CrewAmenities.GetEfficiency(r)) * 100f);
                if (string.IsNullOrEmpty(marks))
                    return "Assigned \u00B7 +" + powerPct + "% pwr";
                return "Assigned \u00B7 " + marks + " \u00B7 +" + powerPct + "% pwr";
            }

            if (string.IsNullOrEmpty(marks))
                return "Assigned \u00B7 " + eff + "%";
            return "Assigned \u00B7 " + marks + " \u00B7 " + eff + "%";
        }

        public static CrewRecord FindCrew(IEnumerable<CrewRecord> all, string crewId)
        {
            if (all == null || string.IsNullOrEmpty(crewId)) return null;
            foreach (var r in all)
                if (r != null && string.Equals(r.CrewId, crewId, StringComparison.Ordinal))
                    return r;
            return null;
        }
    }
}
