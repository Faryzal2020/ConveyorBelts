# ConveyorBelts — Plan (MVP v1)

Status: **draft rev 2 (your answers to Q1–Q5 folded in), awaiting your approval.** Nothing has been
built yet; this folder holds only this document.
Target: RimWorld 1.7 (the CoreCLR / Waylith build installed on this Mac), Core only (no DLCs are installed).
Date: 2026-11-20

Legend: **[You]** = your requirement, idea or answer, as you stated it. **[Proposal]** = my call, open
to correction. **[Verified]** = checked against the decompiled 1.7 `Assembly-CSharp.dll` or Core defs
on this machine.

---

## 1. Intent

Add Factorio-style conveyor belts to RimWorld: one-tile directional belts, driven by powered motors,
that move items between ports so materials can flow into machines without pawns carrying every stack.
Items on belts are simulated and drawn by the mod itself (not as vanilla item stacks), so many items
can move smoothly without hurting performance.

The v1 goal is **working mechanics**, not balance **[You]**: placeholder 64×64 textures, no recipe
tuning.

### Goals (v1)
- Belts that place like a two-click "L" path, delete per tile like conduits, merge from the side.
- Belt motors that power one straight line of belt each.
- Input Port, Pusher Port, Output Port, each with its own settings tab and its own power.
- Several belt tiers (speed + stack size), upgradable in place.
- Own item simulation + rendering, saved with the map.

### Non-goals (v1)
- Recipe / research balancing **[You]**.
- Corner belt graphics **[You]** (any belt can point into the side of another and merge).
- Splitters, filters on belts, underground belts, sorters (later versions).
- DLC machine support (none installed; the adapter system leaves room for it).

### Anti-goals (would be a failure even if it "works")
- Items on belts spawned as real map items each tick (the thing your rendering requirement exists to avoid).
- Items silently lost or duplicated (on deconstruct, upgrade, save/load, port overflow).
- Pawns treating belt items as loose items that need hauling to a stockpile **[You]**.

---

## 2. Requirements (as you stated them)

1. **Conveyor belt** — one-tile, directional, zero-height building. Click once = start, click again =
   end. Path goes along X first, then Z. Example: (0,0) → (5,5) places (0,0)–(4,0) pointing east,
   then (5,0)–(5,5) pointing north (the corner tile (5,0) points north). Belts delete per tile like
   conduits.
2. Belts only move items tile to tile; input/output need other buildings.
3. Pawns cannot put items onto belts, but can take items off belts. A right-click order like vanilla's
   "haul to stockpile" is fine, but pawns must **not** see belt items as loose items needing hauling.
4. **Input Port** — tile building that feeds a belt from any side. Its item settings use **our own
   tab** (opened from the inspect-tab button), simpler than the vanilla storage filter and open to
   later customisation.
5. **Pusher Port** — belts feed into it; must sit next to a machine that takes input material or asks
   for hauled material ("waiting for X to be hauled"). Has its own behaviour settings, e.g. throw an
   unwanted item to the side. First targets: storage/hoppers, refuelables, fermenting barrel,
   construction frames.
6. **Tiers** — different speed and stack size. Upgrade a whole line in place without deconstructing,
   or just place the higher tier over the old one.
7. **Output Port** — automatically throws out items it receives from a belt. Whether it spills when the
   drop cell is full or holds is a behaviour option.
8. **Rendering** — items on belts use the mod's own rendering/positioning so they animate smoothly
   without performance issues.
9. **Belt Motor** — needs electricity. Placed next to a belt, it automatically faces that belt and makes
   it run. Power draw depends on how long the belt is. One motor drives **one line** of belt only, so an
   L-shaped belt needs two motors. Ports (Input, Pusher, Output) have their own power.

Your ideas adopted as-is: 64×64 placeholder textures with a clear arrow and a distinct colour per
building and tier; no corner pieces; no recipe balancing yet.

---

## 3. What I found in the 1.7 game code (bears on the design)

