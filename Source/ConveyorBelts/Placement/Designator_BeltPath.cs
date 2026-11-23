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
    /// X-first (Q/E or middle-click flips to Z-first) path from BeltPath.Compute and clears the start. The designator stays
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

        bool zFirst;

        /// <summary>The current L order: false = across first, true = up/down first. Reset when deselected.</summary>
        public bool ZFirst { get => zFirst; set => zFirst = value; }

        /// <summary>Toggles the L order (what Q/E do while a start is pending).</summary>
        public void FlipL() => zFirst = !zFirst;

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
            zFirst = false;
        }

        public override void DesignateSingleCell(IntVec3 c)
        {
            if (!pendingStart.HasValue)
            {
                pendingStart = c;
                return;
            }
            CompletePath(c, zFirst);
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

        static Building_Belt BeltAt(IntVec3 c, Map map) => BeltGizmos.BeltAt(c, map);


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
            // Any other building or plan (walls, conduits, furniture, motors, ports, rock) blocks a belt; wall
            // attachments and floors may share the cell (BeltCellRules).
            if (BeltCellRules.HasBlockerForBelt(c, map)) return TileAction.Refused;
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
                    // A pending other-tier upgrade on this cell follows the new direction.
                    BeltGizmos.RotatePlans(c, map, rot);
                    break;
                }
                case TileAction.Replace:
                    // God mode: the new belt spawns on top and Building_Belt.SpawnSetup swaps the old one out.
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
            var bp = BeltGizmos.PlaceBeltBlueprint(beltDef, c, rot, Map, StuffDef);
            bp.StyleSourcePrecept = sourcePrecept;
            if (bp.StyleSourcePrecept == null) bp.StyleDef = ThingStyleDefNonPreceptSource;
        }

        void SpawnDirect(IntVec3 c, Rot4 rot) => BeltGizmos.SpawnBelt(beltDef, c, rot, Map, StuffDef);

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
            if (TryConsumeCancel(ev)) return;
            base.DrawMouseAttachments();
        }

        /// <summary>Vanilla's middle-click rotate: a press released within this many seconds (Designator_Place.HandleRotationShortcuts).</summary>
        const float MiddleClickSeconds = 0.15f;
        float middleDownTime = -1f;

        /// <summary>
        /// With a pending start, the rotate inputs flip the L instead of rotating: Q/E, and a middle-click (consumed on
        /// press and release, flips on a short release, as vanilla rotates). Returns true when the event was consumed.
        /// </summary>
        bool TryConsumeFlip(Event ev)
        {
            if (!pendingStart.HasValue || ev == null) return false;
            if (ev.button == 2 && (ev.type == EventType.MouseDown || ev.type == EventType.MouseUp))
            {
                float now = Time.realtimeSinceStartup;
                if (ev.type == EventType.MouseDown) middleDownTime = now;
                else
                {
                    if (middleDownTime >= 0f && now - middleDownTime < MiddleClickSeconds) Flip();
                    middleDownTime = -1f;
                }
                ev.Use();
                return true;
            }
            if (!KeyBindingDefOf.Designator_RotateLeft.KeyDownEvent && !KeyBindingDefOf.Designator_RotateRight.KeyDownEvent) return false;
            Flip();
            ev.Use();
            return true;
        }

        void Flip()
        {
            FlipL();
            SoundDefOf.DragSlider.PlayOneShotOnCamera();
        }

        public override void SelectedProcessInput(Event ev)
        {
            if (TryConsumeCancel(ev)) return;
            if (TryConsumeFlip(ev)) return;
            base.SelectedProcessInput(ev);   // Q/E / middle-click rotation before the first click
        }

        public override void SelectedUpdate()
        {
            var map = Map;
            if (!map.IsPocketMap) GenDraw.DrawNoBuildEdgeLines();
            var mouse = debugMouseCell ?? UI.MouseCell();
            if (!debugMouseCell.HasValue && ArchitectCategoryTab.InfoRect.Contains(UI.MousePositionOnUIInverted)) return;
            if (!mouse.InBounds(map)) return;
            var start = pendingStart ?? mouse;
            foreach (var (cell, dir) in BeltPath.Compute(start.ToCell(), mouse.ToCell(), zFirst, placingRot.ToDir()))
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
