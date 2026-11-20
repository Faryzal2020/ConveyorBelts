using System.Collections.Generic;
using System.Linq;
using ConveyorBelts.Sim;
using LudeonTK;
using RimWorld;
using Verse;

namespace ConveyorBelts
{
    public static class CB_DebugActions
    {
        const string Cat = "Conveyor Belts";

        static List<Building_Belt> Belts(Map map)
            => map.listerThings.AllThings.OfType<Building_Belt>().OrderBy(b => b.thingIDNumber).ToList();

        [DebugAction(Cat, "Seed test items", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        static void SeedTestItems()
        {
            var map = Find.CurrentMap;
            var comp = MapComponent_Belts.For(map);
            int added = 0;
            foreach (var b in Belts(map))
            {
                var cell = b.Position.ToCell();
                if (comp.Sim.ItemsAt(cell).Count > 0) continue;
                var t = ThingMaker.MakeThing(ThingDefOf.Steel);
                t.stackCount = 5;
                if (comp.TryPutOnBelt(b.Position, b.Rotation.Opposite, t)) added++;
                else t.Destroy();
            }
            Log.Message($"[CB] seeded {added} steel stacks");
        }

        [DebugAction(Cat, "Seed raw meat on first belt", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        static void SeedRawMeat()
        {
            var map = Find.CurrentMap;
            var comp = MapComponent_Belts.For(map);
            var first = Belts(map).FirstOrDefault();
            if (first == null) { Log.Warning("[CB] no belts"); return; }
            var def = DefDatabase<ThingDef>.GetNamedSilentFail("Meat_Cow")
                      ?? DefDatabase<ThingDef>.AllDefs.First(d => d.IsMeat);
            var t = ThingMaker.MakeThing(def);
            t.stackCount = 5;
            bool ok = comp.TryPutOnBelt(first.Position, first.Rotation.Opposite, t);
            if (!ok) t.Destroy();
            Log.Message($"[CB] seed {def.defName} x5 on {first.Position}: {(ok ? "ok" : "refused")}");
        }

        /// <summary>When set (debug action), MapComponent_Belts dumps its state right after restoring items on load.</summary>
        internal static bool DumpOnLoad;

        [DebugAction(Cat, "Toggle dump on load", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        static void ToggleDumpOnLoad()
        {
            DumpOnLoad = !DumpOnLoad;
            Log.Message($"[CB] dumpOnLoad={DumpOnLoad}");
        }

        [DebugAction(Cat, "Dump belt state", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        static void DumpBeltState() => Dump(Find.CurrentMap);

        internal static void Dump(Map map)
        {
            var comp = MapComponent_Belts.For(map);
            var belts = Belts(map);
            var lineIds = new HashSet<int>();
            foreach (var b in belts) lineIds.Add(comp.Sim.LineIdAt(b.Position.ToCell()));
            var items = comp.Sim.AllItems().ToList();
            string temp = belts.Count > 0 ? $" temp={GenTemperature.GetTemperatureForCell(belts[0].Position, map):0.0}C" : "";
            Log.Message($"[CB] belts={belts.Count} lines={lineIds.Count} items={items.Count} stored={comp.Store.Count} requireMotors={comp.Sim.RequireMotors} tick={Find.TickManager.TicksGame}{temp}");
            foreach (var (cell, item) in items.OrderBy(x => x.Cell.X).ThenBy(x => x.Cell.Z).ThenByDescending(x => x.Item.Progress))
            {
                var t = comp.Store.Get(item.Handle);
                string rot = "";
                var rc = t?.TryGetComp<CompRottable>();
                if (rc != null) rot = $" rot={rc.RotProgressPct * 100f:0.00}%";
                Log.Message($"[CB]   item ({cell.X},{cell.Z}) {t?.def.defName ?? "MISSING"} x{t?.stackCount ?? item.Count} (sim {item.Count}) p={item.Progress:0.000} from={item.EnteredFrom} id={item.Handle}{rot}");
            }
            // Rottable floor items near the belts (comparison baseline for the rot checks).
            if (belts.Count == 0) return;
            var area = CellRect.FromLimits(belts.Min(b => b.Position.x), belts.Min(b => b.Position.z),
                                           belts.Max(b => b.Position.x), belts.Max(b => b.Position.z)).ExpandedBy(2).ClipInsideMap(map);
            foreach (var c in area)
                foreach (var t in c.GetThingList(map))
                {
                    var rc = t.TryGetComp<CompRottable>();
                    if (rc != null && t.def.category == ThingCategory.Item)
                        Log.Message($"[CB]   floor ({c.x},{c.z}) {t.def.defName} x{t.stackCount} id={t.thingIDNumber} rot={rc.RotProgressPct * 100f:0.00}%");
                }
        }

        [DebugAction(Cat, "Seed 30 steel on first belt", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        static void Seed30Steel()
        {
            var map = Find.CurrentMap;
            var first = Belts(map).FirstOrDefault();
            if (first == null) { Log.Warning("[CB] no belts"); return; }
            var t = ThingMaker.MakeThing(ThingDefOf.Steel);
            t.stackCount = 30;
            bool ok = MapComponent_Belts.For(map).TryPutOnBelt(first.Position, first.Rotation.Opposite, t);
            if (!ok) t.Destroy();
            Log.Message($"[CB] seed Steel x30 on {first.Position} ({first.def.defName}): {(ok ? "ok" : "refused")}");
        }

        [DebugAction(Cat, "Seed raw meat on floor beside first belt", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        static void SeedFloorMeat()
        {
            var map = Find.CurrentMap;
            var first = Belts(map).FirstOrDefault();
            if (first == null) { Log.Warning("[CB] no belts"); return; }
            var def = DefDatabase<ThingDef>.GetNamedSilentFail("Meat_Cow") ?? DefDatabase<ThingDef>.AllDefs.First(d => d.IsMeat);
            var t = ThingMaker.MakeThing(def);
            t.stackCount = 5;
            var cell = first.Position + IntVec3.South;
            GenSpawn.Spawn(t, cell, map);
            Log.Message($"[CB] spawned {def.defName} x5 on floor at {cell}");
        }

        [DebugAction(Cat, "Test: enclose belts in roofed room", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        static void EncloseBelts()
        {
            var map = Find.CurrentMap;
            var belts = Belts(map);
            if (belts.Count == 0) return;
            var rect = CellRect.FromLimits(belts.Min(b => b.Position.x), belts.Min(b => b.Position.z),
                                           belts.Max(b => b.Position.x), belts.Max(b => b.Position.z)).ExpandedBy(2);
            foreach (var c in rect)
            {
                if (rect.IsOnEdge(c))
                {
                    var wall = ThingMaker.MakeThing(ThingDefOf.Wall, ThingDefOf.Steel);
                    wall.SetFaction(Faction.OfPlayer);
                    GenSpawn.Spawn(wall, c, map);
                }
                else map.roofGrid.SetRoof(c, RoofDefOf.RoofConstructed);
            }
            var room = belts[0].Position.GetRoom(map);
            Log.Message($"[CB] enclosed {rect}; room cells={room?.CellCount} outdoorTemp={room?.UsesOutdoorTemperature}");
        }

        static float? heldTemp;

        static void Hold(float? t) { heldTemp = t; Log.Message($"[CB] held room temperature = {(t.HasValue ? t.Value + "C" : "released")}"); }

        [DebugAction(Cat, "Test: hold belt room at -10C", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        static void HoldCold() => Hold(-10f);

        [DebugAction(Cat, "Test: hold belt room at 25C", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        static void HoldWarm() => Hold(25f);

        [DebugAction(Cat, "Test: release room temperature", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        static void HoldRelease() => Hold(null);

        /// <summary>Test-only: pins the temperature of the first belt's (indoor) room. No-op unless a hold action ran.</summary>
        internal static void ApplyHeldTemperature(Map map)
        {
            if (!heldTemp.HasValue) return;
            var first = Belts(map).FirstOrDefault();
            var room = first?.Position.GetRoom(map);
            if (room != null && !room.UsesOutdoorTemperature) room.Temperature = heldTemp.Value;
        }

        [DebugAction(Cat, "Deconstruct all belts", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        static void DeconstructAllBelts()
        {
            var belts = Belts(Find.CurrentMap);
            foreach (var b in belts) if (!b.Destroyed) b.Destroy(DestroyMode.Deconstruct);
            Log.Message($"[CB] deconstructed {belts.Count} belts");
        }

        [DebugAction(Cat, "Toggle require motors", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        static void ToggleRequireMotors()
        {
            var comp = MapComponent_Belts.For(Find.CurrentMap);
            comp.Sim.RequireMotors = !comp.Sim.RequireMotors;
            Log.Message($"[CB] requireMotors={comp.Sim.RequireMotors}");
        }

        [DebugAction(Cat, "Focus camera on belts", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        static void FocusCamera()
        {
            var belts = Belts(Find.CurrentMap);
            if (belts.Count == 0) return;
            var sum = UnityEngine.Vector3.zero;
            foreach (var b in belts) sum += b.Position.ToVector3Shifted();
            Find.CameraDriver.SetRootPosAndSize(sum / belts.Count, 6f);
        }
    }
}
