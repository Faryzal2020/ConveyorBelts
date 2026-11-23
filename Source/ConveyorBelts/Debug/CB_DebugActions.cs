using System.Collections.Generic;
using System.Diagnostics;
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

        [DebugAction(Cat, "Test: wealth report", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        static void WealthReport()
        {
            var map = Find.CurrentMap;
            map.wealthWatcher.ForceRecount();
            Log.Message($"[CB] wealth items={map.wealthWatcher.WealthItems:F1} belt+buffers={MapComponent_Belts.For(map).HeldItemsWealth():F1}");
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

        /// <summary>When set (debug action), the next load dumps the belt state right after restoring items (then it clears).</summary>
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

        static void Hold(float? t) { MapComponent_Belts.For(Find.CurrentMap).HeldTemp = t; Log.Message($"[CB] held room temperature = {(t.HasValue ? t.Value + "C" : "released")}"); }

        [DebugAction(Cat, "Test: hold belt room at -10C", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        static void HoldCold() => Hold(-10f);

        [DebugAction(Cat, "Test: hold belt room at 25C", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        static void HoldWarm() => Hold(25f);

        [DebugAction(Cat, "Test: release room temperature", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        static void HoldRelease() => Hold(null);

        /// <summary>Test-only: pins the temperature of the first belt's (indoor) room. No-op unless a hold action ran.</summary>
        internal static void ApplyHeldTemperature(Map map)
        {
            var held = MapComponent_Belts.For(map)?.HeldTemp;
            if (!held.HasValue) return;
            var first = Belts(map).FirstOrDefault();
            var room = first?.Position.GetRoom(map);
            if (room != null && !room.UsesOutdoorTemperature) room.Temperature = held.Value;
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
            var held = p.HeldThing;
            return $"port {p.thingIDNumber} at {p.Position}: priority={p.PrioritySection.Priority} allowed={p.FilterSection.AllowedDefs().Count} " +
                   $"held={(held == null ? "none" : held.def.defName + " x" + held.stackCount)} powered={p.Powered}";
        }

        static string Check(bool ok) => ok ? "PASS" : "FAIL";

        /// <summary>Runs the filter operations in the order a player would click them and logs PASS/FAIL after each.</summary>
        [DebugAction(Cat, "Test: filter ops on input ports", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        static void TestFilterOps()
        {
            var raw = ThingCategoryDefOf.ResourcesRaw;
            var steel = ThingDefOf.Steel;
            foreach (var p in InputPorts(Find.CurrentMap))
            {
                var s = p.FilterSection;
                var filter = p.GetStoreSettings().filter;
                string State() => $"allowed={s.AllowedDefs().Count} raw={s.CategoryState(raw)} steel={filter.Allows(steel)}";
                Log.Message($"[CB] filter test on port {p.thingIDNumber}: listable defs={s.AllDefs().Count}");
                s.SearchText = "";

                s.ClearAll();
                Log.Message($"[CB]   ClearAll: {State()} filterCount={filter.AllowedDefCount} -> {Check(s.AllowedDefs().Count == 0 && filter.AllowedDefCount == 0)}");

                s.SetCategory(raw, true);
                int n = s.AllowedDefs().Count;
                Log.Message($"[CB]   SetCategory(ResourcesRaw, true): {State()} [{string.Join(", ", s.AllowedDefs().Select(d => d.defName))}] -> {Check(n > 0 && s.CategoryState(raw) == MultiCheckboxState.On)}");

                s.SetItem(steel, false);
                bool steelListed = s.AllowedDefs().Contains(steel);
                Log.Message($"[CB]   SetItem(Steel, false): {State()} steelInAllowed={steelListed} -> {Check(s.CategoryState(raw) == MultiCheckboxState.Partial && !steelListed && s.AllowedDefs().Count == n - 1)}");

                s.SearchText = "ste";
                var res = s.Results();
                bool hasSteel = res.Any(r => r.Def == steel);
                int cats = res.Count(r => r.Category != null);
                string List(List<FilterSearchResult> rs) => string.Join("; ", rs.Select(r => r.Label + " (" + r.Tag + ")" + (r.Def != null ? (filter.Allows(r.Def) ? " on" : " off") : " " + s.CategoryState(r.Category))));
                // No core category label contains "ste"; categories are checked with "stone" below.
                Log.Message($"[CB]   SearchText='ste': {res.Count} results, categories={cats}, allAllowed={s.AllResultsAllowed()} -> {Check(hasSteel)}");
                Log.Message($"[CB]     [{List(res)}]");
                s.SearchText = "stone";
                var stone = s.Results();
                int stoneCats = stone.Count(r => r.Category != null);
                Log.Message($"[CB]   SearchText='stone': {stone.Count} results, categories={stoneCats} -> {Check(stoneCats >= 1 && stone.Any(r => r.Def != null))}");
                Log.Message($"[CB]     [{List(stone)}]");
                s.SearchText = "STEEL";
                Log.Message($"[CB]   SearchText='STEEL' (case): {s.Results().Count} results, steel={s.Results().Any(r => r.Def == steel)}");
                s.SearchText = "ste";

                s.SetAllResults(true);
                Log.Message($"[CB]   SetAllResults(true): {State()} allAllowed={s.AllResultsAllowed()} -> {Check(filter.Allows(steel) && s.AllResultsAllowed())}");
                s.SetAllResults(false);
                Log.Message($"[CB]   SetAllResults(false): {State()} allAllowed={s.AllResultsAllowed()} -> {Check(!filter.Allows(steel) && res.All(r => r.Def == null || !filter.Allows(r.Def)))}");
                s.SetAllResults(true);
                s.SearchText = "";

                s.AllowAll();
                var fixedDefs = p.def.building.fixedStorageSettings.filter.AllowedThingDefs.ToList();
                var missing = fixedDefs.Where(d => !filter.Allows(d)).ToList();
                Log.Message($"[CB]   AllowAll: {State()} listable={s.AllDefs().Count} fixedDefs={fixedDefs.Count} notAllowed={missing.Count} " +
                            $"[{string.Join(", ", missing.Take(20).Select(d => d.defName + (d.PlayerAcquirable ? "" : "(!acquirable)") + (d.virtualDefParent != null ? "(virtual)" : "")))}] " +
                            $"-> {Check(s.AllDefs().All(filter.Allows) && s.CategoryState(raw) == MultiCheckboxState.On)}");

                var managed = s.ManagedSpecialFilters();
                var offSpecial = managed.Where(sf => !filter.Allows(sf)).Select(sf => sf.defName).ToList();
                var fixedOff = DefDatabase<SpecialThingFilterDef>.AllDefs.Where(sf => !p.def.building.fixedStorageSettings.filter.Allows(sf)).Select(sf => sf.defName);
                Log.Message($"[CB]   AllowAll special filters: managed={managed.Count} disallowed=[{string.Join(", ", offSpecial)}] fixedFilterDisallows=[{string.Join(", ", fixedOff)}] -> {Check(offSpecial.Count == 0)}");

                // Special filters follow the category (corpses: colonist / stranger).
                var corpses = ThingCategoryDefOf.Corpses;
                var colonist = DefDatabase<SpecialThingFilterDef>.GetNamed("AllowCorpsesColonist");
                var stranger = DefDatabase<SpecialThingFilterDef>.GetNamed("AllowCorpsesStranger");
                string Corpses() => $"corpses={s.CategoryState(corpses)} colonistSF={filter.Allows(colonist)} strangerSF={filter.Allows(stranger)}";
                s.ClearAll();
                Log.Message($"[CB]   ClearAll: {Corpses()} managedOff={managed.Count(sf => !filter.Allows(sf))}/{managed.Count} -> {Check(!filter.Allows(colonist) && managed.All(sf => !filter.Allows(sf)))}");
                s.SetCategory(corpses, true);
                Log.Message($"[CB]   SetCategory(Corpses, true): {Corpses()} -> {Check(s.CategoryState(corpses) == MultiCheckboxState.On && filter.Allows(colonist) && filter.Allows(stranger))}");
                filter.SetAllow(colonist, false);   // outside change: same def count, no tab toggle
                Log.Message($"[CB]   outside SetAllow(AllowCorpsesColonist, false): {Corpses()} -> {Check(s.CategoryState(corpses) == MultiCheckboxState.Partial)}");
                s.SetCategory(corpses, false);
                Log.Message($"[CB]   SetCategory(Corpses, false): {Corpses()} -> {Check(s.CategoryState(corpses) == MultiCheckboxState.Off && !filter.Allows(colonist))}");
                var human = DefDatabase<ThingDef>.GetNamed("Corpse_Human");
                s.SetItem(human, true);
                Log.Message($"[CB]   SetItem(Corpse_Human, true): {Corpses()} -> {Check(filter.Allows(human) && filter.Allows(colonist) && filter.Allows(stranger))}");
                s.SearchText = "corpses";
                s.SetAllResults(false);
                Log.Message($"[CB]   'corpses' SetAllResults(false): {Corpses()} -> {Check(s.CategoryState(corpses) == MultiCheckboxState.Off && !filter.Allows(colonist))}");
                s.SetAllResults(true);
                Log.Message($"[CB]   'corpses' SetAllResults(true): {Corpses()} allAllowed={s.AllResultsAllowed()} -> {Check(s.CategoryState(corpses) == MultiCheckboxState.On && filter.Allows(colonist))}");
                s.SearchText = "";

                // Stale-cache check: swap one allowed def for another outside the tab (same count, no toggle).
                s.ClearAll();
                s.SetCategory(raw, true);
                int before = s.AllowedDefs().Count;
                var meal = ThingDefOf.MealSimple;
                filter.SetAllow(steel, false);
                filter.SetAllow(meal, true);
                var after = s.AllowedDefs();
                Log.Message($"[CB]   outside swap Steel -> {meal.defName}: allowed {before} -> {after.Count} steel={after.Contains(steel)} meal={after.Contains(meal)} raw={s.CategoryState(raw)} -> {Check(after.Count == before && !after.Contains(steel) && after.Contains(meal) && s.CategoryState(raw) == MultiCheckboxState.Partial)}");
                s.ClearAll();
                Log.Message($"[CB]   reset: {Describe(p)}");
            }

            // fixedFilter = null (what the Pusher will pass): every storable def, corpses included.
            var own = new ThingFilter();
            var free = new Section_ItemFilter(() => own, () => null);
            var corpse = DefDatabase<ThingDef>.AllDefs.FirstOrDefault(d => d.IsCorpse && d.PlayerAcquirable);
            bool corpseListed = corpse != null && free.AllDefs().Contains(corpse);
            free.AllowAll();
            Log.Message($"[CB]   null fixed filter: listable={free.AllDefs().Count} corpse={corpse?.defName} listed={corpseListed} " +
                        $"corpses={free.CategoryState(ThingCategoryDefOf.Corpses)} allowed={free.AllowedDefs().Count} -> " +
                        $"{Check(corpseListed && free.AllowedDefs().Count == free.AllDefs().Count && free.CategoryState(ThingCategoryDefOf.Corpses) == MultiCheckboxState.On)}");
            free.ClearAll();
            Log.Message($"[CB]   null fixed filter ClearAll: allowed={free.AllowedDefs().Count} own={own.AllowedDefCount}");
        }

        /// <summary>Review focus 5: allow Raw resources, ✕ Steel, priority Critical. Spawn steel + wood nearby and tick.</summary>
        [DebugAction(Cat, "Test: input ports allow raw resources except steel", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        static void PortsRawExceptSteel()
        {
            foreach (var p in InputPorts(Find.CurrentMap))
            {
                var s = p.FilterSection;
                s.ClearAll();
                s.SetCategory(ThingCategoryDefOf.ResourcesRaw, true);
                s.SetItem(ThingDefOf.Steel, false);
                p.PrioritySection.SetPriority(StoragePriority.Critical);
                Log.Message($"[CB] {Describe(p)} raw={s.CategoryState(ThingCategoryDefOf.ResourcesRaw)} steel={p.GetStoreSettings().filter.Allows(ThingDefOf.Steel)} " +
                            $"wood={p.GetStoreSettings().filter.Allows(ThingDefOf.WoodLog)} acceptsSteel={p.Accepts(ThingMaker.MakeThing(ThingDefOf.Steel))} acceptsWood={p.Accepts(ThingMaker.MakeThing(ThingDefOf.WoodLog))}");
            }
        }

        static void SetCorpseFilter(bool allCorpses)
        {
            foreach (var p in InputPorts(Find.CurrentMap))
            {
                var s = p.FilterSection;
                s.ClearAll();
                if (allCorpses) s.SetCategory(ThingCategoryDefOf.Corpses, true);
                else s.SetCategory(DefDatabase<ThingCategoryDef>.GetNamed("CorpsesAnimal"), true);
                p.PrioritySection.SetPriority(StoragePriority.Critical);
                var f = p.GetStoreSettings().filter;
                Log.Message($"[CB] {Describe(p)} corpses={s.CategoryState(ThingCategoryDefOf.Corpses)} humanDef={f.Allows(DefDatabase<ThingDef>.GetNamed("Corpse_Human"))} " +
                            $"colonistSF={f.Allows(DefDatabase<SpecialThingFilterDef>.GetNamed("AllowCorpsesColonist"))}");
            }
        }

        [DebugAction(Cat, "Test: input ports allow all corpses", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        static void PortsAllCorpses() => SetCorpseFilter(true);

        [DebugAction(Cat, "Test: input ports allow animal corpses only", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        static void PortsAnimalCorpses() => SetCorpseFilter(false);

        /// <summary>Generates a colonist two cells east of the first input port and kills it (a colonist corpse).</summary>
        [DebugAction(Cat, "Test: colonist corpse beside first input port", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        static void ColonistCorpse()
        {
            var map = Find.CurrentMap;
            var p = InputPorts(map).FirstOrDefault();
            if (p == null) { Log.Warning("[CB] no input port"); return; }
            var pawn = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
            GenSpawn.Spawn(pawn, p.Position + new IntVec3(2, 0, 0), map);
            pawn.Kill(null);
            var corpse = pawn.Corpse;
            Log.Message($"[CB] colonist corpse id={corpse?.thingIDNumber} at {corpse?.Position} accepted={(corpse != null && p.Accepts(corpse))}");
        }

        [DebugAction(Cat, "Test: log corpses and input ports", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        static void LogCorpses()
        {
            var map = Find.CurrentMap;
            foreach (var t in map.listerThings.ThingsInGroup(ThingRequestGroup.Corpse))
                if (t is Corpse c && c.InnerPawn.RaceProps.Humanlike)
                    Log.Message($"[CB] corpse {c.thingIDNumber} {c.InnerPawn.LabelShort} faction={c.InnerPawn.Faction?.Name ?? "none"} at {c.Position} spawned={c.Spawned} " +
                                $"onPort={c.Spawned && c.Position.GetFirstThing<Building_InputPort>(map) != null} acceptedByPort={InputPorts(map).FirstOrDefault()?.Accepts(c)}");
            foreach (var p in InputPorts(map)) Log.Message($"[CB] {Describe(p)}");
        }

        [DebugAction(Cat, "Test: log steel and wood positions", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        static void LogSteelWood()
        {
            var map = Find.CurrentMap;
            foreach (var def in new[] { ThingDefOf.Steel, ThingDefOf.WoodLog })
                foreach (var t in map.listerThings.ThingsOfDef(def))
                    Log.Message($"[CB] {def.defName} x{t.stackCount} id={t.thingIDNumber} at {t.Position} spawned={t.Spawned} onPort={t.Position.GetFirstThing<Building_InputPort>(map) != null} carriedBy={(t.ParentHolder as Pawn_CarryTracker)?.pawn?.LabelShort ?? "-"}");
            foreach (var p in InputPorts(map)) Log.Message($"[CB] {Describe(p)}");
        }

        [DebugAction(Cat, "Cycle input port priority", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        static void CyclePortPriority()
        {
            foreach (var p in InputPorts(Find.CurrentMap))
            {
                var sec = p.PrioritySection;
                var next = sec.Priority >= StoragePriority.Critical ? StoragePriority.Low : sec.Priority + 1;
                sec.SetPriority(next);
                Log.Message($"[CB] priority -> {next}; {Describe(p)}");
            }
        }

        [DebugAction(Cat, "Log input ports", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        static void LogPorts()
        {
            foreach (var p in InputPorts(Find.CurrentMap))
            {
                Log.Message($"[CB] {Describe(p)} allowedDefs=[{string.Join(", ", p.FilterSection.AllowedDefs().Select(d => d.defName))}]");
                Log.Message($"[CB]   tabs=[{string.Join(", ", p.GetInspectTabs()?.Select(t => t.GetType().Name) ?? Enumerable.Empty<string>())}]");
            }
        }

        /// <summary>Selects the first input port, opens its Port tab (browsing, Raw resources and Weapons expanded) for a screenshot.</summary>
        [DebugAction(Cat, "Select first input port and open Port tab", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        static void OpenPortTab()
        {
            var p = InputPorts(Find.CurrentMap).FirstOrDefault();
            if (p == null) { Log.Warning("[CB] no input port"); return; }
            Find.Selector.ClearSelection();
            Find.Selector.Select(p, playSound: false);
            var s = p.FilterSection;
            s.SearchText = "";
            foreach (var name in new[] { "ResourcesRaw", "Weapons" })
                s.SetExpanded(DefDatabase<ThingCategoryDef>.GetNamedSilentFail(name), true);
            PortGizmos.OpenPortTab();
            var pane = (MainTabWindow_Inspect)MainButtonDefOf.Inspect.TabWindow;
            Find.CameraDriver.SetRootPosAndSize(p.Position.ToVector3Shifted(), 8f);
            Log.Message($"[CB] selected {p}; openTab={pane.OpenTabType?.Name ?? "none"} curTabs=[{string.Join(", ", pane.CurTabs?.Select(t => t.GetType().Name) ?? Enumerable.Empty<string>())}]");
        }

        /// <summary>Types text into the first input port's search box (what a player would type).</summary>
        static void Search(string text)
        {
            var p = InputPorts(Find.CurrentMap).FirstOrDefault();
            if (p == null) { Log.Warning("[CB] no input port"); return; }
            p.FilterSection.SearchText = text;
            Log.Message($"[CB] search '{text}': {p.FilterSection.Results().Count} results");
        }

        [DebugAction(Cat, "Port tab: search 'ste' on first input port", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        static void SearchSte() => Search("ste");

        [DebugAction(Cat, "Port tab: search 'stone' on first input port", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        static void SearchStone() => Search("stone");

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

        /// <summary>For every pusher: what it recognises behind / in front, every pull candidate, and Room for the first. Moves nothing.</summary>
        [DebugAction(Cat, "Test: pusher sources and targets", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        static void PusherSourcesTargets()
        {
            var map = Find.CurrentMap;
            foreach (var p in Pushers(map))
            {
                var facing = p.Rotation.ToDir();
                var behind = PullSources.CellBehind(p.Position, facing);
                var front = PushTargets.CellInFront(p.Position, facing);
                Log.Message($"[CB] pusher ({p.Position.x},{p.Position.z}) facing {facing}: pulling from {PullSources.Describe(behind, map, facing) ?? "None"}, " +
                            $"pushing to {PushTargets.Describe(front, map, facing) ?? "None"}");
                var cands = PullSources.At(behind, map, facing, includeForbidden: true).ToList();
                int notForbidden = PullSources.At(behind, map, facing, includeForbidden: false).Count();
                Log.Message($"[CB]   candidates {cands.Count} (without forbidden: {notForbidden})");
                foreach (var c in cands)
                {
                    int rm = PushTargets.Room(front, map, facing, c.Thing, out _);
                    Log.Message($"[CB]   candidate {c.Thing.def.defName} x{c.Available} from {c.SourceLabel}{(c.WholeBatch ? " (whole batch)" : "")} room={rm}");
                }
                if (cands.Count > 0)
                {
                    int room = PushTargets.Room(front, map, facing, cands[0].Thing, out var target);
                    Log.Message($"[CB]   first candidate: room={room} target={(target == null ? "none" : PushTargets.Describe(front, map, facing))}");
                }
            }
        }

        /// <summary>Everything of def on the map: spawned, on belts, in building/comp containers, as barrel product.</summary>
        static int TotalOf(Map map, ThingDef def)
        {
            var comp = MapComponent_Belts.For(map);
            int n = map.listerThings.ThingsOfDef(def).Sum(t => t.stackCount);
            foreach (var (_, item) in comp.Sim.AllItems()) { var t = comp.Store.Get(item.Handle); if (t?.def == def) n += t.stackCount; }
            foreach (var b in map.listerBuildings.allBuildingsColonist)
            {
                if (b is IThingHolder h && h.GetDirectlyHeldThings() is ThingOwner o) n += o.TotalStackCountOfDef(def);
                foreach (var cp in b.AllComps)
                    if (cp is IThingHolder ch && ch.GetDirectlyHeldThings() is ThingOwner co) n += co.TotalStackCountOfDef(def);
                var a = ProductAdapters.For(b);
                var peek = a?.Peek(b);
                if (peek != null && peek.def == def) n += peek.stackCount;
            }
            return n;
        }

        /// <summary>
        /// For every pusher, one move the way Task 4's pusher will do it: first candidate the target has room for,
        /// amount = min(available, stack limit, room) (products: whole batch only), Take then Give. Belt sources move at
        /// most 2 so the split path (remainder stays on the belt) runs. Logs the def's map total before/after.
        /// </summary>
        [DebugAction(Cat, "Test: pusher one move each (take+give)", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        static void PusherOneMove()
        {
            var map = Find.CurrentMap;
            foreach (var p in Pushers(map))
            {
                var facing = p.Rotation.ToDir();
                var behind = PullSources.CellBehind(p.Position, facing);
                var front = PushTargets.CellInFront(p.Position, facing);
                var tag = $"[CB] move ({p.Position.x},{p.Position.z})";
                bool moved = false;
                foreach (var c in PullSources.At(behind, map, facing, includeForbidden: true))
                {
                    int room = PushTargets.Room(front, map, facing, c.Thing, out var target);
                    if (room <= 0) continue;
                    int n = System.Math.Min(c.Available, System.Math.Min(c.Thing.def.stackLimit, room));
                    if (c.SourceLabel == PullSources.BeltLabel) n = System.Math.Min(n, 2);
                    var def = c.Thing.def;
                    int before = TotalOf(map, def);
                    var part = c.Take(n);
                    if (part == null)
                    {
                        Log.Message($"{tag}: Take({n}) of {def.defName} x{c.Available} (room {room}) returned null; total {before} -> {TotalOf(map, def)}");
                        moved = true; break;
                    }
                    string taken = $"took {part.def.defName} x{part.stackCount} (asked {n}) spawned={part.Spawned} owner={(part.holdingOwner == null ? "none" : part.holdingOwner.Owner.ToString())}";
                    PushTargets.Give(target, part);
                    bool left = !part.Destroyed && !part.Spawned && part.holdingOwner == null && part.stackCount > 0;
                    if (left) GenPlace.TryPlaceThing(part, p.Position, map, ThingPlaceMode.Near);   // test only: never lose it
                    int after = TotalOf(map, def);
                    var rest = PullSources.At(behind, map, facing, includeForbidden: true).Where(x => x.Thing.def == def).Sum(x => x.Available);
                    Log.Message($"{tag}: {taken} from {c.SourceLabel} -> {PushTargets.Describe(front, map, facing)} (room was {room}); " +
                                $"leftover={(left ? part.stackCount : 0)}; source now {rest}; room now {(part.Destroyed ? "?" : PushTargets.Room(front, map, facing, part, out _).ToString())}; total {def.defName} {before} -> {after}");
                    moved = true;
                    break;
                }
                if (!moved) Log.Message($"{tag}: nothing to move");
            }
        }

        /// <summary>
        /// For every pusher whose first candidate sits inside a container (held, not spawned, not a product): Take(15)
        /// and log Available before, the part taken, and what is left inside. The part is dropped beside the pusher.
        /// </summary>
        [DebugAction(Cat, "Test: take 15 from container candidates", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        static void TakeFromContainers()
        {
            var map = Find.CurrentMap;
            foreach (var p in Pushers(map))
            {
                var facing = p.Rotation.ToDir();
                var behind = PullSources.CellBehind(p.Position, facing);
                var c = PullSources.At(behind, map, facing, includeForbidden: true).FirstOrDefault(x => !x.WholeBatch && !x.Thing.Spawned && x.Thing.holdingOwner != null
                                                                                                        && x.Thing.holdingOwner.Owner is not BeltItemStore);
                if (c == null) continue;
                var owner = c.Thing.holdingOwner;
                var def = c.Thing.def;
                int before = c.Available, inOwner = owner.TotalStackCountOfDef(def), total = TotalOf(map, def);
                var part = c.Take(15);
                int leftInside = owner.TotalStackCountOfDef(def);
                int again = PullSources.At(behind, map, facing, includeForbidden: true).Where(x => x.Thing.def == def).Sum(x => x.Available);
                Log.Message($"[CB] take ({p.Position.x},{p.Position.z}) from {c.SourceLabel}: {def.defName} available {before} (inside {inOwner}); " +
                            $"Take(15) -> {part?.def.defName} x{part?.stackCount} spawned={part?.Spawned} owner={(part?.holdingOwner == null ? "none" : "SET")}; " +
                            $"inside now {leftInside}; candidates now {again}");
                if (part != null) GenPlace.TryPlaceThing(part, p.Position + IntVec3.North, map, ThingPlaceMode.Near);
                Log.Message($"[CB] take ({p.Position.x},{p.Position.z}): total {def.defName} {total} -> {TotalOf(map, def)} (part dropped on the map)");
            }
        }

        /// <summary>
        /// Builds one east-facing (unpowered, so the v1 pusher accepts nothing from belts) pusher per row on a clear
        /// area near the map centre: A shelf+steel / belt, B belt in (steel) / shelf, C belt sideways (steel) / ground,
        /// D fermented barrel / shelf, E cryptosleep casket with a colonist / ground, F colonist corpse / grave,
        /// G fermented barrel / basic belt (max stack 10 &lt; 25 beer), H cryptosleep casket holding Steel x40 / shelf,
        /// I transport pod holding Steel x40 + WoodLog x30 / torch lamp (empty).
        /// Sources are forbidden so colonists leave them alone (the test action pulls from forbidden).
        /// Belt items only move once "Toggle require motors" turned motors off.
        /// </summary>
        [DebugAction(Cat, "Test: build pusher source/target scenarios", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        static void BuildPusherScenarios()
        {
            var map = Find.CurrentMap;
            var comp = MapComponent_Belts.For(map);
            IntVec3 o = IntVec3.Invalid;
            for (int r = 0; r < 60 && !o.IsValid; r++)
                foreach (var c in GenRadial.RadialCellsAround(map.Center, r, true))
                {
                    var rect = new CellRect(c.x, c.z, 16, 30);
                    if (rect.InBounds(map) && rect.Cells.All(x => x.GetTerrain(map).passability != Traversability.Impassable && !x.Fogged(map)
                                                                   && !x.GetThingList(map).Any(t => t.Faction == Faction.OfPlayer || !t.def.destroyable)))
                    { o = c; break; }
                }
            if (!o.IsValid) { Log.Warning("[CB] no 16x30 area on passable terrain"); return; }
            // Clear it: rocks, chunks, plants, loose items (pawns stay; areas with undestroyable things such as geysers are not chosen).
            foreach (var x in new CellRect(o.x, o.z, 16, 30))
                foreach (var t in x.GetThingList(map).Where(t => !(t is Pawn) && t.def.destroyable).ToList()) t.Destroy();

            var basic = DefDatabase<ThingDef>.GetNamed("CB_Belt_Basic");
            Thing Spawn(string defName, IntVec3 cell, bool behind)
            {
                var def = DefDatabase<ThingDef>.GetNamed(defName);
                var t = ThingMaker.MakeThing(def, def.MadeFromStuff ? GenStuff.DefaultStuffFor(def) : null);
                t.SetFactionDirect(Faction.OfPlayer);
                // Pick a rotation whose footprint stays on its own side of the pusher.
                foreach (var rot in new[] { Rot4.North, Rot4.East, Rot4.South, Rot4.West })
                {
                    var r = GenAdj.OccupiedRect(cell, rot, def.size);
                    if (behind ? r.maxX <= cell.x : r.minX >= cell.x) return GenSpawn.Spawn(t, cell, map, rot);
                }
                return GenSpawn.Spawn(t, cell, map);
            }
            Thing Steel(int n) { var s = ThingMaker.MakeThing(ThingDefOf.Steel); s.stackCount = n; return s; }

            for (int row = 0; row < 9; row++)
            {
                var pos = new IntVec3(o.x + 3 + row / 7 * 8, 0, o.z + 2 + row % 7 * 4);   // rows H, I in a second column
                var behind = pos + IntVec3.West;
                var front = pos + IntVec3.East;
                var pusher = ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("CB_PusherPort"));
                pusher.SetFactionDirect(Faction.OfPlayer);
                GenSpawn.Spawn(pusher, pos, map, Rot4.East);
                switch (row)
                {
                    case 0:
                        Spawn("Shelf", behind, true);
                        GenSpawn.Spawn(Steel(75), behind, map).SetForbidden(true, warnOnFail: false);
                        BeltGizmos.SpawnBelt(basic, front, Rot4.East, map, null);
                        break;
                    case 1:
                        BeltGizmos.SpawnBelt(basic, behind, Rot4.East, map, null);
                        var s1 = Steel(5);
                        if (!comp.TryPutOnBelt(behind, Rot4.West, s1)) s1.Destroy();
                        Spawn("Shelf", front, false);
                        break;
                    case 2:
                        BeltGizmos.SpawnBelt(basic, behind, Rot4.North, map, null);
                        BeltGizmos.SpawnBelt(basic, behind + IntVec3.North, Rot4.North, map, null);
                        var s2 = Steel(5);
                        if (!comp.TryPutOnBelt(behind, Rot4.South, s2)) s2.Destroy();
                        break;
                    case 3:
                        var barrel = (Building_FermentingBarrel)Spawn("FermentingBarrel", behind, true);
                        barrel.AddWort(25);
                        barrel.Progress = 1f;
                        barrel.SetForbidden(true, warnOnFail: false);
                        Spawn("Shelf", front, false);
                        break;
                    case 4:
                        var casket = (Building_CryptosleepCasket)Spawn("CryptosleepCasket", behind, true);
                        casket.SetForbidden(true, warnOnFail: false);
                        var sleeper = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
                        Log.Message($"[CB] casket accepts colonist: {casket.TryAcceptThing(sleeper)} contents={casket.GetDirectlyHeldThings().Count}");
                        break;
                    case 5:
                        var dead = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
                        GenSpawn.Spawn(dead, behind, map);
                        dead.Kill(null);
                        dead.Corpse?.SetForbidden(true, warnOnFail: false);
                        Spawn("Grave", front, false);
                        break;
                    case 6:
                        var barrel2 = (Building_FermentingBarrel)Spawn("FermentingBarrel", behind, true);
                        barrel2.AddWort(25);
                        barrel2.Progress = 1f;
                        BeltGizmos.SpawnBelt(basic, front, Rot4.East, map, null);
                        break;
                    case 7:
                        var box = (Building_CryptosleepCasket)Spawn("CryptosleepCasket", behind, true);
                        box.SetForbidden(true, warnOnFail: false);
                        Log.Message($"[CB] casket accepts Steel x40: {box.TryAcceptThing(Steel(40))} contents={box.GetDirectlyHeldThings().ContentsString}");
                        Spawn("Shelf", front, false);
                        break;
                    case 8:
                        var pod = (ThingWithComps)Spawn("TransportPod", behind, true);
                        var held = pod.GetComp<CompTransporter>().GetDirectlyHeldThings();
                        var wood = ThingMaker.MakeThing(ThingDefOf.WoodLog); wood.stackCount = 30;
                        held.TryAdd(Steel(40)); held.TryAdd(wood);
                        var lamp = Spawn("TorchLamp", front, false);
                        var fuel = lamp.TryGetComp<CompRefuelable>();
                        if (fuel != null && fuel.Fuel > 0) fuel.ConsumeFuel(fuel.Fuel);
                        Log.Message($"[CB] transport pod holds {held.ContentsString}; torch fuel={fuel?.Fuel:0.0}");
                        break;
                }
            }
            Find.CameraDriver.SetRootPosAndSize((o + new IntVec3(4, 0, 12)).ToVector3Shifted(), 14f);
            Log.Message($"[CB] pusher scenarios built at origin ({o.x},{o.z}); pushers at x={o.x + 3} z={o.z + 2}..{o.z + 26} step 4, and x={o.x + 11} z={o.z + 2},{o.z + 6}");
        }

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

        [DebugAction(Cat, "Test: list belt float menu options at first item cell", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        static void TestListFloatMenu()
        {
            var map = Find.CurrentMap;
            var comp = MapComponent_Belts.For(map);
            var pawn = map.mapPawns.FreeColonistsSpawned.FirstOrDefault();
            var first = comp.Sim.AllItems().OrderBy(x => x.Cell.X).ThenBy(x => x.Cell.Z).Cast<(Cell Cell, BeltItem Item)?>().FirstOrDefault();
            if (pawn == null || first == null) { Log.Warning("[CB] float menu test: need a colonist and a belt item"); return; }
            var cell = first.Value.Cell.ToIntVec3();
            Log.Message($"[CB] float menu test: pawn={pawn.LabelShort} cell={cell}");
            foreach (var o in FloatMenuOptionProvider_TakeFromBelt.BuildOptions(pawn, map, cell))
                Log.Message($"[CB]   direct option: {o.Label} (disabled={o.Disabled})");
            var real = FloatMenuMakerMap.GetOptions(new List<Pawn> { pawn }, cell.ToVector3Shifted(), out _);
            foreach (var o in real)
                if (o.Label.Contains("stockpile") || o.Label.StartsWith("Pick up") || o.Label.StartsWith("Cannot"))
                    Log.Message($"[CB]   vanilla pipeline option: {o.Label}");
        }

        [DebugAction(Cat, "Test: steel totals (belts, ground, pawn inventories)", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        static void TestSteelTotals()
        {
            var map = Find.CurrentMap;
            var comp = MapComponent_Belts.For(map);
            int belt = 0, ground = 0, inv = 0;
            foreach (var (_, item) in comp.Sim.AllItems()) { var t = comp.Store.Get(item.Handle); if (t?.def == ThingDefOf.Steel) belt += t.stackCount; }
            foreach (var t in map.listerThings.ThingsOfDef(ThingDefOf.Steel)) ground += t.stackCount;
            foreach (var p in map.mapPawns.FreeColonistsSpawned)
            {
                int n = p.inventory.innerContainer.TotalStackCountOfDef(ThingDefOf.Steel);
                if (n > 0) Log.Message($"[CB]   {p.LabelShort} (id {p.thingIDNumber}) carries {n} steel in inventory");
                inv += n;
            }
            Log.Message($"[CB] steel totals: belt={belt} ground={ground} inventory={inv}");
        }

        [DebugAction(Cat, "Test: seed 75 steel on first belt", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        static void Seed75Steel()
        {
            var map = Find.CurrentMap;
            var first = Belts(map).FirstOrDefault();
            if (first == null) { Log.Warning("[CB] no belts"); return; }
            var t = ThingMaker.MakeThing(ThingDefOf.Steel);
            t.stackCount = 75;
            bool ok = MapComponent_Belts.For(map).TryPutOnBelt(first.Position, first.Rotation.Opposite, t);
            if (!ok) t.Destroy();
            Log.Message($"[CB] seed Steel x75 on {first.Position} ({first.def.defName}) id={t.thingIDNumber}: {(ok ? "ok" : "refused")}");
        }

        [DebugAction(Cat, "Test: destroy all shelves", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        static void DestroyShelves()
        {
            var shelves = Find.CurrentMap.listerThings.AllThings.Where(t => t.def.defName == "Shelf").ToList();
            foreach (var t in shelves) t.Destroy();
            Log.Message($"[CB] destroyed {shelves.Count} shelves");
        }

        [DebugAction(Cat, "Test: disable Hauling on first colonist", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        static void DisableHauling()
        {
            var pawn = Find.CurrentMap.mapPawns.FreeColonistsSpawned.First();
            pawn.WorkTypeIsDisabled(WorkTypeDefOf.Hauling);   // builds the caches
            var f = typeof(Pawn).GetField("cachedDisabledWorkTypes", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var list = (List<WorkTypeDef>)f.GetValue(pawn);
            if (!list.Contains(WorkTypeDefOf.Hauling)) list.Add(WorkTypeDefOf.Hauling);
            Log.Message($"[CB] {pawn.LabelShort} (id {pawn.thingIDNumber}) hauling disabled={pawn.WorkTypeIsDisabled(WorkTypeDefOf.Hauling)}");
        }

        [DebugAction(Cat, "Test: log colonist carry capacity", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        static void LogCapacity()
        {
            foreach (var p in Find.CurrentMap.mapPawns.FreeColonistsSpawned)
                Log.Message($"[CB] {p.LabelShort} (id {p.thingIDNumber}) capacity={MassUtility.Capacity(p):0.0}kg used={MassUtility.GearAndInventoryMass(p):0.0}kg steel-fit={MassUtility.CountToPickUpUntilOverEncumbered(p, ThingMaker.MakeThing(ThingDefOf.Steel))}");
        }

        // ---- Performance test layouts (Task 14, final review I2) ----

        [DebugAction(Cat, "Perf: build test layout", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        static void PerfBuild() => PerfBuildLayout(cols: 1, rows: 20, len: 100);

        /// <summary>286 lines x 7 tiles (13 columns x 22 rows of line segments): the many-short-lines worst case.</summary>
        [DebugAction(Cat, "Perf: build short-line layout", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        static void PerfBuildShort() => PerfBuildLayout(cols: 13, rows: 22, len: 7);

        /// <summary>
        /// cols x rows straight east-facing basic lines of len tiles, each with a powered motor behind its head and a
        /// void sink one cell past its end; segments in a row are len + 2 cells apart (motor, belts, sink cell).
        /// </summary>
        static void PerfBuildLayout(int cols, int rows, int len)
        {
            var map = Find.CurrentMap;
            var comp = MapComponent_Belts.For(map);
            var belt = DefDatabase<ThingDef>.GetNamed("CB_Belt_Basic");
            var motor = DefDatabase<ThingDef>.GetNamed("CB_BeltMotor");
            int pitch = len + 2, width = cols * pitch;
            int ox = map.Center.x - width / 2 + 1, oz = map.Center.z - rows / 2;   // ox = first segment's first belt
            // clear the area (plants, rock, buildings, items, filth) so every cell can take a belt; pawns stay
            for (int x = ox - 1; x < ox - 1 + width; x++)
                for (int z = oz; z < oz + rows; z++)
                {
                    var c = new IntVec3(x, 0, z);
                    foreach (var t in c.GetThingList(map).ToList())
                        if (!(t is Pawn) && !t.Destroyed) t.Destroy();
                    if (!c.GetTerrain(map).affordances.Contains(TerrainAffordanceDefOf.Light))
                        map.terrainGrid.SetTerrain(c, TerrainDefOf.Soil);
                    map.roofGrid.SetRoof(c, null);
                }
            int items = 0;
            comp.PerfLineStarts.Clear();
            comp.PerfRefillOn = true;
            for (int col = 0; col < cols; col++)
                for (int row = 0; row < rows; row++)
                {
                    int x0 = ox + col * pitch, z = oz + row;
                    for (int i = 0; i < len; i++) Spawn(belt, new IntVec3(x0 + i, 0, z), map);
                    Spawn(motor, new IntVec3(x0 - 1, 0, z), map);
                    comp.Sim.AddSink(new Cell(x0 + len, z), new PerfVoidSink(comp));
                    comp.PerfLineStarts.Add(new IntVec3(x0, 0, z));
                    // 2 items per tile at the minimum gap: progress 0.75 (front) and 0.25
                    for (int i = 0; i < len; i++)
                        foreach (float p in new[] { 0.75f, 0.25f })
                        {
                            var steel = ThingMaker.MakeThing(ThingDefOf.Steel);
                            steel.stackCount = 1;
                            long h = comp.Store.Add(steel);
                            if (h < 0 || !comp.Sim.RestoreItem(new Cell(x0 + i, z),
                                    new BeltItem { Handle = h, Count = 1, Progress = p, EnteredFrom = Dir.West }))
                            { Log.Warning("[CB perf] could not place item"); continue; }
                            items++;
                        }
                }
            comp.ResetPerf();
            Log.Message($"[CB perf] built {cols * rows} lines x {len} basic belts = {comp.Sim.BeltCount} tiles, {items} items on belts, area ({ox - 1},{oz})..({ox - 2 + width},{oz + rows - 1}); motors powered via CompPowerTrader.PowerOn=true; a void sink (destroys arriving items) one cell past each line end; each tick every line's first belt is topped up with 1 steel when it has room, so the load stays at ~2 items per tile");
        }

        /// <summary>Test sink: destroys what the belt delivers (so the perf layout never fills up or spills).</summary>
        sealed class PerfVoidSink : IBeltSink
        {
            readonly MapComponent_Belts comp;
            public PerfVoidSink(MapComponent_Belts c) { comp = c; }
            public bool TryAccept(long handle, int count, Dir fromSide) { comp.Store.Take(handle)?.Destroy(); return true; }
        }

        /// <summary>Called at the start of every MapComponentTick (outside Sim.Tick): tops up each perf line's first belt.</summary>
        internal static void PerfRefill(MapComponent_Belts comp)
        {
            if (!comp.PerfRefillOn) return;
            foreach (var c in comp.PerfLineStarts)
            {
                if (!comp.Sim.CanInsert(c.ToCell(), Dir.West)) continue;
                var steel = ThingMaker.MakeThing(ThingDefOf.Steel);
                steel.stackCount = 1;
                if (!comp.TryPutOnBelt(c, Rot4.West, steel)) steel.Destroy();
            }
        }

        [DebugAction(Cat, "Perf: stop refill", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        static void PerfStopRefill() { MapComponent_Belts.For(Find.CurrentMap).PerfRefillOn = false; Log.Message("[CB perf] refill stopped"); }

        static Thing Spawn(ThingDef def, IntVec3 cell, Map map)
        {
            var t = ThingMaker.MakeThing(def);
            t.SetFactionDirect(Faction.OfPlayer);
            GenSpawn.Spawn(t, cell, map, Rot4.East);
            var power = t.TryGetComp<CompPowerTrader>();
            if (power != null) power.PowerOn = true;
            return t;
        }

        [DebugAction(Cat, "Perf: report", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        static void PerfReport()
        {
            var comp = MapComponent_Belts.For(Find.CurrentMap);
            double ms = 1000.0 / Stopwatch.Frequency;
            int n = comp.PerfTickSamples, f = comp.PerfDrawSamples;
            double tick = n > 0 ? comp.PerfSimTicks * ms / n : 0, whole = n > 0 ? comp.PerfCompTicks * ms / n : 0;
            double motorsMs = n > 0 ? comp.PerfMotorTicks * ms / n : 0;
            double inView = f > 0 ? (double)comp.PerfDrawnItems / f : 0;
            double draw = f > 0 ? comp.PerfDrawTicks * ms / f : 0, frame = f > 0 ? comp.PerfFrameSeconds * 1000.0 / f : 0;
            Log.Message($"[CB perf] tick avg {tick:0.000} ms, draw avg {draw:0.000} ms ({inView:0} items in view) over {n} ticks/{f} frames "
                + $"(Sim.Tick max {comp.PerfSimMax * ms:0.00} ms; whole MapComponentTick {whole:0.000} ms; motor Thing ticks {motorsMs:0.000} ms/tick; frame avg {frame:0.00} ms = {(frame > 0 ? 1000.0 / frame : 0):0} fps; belts {comp.Sim.BeltCount}, items {comp.Store.Count})");
            comp.ResetPerf();
        }

        /// <summary>Centres the camera on all belts and zooms out until they all fit (for draw measurements).</summary>
        [DebugAction(Cat, "Perf: zoom camera to whole layout", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        static void PerfZoomOut()
        {
            var belts = Belts(Find.CurrentMap);
            if (belts.Count == 0) return;
            int x0 = belts.Min(b => b.Position.x), x1 = belts.Max(b => b.Position.x);
            int z0 = belts.Min(b => b.Position.z), z1 = belts.Max(b => b.Position.z);
            var centre = new UnityEngine.Vector3((x0 + x1 + 1) / 2f, 0f, (z0 + z1 + 1) / 2f);
            float aspect = (float)UI.screenWidth / UI.screenHeight;
            float size = UnityEngine.Mathf.Max((z1 - z0 + 3) / 2f, (x1 - x0 + 3) / 2f / aspect);
            Find.CameraDriver.SetRootPosAndSize(centre, size);
            Log.Message($"[CB perf] camera centre ({centre.x:0.0},{centre.z:0.0}) size {size:0.0} -> view {Find.CameraDriver.CurrentViewRect}");
        }

        [DebugAction(Cat, "Perf: reset counters", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        static void PerfReset() => MapComponent_Belts.For(Find.CurrentMap).ResetPerf();

        [DebugAction(Cat, "Test: destroy all motors", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        static void DestroyMotors()
        {
            var ms = Find.CurrentMap.listerThings.AllThings.OfType<Building_BeltMotor>().ToList();
            foreach (var m in ms) m.Destroy();
            Log.Message($"[CB] destroyed {ms.Count} motors");
        }

        [DebugAction(Cat, "Test: destroy the belt under each motor's facing", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        static void DestroyBeltAtMotors()
        {
            var map = Find.CurrentMap;
            int n = 0;
            foreach (var m in map.listerThings.AllThings.OfType<Building_BeltMotor>().ToList())
            {
                var b = (m.Position + m.Rotation.FacingCell).GetFirstThing<Building_Belt>(map);
                if (b != null) { b.Destroy(DestroyMode.Deconstruct); n++; }
            }
            Log.Message($"[CB] destroyed {n} belts in front of motors");
        }

        /// <summary>For each line head, puts the floor items on the cell behind it onto the belt (as an input would).</summary>
        [DebugAction(Cat, "Test: put items behind line heads onto belts", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        static void PutItemsBehindHeads()
        {
            var map = Find.CurrentMap;
            var comp = MapComponent_Belts.For(map);
            foreach (var b in Belts(map))
            {
                var rear = b.Position - b.Rotation.FacingCell;
                if (rear.GetFirstThing<Building_Belt>(map) != null) continue;
                foreach (var t in rear.GetThingList(map).Where(t => t.def.category == ThingCategory.Item).ToList())
                    Log.Message($"[CB] put {t} from {rear} onto {b.Position}: {(comp.TryPutOnBelt(b.Position, b.Rotation.Opposite, t) ? "ok" : "refused")}");
            }
        }

        [DebugAction(Cat, "Test: log motors", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        static void LogMotors()
        {
            foreach (var m in Find.CurrentMap.listerThings.AllThings.OfType<Building_BeltMotor>())
            {
                var rotate = m.GetGizmos().OfType<Command_Action>().FirstOrDefault(g => g.defaultLabel == "CB_RotateMotor".Translate());
                string gz = rotate == null ? "no rotate gizmo" : rotate.Disabled ? $"rotate disabled: {rotate.disabledReason}" : "rotate enabled";
                Log.Message($"[CB] motor {m.Position} rot={m.Rotation}: {m.GetInspectString().Replace("\n", " | ")} | {gz}");
            }
        }
    }
}
