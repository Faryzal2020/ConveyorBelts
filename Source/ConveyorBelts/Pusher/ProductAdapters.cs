using System.Runtime.CompilerServices;
using HarmonyLib;
using RimWorld;
using Verse;

namespace ConveyorBelts
{
    /// <summary>
    /// A machine that keeps its product as a count, not as a held item. The pusher can only take the whole batch,
    /// and only when the target takes all of it (no holding).
    /// </summary>
    public interface IProductAdapter
    {
        /// <summary>Whether b is this kind of machine (whether or not a product is ready).</summary>
        bool Applies(Thing b);

        /// <summary>
        /// The ready product as a stand-in thing (def and count only; never spawned, never given away), or null when
        /// nothing is ready.
        /// </summary>
        Thing Peek(Thing b);

        /// <summary>Takes the whole batch out of the machine; returns it unspawned and unowned (null if none ready).</summary>
        Thing TakeAll(Thing b);
    }

    public static class ProductAdapters
    {
        public static readonly IProductAdapter[] All = { new BarrelProductAdapter() };

        public static IProductAdapter For(Thing b)
        {
            foreach (var a in All) if (a.Applies(b)) return a;
            return null;
        }
    }

    /// <summary>Fermenting barrel → beer, through vanilla TakeOutBeer, once fermented.</summary>
    public class BarrelProductAdapter : IProductAdapter
    {
        static readonly AccessTools.FieldRef<Building_FermentingBarrel, int> WortCount =
            AccessTools.FieldRefAccess<Building_FermentingBarrel, int>("wortCount");

        // One stand-in beer thing per barrel, so peeking every move attempt makes no new things.
        static readonly ConditionalWeakTable<Building_FermentingBarrel, Thing> peeks =
            new ConditionalWeakTable<Building_FermentingBarrel, Thing>();

        public bool Applies(Thing b) => b is Building_FermentingBarrel;

        public Thing Peek(Thing b)
        {
            var barrel = (Building_FermentingBarrel)b;
            if (!barrel.Fermented) return null;
            int n = WortCount(barrel);
            if (n <= 0) return null;
            var beer = peeks.GetValue(barrel, _ => ThingMaker.MakeThing(ThingDefOf.Beer));
            beer.stackCount = n;
            return beer;
        }

        public Thing TakeAll(Thing b)
        {
            var barrel = (Building_FermentingBarrel)b;
            return barrel.Fermented ? barrel.TakeOutBeer() : null;
        }
    }
}
