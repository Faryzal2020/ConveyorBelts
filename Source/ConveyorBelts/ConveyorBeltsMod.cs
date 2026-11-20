using RimWorld;
using Verse;

namespace ConveyorBelts
{
    public class ConveyorBeltsMod : Mod
    {
        public ConveyorBeltsMod(ModContentPack content) : base(content)
        {
            Log.Message("[ConveyorBelts] loaded v1");
        }
    }

    /// <summary>
    /// Belt defs have canGenerateDefaultDesignator=false in XML so the architect tab shows only the two-click
    /// Designator_BeltPath_* buttons. But vanilla Ideo.MembersCanBuild refuses any def with that flag false
    /// (unless a meme/style/precept lists it), so colonists of a non-classic ideoligion would never build belts.
    /// Once the designation categories have resolved their designators (that happens before static
    /// constructors run), set the flag back to true; any default Designator_Build that slipped in is removed.
    /// </summary>
    [StaticConstructorOnStartup]
    static class BeltDefFixups
    {
        static BeltDefFixups()
        {
            foreach (var def in BeltGizmos.TierDefs) def.canGenerateDefaultDesignator = true;
            int removed = 0;
            foreach (var cat in DefDatabase<DesignationCategoryDef>.AllDefs)
                removed += cat.AllResolvedDesignators?.RemoveAll(d => d is Designator_Build b && !(d is Designator_BeltPath)
                                                                    && b.PlacingDef is ThingDef td && BeltGizmos.IsBeltDef(td)) ?? 0;
            if (removed > 0) Log.Warning($"[ConveyorBelts] removed {removed} default belt designators");
        }
    }
}
