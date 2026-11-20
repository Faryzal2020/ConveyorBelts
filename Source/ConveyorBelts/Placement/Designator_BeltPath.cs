using System.Collections.Generic;
using ConveyorBelts.Sim;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace ConveyorBelts
{
    /// <summary>
    /// Two-click L-path belt placement (PLAN.md §5.2). Click 1 stores the start; click 2 places the
    /// X-first (Shift: Z-first) path from BeltPath.Compute and clears the start. The designator stays
    /// selected. Right-click / Esc with a pending start clears only the start.
    /// </summary>
    public abstract class Designator_BeltPath : Designator_Build
    {
        /// <summary>What placing one path tile does (also decides the ghost colour).</summary>
        public enum TileAction { Skip, Reorient, Replace, Build, Refused }

        public IntVec3? pendingStart;

        /// <summary>What the last CompletePath did per action (read by the debug tests).</summary>
        public Dictionary<TileAction, int> lastCounts = new Dictionary<TileAction, int>();

        /// <summary>Test-only: stands in for the mouse cell in the ghost preview (the harness cannot move the mouse).</summary>
        internal IntVec3? debugMouseCell;

        /// <summary>Shift state seen in the last OnGUI event; SelectedUpdate runs outside OnGUI.</summary>
        bool shiftHeld;

        readonly ThingDef beltDef;

        protected Designator_BeltPath(ThingDef beltDef) : base(beltDef)
        {
            this.beltDef = beltDef;
            // specialDesignatorClasses sets isOrder = true after construction. In 1.7 isOrder is only read by
            // Designator_PlantsCut (tree handling), so it changes nothing here and needs no reset.
        }

        public Rot4 PlacingRot { get => placingRot; set => placingRot = value; }

        // Null style => DesignatorManager calls DesignateSingleCell on every left mouse-down.
        public override DrawStyleCategoryDef DrawStyleCategory => null;

        // Both clicks are accepted anywhere on the map; each path tile is validated when placing.
        public override AcceptanceReport CanDesignateCell(IntVec3 c) => c.InBounds(Map);

        public override void Selected()
        {
            base.Selected();
            pendingStart = null;
        }

        public override void Deselected()
        {
            base.Deselected();
            pendingStart = null;
            debugMouseCell = null;
        }

        public override void DesignateSingleCell(IntVec3 c)
        {
            if (!pendingStart.HasValue)
            {
                pendingStart = c;
                return;
            }
            bool shift = Event.current != null && Event.current.shift;
            CompletePath(c, shift);
        }

        /// <summary>The second click: places the path from pendingStart to end and clears the start.</summary>
        public Dictionary<TileAction, int> CompletePath(IntVec3 end, bool zFirst)
        {
            var counts = new Dictionary<TileAction, int>();
            if (!pendingStart.HasValue) return counts;
            var start = pendingStart.Value;
            pendingStart = null;
            foreach (var (cell, dir) in BeltPath.Compute(start.ToCell(), end.ToCell(), zFirst, placingRot.ToDir()))
            {
                var action = PlaceTile(cell.ToIntVec3(), dir.ToRot4());
                counts[action] = counts.TryGetValue(action, out int n) ? n + 1 : 1;
            }
            lastCounts = counts;
            return counts;
        }

        static Building_Belt BeltAt(IntVec3 c, Map map)
        {
            var list = c.GetThingList(map);
            for (int i = 0; i < list.Count; i++)
                if (list[i] is Building_Belt b && b.Spawned && !b.swappingOut) return b;
            return null;
        }

        /// <summary>Walls, motors, ports, doors, rock (or plans for them) block belts (spec §5.2).</summary>
        static bool BlockedByEdifice(IntVec3 c, Map map)
        {
            if (c.GetEdifice(map) != null) return true;
            var list = c.GetThingList(map);
            for (int i = 0; i < list.Count; i++)
            {
                var t = list[i];
                if ((t.def.IsBlueprint || t.def.IsFrame) && t.def.entityDefToBuild is ThingDef td && td.IsEdifice()) return true;
            }
            return false;
        }

        /// <summary>A blueprint or frame of this belt def with this rotation already on the cell.</summary>
        bool AlreadyPlanned(IntVec3 c, Rot4 rot)
        {
            var list = c.GetThingList(Map);
            for (int i = 0; i < list.Count; i++)
            {
                var t = list[i];
                if (t.def.entityDefToBuild == beltDef && t.Rotation == rot) return true;
            }
            return false;
        }

        /// <summary>Side-effect-free classification of one path tile.</summary>
        public TileAction Evaluate(IntVec3 c, Rot4 rot)
        {
            var map = Map;
            if (!c.InBounds(map)) return TileAction.Refused;
            var belt = BeltAt(c, map);
            if (belt != null && belt.def == beltDef)
                return belt.Rotation == rot ? TileAction.Skip : TileAction.Reorient;
            if (AlreadyPlanned(c, rot)) return TileAction.Skip;
            if (BlockedByEdifice(c, map)) return TileAction.Refused;
            // godMode false keeps the "space occupied" checks; god mode still ignores the no-build edge.
            var report = GenConstruct.CanPlaceBlueprintAt(beltDef, c, rot, map, godMode: false, ignoreEdgeArea: DebugSettings.godMode);
            if (!report.Accepted) return TileAction.Refused;
            return belt != null ? TileAction.Replace : TileAction.Build;
        }

        /// <summary>Places one tile; returns what it did.</summary>
        public TileAction PlaceTile(IntVec3 c, Rot4 rot)
        {
            var map = Map;
            var action = Evaluate(c, rot);
            switch (action)
            {
                case TileAction.Reorient:
                {
                    var belt = BeltAt(c, map);
                    belt.Rotation = rot;
                    MapComponent_Belts.For(map)?.Sim.SetBelt(c.ToCell(), rot.ToDir(), belt.Tier);
                    map.mapDrawer.MapMeshDirty(c, (ulong)MapMeshFlagDefOf.Things | (ulong)MapMeshFlagDefOf.Buildings);
                    break;
                }
                case TileAction.Replace:
                    // Always a replace blueprint, also in god mode: a direct spawn would put two belts on one cell.
                    PlaceBlueprint(c, rot);
                    break;
                case TileAction.Build:
                    if (DebugSettings.godMode) SpawnDirect(c, rot);
                    else PlaceBlueprint(c, rot);
                    break;
                default:
                    return action;
            }
            FleckMaker.ThrowMetaPuffs(GenAdj.OccupiedRect(c, rot, beltDef.Size), map);
            return action;
        }

        // Same sequence as Designator_Build.DesignateSingleCell (non-god branch), with a per-tile rotation.
        void PlaceBlueprint(IntVec3 c, Rot4 rot)
        {
            var map = Map;
            DestroyReplaceableFrames(c, map);
            GenSpawn.WipeExistingThings(c, rot, beltDef.blueprintDef, map, DestroyMode.Deconstruct);
            var bp = GenConstruct.PlaceBlueprintForBuild(beltDef, c, map, rot, Faction.OfPlayer, StuffDef);
            bp.StyleSourcePrecept = sourcePrecept;
            if (bp.StyleSourcePrecept == null) bp.StyleDef = ThingStyleDefNonPreceptSource;
        }

        void SpawnDirect(IntVec3 c, Rot4 rot)
        {
            var map = Map;
            DestroyReplaceableFrames(c, map);
            var thing = ThingMaker.MakeThing(beltDef, StuffDef);
            thing.SetFactionDirect(Faction.OfPlayer);
            GenSpawn.Spawn(thing, c, map, rot);
        }

        void DestroyReplaceableFrames(IntVec3 c, Map map)
        {
            var list = c.GetThingList(map);
            for (int i = list.Count - 1; i >= 0; i--)
                if (list[i] is Frame frame && beltDef.blueprintDef.replaceTags.NotNullAndContainsAnyElement(frame.def.replaceTags))
                    frame.Destroy(DestroyMode.Cancel);
        }

        /// <summary>Right-click / Esc with a pending start: clear the start only and consume the event.</summary>
        internal bool TryConsumeCancel(Event ev)
        {
            if (!pendingStart.HasValue || ev == null) return false;
            bool rightDown = ev.type == EventType.MouseDown && ev.button == 1;
            if (!rightDown && !KeyBindingDefOf.Cancel.KeyDownEvent) return false;
            pendingStart = null;
            SoundDefOf.CancelMode.PlayOneShotOnCamera();
            ev.Use();
            return true;
        }

        // UIRoot_Play runs DesignatorManager.ProcessInputEvents (which deselects on right-click / Esc) BEFORE
        // SelectedProcessInput, but DesignationManagerOnGUI (which calls this) runs before both for every event.
        public override void DrawMouseAttachments()
        {
            var ev = Event.current;
            if (ev != null) shiftHeld = ev.shift;
            if (TryConsumeCancel(ev)) return;
            base.DrawMouseAttachments();
        }

        public override void SelectedProcessInput(Event ev)
        {
            if (TryConsumeCancel(ev)) return;
            base.SelectedProcessInput(ev);   // Q/E rotation
        }

        public override void SelectedUpdate()
        {
            var map = Map;
            if (!map.IsPocketMap) GenDraw.DrawNoBuildEdgeLines();
            var mouse = debugMouseCell ?? UI.MouseCell();
            if (!debugMouseCell.HasValue && ArchitectCategoryTab.InfoRect.Contains(UI.MousePositionOnUIInverted)) return;
            if (!mouse.InBounds(map)) return;
            var start = pendingStart ?? mouse;
            foreach (var (cell, dir) in BeltPath.Compute(start.ToCell(), mouse.ToCell(), shiftHeld, placingRot.ToDir()))
            {
                var c = cell.ToIntVec3();
                if (!c.InBounds(map)) continue;
                var rot = dir.ToRot4();
                bool refused = Evaluate(c, rot) == TileAction.Refused;
                GhostGraphic(refused).DrawFromDef(c.ToVector3ShiftedWithAltitude(AltitudeLayer.Blueprint), rot, beltDef);
            }
        }

        Graphic ghostOk, ghostRefused;

        /// <summary>
        /// The belt texture, tinted and see-through. Vanilla ghosts use the EdgeDetect shader, which on the
        /// square opaque belt texture shows only the tile outline, hiding the per-tile arrow.
        /// </summary>
        Graphic GhostGraphic(bool refused)
        {
            ref Graphic g = ref refused ? ref ghostRefused : ref ghostOk;
            return g ??= GraphicDatabase.Get<Graphic_Multi>(beltDef.graphicData.texPath, ShaderDatabase.Transparent,
                beltDef.graphicData.drawSize, refused ? CannotPlaceColor : CanPlaceColor);
        }
    }

    public class Designator_BeltPath_Basic : Designator_BeltPath
    {
        public Designator_BeltPath_Basic() : base(DefDatabase<ThingDef>.GetNamed("CB_Belt_Basic")) { }
    }

    public class Designator_BeltPath_Fast : Designator_BeltPath
    {
        public Designator_BeltPath_Fast() : base(DefDatabase<ThingDef>.GetNamed("CB_Belt_Fast")) { }
    }

    public class Designator_BeltPath_Express : Designator_BeltPath
    {
        public Designator_BeltPath_Express() : base(DefDatabase<ThingDef>.GetNamed("CB_Belt_Express")) { }
    }
}