- **Two-click placement fits the vanilla input loop. [Verified]** `DesignatorManager.ProcessInputEvents`
  calls `DesignateSingleCell(cell)` on every left click when the designator has no draw style, and the
  designator stays selected. So a custom build designator can store the first click and place the path
  on the second. Vanilla drag styles do not fit: they are click-and-drag, and the built-in `AngledLine`
  style is a diagonal staircase, not an L.
- **Right-click deselects the whole designator. [Verified]** To make right-click cancel only the
  pending start point, the designator overrides `SelectedProcessInput`.
- **In-place upgrade is mostly a vanilla feature, plus one hook. [Verified by reading code, not yet
  tested in game]** `GenConstruct.CanReplace` allows placing a blueprint of one def over a different def
  that shares a `replaceTags` entry (how walls swap material). But `GenSpawn.SpawningWipes` only removes
  the old building for *edifices*; belts are non-edifices (like conduits), so neither the frame nor the
  finished new belt removes the old belt. Result: **the old belt keeps running while the upgrade is
  under construction** (good: no gap in the line), and the mod must remove the old belt itself when the
  new one spawns (see §5.11).
- **Conduit pattern. [Verified]** Power conduits are `isEdifice=false`, `altitudeLayer=Conduits`,
  `passability=Standable`. Belts copy this: walkable, zero height, deletable per tile with the normal
  deconstruct tool.
- **Storage buildings come with hauling for free. [Verified]** `Building_Storage` implements
  `IHaulDestination` / `ISlotGroupParent` / `IStoreSettingsParent`, so pawns haul into it with no new
  AI. The Input Port keeps this underneath and swaps the vanilla storage tab for our own (§5.7).
- **Machine input APIs for the Pusher Port. [Verified]**
  - `CompRefuelable.GetFuelCountToFullyRefuel()` + `Refuel(List<Thing>)` (generators, torches, stoves).
  - `Building_FermentingBarrel.AddWort(Thing)` (wort).
  - `Frame.ThingCountNeeded(def)` + its resource container (construction frames "waiting for material").
  - Any `Building_Storage` / slot-group parent (nutrient paste hoppers, shelves).
- **Drawing a thing at an arbitrary spot. [Verified]** `Thing.DrawNowAt(Vector3)` exists (vanilla uses
  it for carried items). **[Unverified]** That calling it from `MapComponentUpdate` renders correctly
  and fast on the Waylith engine. This is the biggest technical risk, so it is the first thing to test
  (Milestone 1).

---

## 4. Approaches considered

### 4a. Where belt items live (core decision)

