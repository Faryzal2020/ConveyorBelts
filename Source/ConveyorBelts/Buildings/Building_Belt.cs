using System.Collections.Generic;
using System.Text;
using ConveyorBelts.Sim;
using RimWorld;
using Verse;

namespace ConveyorBelts
{
    public class Building_Belt : Building
    {
        /// <summary>Set by an in-place upgrade (Task 12): the belt leaves without unregistering or dropping items.</summary>
        internal bool swappingOut = false;

        public int Tier => def.GetModExtension<BeltTierExtension>()?.tier ?? Tiers.Basic;

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            // AddBelt on a cell the sim already knows only changes direction and tier: the cell's items stay.
            MapComponent_Belts.For(map).Sim.AddBelt(Position.ToCell(), Rotation.ToDir(), Tier);
            if (!respawningAfterLoad) SwapOutOldBelts(map);
        }

        /// <summary>
        /// In-place upgrade (PLAN.md §5.11): belts are not edifices, so neither the upgrade frame nor this
        /// finished belt wiped the belt already on the cell (it kept running during construction). Remove it
        /// now, refunded like vanilla deconstruction, without unregistering it from the sim or dropping its
        /// items: the cell keeps its items and takes this belt's direction (the upgrade blueprint was placed
        /// with the old belt's rotation) and tier.
        /// </summary>
        void SwapOutOldBelts(Map map)
        {
            List<Building_Belt> old = null;
            foreach (var t in Position.GetThingList(map))
                if (t is Building_Belt b && b != this && !b.swappingOut) (old ??= new List<Building_Belt>()).Add(b);
            if (old == null) return;
            bool playing = Current.ProgramState == ProgramState.Playing;
            bool wasSelected = false;
            foreach (var b in old)
            {
                wasSelected |= playing && Find.Selector.IsSelected(b);
                b.swappingOut = true;
                b.Destroy(DestroyMode.Deconstruct);
            }
            if (wasSelected && !Find.Selector.IsSelected(this)) Find.Selector.Select(this, playSound: false, forceDesignatorDeselect: false);
        }

        public override void DeSpawn(DestroyMode mode = DestroyMode.Vanish)
        {
            var map = Map;
            var cell = Position;
            var comp = MapComponent_Belts.For(map);
            List<BeltItem> dropped = null;
            if (!swappingOut && comp != null)
            {
                dropped = new List<BeltItem>();
                comp.Sim.RemoveBelt(cell.ToCell(), dropped);
            }
            base.DeSpawn(mode);
            if (dropped != null && dropped.Count > 0) comp.DropItems(cell, dropped);
        }

        /// <summary>
        /// Vanilla 1.7 (WorkGiver_DeconstructForBlueprint) has pawns deconstruct any building under a blueprint that
        /// can replace it before building that blueprint. For a belt that would drop its items and cut the line for
        /// the whole upgrade, so a belt with a planned other-tier belt on its cell is not deconstructible: it keeps
        /// running and Building_Belt.SpawnSetup swaps it out when the new belt is finished.
        /// </summary>
        public override AcceptanceReport DeconstructibleBy(Faction faction)
        {
            if (Spawned && BeltGizmos.PlannedReplacement(this) is ThingDef planned)
                return "CB_BeingUpgraded".Translate();
            return base.DeconstructibleBy(faction);
        }

        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (var g in base.GetGizmos()) yield return g;
            foreach (var g in BeltGizmos.For(this)) yield return g;
        }

        public override string GetInspectString()
        {
            var sb = new StringBuilder(base.GetInspectString());
            var comp = MapComponent_Belts.For(Map);
            if (comp == null) return sb.ToString();
            var cell = Position.ToCell();
            int line = comp.Sim.LineIdAt(cell);
            int tiles = comp.Sim.LineCells(line).Count;
            string state = (comp.Sim.IsLineRunning(line) ? "CB_Running" : "CB_Stopped").Translate();

            var items = new StringBuilder();
            foreach (var it in comp.Sim.ItemsAt(cell))
            {
                if (items.Length > 0) items.Append(", ");
                var t = comp.Store.Get(it.Handle);
                items.Append(t != null ? t.Label : "?");
                if (Prefs.DevMode) items.Append(" (").Append(it.Progress.ToString("0.00")).Append(')');
            }
            if (items.Length == 0) items.Append("CB_None".Translate());

            if (sb.Length > 0) sb.AppendLine();
            sb.Append("CB_BeltInspect".Translate(("CB_Tier" + Tier).Translate(), tiles, state, items.ToString()));
            return sb.ToString();
        }
    }
}
