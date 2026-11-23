using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace ConveyorBelts
{
    /// <summary>
    /// The v1 machine adapters (frame, fuel, wort), in priority order, and the lookup of the machine a pusher faces.
    /// Storage is not an adapter: PushTargets handles haul destinations with vanilla's capacity rules.
    /// </summary>
    public static class PushAdapters
    {
        public static readonly IPushAdapter[] All =
        {
            new FrameAdapter(), new RefuelableAdapter(), new BarrelAdapter(),
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
}
