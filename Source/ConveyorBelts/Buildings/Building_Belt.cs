using System.Collections.Generic;
using System.Text;
using ConveyorBelts.Sim;
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
            MapComponent_Belts.For(map).Sim.AddBelt(Position.ToCell(), Rotation.ToDir(), Tier);
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
                items.Append(t != null ? t.Label : "?").Append(" (").Append(it.Progress.ToString("0.00")).Append(')');
            }
            if (items.Length == 0) items.Append("CB_NoItems".Translate());

            if (sb.Length > 0) sb.AppendLine();
            sb.Append("CB_BeltInspect".Translate(("CB_Tier" + Tier).Translate(), tiles, state, items.ToString()));
            return sb.ToString();
        }
    }
}
