using System.Collections.Generic;
using System.Linq;
using LudeonTK;
using RimWorld;
using Verse;
using Verse.AI;

namespace ConveyorBelts
{
    /// <summary>
    /// Test-only debug actions for Task 12 (tier upgrades). The harness cannot click gizmos, so these run the
    /// belts' real gizmo actions the way GizmoGridDrawer does for a grouped click: every selected belt's command
    /// with the clicked label runs once.
    /// </summary>
    public static class CB_UpgradeTests
    {
        const string Cat = "Conveyor Belts";

        static List<Building_Belt> Belts(Map map)
            => map.listerThings.AllThings.OfType<Building_Belt>().OrderBy(b => b.thingIDNumber).ToList();

        static List<Building_Belt> SelectedBelts() => Find.Selector.SelectedObjects.OfType<Building_Belt>().ToList();

        static string Cells(IEnumerable<Building_Belt> belts)
            => string.Join(" ", belts.OrderBy(b => b.Position.x).ThenBy(b => b.Position.z)
                .Select(b => $"({b.Position.x},{b.Position.z}){b.def.defName.Replace("CB_Belt_", "")}/{b.Rotation.ToStringHuman()}"));

        /// <summary>Grouped click: runs the command labelled label of every selected belt. Returns how many ran.</summary>
        static int ClickGroup(string label)
        {
            var cmds = SelectedBelts().SelectMany(b => b.GetGizmos()).OfType<Command_Action>()
                .Where(c => c.defaultLabel == label).ToList();
            foreach (var c in cmds)
                if (!c.Disabled) c.action();
            return cmds.Count;
        }

        static void Select(IEnumerable<Building_Belt> belts)
        {
            Find.Selector.ClearSelection();
            foreach (var b in belts) Find.Selector.Select(b, playSound: false, forceDesignatorDeselect: false);
        }

        static void SelectLineClick(string name)
        {
            var before = SelectedBelts();
            int ran = ClickGroup("CB_SelectLine".Translate());
            var after = SelectedBelts();
            Log.Message($"[CB] {name}: before={before.Count} [{Cells(before)}] commands run={ran} after={after.Count} selected total={Find.Selector.NumSelected} [{Cells(after)}]");
        }

