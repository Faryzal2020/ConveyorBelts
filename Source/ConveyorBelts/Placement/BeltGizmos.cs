using System.Collections.Generic;
using System.Linq;
using ConveyorBelts.Sim;
using RimWorld;
using UnityEngine;
using Verse;

namespace ConveyorBelts
{
    /// <summary>
    /// Belt gizmos (PLAN.md §5.11) and the belt placement helpers they share with Designator_BeltPath.
    /// Every selected belt yields its own commands; commands with the same label group into one button, and a
    /// click runs each grouped command once (GizmoGridDrawer), so each command acts only on its own belt and a
    /// multi-selection is handled belt by belt.
    /// </summary>
    public static class BeltGizmos
    {
        static Texture2D selectLineIcon;
        static List<ThingDef> tierDefs;

        /// <summary>Belt defs ordered by tier.</summary>
        public static List<ThingDef> TierDefs => tierDefs ??= DefDatabase<ThingDef>.AllDefs
            .Where(IsBeltDef)
            .OrderBy(d => d.GetModExtension<BeltTierExtension>().tier)
            .ToList();

        public static bool IsBeltDef(ThingDef d)
            => d != null && d.thingClass != null && typeof(Building_Belt).IsAssignableFrom(d.thingClass) && d.GetModExtension<BeltTierExtension>() != null;

        public static int TierOf(ThingDef d) => d.GetModExtension<BeltTierExtension>()?.tier ?? Tiers.Basic;

        public static ThingDef TierDef(int tier) => TierDefs.FirstOrDefault(d => TierOf(d) == tier);

        /// <summary>The belt's picture: the def's uiIcon, else its graphic's north texture.</summary>
        public static Texture2D TierIcon(ThingDef def)
            => def.uiIcon != null && def.uiIcon != BaseContent.BadTex ? def.uiIcon
             : (def.graphic?.MatNorth?.mainTexture as Texture2D) ?? BaseContent.BadTex;

        public static IEnumerable<Gizmo> For(Building_Belt belt)
        {
            if (belt.Faction != Faction.OfPlayer) yield break;
            selectLineIcon ??= ContentFinder<Texture2D>.Get("ConveyorBelts/UI/SelectLine");

            yield return new Command_Action
            {
                defaultLabel = "CB_SelectLine".Translate(),
                icon = selectLineIcon,
                action = () => SelectLine(belt),
            };

            int own = belt.Tier;
            foreach (var def in TierDefs)
            {
                int tier = TierOf(def);
                if (tier == own) continue;
                yield return new Command_Action
                {
                    defaultLabel = (tier > own ? "CB_Upgrade" : "CB_Downgrade").Translate(),
                    icon = TierIcon(def),
                    action = () => ChangeTier(belt, def),
                };
            }
        }

        /// <summary>
        /// "Select line": adds every belt of this belt's line (BeltSim.LineCells, the run a motor drives) to the
        /// selection, keeping what is already selected, so a multi-selection ends up with the union of its lines.
        /// Returns how many belts were newly selected.
        /// </summary>
        public static int SelectLine(Building_Belt belt)
        {
            if (!belt.Spawned) return 0;
            var map = belt.Map;
            var comp = MapComponent_Belts.For(map);
            if (comp == null) return 0;
            var sim = comp.Sim;
            int added = 0;
            foreach (var cell in sim.LineCells(sim.LineIdAt(belt.Position.ToCell())))
            {
                var b = BeltAt(cell.ToIntVec3(), map);
                if (b == null || Find.Selector.IsSelected(b)) continue;
                Find.Selector.Select(b, playSound: false, forceDesignatorDeselect: false);
                if (Find.Selector.IsSelected(b)) added++;
            }
            return added;
        }

        /// <summary>
        /// "Upgrade" / "Downgrade" to the given tier for one belt: places a replace blueprint of def with the belt's own
        /// rotation (in god mode spawns the belt directly; Building_Belt.SpawnSetup swaps it in). Does nothing if a
        /// blueprint or frame of def is already on the cell or vanilla refuses the blueprint. Returns true if it
        /// placed something.
        /// </summary>
        public static bool ChangeTier(Building_Belt belt, ThingDef def)
        {
            if (!belt.Spawned || belt.swappingOut || belt.def == def) return false;
            var map = belt.Map;
            var c = belt.Position;
            var rot = belt.Rotation;
            if (HasPlanOf(def, c, map)) return false;
            if (DebugSettings.godMode)
            {
                SpawnBelt(def, c, rot, map, null);
                return true;
            }
            if (!GenConstruct.CanPlaceBlueprintAt(def, c, rot, map).Accepted) return false;
            PlaceBeltBlueprint(def, c, rot, map, null);
            return true;
        }

