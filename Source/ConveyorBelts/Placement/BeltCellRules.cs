using RimWorld;
using Verse;

namespace ConveyorBelts
{
    /// <summary>
    /// Belts are surface-only (user rule, 2026-11-21): nothing is built on a belt cell except other belts (tier
    /// upgrades), floors, power conduits and wall-mounted attachments (wall lamps). Underground belts will be a separate building.
    /// Used both ways: by the placement patch (nothing else onto a belt) and by Designator_BeltPath (no belt onto
    /// another building).
    /// </summary>
    public static class BeltCellRules
    {
        /// <summary>May this buildable share a cell with a belt?</summary>
        public static bool AllowedWithBelt(BuildableDef def)
        {
            if (def is TerrainDef) return true;
            if (def is not ThingDef td) return false;
            if (BeltGizmos.IsBeltDef(td)) return true;
            return td.building != null && (td.building.isAttachment || td.building.isPowerConduit);
        }

        /// <summary>A belt, or a blueprint/frame of one, on this cell (ignoring the given things).</summary>
        public static bool HasBeltOrPlan(IntVec3 c, Map map, Thing ignoreA = null, Thing ignoreB = null)
        {
            var list = c.GetThingList(map);
            for (int i = 0; i < list.Count; i++)
            {
                var t = list[i];
                if (t == ignoreA || t == ignoreB) continue;
                if (t is Building_Belt) return true;
                if ((t.def.IsBlueprint || t.def.IsFrame) && t.def.entityDefToBuild is ThingDef td && BeltGizmos.IsBeltDef(td))
                    return true;
            }
            return false;
        }

        /// <summary>A building (or a plan for one) on this cell that a belt may not be placed on.</summary>
        public static bool HasBlockerForBelt(IntVec3 c, Map map)
        {
            var list = c.GetThingList(map);
            for (int i = 0; i < list.Count; i++)
            {
                var built = GenConstruct.BuiltDefOf(list[i].def);
                if (built is ThingDef td && td.category == ThingCategory.Building && !AllowedWithBelt(td)) return true;
                if ((list[i].def.IsBlueprint || list[i].def.IsFrame) && built is ThingDef bd && !AllowedWithBelt(bd)) return true;
            }
            return false;
        }
    }
}
