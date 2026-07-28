using System.Collections.Generic;
using Sandbox.Common.ObjectBuilders;
using Sandbox.ModAPI;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.ObjectBuilders;

namespace HireCrew
{
    /// <summary>
    /// Hides the character subpart on placement; shows it while HireCrew has crew assigned
    /// and no live ambient bot is presenting that seat.
    /// </summary>
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_TerminalBlock), false, "HC_CrewStation_1")]
    public sealed class CrewStationLogic : MyGameLogicComponent
    {
        public const string CharacterSubpartName = "character";
        public const string BlockSubtype = "HC_CrewStation_1";

        private static readonly HashSet<CrewStationLogic> Active = new HashSet<CrewStationLogic>();

        private IMyTerminalBlock _block;
        private bool? _lastVisible;

        public static bool IsCrewStation(IMyTerminalBlock block)
        {
            // BlockDefinition is a struct (SerializableDefinitionId), not a reference type.
            return block != null && block.BlockDefinition.SubtypeName == BlockSubtype;
        }

        /// <summary>Cockpits (non-amenity, non-main) plus HireCrew crew stations.</summary>
        public static bool IsAssignableSeat(IMyTerminalBlock block)
        {
            if (block == null || block.MarkedForClose)
                return false;

            if (IsCrewStation(block))
                return true;

            var cockpit = block as IMyCockpit;
            if (cockpit == null)
                return false;
            if (cockpit.IsMainCockpit)
                return false;
            if (CrewAmenities.DetectKind(cockpit).HasValue)
                return false;
            return true;
        }

        public static bool IsSeatOccupiedByPlayer(IMyTerminalBlock block)
        {
            var seat = block as IMyShipController;
            if (seat == null)
                return false;
            var cockpit = seat as IMyCockpit;
            return (cockpit != null && cockpit.IsOccupied) || seat.Pilot != null;
        }

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            // Delay first hide until after the block/subpart render objects are created.
            NeedsUpdate = MyEntityUpdateEnum.BEFORE_NEXT_FRAME | MyEntityUpdateEnum.EACH_10TH_FRAME;
        }

        public override void OnAddedToScene()
        {
            base.OnAddedToScene();
            _block = Entity as IMyTerminalBlock;
            Active.Add(this);
            _lastVisible = null;
        }

        public override void OnRemovedFromScene()
        {
            Active.Remove(this);
            _block = null;
            _lastVisible = null;
            base.OnRemovedFromScene();
        }

        public override void Close()
        {
            Active.Remove(this);
            _block = null;
            _lastVisible = null;
            base.Close();
        }

        public override void UpdateOnceBeforeFrame()
        {
            // First real opportunity: hide unless already assigned (world load restore).
            Refresh(force: true);
        }

        public override void UpdateBeforeSimulation10()
        {
            Refresh(force: false);
        }

        /// <summary>Call after roster assign/dismiss/sync so character subparts update immediately.</summary>
        public static void RefreshAll()
        {
            foreach (var logic in Active)
            {
                if (logic != null)
                    logic.Refresh(force: true);
            }
        }

        private void Refresh(bool force)
        {
            if (_block == null || _block.MarkedForClose)
                return;

            SetCharacterVisible(ShouldShowCharacterSubpart(_block.EntityId), force);
        }

        /// <summary>
        /// Subpart stands in when crew is seated but no live ambient CharacterEntityId is active.
        /// </summary>
        private static bool ShouldShowCharacterSubpart(long seatEntityId)
        {
            var session = CrewSession.Instance;
            if (session == null || session.Store == null)
                return false;

            foreach (var crew in session.Store.All)
            {
                if (crew == null || crew.Status != CrewStatus.Seated)
                    continue;
                if (!crew.SeatEntityId.HasValue || crew.SeatEntityId.Value != seatEntityId)
                    continue;
                if (crew.CharacterEntityId.HasValue)
                    return false;
                return true;
            }
            return false;
        }

        private void SetCharacterVisible(bool visible, bool force)
        {
            if (!force && _lastVisible.HasValue && _lastVisible.Value == visible)
                return;

            MyEntitySubpart subpart;
            if (!TryGetCharacterSubpart(out subpart) || subpart == null || subpart.Render == null)
            {
                // Subpart not ready yet; keep retrying on the 10-tick update.
                _lastVisible = null;
                return;
            }

            // Visible alone is not enough if hide ran before AddRenderObjects:
            // UpdateRenderObject(true) recreates missing render objects when showing.
            subpart.Render.Visible = visible;
            subpart.Render.UpdateRenderObject(visible);
            _lastVisible = visible;
        }

        private bool TryGetCharacterSubpart(out MyEntitySubpart subpart)
        {
            subpart = null;
            var ent = Entity ?? _block;
            if (ent == null)
                return false;

            // Name is without the "subpart_" prefix from the model empty.
            return ent.TryGetSubpart(CharacterSubpartName, out subpart) && subpart != null;
        }
    }
}
