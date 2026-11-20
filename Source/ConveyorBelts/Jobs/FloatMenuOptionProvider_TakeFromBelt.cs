using System.Collections.Generic;
using ConveyorBelts.Sim;
using RimWorld;
using Verse;
using Verse.AI;

namespace ConveyorBelts
{
    /// <summary>Right-click a belt tile: per item, "Haul to stockpile" (only when storage accepts it) and "Pick up".</summary>
    public class FloatMenuOptionProvider_TakeFromBelt : FloatMenuOptionProvider
    {
        protected override bool Drafted => true;
        protected override bool Undrafted => true;
        protected override bool Multiselect => false;
        protected override bool RequiresManipulation => true;

        public override IEnumerable<FloatMenuOption> GetOptions(FloatMenuContext context)
            => BuildOptions(context.FirstSelectedPawn, context.map, context.ClickedCell);

        /// <summary>Also called by the debug action, since float menus cannot be clicked headless.</summary>
        public static IEnumerable<FloatMenuOption> BuildOptions(Pawn pawn, Map map, IntVec3 cell)
        {
            if (pawn == null || map == null || !cell.InBounds(map)) yield break;
            var comp = MapComponent_Belts.For(map);
            if (comp == null) yield break;
            var items = comp.Sim.ItemsAt(cell.ToCell());
            if (items == null || items.Count == 0) yield break;
            if (!pawn.CanReach(cell, PathEndMode.OnCell, Danger.Deadly)) yield break;

            // snapshot: the sim may change while the menu is open
            var handles = new List<long>();
            foreach (var it in items) handles.Add(it.Handle);

            foreach (long handle in handles)
            {
                Thing thing = comp.Store.Get(handle);
                if (thing == null) continue;
                string label = thing.LabelCap;
                long h = handle;

                if (pawn.WorkTypeIsDisabled(WorkTypeDefOf.Hauling))
                    yield return new FloatMenuOption("CB_CannotHaul".Translate(label), null);
                else if (StoreUtility.TryFindBestBetterStoreCellFor(thing, pawn, map, StoragePriority.Unstored, pawn.Faction, out _))
                    yield return new FloatMenuOption("CB_HaulFromBelt".Translate(label),
                        () => Order(pawn, CB_JobDefOf.CB_HaulFromBelt, cell, h), MenuOptionPriority.High);

                if (MassUtility.WillBeOverEncumberedAfterPickingUp(pawn, thing, 1))
                    yield return new FloatMenuOption("CB_CannotPickUp".Translate(label) + ": " + "TooHeavy".Translate(), null);
                else
                    yield return new FloatMenuOption("CB_PickUpFromBelt".Translate(label),
                        () => Order(pawn, CB_JobDefOf.CB_PickUpFromBelt, cell, h), MenuOptionPriority.Default);
            }
        }

        static void Order(Pawn pawn, JobDef def, IntVec3 cell, long handle)
        {
            var job = JobMaker.MakeJob(def, cell);
            job.count = (int)handle;
            pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
        }
    }
}
