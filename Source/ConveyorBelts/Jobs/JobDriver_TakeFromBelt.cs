using System.Collections.Generic;
using RimWorld;
using UnityEngine;
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
                // Vanilla caps pick-ups by carrying capacity: take what fits, the rest becomes a normal loose item.
                int fit = Mathf.Clamp(MassUtility.CountToPickUpUntilOverEncumbered(pawn, thing), 0, thing.stackCount);
                Thing part = fit >= thing.stackCount ? thing : fit > 0 ? thing.SplitOff(fit) : null;
                if (part != null)
                {
                    pawn.inventory.innerContainer.TryAdd(part);
                    if (!part.Destroyed && part.holdingOwner == null) Drop(part, map);   // never lose the item
                }
                if (part != thing && !thing.Destroyed && thing.holdingOwner == null) Drop(thing, map);
                return;
            }

            Thing placed = Drop(thing, map);
            if (placed != null && placed.Spawned)
            {
                // null when storage filled / became forbidden before arrival: the item just stays on the ground.
                Job haul = HaulAIUtility.HaulToStorageJob(pawn, placed, forced: true);
                if (haul != null) pawn.jobs.jobQueue.EnqueueFirst(haul);
            }
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
