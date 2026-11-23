using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using LudeonTK;
using RimWorld;
using UnityEngine;
using Verse;

namespace ConveyorBelts
{
    /// <summary>
    /// Test-only debug actions that drive Designator_BeltPath through its real entry points
    /// (the category's own designator instances, DesignateSingleCell / CompletePath), since the harness
    /// cannot click. Coordinates are relative to an origin O found near the map centre and logged.
    /// Layout: path A at O, path B at O+(8,0), path C at O+(16,0); the area O..O+(21,7) is checked clear.
    /// </summary>
    public static class CB_PlacementTests
    {
        const string Cat = "Conveyor Belts";
        const int AreaW = 22, AreaH = 8;

        static int originMapId = -1;
        static IntVec3 origin;

        static IntVec3 O(Map map)
        {
            if (originMapId == map.uniqueID) return origin;
            var basic = DefDatabase<ThingDef>.GetNamed("CB_Belt_Basic");
            foreach (var c in GenRadial.RadialCellsAround(map.Center, 50f, true))
            {
                var rect = new CellRect(c.x, c.z, AreaW, AreaH);
                if (rect.Cells.All(x => x.InBounds(map) && !x.Fogged(map) && x.GetFirstBuilding(map) == null
                        && x.GetThingList(map).All(t => t.def.category == ThingCategory.Plant || t.def.category == ThingCategory.Filth)
                        && GenConstruct.CanPlaceBlueprintAt(basic, x, Rot4.North, map).Accepted))
                {
                    origin = c;
                    originMapId = map.uniqueID;
                    Log.Message($"[CB] test origin O=({c.x},{c.z}) area {AreaW}x{AreaH}");
                    return origin;
                }
            }
            Log.Error("[CB] no clear test area near the map centre");
            originMapId = map.uniqueID;
            origin = map.Center;
            return origin;
        }

        static Designator_BeltPath Des<T>() where T : Designator_BeltPath
            => DefDatabase<DesignationCategoryDef>.GetNamed("CB_Conveyors").AllResolvedDesignators.OfType<T>().First();

        static string Fmt(Dictionary<Designator_BeltPath.TileAction, int> counts)
            => counts.Count == 0 ? "nothing" : string.Join(" ", counts.OrderBy(k => k.Key).Select(k => $"{k.Key}={k.Value}"));

        static string Rel(IntVec3 c, IntVec3 o) => $"O+({c.x - o.x},{c.z - o.z})";

        /// <summary>Selects des (as the architect button does), runs both clicks, deselects. zFirst null = real DesignateSingleCell.</summary>
        static void RunPath<T>(string name, IntVec3 start, IntVec3 end, bool? zFirst = null, Rot4? rot = null) where T : Designator_BeltPath
        {
            var map = Find.CurrentMap;
            var o = O(map);
            var des = Des<T>();
            Find.DesignatorManager.Select(des);
            try
            {
                if (rot.HasValue) des.PlacingRot = rot.Value;
                des.DesignateSingleCell(start);
                string afterFirst = des.pendingStart.HasValue ? Rel(des.pendingStart.Value, o) : "null";
                Dictionary<Designator_BeltPath.TileAction, int> counts;
                if (zFirst.HasValue) counts = des.CompletePath(end, zFirst.Value);
                else { des.DesignateSingleCell(end); counts = des.lastCounts; }
                bool stillSelected = Find.DesignatorManager.SelectedDesignator == des;
                Log.Message($"[CB] {name}: {des.GetType().Name} {Rel(start, o)} -> {Rel(end, o)} zFirst={(zFirst.HasValue ? zFirst.Value.ToString() : "des:" + des.ZFirst)} " +
                            $"rot={des.PlacingRot} pendingAfterClick1={afterFirst} pendingAfterClick2={(des.pendingStart.HasValue ? "set" : "null")} stillSelected={stillSelected} result: {Fmt(counts)} godMode={DebugSettings.godMode}");
            }
            finally { Find.DesignatorManager.Deselect(); }
        }

