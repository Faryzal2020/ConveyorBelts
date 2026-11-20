# ConveyorBelts v1 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A RimWorld 1.7 mod with motor-driven conveyor belts (3 tiers), Input / Pusher / Output ports with their own settings tab, and a self-simulated, self-rendered item flow.

**Architecture:** A pure-C# simulation (`Sim/`, no RimWorld types, unit-tested with xUnit) owns belts, lines, motors and item movement. A `MapComponent` hosts one simulation per map, keeps the real item `Thing`s off-map in an `IThingHolder` store, saves/loads them, and draws them. Thin `Building` subclasses register themselves with the simulation on spawn and act as sinks/sources.

**Tech Stack:** C# / net10.0 class library against the installed 1.7 `Assembly-CSharp.dll` (CoreCLR "Waylith" engine), xUnit for `Sim/`, Python 3 (stdlib only) for textures, the RimTestHarness Node runner for in-game checks.

**Spec:** `ConveyorBelts/PLAN.md` (rev 2, approved 2026-11-20). Section refs below (§5.3 etc.) point there.

## Global Constraints

- Game: RimWorld 1.7 only (`supportedVersions` 1.7), Core only. `TargetFramework` net10.0 (1.7 mods must be net10.0; see `RimTestHarness/README.md` "Why net10.0").
- No Harmony dependency unless Task 9 proves the vanilla Storage tab can't be hidden through defs alone.
- packageId `faryzal2020.conveyorbelts`; author `visiadidaya`; C# namespace `ConveyorBelts`; every def name prefixed `CB_`; translation keys prefixed `CB_`.
- Defs: `CB_Belt_Basic`, `CB_Belt_Fast`, `CB_Belt_Express`, `CB_BeltMotor`, `CB_InputPort`, `CB_PusherPort`, `CB_OutputPort`; architect tab `CB_Conveyors` (label "conveyors").
- Tiers (60 ticks = 1 s): Basic 1.5 tiles/s, stack 10, 5 W/tile; Fast 3, 25, 10 W; Express 6, 75, 20 W. 2 items per tile, min gap 0.5 tile.
- Motor: 20 W base + per-tile watts of its line when driving; 5 W when standby or idle. Ports: flat 50 W.
- Colours: belts yellow / red / blue; motor grey; input green; pusher orange; output cyan. 64×64 PNGs, `Graphic_Multi`, big arrow.
- Belts: `isEdifice=false`, `altitudeLayer=Conduits`, `passability=Standable`, `rotatable=true`, `replaceTags: ConveyorBelt`, cost 1 steel, no research. Motor and ports: 1×1 edifices, standable, cost 10 steel + 1 component (placeholder), no research.
- Belt items never appear in haulables or the resource counter; never lost or duplicated (spec §1 anti-goals).
- `Sim/` must compile without any RimWorld/Unity reference (the test project compiles those files alone; that enforces it).

## Review Focus

1. **Closed belt loops** (four belts in a 2×2 ring) — must neither hang the line builder nor lose items when the ring is full. → Task 5 `Ring_FullRingKeepsItems`, Task 4 `Lines_RingTerminates`.
2. **Two belts pointing at each other** — items stop at the end; nothing swaps or vanishes. → Task 5 `HeadOn_DoesNotTransfer`.
3. **Item leaves the tile before the ordered pawn arrives** (it moved on, the belt was removed) — the job ends quietly, no exception, no duplicate. → Task 13 step 5.
4. **Port destroyed while holding an item in its buffer** — the item drops on the port's cell. → Task 10 step 6, Task 11 step 7.
5. **Save/load while items are mid-belt and an upgrade frame is under construction** — items keep their positions; the old belt keeps running; the swap completes after load. → Task 12 step 6.

---

## Shared commands

```bash
MOD="/Users/visiadidayateknologi/Library/Application Support/Steam/steamapps/common/RimWorld/Mods/ConveyorBelts"
MODS="ludeon.rimworld,brrainz.harmony,rimtestharness.harness,faryzal2020.conveyorbelts"
run() { (cd "$MOD/../RimTestHarness/runner" && node runner.js "$@"); }   # every `run ...` below is this function
```

- Build mod: `cd "$MOD/Source/ConveyorBelts" && dotnet build -c Release` → `Build succeeded`, `0 Error(s)`.
- Unit tests: `cd "$MOD/Source/ConveyorBelts.Tests" && dotnet test` → `Passed!`.
- Headless boot: `run launch --mods=$MODS` (waits for Playing), then `run log level=error` → no lines mentioning `ConveyorBelts` or `CB_`.
- Windowed boot (rendering/UI checks): `run launch --mods=$MODS --windowed=1280x720`.
- After every DLL rebuild, `run quit 0` and relaunch (no hot reload in 1.7).
- Mod debug actions are listed by `run list_debug_actions depth=3 | grep -i conveyor`; run one with `run debug_action 'path=<path printed there>'`.
- Read a building's inspect string: `run query_things defName=CB_Belt_Basic deep=true`.
- Find build space: `run find_cell x=125 z=125 clearRadius=10` → origin (OX, OZ); all in-game coordinates below are relative to it.
- UI-only checks (mouse placement, tabs, float menus) can't be driven headless: do them in the windowed game and save a `run screenshot path=...` as evidence. Unit tests cover the logic behind each.

---

## File map

```
ConveyorBelts/
  About/About.xml
  .gitignore                                   bin/, obj/, *.user
  Defs/DesignationCategoryDefs/CB_Conveyors.xml
  Defs/ThingDefs_Buildings/CB_Belts.xml        3 belt tiers (abstract CB_BeltBase parent)
  Defs/ThingDefs_Buildings/CB_Machines.xml     motor + 3 ports
  Defs/JobDefs/CB_Jobs.xml
  Languages/English/Keyed/CB_Keys.xml
  Textures/ConveyorBelts/<Name>_{north,east,south,west}.png, UI/*.png
  tools/make_textures.py
  Assemblies/ConveyorBelts.dll
  Source/ConveyorBelts/ConveyorBelts.csproj
  Source/ConveyorBelts/ConveyorBeltsMod.cs     Mod subclass, startup log line
  Source/ConveyorBelts/Sim/                    Cell.cs, Dir.cs, BeltPath.cs, Tiers.cs, BeltItem.cs,
                                               IBeltHost.cs, IBeltSink.cs, BeltSim.cs (+ BeltSim.Lines.cs,
                                               BeltSim.Motors.cs, BeltSim.Items.cs partials), PusherLogic.cs
  Source/ConveyorBelts/Map/                    MapComponent_Belts.cs, BeltItemStore.cs, SavedBeltItem.cs,
                                               BeltRenderer.cs, Conv.cs (IntVec3/Rot4 <-> Cell/Dir)
  Source/ConveyorBelts/Buildings/              BeltTierExtension.cs, Building_Belt.cs, Building_BeltMotor.cs,
                                               Building_InputPort.cs, Building_PusherPort.cs, Building_OutputPort.cs
  Source/ConveyorBelts/Pusher/                 IPushAdapter.cs, PushAdapters.cs (4 adapters + registry)
  Source/ConveyorBelts/UI/                     ITab_Port.cs, IPortTabOwner.cs, PortTabSection.cs,
                                               Section_InputFilter.cs, Section_PusherModes.cs, Section_OutputModes.cs
  Source/ConveyorBelts/Placement/              Designator_BeltPath.cs (+ 3 tier subclasses), BeltGizmos.cs
  Source/ConveyorBelts/Jobs/                   JobDriver_TakeFromBelt.cs, FloatMenuOptionProvider_TakeFromBelt.cs
  Source/ConveyorBelts/Debug/                  CB_DebugActions.cs
  Source/ConveyorBelts.Tests/ConveyorBelts.Tests.csproj   compiles ../ConveyorBelts/Sim/**/*.cs only
  Source/ConveyorBelts.Tests/*Tests.cs
```

