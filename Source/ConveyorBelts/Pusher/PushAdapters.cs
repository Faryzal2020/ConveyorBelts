using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace ConveyorBelts
{
    /// <summary>
    /// The v1 machine adapters (frame, fuel, wort), in priority order (PushTargets walks them on the front cell).
    /// Storage is not an adapter: PushTargets handles haul destinations with vanilla's capacity rules.
    /// </summary>
    public static class PushAdapters
    {
        public static readonly IPushAdapter[] All =
        {
            new FrameAdapter(), new RefuelableAdapter(), new BarrelAdapter(),
        };
    }

    /// <summary>Construction frames: the materials still missing.</summary>
    public class FrameAdapter : IPushAdapter
    {
        public bool Applies(Thing target) => target is Frame;

        public int CanAccept(Thing target, Thing item) => Mathf.Max(0, ((Frame)target).ThingCountNeeded(item.def));

        public void Accept(Thing target, Thing item) => ((Frame)target).resourceContainer.TryAdd(item);
    }

    /// <summary>CompRefuelable: fuel up to the target level, with vanilla's refuel rules (full, not-empty, atomic).</summary>
    public class RefuelableAdapter : IPushAdapter
    {
        public bool Applies(Thing target) => target.TryGetComp<CompRefuelable>() != null;

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
    }

    /// <summary>Fermenting barrel: wort up to its free space (none once it holds beer).</summary>
    public class BarrelAdapter : IPushAdapter
    {
        public bool Applies(Thing target) => target is Building_FermentingBarrel;

        public int CanAccept(Thing target, Thing item)
            => item.def == ThingDefOf.Wort ? ((Building_FermentingBarrel)target).SpaceLeftForWort : 0;

        /// <summary>AddWort(Thing) destroys the part it adds (SplitOff(n).Destroy()); a remainder stays in item.</summary>
        public void Accept(Thing target, Thing item) => ((Building_FermentingBarrel)target).AddWort(item);
    }
}
