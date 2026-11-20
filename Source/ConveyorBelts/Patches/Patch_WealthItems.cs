using HarmonyLib;
using RimWorld;
using Verse;

namespace ConveyorBelts
{
    /// <summary>
    /// Vanilla item wealth skips things held by map components (WealthWatcher.WealthItemsFilter), so items on belts
    /// and in port buffers would not count. Add them, skipping fogged cells like vanilla does.
    /// </summary>
    [HarmonyPatch(typeof(WealthWatcher), "CalculateWealthItems")]
    static class Patch_WealthItems
    {
        static void Postfix(ref float __result, Map ___map)
        {
            var comp = MapComponent_Belts.For(___map);
            if (comp != null) __result += comp.HeldItemsWealth();
        }
    }
}
