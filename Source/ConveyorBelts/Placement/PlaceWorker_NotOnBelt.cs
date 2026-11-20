using Verse;

namespace ConveyorBelts
{
    /// <summary>
    /// Motors and ports never share a cell with a belt (spec §5.1): a port over a belt would never be asked to take
    /// items (the belt wins), so refuse a cell holding a belt or a belt blueprint/frame.
    /// </summary>
    public class PlaceWorker_NotOnBelt : PlaceWorker
    {
        public override AcceptanceReport AllowsPlacing(BuildableDef checkingDef, IntVec3 loc, Rot4 rot, Map map,
            Thing thingToIgnore = null, Thing thing = null)
        {
            foreach (var t in loc.GetThingList(map))
            {
                if (t == thingToIgnore || t == thing) continue;
                if (t is Building_Belt || IsBelt(t.def.entityDefToBuild)) return "CB_RemoveBeltFirst".Translate();
            }
            return true;
        }

        static bool IsBelt(BuildableDef d)
            => d is ThingDef td && td.thingClass != null && typeof(Building_Belt).IsAssignableFrom(td.thingClass);
    }
}