        // ---- placement helpers shared with Designator_BeltPath ----

        /// <summary>The spawned belt on c that is not being swapped out, or null.</summary>
        public static Building_Belt BeltAt(IntVec3 c, Map map)
        {
            var list = c.GetThingList(map);
            for (int i = 0; i < list.Count; i++)
                if (list[i] is Building_Belt b && b.Spawned && !b.swappingOut) return b;
            return null;
        }

        static bool IsBeltPlan(Thing t, out ThingDef built)
        {
            built = (t.def.IsBlueprint || t.def.IsFrame) ? t.def.entityDefToBuild as ThingDef : null;
            return IsBeltDef(built);
        }

        /// <summary>A blueprint or frame of def is on c.</summary>
        public static bool HasPlanOf(ThingDef def, IntVec3 c, Map map)
        {
            var list = c.GetThingList(map);
            for (int i = 0; i < list.Count; i++)
                if (IsBeltPlan(list[i], out var built) && built == def) return true;
            return false;
        }

        /// <summary>The belt def of a blueprint or frame on the belt's cell that would replace it (another tier), or null.</summary>
        public static ThingDef PlannedReplacement(Building_Belt belt)
        {
            var list = belt.Position.GetThingList(belt.Map);
            for (int i = 0; i < list.Count; i++)
                if (IsBeltPlan(list[i], out var built) && built != belt.def) return built;
            return null;
        }

        /// <summary>Turns every belt blueprint / frame on c to rot (used when a belt is re-oriented in place).</summary>
        public static void RotatePlans(IntVec3 c, Map map, Rot4 rot)
        {
            var list = c.GetThingList(map);
            bool changed = false;
            for (int i = 0; i < list.Count; i++)
                if (IsBeltPlan(list[i], out _) && list[i].Rotation != rot) { list[i].Rotation = rot; changed = true; }
            if (changed) map.mapDrawer.MapMeshDirty(c, (ulong)MapMeshFlagDefOf.Things | (ulong)MapMeshFlagDefOf.Buildings);
        }

        /// <summary>Cancels (full refund) belt frames on c that a plan of def replaces, as Designator_Build does.</summary>
        static void DestroyReplaceableFrames(ThingDef def, IntVec3 c, Map map)
        {
            var list = c.GetThingList(map);
            for (int i = list.Count - 1; i >= 0; i--)
                if (list[i] is Frame frame && def.blueprintDef.replaceTags.NotNullAndContainsAnyElement(frame.def.replaceTags))
                    frame.Destroy(DestroyMode.Cancel);
        }

        /// <summary>Same sequence as Designator_Build.DesignateSingleCell (non-god branch).</summary>
        public static Blueprint_Build PlaceBeltBlueprint(ThingDef def, IntVec3 c, Rot4 rot, Map map, ThingDef stuff)
        {
            DestroyReplaceableFrames(def, c, map);
            GenSpawn.WipeExistingThings(c, rot, def.blueprintDef, map, DestroyMode.Deconstruct);
            return GenConstruct.PlaceBlueprintForBuild(def, c, map, rot, Faction.OfPlayer, stuff);
        }

        /// <summary>
        /// God-mode placement: cancels belt plans on c and spawns the belt. If a belt is already there,
        /// Building_Belt.SpawnSetup swaps it out (items kept, old belt refunded).
        /// </summary>
        public static Thing SpawnBelt(ThingDef def, IntVec3 c, Rot4 rot, Map map, ThingDef stuff)
        {
            var list = c.GetThingList(map);
            for (int i = list.Count - 1; i >= 0; i--)
                if (IsBeltPlan(list[i], out _)) list[i].Destroy(DestroyMode.Cancel);
            var thing = ThingMaker.MakeThing(def, stuff);
            thing.SetFactionDirect(Faction.OfPlayer);
            return GenSpawn.Spawn(thing, c, map, rot);
        }
    }
}