        [DebugAction(Cat, "Test: select line of first belt", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        static void SelectLineOfFirst()
        {
            var first = Belts(Find.CurrentMap).FirstOrDefault();
            if (first == null) { Log.Warning("[CB] no belts"); return; }
            Select(new[] { first });
            SelectLineClick("select line of first belt");
        }

        [DebugAction(Cat, "Test: select line of first and last belt", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        static void SelectLineOfFirstAndLast()
        {
            var belts = Belts(Find.CurrentMap);
            if (belts.Count == 0) { Log.Warning("[CB] no belts"); return; }
            Select(new[] { belts.First(), belts.Last() }.Distinct());
            SelectLineClick("select line of first and last belt");
        }

        [DebugAction(Cat, "Test: select all belts", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        static void SelectAll()
        {
            Select(Belts(Find.CurrentMap));
            Log.Message($"[CB] selected all belts: {SelectedBelts().Count}");
        }

        [DebugAction(Cat, "Test: list gizmos of selected belts", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        static void ListGizmos()
        {
            foreach (var b in SelectedBelts())
            {
                var labels = b.GetGizmos().OfType<Command>().Select(c => $"{c.defaultLabel}{(c.icon != null ? "" : "(no icon)")}");
                Log.Message($"[CB] gizmos of {b.def.defName} ({b.Position.x},{b.Position.z}): {string.Join(" | ", labels)}");
            }
        }

        static void ChangeTierClick(string labelKey, int tier)
        {
            var label = labelKey.Translate(("CB_Tier" + tier).Translate()).ToString();
            var map = Find.CurrentMap;
            int plansBefore = Plans(map).Count;
            int beltsBefore = Belts(map).Count;
            int ran = ClickGroup(label);
            Log.Message($"[CB] click '{label}' on {SelectedBelts().Count} selected belts: commands run={ran} godMode={DebugSettings.godMode} " +
                        $"belt plans {plansBefore}->{Plans(map).Count} belts {beltsBefore}->{Belts(map).Count}");
            LogPlans();
        }

        [DebugAction(Cat, "Test: upgrade selected to fast", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        static void UpgradeToFast() => ChangeTierClick("CB_UpgradeTo", 1);

        [DebugAction(Cat, "Test: upgrade selected to express", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        static void UpgradeToExpress() => ChangeTierClick("CB_UpgradeTo", 2);

        [DebugAction(Cat, "Test: downgrade selected to fast", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        static void DowngradeToFast() => ChangeTierClick("CB_DowngradeTo", 1);

        [DebugAction(Cat, "Test: downgrade selected to basic", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        static void DowngradeToBasic() => ChangeTierClick("CB_DowngradeTo", 0);

        static List<Thing> Plans(Map map)
            => map.listerThings.ThingsInGroup(ThingRequestGroup.Blueprint)
                .Concat(map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingFrame))
                .Where(t => t.def.entityDefToBuild is ThingDef td && BeltGizmos.IsBeltDef(td))
                .OrderBy(t => t.Position.x).ThenBy(t => t.Position.z).ToList();

        [DebugAction(Cat, "Test: log belt plans", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        static void LogPlans()
        {
            var map = Find.CurrentMap;
            var sim = MapComponent_Belts.For(map).Sim;
            var plans = Plans(map);
            Log.Message($"[CB] belt plans: {plans.Count}");
            foreach (var t in plans)
            {
                string extra = "";
                if (t is Frame f)
                    extra = $" work={f.workDone:0.0}/{f.WorkToBuild:0.0} resources=[{string.Join(",", f.resourceContainer.Select(r => r.def.defName + " x" + r.stackCount))}]";
                var belts = t.Position.GetThingList(map).OfType<Building_Belt>().Select(b => $"{b.def.defName}#{b.thingIDNumber}/{b.Rotation.ToStringHuman()}");
                bool inSim = sim.TryGetBelt(t.Position.ToCell(), out var d, out var tier);
                Log.Message($"[CB]   ({t.Position.x},{t.Position.z}) {t.def.defName}#{t.thingIDNumber} rot={t.Rotation.ToStringHuman()}{extra} | belts on cell: [{string.Join(",", belts)}] sim={(inSim ? d + "/t" + tier : "none")} items={sim.ItemsAt(t.Position.ToCell()).Count}");
            }
        }

        [DebugAction(Cat, "Test: blocking check for belt plans", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        static void BlockingCheck()
        {
            var map = Find.CurrentMap;
            foreach (var plan in Plans(map))
                foreach (var t in plan.Position.GetThingList(map))
                    if (t != plan)
                        Log.Message($"[CB] plan {plan.def.defName} at ({plan.Position.x},{plan.Position.z}) vs {t.def.defName}: BlocksConstruction={GenConstruct.BlocksConstruction(plan, t)} " +
                                    $"SpawningWipes(built)={GenSpawn.SpawningWipes(plan.def.entityDefToBuild, t.def)} SpawningWipes(frameDef)={GenSpawn.SpawningWipes(plan.def.entityDefToBuild.frameDef, t.def)} " +
                                    $"SpawningWipes(bpDef)={GenSpawn.SpawningWipes(plan.def.entityDefToBuild.blueprintDef, t.def)} fillage={t.def.Fillage}");
        }

        [DebugAction(Cat, "Test: why colonists don't build belt plans", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        static void WhyNoConstruction()
        {
            var map = Find.CurrentMap;
            var givers = new[] { "ConstructDeliverResourcesToBlueprints", "ConstructDeliverResourcesToFrames", "ConstructFinishFrames", "DeconstructForBlueprint" }
                .Select(n => DefDatabase<WorkGiverDef>.GetNamedSilentFail(n)).Where(d => d != null).ToList();
            foreach (var p in map.mapPawns.FreeColonistsSpawned)
            {
                Log.Message($"[CB] pawn {p} constructionDisabled={p.WorkTypeIsDisabled(WorkTypeDefOf.Construction)} priority={p.workSettings?.GetPriority(WorkTypeDefOf.Construction)} job={p.CurJobDef} drafted={p.Drafted}");
                foreach (var t in Plans(map).Concat(map.listerThings.AllThings.OfType<Building_Belt>().Where(b => BeltGizmos.PlannedReplacement(b) != null)))
                    foreach (var g in givers)
                    {
                        if (!(g.Worker is WorkGiver_Scanner sc)) continue;
                        JobFailReason.Clear();
                        bool has = sc.HasJobOnThing(p, t, false);
                        Log.Message($"[CB]   {g.defName} on {t.def.defName}({t.Position.x},{t.Position.z}): {has} {JobFailReason.Reason}");
                    }
                foreach (var t in Plans(map))
                    Log.Message($"[CB]   plan {t.def.defName}: touch={GenConstruct.CanTouchTargetFromValidCell(t, p)} blocking={GenConstruct.FirstBlockingThing(t, p)} " +
                                $"canConstruct={GenConstruct.CanConstruct(t, p, WorkTypeDefOf.Construction, true)} reach={p.CanReach(t, PathEndMode.Touch, Danger.Deadly)} " +
                                $"resources={(t is Blueprint bp ? GenConstruct.CanGetResources_NewTemp(bp, p, true).ToString() : "-")} " +
                                $"reserve={p.CanReserveAndReach(t, PathEndMode.Touch, Danger.Deadly, 1, -1, null, true)} reserver={map.reservationManager.FirstRespectedReserver(t, p)} " +
                                $"ideo={p.Ideo?.name ?? "none"} ideoCanBuild={p.Ideo?.MembersCanBuild(t)} " +
                                $"histEvent={new HistoryEvent(HistoryEventDefOf.BuildSpecificDef, p.Named(HistoryEventArgsNames.Doer), NamedArgumentUtility.Named(t.def.entityDefToBuild, HistoryEventArgsNames.Building)).Notify_PawnAboutToDo_Job()}");
            }
        }

        [DebugAction(Cat, "Test: assign all colonists to construction", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        static void AssignConstruction()
        {
            foreach (var p in Find.CurrentMap.mapPawns.FreeColonistsSpawned)
            {
                if (p.WorkTypeIsDisabled(WorkTypeDefOf.Construction)) continue;
                p.workSettings.EnableAndInitializeIfNotAlreadyInitialized();
                p.workSettings.SetPriority(WorkTypeDefOf.Construction, 1);
                Log.Message($"[CB] {p} construction priority = {p.workSettings.GetPriority(WorkTypeDefOf.Construction)}");
            }
        }

        [DebugAction(Cat, "Test: cancel belt frames", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        static void CancelFrames()
        {
            var map = Find.CurrentMap;
            var des = new Designator_Cancel();
            foreach (var t in Plans(map).Where(t => t is Frame).ToList())
            {
                bool ok = des.CanDesignateThing(t).Accepted;
                if (ok) des.DesignateThing(t);
                Log.Message($"[CB] cancel frame {t.def.defName}#{t.thingIDNumber} at ({t.Position.x},{t.Position.z}): {(ok ? "cancelled" : "refused")} destroyed={t.Destroyed}");
            }
        }
    }
}