        static IntVec3 At(int dx, int dz) { var o = O(Find.CurrentMap); return new IntVec3(o.x + dx, 0, o.z + dz); }

        [DebugAction(Cat, "Test: path A basic X-first O to O+(5,5)", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        static void PathA() => RunPath<Designator_BeltPath_Basic>("path A", At(0, 0), At(5, 5));

        [DebugAction(Cat, "Test: path B basic Z-first O+(8,0) to O+(13,5)", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        static void PathB() => RunPath<Designator_BeltPath_Basic>("path B", At(8, 0), At(13, 5), zFirst: true);

        [DebugAction(Cat, "Test: path with Q/E flip", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        static void PathFlip()
        {
            var des = Des<Designator_BeltPath_Basic>();
            Find.DesignatorManager.Select(des);
            try
            {
                des.DesignateSingleCell(At(0, 0));
                des.FlipL();
                Log.Message($"[CB] flip: ZFirst={des.ZFirst}");
                des.DesignateSingleCell(At(5, 5));
                Log.Message($"[CB] flip path result: {Fmt(des.lastCounts)} ZFirst after={des.ZFirst}");
            }
            finally { Find.DesignatorManager.Deselect(); }
            Log.Message($"[CB] after deselect ZFirst={des.ZFirst}");
            DumpTiles();
        }

        [DebugAction(Cat, "Test: path C basic same cell O+(16,0) twice rot East", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        static void PathC() => RunPath<Designator_BeltPath_Basic>("path C", At(16, 0), At(16, 0), rot: Rot4.East);

        [DebugAction(Cat, "Test: re-place A X leg same rot", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        static void ReplaceSame() => RunPath<Designator_BeltPath_Basic>("re-place same", At(0, 0), At(4, 0));

        [DebugAction(Cat, "Test: re-place A X leg reversed", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        static void ReplaceReversed() => RunPath<Designator_BeltPath_Basic>("re-place reversed", At(4, 0), At(0, 0));

        [DebugAction(Cat, "Test: re-place A X leg forward", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        static void ReplaceForward() => RunPath<Designator_BeltPath_Basic>("re-place forward", At(0, 0), At(4, 0));

        [DebugAction(Cat, "Test: fast over path A", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        static void FastOverA() => RunPath<Designator_BeltPath_Fast>("fast over A", At(0, 0), At(5, 5));

        [DebugAction(Cat, "Test: fast over path B (Z-first)", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        static void FastOverB() => RunPath<Designator_BeltPath_Fast>("fast over B", At(8, 0), At(13, 5), zFirst: true);

        [DebugAction(Cat, "Test: basic path reversed over O+(9..16,4)", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        static void ReversedRow4() => RunPath<Designator_BeltPath_Basic>("basic reversed row 4", At(16, 4), At(9, 4));

        [DebugAction(Cat, "Test: spawn basic line O+(9..16,4) east with motor at O+(8,4)", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        static void SpawnRow4()
        {
            var map = Find.CurrentMap;
            var basic = DefDatabase<ThingDef>.GetNamed("CB_Belt_Basic");
            for (int dx = 9; dx <= 16; dx++) BeltGizmos.SpawnBelt(basic, At(dx, 4), Rot4.East, map, null);
            var motor = ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("CB_BeltMotor"));
            motor.SetFactionDirect(Faction.OfPlayer);
            GenSpawn.Spawn(motor, At(8, 4), map, Rot4.East);
            Find.CameraDriver.SetRootPosAndSize(At(12, 4).ToVector3Shifted(), 12f);
            Log.Message("[CB] spawned basic row O+(9..16,4) east, motor O+(8,4)");
        }

        [DebugAction(Cat, "Test: express path over wall at O+(19,2)", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        static void ExpressOverWall()
        {
            var wall = ThingMaker.MakeThing(ThingDefOf.Wall, ThingDefOf.Steel);
            wall.SetFaction(Faction.OfPlayer);
            GenSpawn.Spawn(wall, At(19, 2), Find.CurrentMap);
            RunPath<Designator_BeltPath_Express>("express over wall", At(19, 0), At(19, 4));
        }

        [DebugAction(Cat, "Test: express path over conduit at O+(19,2)", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        static void ExpressOverConduit()
        {
            var map = Find.CurrentMap;
            var conduit = ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("PowerConduit"));
            conduit.SetFaction(Faction.OfPlayer);
            GenSpawn.Spawn(conduit, At(19, 2), map);
            RunPath<Designator_BeltPath_Express>("express over conduit", At(19, 0), At(19, 4));
            var c = At(19, 2);
            Log.Message($"[CB] conduit kept={c.GetThingList(map).Any(t => t.def.defName == "PowerConduit")} belt/plan there={BeltCellRules.HasBeltOrPlan(c, map)}");
        }

        [DebugAction(Cat, "Test: right-click with pending start", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        static void RightClickPending()
        {
            var des = Des<Designator_BeltPath_Basic>();
            Find.DesignatorManager.Select(des);
            try
            {
                des.DesignateSingleCell(At(18, 6));
                bool hadStart = des.pendingStart.HasValue;
                var ev = new Event { type = EventType.MouseDown, button = 1 };
                des.SelectedProcessInput(ev);
                Log.Message($"[CB] right-click: hadStart={hadStart} pendingAfter={(des.pendingStart.HasValue ? "set" : "null")} eventUsed={ev.type == EventType.Used} stillSelected={Find.DesignatorManager.SelectedDesignator == des}");
                // Without a start the event is not consumed (vanilla then deselects). Called directly: the
                // vanilla fall-through (rotation keys) reads Event.current, which is null outside OnGUI.
                ev = new Event { type = EventType.MouseDown, button = 1 };
                bool consumed = des.TryConsumeCancel(ev);
                Log.Message($"[CB] right-click without start: consumed={consumed} eventUsed={ev.type == EventType.Used}");
            }
            finally { Find.DesignatorManager.Deselect(); }
        }

        /// <summary>
        /// A short middle-click (press + release through SelectedProcessInput, with Event.current set as in OnGUI): with a
        /// pending start it flips the L and is consumed; without one vanilla rotates placingRot.
        /// </summary>
        [DebugAction(Cat, "Test: middle-click with pending start", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        static void MiddleClickPending()
        {
            var des = Des<Designator_BeltPath_Basic>();
            var prev = Event.current;
            Find.DesignatorManager.Select(des);
            try
            {
                (bool downUsed, bool upUsed) Click()
                {
                    var down = new Event { type = EventType.MouseDown, button = 2 };
                    Event.current = down;
                    des.SelectedProcessInput(down);
                    var up = new Event { type = EventType.MouseUp, button = 2 };
                    Event.current = up;
                    des.SelectedProcessInput(up);
                    return (down.type == EventType.Used, up.type == EventType.Used);
                }
                des.DesignateSingleCell(At(18, 6));
                var rot = des.PlacingRot;
                bool z0 = des.ZFirst;
                var (d1, u1) = Click();
                bool z1 = des.ZFirst;
                var (d2, u2) = Click();
                Log.Message($"[CB] middle-click pending: ZFirst {z0} -> {z1} -> {des.ZFirst} rot {rot} -> {des.PlacingRot} used down={d1}/{d2} up={u1}/{u2} pending={(des.pendingStart.HasValue ? "set" : "null")}");
                des.pendingStart = null;
                var (d3, u3) = Click();
                Log.Message($"[CB] middle-click no start: ZFirst={des.ZFirst} rot {rot} -> {des.PlacingRot} used down={d3} up={u3}");
            }
            finally
            {
                Event.current = prev;
                Find.DesignatorManager.Deselect();
            }
        }

        [DebugAction(Cat, "Test: deconstruct belt at O+(2,0) via vanilla designator", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        static void DeconstructA() => DeconstructAt(2, 0);

        [DebugAction(Cat, "Test: deconstruct belt at O+(16,0) via vanilla designator", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        static void DeconstructC() => DeconstructAt(16, 0);

        /// <summary>Vanilla deconstruct tool on one cell: instant in god mode, else a designation a pawn works off.</summary>
        static void DeconstructAt(int dx, int dz)
        {
            var map = Find.CurrentMap;
            var c = At(dx, dz);
            var des = new Designator_Deconstruct();
            var rep = des.CanDesignateCell(c);
            if (!rep.Accepted) { Log.Warning($"[CB] deconstruct refused at O+({dx},{dz}): {rep.Reason}"); return; }
            des.DesignateSingleCell(c);
            Log.Message($"[CB] deconstruct at O+({dx},{dz}) godMode={DebugSettings.godMode}: belt still there={c.GetThingList(map).OfType<Building_Belt>().Any()} designation={map.designationManager.AllDesignationsAt(c).Any()}");
        }

        [DebugAction(Cat, "Test: dump belt tiles", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        static void DumpTiles()
        {
            var map = Find.CurrentMap;
            var o = O(map);
            var sim = MapComponent_Belts.For(map).Sim;
            var belts = map.listerThings.AllThings.OfType<Building_Belt>().OrderBy(b => b.Position.x).ThenBy(b => b.Position.z).ToList();
            Log.Message($"[CB] tiles: {belts.Count} belts, sim belts={sim.BeltCount}");
            foreach (var b in belts)
            {
                bool inSim = sim.TryGetBelt(b.Position.ToCell(), out var d, out var tier);
                Log.Message($"[CB]   {Rel(b.Position, o)} {b.def.defName} rot={b.Rotation.ToStringHuman()} sim={(inSim ? d + "/t" + tier : "MISSING")}");
            }
            foreach (var t in map.listerThings.ThingsInGroup(ThingRequestGroup.Blueprint).Concat(map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingFrame))
                         .Where(t => t.def.entityDefToBuild is ThingDef td && td.thingClass == typeof(Building_Belt))
                         .OrderBy(t => t.Position.x).ThenBy(t => t.Position.z))
                Log.Message($"[CB]   plan {Rel(t.Position, o)} {t.def.defName} rot={t.Rotation.ToStringHuman()}");
        }

        [DebugAction(Cat, "Test: open architect conveyors tab", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        static void OpenArchitect()
        {
            Find.MainTabsRoot.SetCurrentTab(MainButtonDefOf.Architect, playSound: false);
            var win = (MainTabWindow_Architect)MainButtonDefOf.Architect.TabWindow;
            var panels = (List<ArchitectCategoryTab>)typeof(MainTabWindow_Architect)
                .GetField("desPanelsCached", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(win);
            win.selectedDesPanel = panels.First(p => p.def.defName == "CB_Conveyors");
            var names = win.selectedDesPanel.def.AllResolvedDesignators.Select(d => $"{d.GetType().Name}:{d.Label}:order={d.Order}:visible={d.Visible}");
            Log.Message($"[CB] architect tab opened: {string.Join(", ", names)}");
        }

        [DebugAction(Cat, "Test: ghost preview basic O+(17,2) to O+(21,6)", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        static void GhostPreview()
        {
            var des = Des<Designator_BeltPath_Basic>();
            Find.DesignatorManager.Select(des);
            des.DesignateSingleCell(At(17, 2));
            des.debugMouseCell = At(21, 6);
            Find.CameraDriver.SetRootPosAndSize(At(14, 3).ToVector3Shifted(), 14f);
            Log.Message("[CB] basic designator selected, pending start O+(17,2), preview end O+(21,6) (left selected for a screenshot)");
        }
    }
}