---

### Task 1: Scaffold — mod loads, defs, textures, test project

**Files:**
- Create: `About/About.xml`, `.gitignore`, `Source/ConveyorBelts/ConveyorBelts.csproj`, `Source/ConveyorBelts/ConveyorBeltsMod.cs`, `Source/ConveyorBelts.Tests/ConveyorBelts.Tests.csproj`, `Source/ConveyorBelts.Tests/SmokeTests.cs`, `tools/make_textures.py`, `Textures/ConveyorBelts/**`, `Defs/DesignationCategoryDefs/CB_Conveyors.xml`, `Defs/ThingDefs_Buildings/CB_Belts.xml`, `Languages/English/Keyed/CB_Keys.xml`

**Interfaces:**
- Produces: `ConveyorBelts.ConveyorBeltsMod : Verse.Mod` (logs `[ConveyorBelts] loaded v1` in its constructor); texture paths `ConveyorBelts/Belt_Basic`, `Belt_Fast`, `Belt_Express`, `Motor`, `InputPort`, `PusherPort`, `OutputPort` (each with the 4 `_north/_east/_south/_west` files); `ConveyorBelts/UI/` icons `SelectLine`, `UpgradeLine`, `RotateMotor`, `TabPort`.

- [ ] **Step 1:** `git init` in `$MOD`, add `.gitignore`.
- [ ] **Step 2:** Write `ConveyorBelts.csproj` by copying `RimTestHarness/Source/RimTestHarness/RimTestHarness.csproj` and keeping only: the PropertyGroup (AssemblyName/RootNamespace `ConveyorBelts`, OutputPath `..\..\Assemblies\`, PathMap to `ConveyorBelts`), the GameRoot/GameManaged probing, and the `Assembly-CSharp`, `UnityStubs`, `Brutal.Core.Maths` references plus the Krafs fallback. Drop Harmony, CE and SQLite.
- [ ] **Step 3:** Write `ConveyorBelts.Tests.csproj`: net10.0, xUnit + `Microsoft.NET.Test.Sdk` + `xunit.runner.visualstudio` (current stable versions), `<Compile Include="..\ConveyorBelts\Sim\**\*.cs" />`, no reference to the mod project or any game DLL. `SmokeTests.cs` holds one test `Smoke_True` asserting `true` (deleted in Task 3).
- [ ] **Step 4:** Write `tools/make_textures.py` (stdlib `zlib`+`struct` PNG writer, no PIL): 64×64 RGBA, filled rounded square in the def colour, white arrow pointing up for `_north` and rotated for the others; belts get a darker 4 px border on the two non-arrow sides so lines read as lanes. Colours (RGB): basic `#E8C33A`, fast `#D9483B`, express `#3B7DD9`, motor `#8A8F96`, input `#3FAF5A`, pusher `#E58A2E`, output `#2EC4C9`. UI icons 64×64, white glyph on transparent. Run: `python3 -I tools/make_textures.py` → prints 32 file paths.
- [ ] **Step 5:** Write `About.xml` (name "Conveyor Belts", packageId, author, supportedVersions 1.7, short description), `CB_Conveyors.xml` (`DesignationCategoryDef`, order after Misc, `specialDesignatorClasses` left empty until Task 7), `CB_Belts.xml` with abstract `CB_BeltBase` (all belt fields from Global Constraints, `thingClass` `Building` for now, `graphicData` `Graphic_Multi`, `designationCategory` `CB_Conveyors`) and the 3 tier defs, `CB_Keys.xml` (empty `LanguageData`).
- [ ] **Step 6: Verify.** Build mod → succeeds. `dotnet test` → 1 passed. Headless boot → `run log contains="[ConveyorBelts] loaded v1"` returns 1 line; no errors. `run defs type=ThingDef contains=CB_Belt` → 3 defs.
- [ ] **Step 7: Commit** `git add -A && git commit -m "chore: scaffold ConveyorBelts mod, textures, test project"`

---

### Task 2: Rendering spike (throwaway)

Answers spec risk 1 before any real code depends on it.

**Files:**
- Create: `Source/ConveyorBelts/Debug/CB_DebugActions.cs`, `Source/ConveyorBelts/Debug/MapComponent_RenderSpike.cs` (deleted at the end of this task)
- Modify: `PLAN.md` §8 risk 1 (record result)

- [ ] **Step 1:** `[DebugAction("Conveyor Belts", "Render spike: 500 items", allowedGameStates = AllowedGameStates.PlayingOnMap)]` creates 500 unspawned steel stacks (count 10) and gives them to `MapComponent_RenderSpike`, which moves them around a 20×20 rectangle at 3 tiles/s and draws each with `thing.DrawNowAt(pos)` from `MapComponentUpdate`. A second action "Render spike: switch to MapComponentDraw" moves drawing to `MapComponentDraw`. Each logs `[CB spike] draw avg {ms} ms over 300 frames` using a `Stopwatch`.
- [ ] **Step 2:** Windowed boot, run the action, `run set_speed speed=3`, wait ~10 s, take a screenshot, read the log line. Repeat with the Draw variant.
- [ ] **Step 3: Decide.** Pass = items visibly move smoothly in the screenshot sequence and draw avg ≤ 2 ms. Record in PLAN.md §8 risk 1: which hook (Update vs Draw), measured ms, and whether the fallback (cached material + batched meshes) is needed. If neither variant passes, stop and bring the numbers to the user before Task 6.
- [ ] **Step 4:** Delete `MapComponent_RenderSpike.cs` and the spike actions (keep the empty `CB_DebugActions` class). Build, commit `git commit -am "spike: measure belt item rendering on Waylith (result in PLAN.md)"`.

---

### Task 3: Sim primitives and the L-path rule

**Files:**
- Create: `Sim/Cell.cs`, `Sim/Dir.cs`, `Sim/BeltPath.cs`, `Sim/Tiers.cs`
- Test: `ConveyorBelts.Tests/BeltPathTests.cs`, `DirTests.cs` (delete `SmokeTests.cs`)

**Interfaces:**
- Produces:
  - `readonly record struct Cell(int X, int Z)` with `Cell Step(Dir d)`.
  - `enum Dir : byte { North = 0, East = 1, South = 2, West = 3 }` (same ints as `Rot4.AsInt`).
  - `static class DirExt`: `Dir Opposite(this Dir)`, `Dir Left(this Dir)` (counter-clockwise), `Dir Right(this Dir)`, `(int dx, int dz) Offset(this Dir)`.
  - `static class BeltPath`: `List<(Cell Cell, Dir Dir)> Compute(Cell start, Cell end, bool zFirst, Dir singleCellDir)`.
  - `sealed record TierSpec(float TilesPerTick, int MaxStack, float WattsPerTile)`; `static class Tiers { const int Basic = 0, Fast = 1, Express = 2; TierSpec Get(int tier); }` with values from Global Constraints (`TilesPerTick` = tiles/s ÷ 60).

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void LPath_XFirst_ExampleFromSpec() {
    var p = BeltPath.Compute(new(0,0), new(5,5), zFirst:false, Dir.North);
    Assert.Equal(11, p.Count);
    for (int x = 0; x <= 4; x++) Assert.Equal((new Cell(x,0), Dir.East), p[x]);
    for (int z = 0; z <= 5; z++) Assert.Equal((new Cell(5,z), Dir.North), p[5+z]);
}
[Fact] public void LPath_Reversed_GoesWestThenSouth() {
    var p = BeltPath.Compute(new(5,5), new(0,0), false, Dir.North);
    Assert.Equal((new Cell(1,5), Dir.West), p[4]);
    Assert.Equal((new Cell(0,5), Dir.South), p[5]);
    Assert.Equal((new Cell(0,0), Dir.South), p[^1]);
}
[Fact] public void LPath_ZFirst() {
    var p = BeltPath.Compute(new(0,0), new(5,5), true, Dir.North);
    Assert.Equal((new Cell(0,4), Dir.North), p[4]);
    Assert.Equal((new Cell(0,5), Dir.East), p[5]);
    Assert.Equal((new Cell(5,5), Dir.East), p[^1]);
}
[Fact] public void Straight_AllSameDir() =>
    Assert.All(BeltPath.Compute(new(0,0), new(3,0), false, Dir.North), t => Assert.Equal(Dir.East, t.Dir));
[Fact] public void Vertical_South() {
    var p = BeltPath.Compute(new(0,0), new(0,-2), false, Dir.North);
    Assert.Equal(3, p.Count); Assert.All(p, t => Assert.Equal(Dir.South, t.Dir));
}
[Fact] public void SingleCell_UsesGivenDir() =>
    Assert.Equal(new[] { (new Cell(2,2), Dir.West) }, BeltPath.Compute(new(2,2), new(2,2), false, Dir.West));
// DirTests: Opposite/Left/Right round-trips; Cell(0,0).Step(Dir.North) == Cell(0,1); Step(East) == Cell(1,0)
```

- [ ] **Step 2:** `dotnet test` → FAIL (types missing).
- [ ] **Step 3:** Implement. Rule: first leg along the first axis toward `end`, all tiles pointing toward `end` on that axis; the corner tile and the second leg point toward `end` on the second axis; when the second leg has length 0 the corner keeps the first-leg direction (straight line).
- [ ] **Step 4:** `dotnet test` → all pass.
- [ ] **Step 5: Commit** `git commit -am "feat(sim): cell/dir primitives, tiers, two-click L path rule"`

---

### Task 4: Sim belts, lines and motors

**Files:**
- Create: `Sim/BeltSim.cs`, `Sim/BeltSim.Lines.cs`, `Sim/BeltSim.Motors.cs`, `Sim/IBeltHost.cs`, `Sim/BeltItem.cs`
- Test: `ConveyorBelts.Tests/LineTests.cs`, `MotorTests.cs`, `Fakes.cs` (`FakeHost : IBeltHost` with a handle counter and a log of `Split` calls)

**Interfaces:**
- Consumes: Task 3 types.
- Produces (on `public sealed partial class BeltSim`):
  - `BeltSim(IBeltHost host)`; `bool RequireMotors { get; set; } = true`.
  - `void AddBelt(Cell c, Dir dir, int tier)`, `void SetBelt(Cell c, Dir dir, int tier)` (keeps items), `void RemoveBelt(Cell c, List<BeltItem> dropped)`, `bool TryGetBelt(Cell c, out Dir dir, out int tier)`, `int BeltCount`.
  - `int LineIdAt(Cell c)` (-1 if none), `IReadOnlyList<Cell> LineCells(int lineId)` (head → tail), `bool IsLineRunning(int lineId)`.
  - `void AddMotor(Cell motor, Dir facing)`, `void SetMotorFacing(Cell motor, Dir facing)`, `void SetMotorPowered(Cell motor, bool powered)`, `void RemoveMotor(Cell motor)`, `MotorStatus GetMotorStatus(Cell motor)`, `IEnumerable<Dir> AdjacentBeltDirs(Cell c)` (order N, E, S, W).
  - `enum MotorState { Idle, Standby, Driving }`; `readonly record struct MotorStatus(MotorState State, int LineTiles, float Watts)`; consts `MotorBaseWatts = 20f`, `MotorStandbyWatts = 5f`.
  - `struct BeltItem { long Handle; int Count; float Progress; Dir EnteredFrom; }`; `interface IBeltHost { long Split(long handle, int count); }` (returns a new handle carrying `count`; the old handle keeps the rest).
- Rules (spec §5.3): a line is a maximal run where each tile has a belt with the same dir and the next tile is `c.Step(dir)`. Lines are rebuilt lazily (dirty flag) on any belt add/remove/set. Driver of a line = first-registered *powered* motor facing a cell of that line, else the first-registered motor facing it; it is `Driving`, all others facing that line are `Standby`; a motor whose facing cell has no belt is `Idle`. Driving watts = 20 + Σ tile `WattsPerTile`; Standby/Idle = 5. A line runs iff `!RequireMotors` or its driver is powered.

- [ ] **Step 1: Write the failing tests**

```csharp
// LineTests
[Fact] public void Lines_LPathMakesTwoLines()        // place BeltPath (0,0)->(5,5): LineCells(LineIdAt((0,0))) == (0,0)..(4,0); LineCells(LineIdAt((5,0))) == (5,0)..(5,5)
[Fact] public void Lines_SideMergeIsSeparateLine()   // (0,0)E,(1,0)E,(2,0)E + (1,1)S : LineIdAt((1,1)) != LineIdAt((1,0)); line of (0,0) has 3 cells
[Fact] public void Lines_MixedTiersAreOneLine()      // (0,0)E basic,(1,0)E express,(2,0)E fast → one line, 3 cells
[Fact] public void Lines_RemovingMiddleSplits()      // 5 east belts, RemoveBelt((2,0)) → lines of 2 and 2 cells
[Fact] public void Lines_RingTerminates()            // (0,0)E,(1,0)N,(1,1)W,(0,1)S → 4 lines of 1 cell, completes (no hang)
// MotorTests
[Fact] public void Motor_DrivesFacedLine_WattsScaleWithTiers() {
    // belts (0,0)..(4,0) basic East, (5,0) fast East; motor at (2,1) facing South, powered
    // → status (Driving, 6, 20 + 5*5 + 10 = 55f); IsLineRunning(line) == true
}
[Fact] public void Motor_Unpowered_LineStopped_StillReportsFullDraw()  // same layout, SetMotorPowered(false) → (Driving, 6, 55f), IsLineRunning false
[Fact] public void Motor_SecondMotorStandby_TakesOverWhenFirstUnpowered()
    // motors A (2,1)S and B (3,-1)N, both powered → A Driving, B (Standby, 6, 5f); SetMotorPowered(A,false) → B Driving 55f, A Standby, line runs
[Fact] public void Motor_FacingNoBelt_IsIdle()       // (Idle, 0, 5f)
[Fact] public void Motor_BeltRemovedUnderFacing_BecomesIdle()
[Fact] public void LShape_NeedsTwoMotors()           // BeltPath (0,0)->(5,5), one motor beside (2,0): x-leg runs, z-leg does not
[Fact] public void RequireMotorsOff_EverythingRuns()
[Fact] public void AdjacentBeltDirs_OrderIsNESW()
```

- [ ] **Step 2:** `dotnet test` → FAIL.
- [ ] **Step 3:** Implement. Store belts in `Dictionary<Cell, BeltCell>` (`Dir`, `Tier`, `List<BeltItem>` capacity 2, `int LineId`, round-robin cursor `Dir LastFrom`). Line rebuild: heads are belts whose `c.Step(dir.Opposite())` is not a same-dir belt; walk forward from each head; any belt not reached (impossible for straight runs, but guard against cycles) becomes its own line.
- [ ] **Step 4:** `dotnet test` → all pass.
- [ ] **Step 5: Commit** `git commit -am "feat(sim): belts, straight lines, motor election and power draw"`

---

### Task 5: Sim item movement, merging, sinks

**Files:**
- Create: `Sim/BeltSim.Items.cs`, `Sim/IBeltSink.cs`
- Test: `ConveyorBelts.Tests/ItemTests.cs`, `RenderPosTests.cs`

**Interfaces:**
- Consumes: Task 4.
- Produces:
  - `interface IBeltSink { bool TryAccept(long handle, int count, Dir fromSide); }`
  - `void AddSink(Cell c, IBeltSink sink)`, `void RemoveSink(Cell c)`.
  - `int MaxStackAt(Cell belt)`; `bool CanInsert(Cell belt, Dir fromSide)`; `bool TryInsert(Cell belt, Dir fromSide, long handle, int count)` (false if no belt, `belt.Dir == fromSide`, no entry space, or `count > MaxStackAt`).
  - `bool TryRemoveItem(Cell belt, long handle, out BeltItem item)`; `bool RestoreItem(Cell belt, BeltItem item)` (load path; false if no belt there).
  - `IReadOnlyList<BeltItem> ItemsAt(Cell c)` (front item first); `IEnumerable<(Cell Cell, BeltItem Item)> AllItems()`.
  - `void Tick()`.
  - `static (float Dx, float Dz) LocalPos(Dir enteredFrom, Dir beltDir, float progress)` — offset from cell centre: progress 0 → edge on `enteredFrom` side, 0.5 → centre, 1 → edge on `beltDir` side, straight segments between.
- Conventions: `fromSide` is the side of the *receiving* cell the item enters through (direction from receiver toward source). Belt→belt: `fromSide = sourceDir.Opposite()`; transfer refused when `target.Dir == fromSide` (points straight back). An entering item gets `Progress = 0`, `EnteredFrom = fromSide`. Entry space = fewer than 2 items and the rearmost item's progress ≥ 0.5.
- Tick (spec §5.4): **phase 1**, for each running line, each tile, front item first: `p = min(p + TilesPerTick, 1)`, each following item `min(p + TilesPerTick, ahead.p - 0.5)`. **Phase 2**, collect front items with `p >= 1` on running lines, grouped by receiving cell (`c.Step(dir)`); per receiving cell, try candidates in N, E, S, W order starting after `LastFrom`, accept at most one per tick: into a belt (split with `host.Split` when `count > MaxStackAt(target)`, the remainder stays at `p = 1`) or into a sink via `TryAccept`. Set `LastFrom` on success.

- [ ] **Step 1: Write the failing tests** (all on `RequireMotors = false` unless stated; `Approx` = 1e-4 tolerance)

```csharp
[Fact] public void Item_MovesAtTierSpeed()             // basic (0,0)E, insert from West, 20 ticks → progress ≈ 0.5
[Fact] public void Item_TransfersToNextBelt()          // (0,0)E,(1,0)E: tick 40 → still (0,0) p≈1; tick 41 → at (1,0) p=0, EnteredFrom West
[Fact] public void Item_StopsAtEndWithNoTarget()       // single belt, 200 ticks → p == 1, still there
[Fact] public void Insert_RespectsMinGap()             // TryInsert true, immediate second TryInsert false, after 20 ticks true; third (3 items) false
[Fact] public void Insert_RefusedFromFrontSide()       // belt (0,0)E: TryInsert(fromSide: East) == false
[Fact] public void Insert_RefusedAboveMaxStack()       // basic: count 11 → false; 10 → true
[Fact] public void HeadOn_DoesNotTransfer()            // (0,0)E,(1,0)W, item on (0,0): 200 ticks → still (0,0) p=1; (1,0) unchanged
[Fact] public void SideMerge_IsRoundRobin()            // main (0,0)E,(1,0)E,(2,0)E; side (1,1)S; keep refilling (0,0) from West and (1,1) from North for 1200 ticks; count arrivals at (1,0) by EnteredFrom → |west - north| <= 1 and both > 0
[Fact] public void TierSplit_ExpressIntoBasic()        // express (0,0)E holding count 75 → basic (1,0)E: after transfer (1,0) count 10, (0,0) count 65; FakeHost logged Split(h, 10)
[Fact] public void StoppedLine_Freezes_ThenRuns()      // RequireMotors=true, no motor: 30 ticks → p == 0; add powered motor → 20 ticks → p ≈ 0.5
[Fact] public void StoppedLine_StillAcceptsAtEntry()   // stopped (1,0) line accepts one item from running (0,0) when entry space exists
[Fact] public void Sink_ReceivesWithFromSide()         // belt (0,0)E, sink at (1,0) accepting → sink saw (h, n, West); ItemsAt((0,0)) empty
[Fact] public void Sink_Refusing_BacksUp()
[Fact] public void RemoveBelt_ReturnsItems()           // dropped list contains both items, ItemsAt empty
[Fact] public void SetBelt_KeepsItems_NewSpeedApplies() // basic→express mid-tile: next tick advances 0.1
[Fact] public void TryRemoveItem_ByHandle()
[Fact] public void RestoreItem_NoBelt_ReturnsFalse()
[Fact] public void Ring_FullRingKeepsItems()           // 2x2 ring (Task 4 layout), 8 items: 600 ticks, no exception, AllItems().Count() == 8
[Fact] public void Ring_SevenItemsCirculate()          // 7 items: after 600 ticks at least one item changed cell; count stays 7
// RenderPosTests
[Fact] public void LocalPos_Straight()   // (West, East, 0) → (-0.5, 0); 0.5 → (0, 0); 1 → (0.5, 0)
[Fact] public void LocalPos_SideEntry()  // (North, East, 0.25) → (0, 0.25); (North, East, 0.75) → (0.25, 0)
```

- [ ] **Step 2:** `dotnet test` → FAIL.
- [ ] **Step 3:** Implement per the Tick description above. Skip cells with no items in both phases.
- [ ] **Step 4:** `dotnet test` → all pass.
- [ ] **Step 5: Commit** `git commit -am "feat(sim): item movement, back-pressure, round-robin merge, tier split, sinks"`

---

### Task 6: Belt building, map component, item store, rendering, save/load

**Files:**
- Create: `Map/Conv.cs`, `Map/BeltItemStore.cs`, `Map/SavedBeltItem.cs`, `Map/MapComponent_Belts.cs`, `Map/BeltRenderer.cs`, `Buildings/BeltTierExtension.cs`, `Buildings/Building_Belt.cs`
- Modify: `Defs/ThingDefs_Buildings/CB_Belts.xml` (`thingClass` `ConveyorBelts.Building_Belt`, `modExtensions` with `<tier>`), `Debug/CB_DebugActions.cs`, `Languages/English/Keyed/CB_Keys.xml`

**Interfaces:**
- Consumes: Tasks 3–5.
- Produces:
  - `static class Conv`: `Cell ToCell(this IntVec3)`, `IntVec3 ToIntVec3(this Cell)`, `Dir ToDir(this Rot4)`, `Rot4 ToRot4(this Dir)`.
  - `class BeltTierExtension : DefModExtension { public int tier; }`.
  - `class BeltItemStore : IThingHolder` — `ThingOwner<Thing> things`; `long Add(Thing t)` (despawns if spawned; handle = `thingIDNumber`), `Thing Get(long handle)`, `Thing Take(long handle)` (removes from owner, returns unspawned), `long Split(long handle, int count)` (`SplitOff`, adds the new piece).
  - `class MapComponent_Belts : MapComponent, IBeltHost` — `BeltSim Sim`, `BeltItemStore Store`; `static MapComponent_Belts For(Map map)`; `bool TryPutOnBelt(IntVec3 belt, Rot4 fromSide, Thing t)` (adds to store + `Sim.TryInsert`; on refusal leaves `t` untouched and unstored); `Thing TakeFromBelt(IntVec3 belt, long handle)` (null if gone); `void DropItems(IntVec3 cell, List<BeltItem> items)` (`GenPlace.TryPlaceThing(..., ThingPlaceMode.Near)` for each).
  - `class Building_Belt : Building` — `int Tier`; registers on `SpawnSetup` (`Sim.AddBelt`), unregisters + drops items on `DeSpawn` unless `internal bool swappingOut`; inspect string: `CB_BeltInspect` → "Tier: {tier} · Line: {n} tiles, {running|stopped} · Items: {label} ({progress:0.00}), ..."
  - Debug actions: "Seed test items" (1 steel ×5 inserted from behind on every empty belt), "Seed raw meat on first belt", "Dump belt state" (logs `[CB] belts={n} lines={n} items={n}` plus one line per item: cell, def, count, progress, rot % for rottables), "Deconstruct all belts" (`Destroy(DestroyMode.Deconstruct)` on every belt), "Toggle require motors".
- Behaviour: `MapComponentTick` → `Sim.Tick()` when `Sim.BeltCount > 0`; every 250 ticks call `CompRottable.CompTickInterval(250)` on every stored thing that has one (spec §5.5). `RequireMotors` defaults to `false` in this task (motors arrive in Task 8; the toggle stays as a debug action). Rendering uses the hook chosen in Task 2: for each item in the camera's `CellRect` (`Find.CameraDriver.CurrentViewRect`), `thing.DrawNowAt(cellCentre + LocalPos, altitude AltitudeLayer.Item)`. Save: `ExposeData` deep-saves `Store` and a `List<SavedBeltItem>` (`IntVec3 cell, long handle, int count, float progress, Rot4 enteredFrom`, `IExposable`); `FinalizeInit` restores each via `Sim.RestoreItem`, dropping the thing on the cell when it returns false.

- [ ] **Step 1:** Implement the files above. Belt items must not be spawned at any point while on a belt.
- [ ] **Step 2: Verify load + movement (headless).** Build, boot. Spawn a 6-tile east line: `run spawn_thing defName=CB_Belt_Basic x=OX+i z=OZ rot=east faction=player` for i=0..5. Run "Seed test items", `run tick 60`, "Dump belt state" → `run log contains="[CB]"` shows items moved toward x=OX+5 (front item at (OX+5) after ~240 ticks, `items=6` constant).
- [ ] **Step 3: Verify drop on deconstruct.** Seeded line, `tick 60`, note total steel (`run query_things defName=Steel` counts plus dump counts). Run "Deconstruct all belts" → `run cell_info` on the former belt cells shows the steel stacks, dump shows `items=0`, total steel unchanged (belt refunds aside).
- [ ] **Step 4: Verify save/load.** Rebuild the line, seed, `run tick 90`, dump, `run save_game name=cb_t6`, `run load_save name=cb_t6`, dump → identical item cells and progress (±0.01), `items` equal, no errors.
- [ ] **Step 5: Verify rendering (windowed).** Same layout, `set_speed 1`, two screenshots 1 s apart → items visible and moved, drawn above belts.
- [ ] **Step 6: Verify rot.** "Seed raw meat on first belt", let it reach the end of a dead-end line, dump, `run tick 60000`, dump → the meat's rot % increased.
- [ ] **Step 7: Commit** `git commit -am "feat: belt building, map component, off-map item store, rendering, save/load"`

---

### Task 7: Two-click belt placement designator

**Files:**
- Create: `Placement/Designator_BeltPath.cs` (abstract base + `Designator_BeltPath_Basic`, `_Fast`, `_Express` with parameterless ctors)
- Modify: `CB_Belts.xml` (`canGenerateDefaultDesignator` false on belts), `CB_Conveyors.xml` (`specialDesignatorClasses` = the 3 subclasses)

**Interfaces:**
- Consumes: `BeltPath.Compute`, `Building_Belt`, `MapComponent_Belts`.
- Produces: `abstract class Designator_BeltPath : Designator_Build` with ctor `(ThingDef beltDef)`; `IntVec3? pendingStart`.
- Behaviour (spec §5.2): `DrawStyleCategory => null` (so every click calls `DesignateSingleCell`). First click stores `pendingStart`; second click computes `BeltPath.Compute(start, end, zFirst: Event.current.shift, placingRot.ToDir())` and for each tile: same def + same rot → skip; same def + other rot → set `Rotation`, `Sim.SetBelt`, dirty the map mesh at that cell (free, instant); other belt tier → replace blueprint via `GenConstruct.PlaceBlueprintForBuild` (god mode: spawn directly, which triggers the Task 12 swap); empty → `CanPlaceBlueprintAt` then blueprint (or spawn in god mode); otherwise skip. Then clear `pendingStart`. `SelectedUpdate` draws ghosts for the whole path with per-tile rotation, red where placement is refused. `SelectedProcessInput`: right-click or Esc with a pending start clears it and consumes the event; otherwise falls through to vanilla (deselect). Each designator has the tier's icon and label; ensure `isOrder` (set by `specialDesignatorClasses`) doesn't change how it displays; reset it in `Designator_BeltPath` if it does.

- [ ] **Step 1:** Implement.
- [ ] **Step 2: Verify headless:** boot → no errors; `run log contains=specialDesignator` → nothing.
- [ ] **Step 3: Verify windowed (manual, screenshots):** with god mode, Conveyors tab shows 3 belt buttons; click (OX,OZ) then (OX+5,OZ+5) → `query_things defName=CB_Belt_Basic` lists exactly the 11 cells/rotations from the Task 3 example; Shift variant gives the Z-first shape; right-click after the first click clears only the start; re-placing over the X leg with Q/E rotation unchanged skips; placing a Fast path over it creates replace blueprints (non-god mode).
- [ ] **Step 4:** Per-tile deconstruct with the vanilla deconstruct tool removes one tile and leaves neighbours.
- [ ] **Step 5: Commit** `git commit -am "feat: two-click L-path belt placement"`

---

### Task 8: Belt motor

**Files:**
- Create: `Buildings/Building_BeltMotor.cs`
- Create: `Defs/ThingDefs_Buildings/CB_Machines.xml` (motor def: `CompPowerTrader` with `basePowerConsumption` 5, `shortCircuitInRain` false, `rotatable` true)
- Modify: `MapComponent_Belts` (`Sim.RequireMotors = true` default), keyed strings

**Interfaces:**
- Consumes: `Sim.AddMotor/SetMotorFacing/SetMotorPowered/RemoveMotor/GetMotorStatus/AdjacentBeltDirs`.
- Produces: `class Building_BeltMotor : Building` — on `SpawnSetup`: if its facing cell has no belt, rotate to the first of `AdjacentBeltDirs`; `AddMotor`. Each tick: push `power.PowerOn` to the sim when it changes; every 30 ticks set `power.PowerOutput = -GetMotorStatus().Watts`; if `Idle` and an adjacent belt exists, auto-face it. Gizmo "Rotate motor" (`CB_RotateMotor`, icon `UI/RotateMotor`) cycles through `AdjacentBeltDirs` only. Inspect: `CB_MotorDriving` "Driving {0} tiles, {1} W" / `CB_MotorStandby` "Line already driven" / `CB_MotorIdle` "No belt in front".

- [ ] **Step 1:** Implement.
- [ ] **Step 2: Verify headless.** Build an L path with belts via `spawn_thing` (X leg 0..4 east at OZ, Z leg at OX+5 north 0..5). Seed items. Spawn motor at (OX+2, OZ-1) `power=true` → its inspect shows "Driving 5 tiles, 45 W"; X-leg items move, Z-leg items don't (dump after 120 ticks). Add a second motor beside the Z leg → Z-leg moves; third motor on the X leg → "Line already driven"; `run power id=<first> on=false` → third motor takes over, X leg keeps moving.
- [ ] **Step 3:** Remove the belt the motor faces (debug destroy) → motor "No belt in front" and 5 W; placing a belt next to it → it turns to face it.
- [ ] **Step 4: Commit** `git commit -am "feat: belt motor drives one line, power scales with line"`

---

### Task 9: Port tab framework + Input Port

**Files:**
- Create: `UI/IPortTabOwner.cs`, `UI/PortTabSection.cs`, `UI/ITab_Port.cs`, `UI/Section_InputFilter.cs`, `Buildings/Building_InputPort.cs`
- Modify: `CB_Machines.xml` (input port def: `thingClass` `ConveyorBelts.Building_InputPort`, `inspectorTabs` = only `ConveyorBelts.ITab_Port`, `building/defaultStorageSettings` with an empty filter and priority Normal, `building/fixedStorageSettings` allowing all haulable items, `CompPowerTrader` 50 W), keyed strings

**Interfaces:**
- Produces:
  - `interface IPortTabOwner { IEnumerable<PortTabSection> TabSections { get; } }`.
  - `abstract class PortTabSection { abstract float Draw(Rect rect); }` (returns height used).
  - `class ITab_Port : ITab` — label `CB_TabPort` "Port"; stacks the selected owner's sections in a scroll view; size 420×480.
  - `class Section_InputFilter : PortTabSection` — priority dropdown (vanilla `StoragePriority` values), "Allowed items (n)" list with ✕ remove buttons, search box + matching item list (labels of `ThingDef`s allowed by `fixedStorageSettings`, case-insensitive substring, max 30 shown) where clicking adds. Edits the port's `StorageSettings.filter` / `Priority` directly.
  - `class Building_InputPort : Building_Storage, IPortTabOwner` — while powered, each tick: if it holds a stack and `TryFeed` finds an adjacent belt cell `b` (round-robin over N, E, S, W) with `Sim.CanInsert(b, fromSide)` where `fromSide` = direction from `b` toward the port, split off `min(stack, Sim.MaxStackAt(b))` and `TryPutOnBelt`. Gizmo "Configure items" opens `ITab_Port`. Vanilla copy/paste storage-settings gizmos kept.
- Risk 3 check: confirm the vanilla Storage tab is not shown. If it still appears, add Harmony (dependency in About.xml, csproj reference as in RimTestHarness) and hide it with a postfix; record the outcome in PLAN.md §8.

- [ ] **Step 1:** Implement.
- [ ] **Step 2: Verify headless.** Spawn input port at (OX-1, OZ) `power=true` feeding the east line from Task 8 (motor powered). With default settings, spawn 30 steel 3 cells away and a colonist: `run tick 2000` → steel not hauled (default allows nothing). Add Steel via a debug action "Allow steel on all input ports" (mirrors what the tab does through the same `StorageSettings` call) → `tick 3000` → steel hauled into the port, then dump shows items of count ≤ 10 travelling.
- [ ] **Step 3:** Spawn steel ×75 directly on the port cell → belt items appear in stacks of 10 (basic max), port count decreases by 10 per insert.
- [ ] **Step 4:** `power on=false` on the port → no new belt items; hauling still fills the port.
- [ ] **Step 5: Verify windowed (manual, screenshot):** Port tab opens from the inspect bar and from the gizmo; vanilla Storage tab absent; search "ste" lists Steel; add/remove works and is reflected in hauling.
- [ ] **Step 6: Commit** `git commit -am "feat: port tab framework and input port"`

---

### Task 10: Output Port

**Files:**
- Create: `Buildings/Building_OutputPort.cs`, `UI/Section_OutputModes.cs`
- Modify: `CB_Machines.xml` (output port def, `rotatable` true, `CompPowerTrader` 50 W, `inspectorTabs` `ITab_Port`), keyed strings

**Interfaces:**
- Produces: `enum OutputFullMode { Hold, Spill }`; `class Building_OutputPort : Building, IBeltSink, IPortTabOwner` — `OutputFullMode fullMode = Hold` (saved); buffer `Thing buffer` (deep-saved); `TryAccept`: false when unpowered, buffer occupied, or `fromSide == Rotation.ToDir()`; on accept takes the thing out of the store into `buffer`. Tick: `GenPlace.TryPlaceThing(buffer, front, map, ThingPlaceMode.Direct)`; if not fully placed and `Spill`, retry with `Near`; else keep the remainder. `Destroy`/`DeSpawn` drops `buffer` on its own cell. Registers as sink on spawn. Section: two radio buttons (`CB_OutputHold`, `CB_OutputSpill`). Copy/paste gizmos for the mode (static clipboard).

- [ ] **Step 1:** Implement.
- [ ] **Step 2: Verify headless.** Input port → 6-tile line with motor → output port at the end facing east, all powered; allow steel; 60 steel on the input port; `tick 3000` → `cell_info` at output front cell shows Steel 60 in one stack (merged), `[CB] items=0`.
- [ ] **Step 3:** Put a wall-blocked or different-item-occupied front cell: place 1 wood on the front cell → Hold: belt backs up (items stay, dump shows front item p=1), steel count conserved. Switch to Spill via debug action "Set output ports to Spill" → steel appears on nearby cells.
- [ ] **Step 4:** Belt pointing into the output's front side is refused (items stay).
- [ ] **Step 5:** Unpowered output → belt backs up.
- [ ] **Step 6: Review focus 4.** With an item in `buffer` (Hold + blocked), destroy the output port → the item lies on the port's former cell; total steel conserved.
- [ ] **Step 7: Commit** `git commit -am "feat: output port with hold/spill"`

---

### Task 11: Pusher Port and machine adapters

**Files:**
- Create: `Sim/PusherLogic.cs`, `Pusher/IPushAdapter.cs`, `Pusher/PushAdapters.cs`, `Buildings/Building_PusherPort.cs`, `UI/Section_PusherModes.cs`
- Modify: `CB_Machines.xml` (pusher def like the output port), keyed strings
- Test: `ConveyorBelts.Tests/PusherLogicTests.cs`

**Interfaces:**
- Produces:
  - `enum PusherRejectMode { EjectIfNeverAccepted, EjectIfNotAcceptedNow, AlwaysWait }`, `enum EjectSides { LeftThenRight, RightThenLeft }`, `enum PusherAction { Push, Wait, Eject }`, `static PusherAction PusherLogic.Decide(PusherRejectMode mode, int acceptNow, bool everAccepts)`.
  - `interface IPushAdapter { bool Applies(Thing target); bool EverAccepts(Thing target, ThingDef def); int CanAccept(Thing target, Thing item); void Accept(Thing target, Thing item); }` (`item` passed to `Accept` already has the accepted count).
  - `static class PushAdapters { IPushAdapter For(Thing target); Thing FindTarget(IntVec3 cell, Map map); }` — order: Frame, CompRefuelable, Building_FermentingBarrel, storage (`ISlotGroupParent`). Adapters:
    - Frame: `EverAccepts` = def in `TotalMaterialCost()`; `CanAccept` = `ThingCountNeeded(def)`; `Accept` = `resourceContainer.TryAdd`.
    - Refuelable: `EverAccepts` = `Props.fuelFilter.Allows(def)`; `CanAccept` = `GetFuelCountToFullyRefuel()`; `Accept` = `Refuel(new List<Thing>{item})`.
    - Barrel: `EverAccepts` = def is `ThingDefOf.Wort`; `CanAccept` = `SpaceLeftForWort`; `Accept` = `AddWort(item)`.
    - Storage: `EverAccepts` = `GetStoreSettings().AllowedToAccept(def)`; `CanAccept` = space in its slot cells for this thing (empty valid cell → stack limit; same-def stack → room left); `Accept` = place into that cell with `ThingPlaceMode.Direct`.
  - `class Building_PusherPort : Building, IBeltSink, IPortTabOwner` — modes saved (`rejectMode = EjectIfNeverAccepted`, `ejectSides = LeftThenRight`), one-item `buffer` (deep-saved); `TryAccept` like the output port. Tick when powered with a buffer: target = `FindTarget(front)`; `acceptNow = min(buffer.stackCount, adapter.CanAccept)` (0 with no target/adapter), `everAccepts` from adapter (false with none) → `Decide`: Push → split and `Accept`; Eject → side cells in `ejectSides` order: a belt there whose dir points away from the pusher and `TryPutOnBelt` succeeds, else `GenPlace.TryPlaceThing(Direct)` on that cell; if neither side works, wait. Destroy drops `buffer`. Section shows "Facing: {label}", "Accepts now: {def} (up to {n})", radio buttons for the 3 modes, eject-side dropdown.

- [ ] **Step 1: Write the failing test**

```csharp
[Theory]
[InlineData(PusherRejectMode.EjectIfNeverAccepted, 5, true,  PusherAction.Push)]
[InlineData(PusherRejectMode.EjectIfNeverAccepted, 0, true,  PusherAction.Wait)]
[InlineData(PusherRejectMode.EjectIfNeverAccepted, 0, false, PusherAction.Eject)]
[InlineData(PusherRejectMode.EjectIfNotAcceptedNow, 0, true, PusherAction.Eject)]
[InlineData(PusherRejectMode.AlwaysWait, 0, false, PusherAction.Wait)]
[InlineData(PusherRejectMode.AlwaysWait, 3, false, PusherAction.Push)]
public void Decide(PusherRejectMode m, int now, bool ever, PusherAction expected) =>
    Assert.Equal(expected, PusherLogic.Decide(m, now, ever));
```

- [ ] **Step 2:** `dotnet test` → FAIL; implement `PusherLogic`; → PASS.
- [ ] **Step 3:** Implement adapters and the building.
- [ ] **Step 4: Verify headless, one layout per adapter** (input port → 4-tile line with motor → pusher facing the machine, all powered):
  - Wood-fired generator (`WoodFiredGenerator`) at empty fuel, wood fed → its fuel rises to full, then extra wood waits on the belt (default mode, acceptable but full).
  - Nutrient paste hopper (`Hopper`) with rice → rice stack appears in the hopper; steel fed instead → ejected to the left cell.
  - Fermenting barrel with wort ×30 → barrel inspect shows 25 wort, 5 wort wait.
  - Wall frame: `run spawn_thing defName=Frame_Wall stuff=Steel faction=player` in front of the pusher, steel fed → frame inspect shows its steel requirement met; surplus steel waits.
- [ ] **Step 5:** Eject onto a belt: a belt on the pusher's left pointing away, with motor → rejected items travel away on it.
- [ ] **Step 6:** Both sides blocked → item waits; `EjectIfNotAcceptedNow` mode on a full generator → wood ejected.
- [ ] **Step 7: Review focus 4.** Destroy a pusher with a buffered item → item on the pusher's former cell.
- [ ] **Step 8: Verify windowed (manual, screenshot):** Pusher section shows the facing machine and live "accepts now".
- [ ] **Step 9: Commit** `git commit -am "feat: pusher port with refuel/barrel/frame/storage adapters and reject modes"`

---

### Task 12: Tier upgrades — swap hook, Select line, Upgrade line

**Files:**
- Create: `Placement/BeltGizmos.cs`
- Modify: `Buildings/Building_Belt.cs` (swap on spawn, gizmos), keyed strings

**Interfaces:**
- Consumes: `Sim.LineIdAt`, `Sim.LineCells`, `Sim.SetBelt`.
- Produces: in `Building_Belt.SpawnSetup`, if another `Building_Belt` is on the cell: keep that belt's `Rotation` when this spawn came from an upgrade blueprint placed by the gizmo or designator (both place it with the old rotation, so just use `Rotation`), set `old.swappingOut = true`, `Sim.SetBelt(cell, dir, Tier)`, `old.Destroy(DestroyMode.Deconstruct)` (vanilla refund). Gizmos (multi-select aware): "Select line" (`CB_SelectLine`) selects every `Building_Belt` in `LineCells` of each selected belt's line; "Upgrade to {tier}" / "Downgrade to {tier}" (`CB_ChangeTier`, one per other tier) places `PlaceBlueprintForBuild(tierDef, cell, map, belt.Rotation, Faction.OfPlayer, null)` on every selected belt where `CanPlaceBlueprintAt` accepts.

- [ ] **Step 1:** Implement.
- [ ] **Step 2: Verify place-over keeps running (headless, risk 2).** Running 6-tile basic line with items. `run place_blueprint defName=CB_Belt_Fast x=OX+3 z=OZ rot=east` → accepted; spawn a colonist with steel nearby, `tick` until a `Frame` exists on (OX+3) → `query_things` shows both the frame and the basic belt there; items keep passing (dump). After completion: one `CB_Belt_Fast` at (OX+3), no basic belt, items on that cell kept, steel refund lying nearby, motor watts went up by 5.
- [ ] **Step 3:** God mode (`set_debug field=godMode value=true`) + direct `spawn_thing` of `CB_Belt_Express` on an occupied belt cell → instant swap, items kept.
- [ ] **Step 4: Verify windowed (manual):** select one belt → "Select line" selects the whole straight run → "Upgrade to Fast" places blueprints on all, rotations preserved.
- [ ] **Step 5:** Cancel an upgrade frame mid-construction → old belt still there and running.
- [ ] **Step 6: Review focus 5.** With items mid-line and an upgrade frame half-built: `save_game` → `load_save` → items at the same positions (±0.01), old belt runs, frame completes later and swaps cleanly.
- [ ] **Step 7: Commit** `git commit -am "feat: in-place tier upgrades, select line and upgrade line gizmos"`

---

### Task 13: Taking items off belts

**Files:**
- Create: `Jobs/JobDriver_TakeFromBelt.cs`, `Jobs/FloatMenuOptionProvider_TakeFromBelt.cs`, `Defs/JobDefs/CB_Jobs.xml`
- Modify: keyed strings

**Interfaces:**
- Produces: JobDefs `CB_HaulFromBelt` and `CB_PickUpFromBelt` (both `driverClass` `ConveyorBelts.JobDriver_TakeFromBelt`, report strings "taking TargetA from belt"). Job layout: `targetA` = belt cell, `count` = item handle (the thing's `thingIDNumber`). `FloatMenuOptionProvider_TakeFromBelt : FloatMenuOptionProvider` (Drafted and Undrafted true, Multiselect false, RequiresManipulation true): for each item on `context.ClickedCell`'s belt, option `CB_HaulFromBelt` "Haul {0} to stockpile" only when `StoreUtility.TryFindBestBetterStoreCellFor(thing, pawn, map, StoragePriority.Unstored, pawn.Faction, out _)`, and `CB_PickUpFromBelt` "Pick up {0}". Driver: goto cell (`PathEndMode.OnCell`), then `MapComponent_Belts.TakeFromBelt(cell, handle)`; null → end job `Incompletable` silently; pick-up → `pawn.inventory.innerContainer.TryAdd(thing)`; haul → `GenPlace.TryPlaceThing(thing, cell, map, Near, out placed)` then `jobQueue.EnqueueFirst(HaulAIUtility.HaulToStorageJob(pawn, placed, forced: true))`.

- [ ] **Step 1:** Implement.
- [ ] **Step 2: Verify haulables (headless).** Stopped line full of steel, colonist idle, stockpile zone nearby (`debug_action` or spawn a shelf) → `tick 3000` → belt items unchanged (pawns ignore them, spec §5.5).
- [ ] **Step 3: Verify order (headless).** `run order_job pawnId=<id> jobDef=CB_PickUpFromBelt targetX=.. targetZ=.. count=<handle from dump>` → after ticking, pawn inventory has the steel (`query_things id=<pawn> deep=true`), belt item gone, steel total conserved. Same with `CB_HaulFromBelt` → steel ends on the stockpile/shelf.
- [ ] **Step 4: Verify windowed (manual):** right-click a belt with items shows both options per item; "Haul" hidden when no storage accepts it.
- [ ] **Step 5: Review focus 3.** Order pick-up of an item on a running line far from the pawn → by arrival it has moved on → job ends, no error in log, no duplicate. Same with the belt deconstructed before arrival.
- [ ] **Step 6: Commit** `git commit -am "feat: haul / pick up items from belts on order"`

---

### Task 14: Performance pass, edge checklist, README

**Files:**
- Modify: `Debug/CB_DebugActions.cs`, `Map/MapComponent_Belts.cs` (timing counters), `PLAN.md` (§5.6 measured numbers)
- Create: `README.md`

- [ ] **Step 1:** Debug action "Perf: build test layout" spawns 20 parallel east lines of 100 basic belts (2,000 tiles), a powered motor per line and 4,000 steel items (2 per tile), plus "Perf: report" logging `[CB perf] tick avg {ms} ms, draw avg {ms} ms over 600 ticks/frames` (Stopwatch around `Sim.Tick()` and the draw loop).
- [ ] **Step 2: Measure (windowed).** `set_speed speed=3`, wait 30 s, report. Target (spec §5.6): tick avg ≤ 2 ms and smooth frames. Record the numbers in PLAN.md §5.6. If missed, record by how much and stop: optimisation (spec approach 4a-B) is a separate follow-up, not part of v1.
- [ ] **Step 3: Edge checklist** (each as a short headless run; note results in README "Verification"): deconstruct belt with items → dropped; destroy input/pusher/output with contents → dropped; save/load mid-flow; motor removed mid-flow → line freezes; belt removed under a motor → idle; upgrade mid-flow; steel totals conserved in every case.
- [ ] **Step 4:** Write `README.md`: what the mod does, buildings and tiers table, how to build (`dotnet build -c Release`), how to run tests, verification results, known limits (belt items not counted in wealth; no splitters/filters).
- [ ] **Step 5: Commit** `git commit -am "chore: perf measurement, edge checklist, README"`

---

## Self-review notes

- Spec coverage: §5.1 → T1/T8–T11; §5.2 → T3/T7; §5.3 → T4/T8; §5.4 → T5; §5.5 → T6/T13; §5.6 → T2/T6/T14; §5.7 → T9–T11; §5.8 → T9; §5.9 → T11; §5.10 → T10; §5.11 → T7/T12; §5.12 → T13; §8 risks 1–3 → T2/T12/T9.
- Names used across tasks: `BeltSim`, `MapComponent_Belts.For`, `TryPutOnBelt`, `TakeFromBelt`, `IBeltSink.TryAccept(long, int, Dir)`, `Sim.CanInsert(Cell, Dir)`, `Sim.MaxStackAt(Cell)` — defined in Tasks 4–6, consumed unchanged in 8–13.
