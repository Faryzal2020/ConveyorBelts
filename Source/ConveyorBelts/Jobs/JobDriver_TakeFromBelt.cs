using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace ConveyorBelts
{
    /// <summary>
    /// Takes one item off a belt on an explicit order. targetA = belt cell, job.count = item handle.
    /// If the item left the tile (or the belt is gone) by arrival, the job ends quietly.
    /// </summary>
    public class JobDriver_TakeFromBelt : JobDriver
    {
        IntVec3 Cell => job.targetA.Cell;
        long Handle => job.count;

        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;

        protected override IEnumerable<Toil> MakeNewToils()
        {
            yield return Toils_Goto.GotoCell(Cell, PathEndMode.OnCell);
            yield return new Toil
            {
                initAction = Take,
                defaultCompleteMode = ToilCompleteMode.Instant,
            };
        }

        void Take()
        {
            var map = pawn.Map;
            var comp = MapComponent_Belts.For(map);
            Thing thing = comp?.TakeFromBelt(Cell, Handle);
            if (thing == null)
            {
                EndJobWith(JobCondition.Incompletable);
                return;
            }

            if (job.def == CB_JobDefOf.CB_PickUpFromBelt)
            {
                pawn.inventory.innerContainer.TryAdd(thing);
                if (!thing.Destroyed && thing.holdingOwner == null)
                    Drop(thing, map);   // no room in inventory: never lose the item
                return;
            }

            Thing placed = Drop(thing, map);
            if (placed != null && placed.Spawned)
                pawn.jobs.jobQueue.EnqueueFirst(HaulAIUtility.HaulToStorageJob(pawn, placed, forced: true));
        }

        Thing Drop(Thing thing, Map map)
        {
            if (GenPlace.TryPlaceThing(thing, Cell, map, ThingPlaceMode.Near, out Thing placed)) return placed;
            Log.Warning($"[CB] no room near {Cell} for {thing}; spawning on the cell");
            return GenSpawn.Spawn(thing, Cell, map);
        }
    }

    [DefOf]
    public static class CB_JobDefOf
    {
        public static JobDef CB_HaulFromBelt;
        public static JobDef CB_PickUpFromBelt;
        static CB_JobDefOf() => DefOfHelper.EnsureInitializedInCtor(typeof(CB_JobDefOf));
    }
}
