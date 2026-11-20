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

        // ---- Input port / Port tab (call the same operations the tab's widgets call) ----

        static List<Building_InputPort> InputPorts(Map map)
            => map.listerThings.AllThings.OfType<Building_InputPort>().OrderBy(p => p.thingIDNumber).ToList();

        static string Describe(Building_InputPort p)
        {
            var sec = p.FilterSection;
            var held = p.HeldThing;
            return $"port {p.thingIDNumber} at {p.Position}: priority={sec.Priority} allowed=[{string.Join(", ", sec.AllowedDefs().Select(d => d.defName))}] " +
                   $"held={(held == null ? "none" : held.def.defName + " x" + held.stackCount)} powered={p.Powered}";
        }

        [DebugAction(Cat, "Allow steel on all input ports", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        static void AllowSteelOnPorts()
        {
            foreach (var p in InputPorts(Find.CurrentMap))
            {
                bool ok = p.FilterSection.Allow(ThingDefOf.Steel);
                Log.Message($"[CB] allow Steel: {ok}; {Describe(p)}");
            }
        }

        [DebugAction(Cat, "Remove steel from all input ports", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        static void RemoveSteelFromPorts()
        {
            foreach (var p in InputPorts(Find.CurrentMap))
            {
                p.FilterSection.Disallow(ThingDefOf.Steel);
                Log.Message($"[CB] remove Steel; {Describe(p)}");
            }
        }

        [DebugAction(Cat, "Cycle input port priority", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        static void CyclePortPriority()
        {
            foreach (var p in InputPorts(Find.CurrentMap))
            {
                var sec = p.FilterSection;
                var next = sec.Priority >= StoragePriority.Critical ? StoragePriority.Low : sec.Priority + 1;
                sec.SetPriority(next);
                Log.Message($"[CB] priority -> {next}; {Describe(p)}");
            }
        }

        [DebugAction(Cat, "Log input ports and search", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        static void LogPorts()
        {
            foreach (var p in InputPorts(Find.CurrentMap))
            {
                Log.Message($"[CB] {Describe(p)}");
                Log.Message($"[CB]   tabs=[{string.Join(", ", p.GetInspectTabs()?.Select(t => t.GetType().Name) ?? Enumerable.Empty<string>())}]");
                foreach (var q in new[] { "ste", "STEEL", "zzzz", "" })
                {
                    var res = p.FilterSection.Search(q, Section_InputFilter.MaxSearchResults, out int total);
                    Log.Message($"[CB]   search '{q}': total={total} shown={res.Count} [{string.Join(", ", res.Select(d => d.label))}]");
                }
                var wide = p.FilterSection.Search("a", Section_InputFilter.MaxSearchResults, out int wideTotal);
                Log.Message($"[CB]   search 'a': total={wideTotal} shown={wide.Count}");
            }
        }

        [DebugAction(Cat, "Select first input port and open Port tab", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        static void OpenPortTab()
        {
            var p = InputPorts(Find.CurrentMap).FirstOrDefault();
            if (p == null) { Log.Warning("[CB] no input port"); return; }
            Find.Selector.ClearSelection();
            Find.Selector.Select(p, playSound: false);
            p.FilterSection.SearchText = "ste";   // what a player would type; the screenshot shows the matches
            PortGizmos.OpenPortTab();
            var pane = (MainTabWindow_Inspect)MainButtonDefOf.Inspect.TabWindow;
            Find.CameraDriver.SetRootPosAndSize(p.Position.ToVector3Shifted(), 8f);
            Log.Message($"[CB] selected {p}; openTab={pane.OpenTabType?.Name ?? "none"} curTabs=[{string.Join(", ", pane.CurTabs?.Select(t => t.GetType().Name) ?? Enumerable.Empty<string>())}]");
        }

        // ---- Output port ----

        static List<Building_OutputPort> OutputPorts(Map map)
            => map.listerThings.AllThings.OfType<Building_OutputPort>().OrderBy(p => p.thingIDNumber).ToList();

        static string Describe(Building_OutputPort p)
            => $"output {p.thingIDNumber} at {p.Position} rot={p.Rotation} mode={p.FullMode} powered={p.Powered} " +
               $"buffer={(p.Buffer == null ? "none" : p.Buffer.def.defName + " x" + p.Buffer.stackCount)}";

        [DebugAction(Cat, "Set output ports to Spill", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        static void OutputsSpill() => SetOutputs(OutputFullMode.Spill);

        [DebugAction(Cat, "Set output ports to Hold", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        static void OutputsHold() => SetOutputs(OutputFullMode.Hold);

        static void SetOutputs(OutputFullMode m)
        {
            foreach (var p in OutputPorts(Find.CurrentMap))
            {
                new Section_OutputModes(p).SetMode(m);   // the call the radio buttons make
                Log.Message($"[CB] {Describe(p)}");
            }
        }

        [DebugAction(Cat, "Log output ports", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        static void LogOutputs()
        {
            foreach (var p in OutputPorts(Find.CurrentMap))
                Log.Message($"[CB] {Describe(p)} tabs=[{string.Join(", ", p.GetInspectTabs()?.Select(t => t.GetType().Name) ?? Enumerable.Empty<string>())}]");
        }

        [DebugAction(Cat, "Destroy output ports", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        static void DestroyOutputs()
        {
            foreach (var p in OutputPorts(Find.CurrentMap))
            {
                Log.Message($"[CB] destroying {Describe(p)}");
                p.Destroy(DestroyMode.Vanish);
            }
        }

        [DebugAction(Cat, "Select first output port and open Port tab", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        static void OpenOutputTab()
        {
            var p = OutputPorts(Find.CurrentMap).FirstOrDefault();
            if (p == null) { Log.Warning("[CB] no output port"); return; }
            Find.Selector.ClearSelection();
            Find.Selector.Select(p, playSound: false);
            PortGizmos.OpenPortTab();
            var pane = (MainTabWindow_Inspect)MainButtonDefOf.Inspect.TabWindow;
            Find.CameraDriver.SetRootPosAndSize(p.Position.ToVector3Shifted(), 8f);
            Log.Message($"[CB] selected {p}; openTab={pane.OpenTabType?.Name ?? "none"} curTabs=[{string.Join(", ", pane.CurTabs?.Select(t => t.GetType().Name) ?? Enumerable.Empty<string>())}]");
        }

        // ---- Pusher port ----

        static List<Building_PusherPort> Pushers(Map map)
            => map.listerThings.AllThings.OfType<Building_PusherPort>().OrderBy(p => p.thingIDNumber).ToList();

        /// <summary>What the machine holds: fuel, wort, frame materials or the items on its storage cells.</summary>
        static string TargetState(Thing t)
        {
            if (t == null) return "none";
            var parts = new List<string> { $"{t.def.defName}#{t.thingIDNumber}" };
            if (t is Frame f)
                parts.Add("container=[" + string.Join(", ", f.resourceContainer.Select(x => x.def.defName + " x" + x.stackCount)) + "] needed=[" +
                          string.Join(", ", f.TotalMaterialCost().Select(c => c.thingDef.defName + " " + f.ThingCountNeeded(c.thingDef) + "/" + c.count)) + "]");
            var fuel = t.TryGetComp<CompRefuelable>();
            if (fuel != null) parts.Add($"fuel={fuel.Fuel:0.##}/{fuel.TargetFuelLevel:0.##} full={fuel.IsFull}");
            if (t is Building_FermentingBarrel b) parts.Add($"wort={25 - b.SpaceLeftForWort} space={b.SpaceLeftForWort}");
            if (t is ISlotGroupParent sg)
                parts.Add("cells=[" + string.Join(", ", sg.AllSlotCellsList().SelectMany(c => c.GetThingList(t.Map))
                          .Where(x => x.def.category == ThingCategory.Item).Select(x => x.def.defName + " x" + x.stackCount)) + "]");
            return string.Join(" ", parts);
        }

        static string Describe(Building_PusherPort p)
        {
            var sec = new Section_PusherModes(p);
            return $"pusher {p.thingIDNumber} at {p.Position} rot={p.Rotation} mode={p.RejectMode} sides={p.EjectSides} powered={p.Powered} " +
                   $"buffer={(p.Buffer == null ? "none" : p.Buffer.def.defName + " x" + p.Buffer.stackCount)} | {sec.FacingLine()} | {sec.AcceptsLine()} | target: {TargetState(p.Target)}";
        }

        static void SetPushers(PusherRejectMode m)
        {
            foreach (var p in Pushers(Find.CurrentMap))
            {
                p.SetRejectMode(m);   // the call the radio buttons make
                Log.Message($"[CB] {Describe(p)}");
            }
        }

        [DebugAction(Cat, "Pushers: eject if never accepted", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        static void PushersEjectNever() => SetPushers(PusherRejectMode.EjectIfNeverAccepted);

        [DebugAction(Cat, "Pushers: eject whenever not accepted", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        static void PushersEjectNow() => SetPushers(PusherRejectMode.EjectIfNotAcceptedNow);

        [DebugAction(Cat, "Pushers: always wait", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        static void PushersWait() => SetPushers(PusherRejectMode.AlwaysWait);

        [DebugAction(Cat, "Pushers: toggle eject side", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        static void PushersToggleSide()
        {
            foreach (var p in Pushers(Find.CurrentMap))
            {
                p.SetEjectSides(p.EjectSides == EjectSides.LeftThenRight ? EjectSides.RightThenLeft : EjectSides.LeftThenRight);   // the dropdown's call
                Log.Message($"[CB] {Describe(p)}");
            }
        }

        [DebugAction(Cat, "Log pushers", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        static void LogPushers()
        {
            foreach (var p in Pushers(Find.CurrentMap))
                Log.Message($"[CB] {Describe(p)} tabs=[{string.Join(", ", p.GetInspectTabs()?.Select(t => t.GetType().Name) ?? Enumerable.Empty<string>())}]");
        }

        [DebugAction(Cat, "Destroy pushers", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        static void DestroyPushers()
        {
            foreach (var p in Pushers(Find.CurrentMap))
            {
                Log.Message($"[CB] destroying {Describe(p)}");
                p.Destroy(DestroyMode.Vanish);
            }
        }

        /// <summary>Clicks (runs the action of) the first port's Copy gizmo, then every other same-kind port's Paste gizmo.</summary>
        [DebugAction(Cat, "Test: copy first port's settings, paste on the rest", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        static void CopyPasteGizmos()
        {
            var map = Find.CurrentMap;
            foreach (var group in new[] { Pushers(map).Cast<Building>().ToList(), OutputPorts(map).Cast<Building>().ToList() })
            {
                if (group.Count < 2) continue;
                Command_Action Gizmo(Building b, string key)
                    => b.GetGizmos().OfType<Command_Action>().FirstOrDefault(g => g.defaultLabel == key.Translate());
                Gizmo(group[0], "CB_CopyMode")?.action();
                foreach (var b in group.Skip(1))
                {
                    var paste = Gizmo(b, "CB_PasteMode");
                    Log.Message($"[CB] paste gizmo on {b}: {(paste == null ? "missing" : "run")}");
                    paste?.action();
                }
            }
            foreach (var p in Pushers(map)) Log.Message($"[CB] {Describe(p)}");
            foreach (var p in OutputPorts(map)) Log.Message($"[CB] {Describe(p)}");
        }

        [DebugAction(Cat, "Select first pusher and open Port tab", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        static void OpenPusherTab()
        {
            var p = Pushers(Find.CurrentMap).FirstOrDefault();
            if (p == null) { Log.Warning("[CB] no pusher"); return; }
            Find.Selector.ClearSelection();
            Find.Selector.Select(p, playSound: false);
            PortGizmos.OpenPortTab();
            var pane = (MainTabWindow_Inspect)MainButtonDefOf.Inspect.TabWindow;
            Find.CameraDriver.SetRootPosAndSize(p.Position.ToVector3Shifted(), 8f);
            Log.Message($"[CB] selected {p}; openTab={pane.OpenTabType?.Name ?? "none"} curTabs=[{string.Join(", ", pane.CurTabs?.Select(t => t.GetType().Name) ?? Enumerable.Empty<string>())}]");
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
