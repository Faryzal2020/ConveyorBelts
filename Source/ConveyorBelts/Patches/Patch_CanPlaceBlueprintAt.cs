using HarmonyLib;
using RimWorld;
using Verse;

namespace ConveyorBelts
{
    /// <summary>
    /// Refuse any building (blueprint, god-mode build, reinstall) on a cell holding a belt or a belt plan, unless
    /// BeltCellRules allows it (belts, floors, wall attachments). Vanilla would otherwise let edifices go over
    /// belts, because belts are non-edifices.
    /// </summary>
    [HarmonyPatch(typeof(GenConstruct), nameof(GenConstruct.CanPlaceBlueprintAt_NewTemp))]
    static class Patch_CanPlaceBlueprintAt
    {
        static void Postfix(ref AcceptanceReport __result, BuildableDef entDef, IntVec3 center, Rot4 rot, Map map,
            Thing thingToIgnore, Thing thing)
        {
            if (!__result.Accepted || map == null || BeltCellRules.AllowedWithBelt(entDef)) return;
            foreach (var c in GenAdj.OccupiedRect(center, rot, entDef.Size))
            {
                if (!c.InBounds(map)) continue;
                if (BeltCellRules.HasBeltOrPlan(c, map, thingToIgnore, thing))
                {
                    __result = new AcceptanceReport("CB_RemoveBeltFirst".Translate());
                    return;
                }
            }
        }
    }
}
