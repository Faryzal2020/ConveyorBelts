using ConveyorBelts.Sim;
using UnityEngine;
using Verse;

namespace ConveyorBelts
{
    /// <summary>Draws unspawned belt items at their interpolated positions (called from MapComponentDraw).</summary>
    public static class BeltRenderer
    {
        /// <summary>Returns the number of items drawn (in view and not fogged).</summary>
        public static int Draw(MapComponent_Belts comp)
        {
            if (comp.Store.Count == 0 || comp.map != Find.CurrentMap) return 0;
            int drawn = 0;
            var view = Find.CameraDriver.CurrentViewRect.ExpandedBy(1);
            var fog = comp.map.fogGrid;
            float y = AltitudeLayer.Item.AltitudeFor();
            foreach (var (cell, item) in comp.Sim.AllItems())
            {
                var c = cell.ToIntVec3();
                if (!view.Contains(c) || fog.IsFogged(c)) continue;
                if (!comp.Sim.TryGetBelt(cell, out var dir, out _)) continue;
                var t = comp.Store.Get(item.Handle);
                if (t == null) continue;
                var (dx, dz) = BeltSim.LocalPos(item.EnteredFrom, dir, item.Progress);
                var pos = c.ToVector3Shifted();
                t.DrawNowAt(new Vector3(pos.x + dx, y, pos.z + dz));
                drawn++;
            }
            return drawn;
        }
    }
}