| | Approach | Trade-off |
|---|---|---|
| **A (chosen)** | **Per-cell belt state in a `MapComponent`.** Each belt cell keeps a small ordered list of items, each with a progress value 0→1 along the tile. Each item wraps the real `Thing`, despawned (off the map, not ticked or drawn by vanilla). | Simple, fast enough for thousands of items (one float update per item per tick). Keeps every item detail (quality, HP, stuff, ingredients) because the real Thing is kept. State is keyed by cell, not by building, so items survive a belt being swapped for an upgrade. |
| B | Segment / gap-list simulation (Factorio's approach: long belt runs compiled into segments; only gaps tracked). | Much cheaper at very large scale, but complex rebuilds on every edit. Can replace A later behind the same interface if profiling demands it. |
| C | Real map items moved each tick. | Ruled out by your rendering requirement. |

### 4b. Input Port
- **A (chosen):** subclass `Building_Storage` (1 cell) so pawns haul into it with vanilla AI, but hide
  the vanilla Storage tab and show our own **Port tab** that edits the same underlying filter (§5.7).
- B: custom WorkGiver + Job to haul into a non-storage building. More code, no benefit.

### 4c. Placement
- **A (chosen):** custom `Designator_Build` subclass with no draw style and a two-click state machine,
  drawing a ghost preview of the L path with per-tile arrows.
- B: vanilla drag style. Doesn't match the click-click behaviour you specified.

### 4d. Pawns taking items off belts
- **A (chosen, per your answer):** belt items are invisible to vanilla hauling AI (never listed as
  haulables). Right-clicking a belt with a pawn selected offers orders modelled on vanilla's loose-item
  menu (§5.11).
- B: make belt items visible to vanilla haul and bill-ingredient searches. Ruled out by your answer.

---

## 5. Design

### 5.1 Buildings and defs

| Def | Type | Colour [Proposal] | Power | Notes |
|---|---|---|---|---|
| `CB_Belt_Basic` | belt | yellow | via motor | tier 1 |
| `CB_Belt_Fast` | belt | red | via motor | tier 2 |
| `CB_Belt_Express` | belt | blue | via motor | tier 3 |
| `CB_BeltMotor` | motor | grey | yes, scales with line length | drives one line (§5.3) |
| `CB_InputPort` | port | green | yes, flat | storage, feeds belts |
| `CB_PusherPort` | port | orange | yes, flat | rotatable; front faces the machine |
| `CB_OutputPort` | port | cyan | yes, flat | rotatable; front faces the drop cell |

- Belts: `isEdifice=false`, `altitudeLayer=Conduits`, `Standable`, rotatable, `replaceTags: [ConveyorBelt]`,
  cheap placeholder cost (e.g. a little steel), no research **[Proposal]**. Belts themselves have no
  power comp.
- Motor and ports: 1×1 edifices with `CompPowerTrader`, connect to power like any powered building
  (conduit or adjacent power). **[Proposal]** Nothing shares a cell with a belt.
- Textures: 64×64 PNGs per direction (`Graphic_Multi`: `_north/_east/_south/_west`), a coloured square
  with a big arrow, generated by a small script so they can be regenerated **[You + Proposal]**.

**Tier stats [Proposal, placeholder numbers]** (60 ticks = 1 s at 1× speed):

| Tier | Speed (tiles/s) | Max stack per belt item | Items per tile | Motor draw per tile |
|---|---|---|---|---|
| Basic | 1.5 | 10 | 2 | 5 W |
| Fast | 3 | 25 | 2 | 10 W |
| Express | 6 | 75 | 2 | 20 W |

Items per tile is fixed at 2 (min spacing 0.5 tile) for every tier. Ports draw a flat placeholder 50 W
each; a motor adds a 20 W base on top of its per-tile load.

### 5.2 Placement (two-click L path)
- Click 1: store start cell; a ghost path follows the mouse.
- Click 2: compute the path, place blueprints (or instant-build in god mode), clear the start, stay
  in placement mode for the next line.
- **Path rule [You]:** walk along X from start to end.x, then along Z to end.z. Tiles on the X leg
  point east/west (toward the end); the corner tile and the Z leg point north/south. If start and end
  are on the same row or column it's a straight line. Same cell twice = one tile using the current
  rotation (R/Q keys).
- **[Proposal]** Holding Shift while clicking the end flips the order (Z first, then X), for the other
  kind of corner.
- **[Proposal]** Right-click / Esc with a pending start clears the start only; a second right-click
  exits placement.
- Placing over an existing belt of a **different tier** makes a replace blueprint (vanilla
  `replaceTags`). Placing over the **same tier** with a different direction re-orients it instantly
  at no cost **[Proposal]**; same tier and same direction is skipped.
- Cells that can't take a belt (walls, ports, motors, water) are skipped and shown red in the preview;
  the rest of the path is still placed **[Proposal]**.

### 5.3 Lines and motors [You + Proposal]
- **A line** is a maximal straight run of connected belt tiles that all point the same way, each one
  feeding directly into the next. A change of direction starts a new line. In the (0,0)→(5,5) example
  that makes two lines: (0,0)–(4,0) east and (5,0)–(5,5) north, so it needs two motors **[You]**. A belt
  merging from the side is its own line. Tiers can be mixed within a line (half-upgraded lines still
  count as one).
- **A motor** is placed on any cell next to a line (beside it, behind its first tile, or past its
  last tile). On placement it turns to face an adjacent belt **[You]**. If more than one belt is
  adjacent, it picks the first and the player can rotate it, cycling through only the directions that
  face a belt **[Proposal]**. The motor drives the whole line that contains the belt it faces.
- **Power draw [You + Proposal]:** 20 W base + the per-tile draw of each tile in the line (by tier,
  table above). It updates whenever the line changes (tiles added, removed, upgraded, rotated). The
  motor's inspect text shows "Driving 12 tiles, 80 W".
- **Belt state:** a line moves only if it has a powered motor. An unpowered line freezes in place:
  items stop, and upstream belts back up behind it. Items may still be pushed onto the entry of a
  stopped line if there's room; they just don't move.
- **Two motors on one line [Proposal]:** the first powered one drives it; the others go to standby
  (minimal draw, inspect text "Line already driven"). If the driver loses power or is removed, a standby
  motor takes over.
- **No hard length cap** in v1 **[Proposal]**; long lines just cost more power.
- The map component keeps the line table, recomputed locally on belt spawn / despawn / rotate. The same
  "line" is used by the **Select line** and **Upgrade line** gizmos (§5.11), so all three agree.

### 5.4 Belt movement and merging
- A belt cell's **target** is the cell in front of it.
  - Target is a belt that doesn't point straight back → the item moves onto it. If it enters from the
    side, that's a merge **[You]**.
  - Target is a Pusher Port or Output Port → handed to the port when the port has room and is powered.
  - Anything else → the item stops at the end of the tile (belt backs up).
- **Item path within a tile:** an item enters at the edge it came from, travels to the tile centre,
  then to the exit edge. Side merges and corners therefore look right without corner sprites.
- **Merge fairness [Proposal]:** when several inputs compete for space on one tile, they alternate
  (round-robin), so a side feed can't starve forever.
- **Back-pressure:** an item can't advance into the space of the item ahead (min gap 0.5 tile).
- **Tier transitions [Proposal]:** a stack bigger than the receiving tier's max is split; the rest
  waits on the upstream tile.
- Only cells on running lines with moving items are processed each tick.

### 5.5 Item state, saving, destruction
- `BeltItem { Thing thing; float progress; Rot4 enteredFrom; }`, stored per cell in the map component;
  the Things are deep-saved with the component so all their data survives save/load.
- Belt items are **never** registered as haulables or in the resource counter, so pawns ignore them
  unless ordered **[You]**.
- **Deconstructing / destroying a belt tile** drops its items on that cell as normal map items
  (nothing lost).
- **Upgrading in place:** state is keyed by cell. The swap in §5.11 removes the old belt in the same
  call that spawns the new one and tells the map component it is a swap, so items stay on the cell and
  keep moving at the new tier's speed.
- **Perishables [Proposal]:** belt items keep rotting; the component ticks their rot comps on the same
  interval vanilla uses. Exact hook is left to the builder, but rot must not pause on belts.
- **Colony wealth / resource readout:** not counted in v1 **[Proposal]**; revisit later.

### 5.6 Rendering
- `MapComponentUpdate` (every frame) draws belt items inside the camera view only, at their
  interpolated position, using `thing.DrawNowAt(pos)` first, at a slightly reduced scale if possible.
- Drawn on an altitude above belts and below pawns.
- Fallback if `DrawNowAt` is too slow or misbehaves on Waylith: cache a material per def and draw
  meshes directly, batched (instanced) per material.
- Performance target **[Proposal]:** 2,000 belt tiles and 4,000 moving items at 3× speed adding
  ≤ 2 ms per tick and staying smooth in frame rate. Measured in Milestone 8.

### 5.7 Port settings tab (shared by all three ports) [You + Proposal]
Each port has one inspect tab, **"Port"**, built by us, replacing the vanilla Storage tab. The tab
is a small framework (a header plus stacked sections) so later options can be added without
redesigning it. Sketches (rough layout, not final art):

```
Input Port                                  Pusher Port
┌──────────────────────────────────┐        ┌──────────────────────────────────┐
│ Haul priority:   [ Normal    ▾ ] │        │ Facing: Wood-fired generator     │
│                                  │        │ Accepts now: Wood (up to 37)     │
│ Allowed items (3)    [ + Add ]   │        │                                  │
│   ▪ Steel                    ✕   │        │ When the machine won't take it:  │
│   ▪ Wood                     ✕   │        │ (•) Eject only if never accepted │
│   ▪ Component                ✕   │        │ ( ) Eject whenever not accepted  │
│                                  │        │ ( ) Always wait                  │
│ Add: [ search…            ]      │        │                                  │
│   Stone blocks (granite)         │        │ Eject side:  [ Left, then right ▾]│
│   Silver                         │        └──────────────────────────────────┘
│   …                              │
└──────────────────────────────────┘        Output Port
                                            ┌──────────────────────────────────┐
                                            │ When the drop cell is full:      │
                                            │ (•) Hold (belt backs up)         │
                                            │ ( ) Spill onto nearby cells      │
                                            └──────────────────────────────────┘
```

- **Input Port:** a list of allowed items with remove buttons, and a search box that adds items. No
  hit-point or quality sliders, no category tree in v1. Underneath it edits the vanilla `StorageSettings`
  filter and priority, so vanilla hauling keeps working.
- **Pusher Port:** shows the machine it faces and what that machine accepts right now; rejection mode;
  eject side.
- **Output Port:** full-cell behaviour **[You: hold or spill as an option]**; default Hold **[Proposal]**.
- Settings copy/paste between ports of the same kind via vanilla-style copy/paste gizmos **[Proposal]**.

### 5.8 Input Port
- `Building_Storage` subclass, 1 cell, holds one stack, needs power.
- **Default: no items allowed [Proposal]**, so a new port doesn't pull every item in the colony.
- Each tick it is powered, has a stack, and an adjacent belt has room at its entry, it splits off up to
  that belt's max stack and puts it on the belt.
- **Feeds from any side [You]:** pushes into any of the 4 adjacent belts that don't point into the
  port; several outputs take turns.
- Nothing can feed *into* an Input Port from a belt.
- Unpowered: pawns can still haul into it (it is storage), but it stops feeding belts **[Proposal]**.

### 5.9 Pusher Port
- Rotatable; the **front** faces the machine. Belts feed it from back or sides. Needs power.
- Holds a one-item buffer. Each tick it asks the machine in front, through a **target adapter**, how
  many of this item it will take, and pushes that many.
- **v1 adapters [You: confirmed]** (one small class each, picked by what the front building is):
  1. Storage / slot-group buildings — hoppers, shelves (respects their filters and space).
  2. `CompRefuelable` — fuel items up to "fully refuelled".
  3. Fermenting barrel — wort.
  4. Construction frames — materials still needed.
  Later (post-v1): work tables with bills, DLC machines.
- **Rejection behaviour** (Port tab) **[You + Proposal]**:
  - *Eject only if never accepted (default):* if the machine would never take this item (wrong type),
    throw it to the side. If it's acceptable but the machine is just full right now, wait.
  - *Eject whenever not accepted:* throw it to the side immediately.
  - *Always wait:* hold the item and block the belt.
- **"Throw to the side" [Proposal]:** in the chosen side order. If that side cell has a belt pointing
  away from the pusher, the item goes onto that belt (lets you build an overflow line); otherwise it
  drops on the ground there. If both sides are blocked, it waits.
- With no machine in front, it behaves as "not accepted". Unpowered: accepts nothing, belt backs up.

### 5.10 Output Port
- Rotatable; front faces the drop cell. Needs power.
- Takes items from belts pointing into it and drops them on the cell in front, merging into an
  existing same-kind stack there when possible.
- **Front cell full:** *Hold* (port keeps the item, belt backs up) or *Spill* (drop on the nearest free
  cell), chosen in the Port tab **[You]**.

### 5.11 Upgrades
- Place a higher tier directly over a line with the normal two-click tool → replace blueprints
  (vanilla).
- **"Upgrade line" gizmo [Proposal]:** with belts selected, a gizmo per other tier places replace
  blueprints on every selected belt, keeping each tile's direction. A **"Select line"** gizmo selects
  the whole line (as defined in §5.3), so upgrading a line is two clicks.
- **How the swap works:** while the upgrade frame is being built, the old belt stays and keeps moving
  items. When the new belt spawns, `Building_Belt.SpawnSetup` finds the other belt on its cell, keeps
  that cell's items, keeps the tile's direction, and destroys the old belt with deconstruct refund. If
  the frame is cancelled, the old belt simply stays. The motor's power draw updates to the new tier.
- **Cost [Proposal]:** the new tier costs its full price; the old belt is refunded the way vanilla
  deconstruction refunds.

### 5.12 Taking items off belts [You + Proposal]
- Belt items are never auto-hauled (§5.5).
- Right-click a belt tile with a pawn selected → for each item on that tile, the same orders vanilla
  offers for a loose item:
  - **"Haul [item] to stockpile"**, shown only when some stockpile or storage accepts it (as in vanilla);
  - **"Pick up [item]"** into the pawn's inventory.
- One small job: walk to the cell, then remove the item from the belt if it's still on that tile
  (it may have moved on, then the job ends), hand the pawn the real Thing, and continue as vanilla
  hauling or pick-up would.
- Pawns never put items on belts (no job does) **[You]**.

---

## 6. Code layout [Proposal]

```
ConveyorBelts/
  About/About.xml                  packageId e.g. faryzal2020.conveyorbelts, supportedVersions 1.7, Harmony dep
  Defs/ThingDefs_Buildings/ConveyorBelts.xml
  Defs/JobDefs/ConveyorBelts_Jobs.xml
  Languages/English/Keyed/ConveyorBelts.xml
  Textures/ConveyorBelts/...       generated 64x64 PNGs
  Assemblies/ConveyorBelts.dll
  Source/ConveyorBelts/            net10.0 csproj, references copied from RimTestHarness's csproj
    Sim/          BeltSim — pure C#, no RimWorld types: grid, lines, running flags, items as handles,
                  tick, merge, back-pressure, splitting
    Map/          MapComponent_Belts (owns BeltSim + Thing table, save/load, render)
    Buildings/    Building_Belt, Building_BeltMotor, Building_InputPort, Building_PusherPort,
                  Building_OutputPort
    Pusher/       IPushTarget + adapters
    UI/           ITab_Port + section classes (input filter list, pusher modes, output modes)
    Placement/    Designator_BeltPath, gizmos (select line, upgrade line)
    Jobs/         JobDriver_TakeFromBelt, float-menu provider
  Source/ConveyorBelts.Tests/      xUnit tests for Sim/ (run with `dotnet test`, no game needed)
  tools/make_textures.*            texture generator
  PLAN.md
```

Keeping the simulation free of RimWorld types means movement, lines, motors-on/off, merging,
back-pressure and splitting can be tested in seconds without launching the game.

---

## 7. Milestones

| # | Milestone | Done when |
|---|---|---|
| M0 | Scaffold: About.xml, csproj, textures, defs | Game boots with the mod, no errors in Player.log; buildings show in the architect menu |
| M1 | **Rendering spike** (throwaway) | 500 fake items move along a hard-coded path on Waylith via `MapComponentUpdate`; frame time measured; draw method chosen |
| M2 | Belt building + two-click designator + deconstruct | (0,0)→(5,5) produces exactly the tiles and directions in §5.2; per-tile deconstruct works |
| M3 | BeltSim + unit tests + map component + save/load | Items move, merge from sides, back up, survive save/load (tests + in game, motors faked as always-on) |
| M4 | Belt motor + lines + power | L path needs two motors; unpowered line freezes; draw matches tile count and tier; standby takeover works |
| M5 | Input Port + Output Port + Port tab framework | Pawn hauls steel into Input Port (set via our tab) → it travels → Output Port drops it; hold/spill both work |
| M6 | Pusher Port + 4 adapters + rejection modes | Fuels a generator, fills a hopper, feeds a barrel, supplies a frame; unwanted items eject sideways |
| M7 | Tiers + upgrade gizmos | Line upgrade keeps directions and items, belt keeps running during construction; place-over works |
| M8 | Take-from-belt orders, drop-on-destroy, perf pass | Pawns ignore belt items unless ordered; items never lost/duplicated in an edge-case checklist; perf target met |

**Testing:** `dotnet test` for the sim; in-game checks driven through the RimTestHarness mod (headless
boot to catch def/load errors; windowed for rendering). New harness commands may be needed (e.g. read
belt state); those go in the harness repo, not this mod.

---

## 8. Risks
1. **Rendering on Waylith** (unknown speed/behaviour of immediate-mode drawing) → M1 spike first.
   **RESOLVED (2026-11-20, Task 2 spike): PASS.** 500 unspawned steel stacks (count 10) drawn each frame with
   `thing.DrawNowAt(Vector3)` around a 20x20 loop at 3 tiles/s, speed 3, windowed 1280x720 (2880x1800 capture).
   Measured draw cost (Stopwatch around the loop, avg over 300 frames): `MapComponentUpdate` 0.13-0.19 ms,
   `MapComponentDraw` 0.12-0.20 ms; limit was 2 ms. Items render and move across the screenshot sequence with
   both hooks, no errors logged. **Chosen hook: `MapComponentDraw`** (equal cost; the correct phase). The fallback
   (cached material + batched meshes) is NOT needed for v1. Note: items under roofed/fogged rock are hidden as usual.
2. **Replace behaviour for non-edifices** is read from code, not yet tested: confirm in M7 that the
   blueprint can be placed over the old belt, the old belt survives the frame stage, and the spawn hook
   swaps cleanly.
3. **Hiding the vanilla Storage tab** on a `Building_Storage` subclass: if it can't be removed via the
   def's inspector tabs alone, it needs a small Harmony patch. Check in M5.
   **RESOLVED (2026-11-20, Task 9): no Harmony needed.** In 1.7 `Building_Storage` adds no tab itself:
   `Thing.GetInspectTabs()` returns `def.inspectorTabsResolved`, and `ITab_Storage` only comes from the def's
   `inspectorTabs` (vanilla shelves inherit it from `ShelfBase`). `CB_InputPort` lists only `ConveyorBelts.ITab_Port`
   (its parents `CB_MachineBase`/`BuildingBase` list none), so the inspect pane shows just "Port". Verified in game:
   `GetInspectTabs()` = [ITab_Port], and a windowed screenshot shows only the Port tab button. The vanilla copy/paste
   and link-settings gizmos stay (`storageGroupTag` `CB_InputPort`, so ports link only with ports).
4. **Mod conflicts** with other mods touching designators or storage → low for v1; note in README.

---

## 9. Your answers (2026-11-20)

- **Q1 Taking items:** vanilla-style right-click order is fine; pawns must not see belt items as
  loose items needing hauling. → §4d, §5.5, §5.12.
- **Q2 Input Port UI:** our own inspect tab, simpler than vanilla's filter, extensible. → §5.7.
- **Q3 Output Port full:** hold or spill as a behaviour option. → §5.7, §5.10.
- **Q4 Pusher targets:** storage/hoppers, refuelables, fermenting barrel, frames confirmed. → §5.9.
- **Q5 Power:** belt motors that face an adjacent belt, drive one line, draw power by line length;
  ports have their own power. → §5.3.

## 10. Left to the builder
Exact tick budget per item, interpolation details, the texture-generation tool, gizmo icons, translation
keys, the rot-ticking hook, Port tab layout details, and the def costs and power numbers, as long as they
meet the behaviour above.
