using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace ConveyorBelts
{
    /// <summary>The v1 adapters, in priority order, and the lookup of the machine a pusher faces.</summary>
    public static class PushAdapters
    {
        public static readonly IPushAdapter[] All =
        {
            new FrameAdapter(), new RefuelableAdapter(), new BarrelAdapter(), new StorageAdapter(),
        };

        public static IPushAdapter For(Thing target)
        {
            if (target == null || target.Destroyed) return null;
            foreach (var a in All) if (a.Applies(target)) return a;
            return null;
        }

        /// <summary>The thing at cell that some adapter can feed (adapter order decides between several), or null.</summary>
        public static Thing FindTarget(IntVec3 cell, Map map)
        {
            if (map == null || !cell.InBounds(map)) return null;
            var list = map.thingGrid.ThingsListAtFast(cell);
            foreach (var a in All)
                for (int i = 0; i < list.Count; i++)
                    if (a.Applies(list[i])) return list[i];
            return null;
        }

        internal static string UpTo(ThingDef def, int n) => "CB_PusherUpTo".Translate(def.LabelCap, n);
    }

    /// <summary>Construction frames: the materials still missing.</summary>
    public class FrameAdapter : IPushAdapter
    {
        public bool Applies(Thing target) => target is Frame;

        public bool EverAccepts(Thing target, ThingDef def)
            => ((Frame)target).TotalMaterialCost().Any(c => c.thingDef == def);

        public int CanAccept(Thing target, Thing item) => Mathf.Max(0, ((Frame)target).ThingCountNeeded(item.def));

        public void Accept(Thing target, Thing item) => ((Frame)target).resourceContainer.TryAdd(item);

        public string DescribeAcceptsNow(Thing target)
        {
            var f = (Frame)target;
            var parts = f.TotalMaterialCost().Select(c => (c.thingDef, n: f.ThingCountNeeded(c.thingDef)))
                         .Where(x => x.n > 0).Select(x => PushAdapters.UpTo(x.thingDef, x.n)).ToList();
            return parts.Count == 0 ? null : string.Join(", ", parts);
        }
    }

    /// <summary>CompRefuelable: fuel up to the target level, with vanilla's refuel rules (full, not-empty, atomic).</summary>
    public class RefuelableAdapter : IPushAdapter
    {
        public bool Applies(Thing target) => target.TryGetComp<CompRefuelable>() != null;

        public bool EverAccepts(Thing target, ThingDef def) => target.TryGetComp<CompRefuelable>().Props.fuelFilter.Allows(def);

        /// <summary>How much fuel it takes now, ignoring the item kind (0 when full or when vanilla wouldn't refuel it).</summary>
        static int Wanted(CompRefuelable c)
        {
            if (c.IsFull) return 0;   // GetFuelCountToFullyRefuel() is at least 1 even when full
            if (c.FuelPercentOfMax > 0f && !c.Props.allowRefuelIfNotEmpty) return 0;
            return c.GetFuelCountToFullyRefuel();
        }

        public int CanAccept(Thing target, Thing item)
        {
            var c = target.TryGetComp<CompRefuelable>();
            if (!c.Props.fuelFilter.Allows(item)) return 0;
            int n = Wanted(c);
            // Atomic fueling needs the whole amount in one go; Refuel() refuses (and logs an error) otherwise.
            if (c.Props.atomicFueling && item.stackCount < n) return 0;
            return n;
        }

        /// <summary>Refuel(List) destroys what it uses (SplitOff(n).Destroy()); a remainder stays in item.</summary>
        public void Accept(Thing target, Thing item)
            => target.TryGetComp<CompRefuelable>().Refuel(new List<Thing> { item });

        public string DescribeAcceptsNow(Thing target)
        {
            var c = target.TryGetComp<CompRefuelable>();
            int n = Wanted(c);
            var def = c.Props.fuelFilter.AnyAllowedDef;
            return n <= 0 || def == null ? null : PushAdapters.UpTo(def, n);
        }
    }

    /// <summary>Fermenting barrel: wort up to its free space (none once it holds beer).</summary>
    public class BarrelAdapter : IPushAdapter
    {
        public bool Applies(Thing target) => target is Building_FermentingBarrel;

        public bool EverAccepts(Thing target, ThingDef def) => def == ThingDefOf.Wort;

        public int CanAccept(Thing target, Thing item)
            => item.def == ThingDefOf.Wort ? ((Building_FermentingBarrel)target).SpaceLeftForWort : 0;

        /// <summary>AddWort(Thing) destroys the part it adds (SplitOff(n).Destroy()); a remainder stays in item.</summary>
        public void Accept(Thing target, Thing item) => ((Building_FermentingBarrel)target).AddWort(item);

        public string DescribeAcceptsNow(Thing target)
        {
            int n = ((Building_FermentingBarrel)target).SpaceLeftForWort;
            return n <= 0 ? null : PushAdapters.UpTo(ThingDefOf.Wort, n);
        }
    }

    /// <summary>Storage buildings (hoppers, shelves): their filter and the room left in their cells.</summary>
    public class StorageAdapter : IPushAdapter
    {
        public bool Applies(Thing target) => target is ISlotGroupParent && target.Spawned;

        public bool EverAccepts(Thing target, ThingDef def)
            => ((ISlotGroupParent)target).GetStoreSettings()?.AllowedToAccept(def) ?? false;

        /// <summary>Room for item in one cell: free item slots × stack limit + room in stacks it merges with.</summary>
        static int SpaceIn(IntVec3 c, Map map, Thing item)
        {
            if (!c.InBounds(map)) return 0;
            var list = map.thingGrid.ThingsListAtFast(c);
            int items = 0, room = 0;
            for (int i = 0; i < list.Count; i++)
            {
                var t = list[i];
                if (t.def.category != ThingCategory.Item) continue;
                items++;
                if (t.CanStackWith(item)) room += Mathf.Max(0, t.def.stackLimit - t.stackCount);
            }
            return room + Mathf.Max(0, c.GetMaxItemsAllowedInCell(map) - items) * item.def.stackLimit;
        }

        public int CanAccept(Thing target, Thing item)
        {
            var p = (ISlotGroupParent)target;
            var s = p.GetStoreSettings();
            if (s == null || !s.AllowedToAccept(item)) return 0;
            int total = 0;
            foreach (var c in p.AllSlotCellsList()) total += SpaceIn(c, target.Map, item);
            return total;
        }

        /// <summary>Places item cell by cell (ThingPlaceMode.Direct, merging into stacks there) up to each cell's room.</summary>
        public void Accept(Thing target, Thing item)
        {
            var map = target.Map;
            foreach (var c in ((ISlotGroupParent)target).AllSlotCellsList())
            {
                if (item.Destroyed || item.Spawned || item.stackCount <= 0) return;
                int space = SpaceIn(c, map, item);
                if (space <= 0) continue;
                var part = item.stackCount <= space ? item : item.SplitOff(space);
                GenPlace.TryPlaceThing(part, c, map, ThingPlaceMode.Direct);
                // A split part that did not fully fit goes back into item (Direct may have spawned or absorbed the rest).
                if (part != item && !part.Destroyed && !part.Spawned && part.stackCount > 0)
                    item.TryAbsorbStack(part, respectStackLimit: false);
            }
        }

        public string DescribeAcceptsNow(Thing target)
        {
            var p = (ISlotGroupParent)target;
            var s = p.GetStoreSettings();
            if (s == null) return null;
            int free = 0;
            foreach (var c in p.AllSlotCellsList())
                free += Mathf.Max(0, c.GetMaxItemsAllowedInCell(target.Map) - c.GetItemCount(target.Map));
            return "CB_PusherStorage".Translate(s.filter.Summary, free);
        }
    }
}
