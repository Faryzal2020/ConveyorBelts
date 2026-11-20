# Conveyor Belts

RimWorld 1.7 mod (Core only, no Harmony). Powered conveyor belts move item stacks between ports, with per-tile item simulation kept off the map (belt items are not spawned things).

packageId `faryzal2020.conveyorbelts`, author visiadidaya. Architect tab: **conveyors**.

## What it does

- Draw a belt path with two clicks (start, end); the path is laid as belts with arrows. Right-click the belt ghost to cancel.
- A **belt motor** built on any free cell next to a line (beside it, behind its first tile, or past its last tile) turns to face that belt and drives the whole line while powered. No power or no motor: the line stops and items stay where they are. Motors and ports cannot be built on a belt cell ("Remove the belt first").
- **Input ports** take items that haulers bring (filtered in the Port tab) and put them on adjacent belts.
- **Pusher ports** push what a belt delivers into the machine they face (storage, fuel users, fermenting barrels, construction frames).
- **Output ports** put what a belt delivers on the cell they face.
- Belts upgrade/downgrade in place (Select line, Upgrade/Downgrade gizmos); items stay on their cells.
- Right-click a belt tile holding items: "Pick up" / "Haul to stockpile" orders.
- Belt items keep rotting (at the belt cell's temperature).

## Buildings

| Building | Speed | Max stack | Power | Notes |
|---|---|---|---|---|
| Basic conveyor belt (yellow) | 1.5 tiles/s | 10 | 5 W per tile | 1 steel |
| Fast conveyor belt (red) | 3 tiles/s | 25 | 10 W per tile | 1 steel |
| Express conveyor belt (blue) | 6 tiles/s | 75 | 20 W per tile | 1 steel |
| Belt motor (grey) | | | 20 W + line's per-tile watts driving, 5 W standby/idle | 10 steel, 1 component |
| Input port (green) | | | 50 W | 10 steel, 1 component |
| Pusher port (orange) | | | 50 W | 10 steel, 1 component |
| Output port (cyan) | | | 50 W | 10 steel, 1 component |

Two items per tile, minimum gap half a tile. Tiers can be mixed in one line.

## Build and test

```bash
cd Source/ConveyorBelts && dotnet build -c Release     # writes ../../Assemblies/ConveyorBelts.dll
cd ../ConveyorBelts.Tests && dotnet test               # 59 unit tests (Sim/ compiled alone, no RimWorld references)
```

Needs the game's `Assembly-CSharp.dll` (see the csproj for the lookup path). There is no hot reload in 1.7: restart the game after rebuilding.

Debug actions (dev mode, category "Conveyor Belts") cover seeding items, dumping state, port/pusher/output tests, upgrade tests and the performance layouts ("Perf: build test layout" = 20 x 100 tiles, "Perf: build short-line layout" = 286 x 7 tiles, "Perf: zoom camera to whole layout", "Perf: report", "Perf: reset counters", "Perf: stop refill"). They were driven headless with `RimTestHarness` (`../RimTestHarness`).

## Performance (spec 5.6 target: 2,000 tiles, 4,000 moving items, <= 2 ms per tick)

Two layouts, each with a powered motor per line, a void sink at each line end and a top-up of 1 steel per line per tick, so about 4,000 items keep moving: 20 parallel lines of 100 basic belts, and 286 lines of 7 basic belts (the many-short-lines case). Windowed 1280x720, speed 2 (3x), 30 s, camera zoomed out so the whole layout is in view.

| Layout | Sim.Tick avg | max | motor Thing ticks | draw avg (items in view) |
|---|---|---|---|---|
| 20 x 100 tiles | 0.24 ms | 1.4 ms | 0.006 ms/tick | 1.7 ms/frame (3,999) |
| 286 x 7 tiles | 0.42-0.44 ms | 8.7-12 ms | 0.043 ms/tick | 1.6 ms/frame (~3,990) |

Target met for both. Caveats: Sim.Tick includes the test sinks destroying delivered items (about 14 per tick on the short-line layout), the short-line max is a single tick per ~4,900 (likely GC), port Thing ticks are not measured, and the frame rate stayed at the 30 fps window cap. Details in `PLAN.md` 5.6.

## Verification

Item totals are steel on belts + on the ground + in inventories (+ port buffers where noted). All runs headless, 8 basic belts + motor unless noted, `log level=error` empty in all.

| Case | Before | After |
|---|---|---|
| Deconstruct belts holding items | belt 40, ground 1170 | belt 0, ground 1214 (40 dropped + 4 vanilla refund) |
| Output port destroyed holding a stack (front cell walled, Hold) | belt 15 + buffer 5, ground 1170 | belt 15, ground 1175 (stack dropped on the port's cell) |
| Pusher port destroyed holding a stack (facing a wall, Always wait) | belt 15 + buffer 5, ground 1170 | belt 15, ground 1175 |
| Input port destroyed | has no buffer, nothing to lose | n/a |
| Save/load mid-flow | belt 40, ground 1170 | `FinalizeInit: restored 8/8 belt items` (logged in dev mode only), belt 40, ground 1170, items keep moving |
| Save/load, 286 x 7 layout (final review) | belt 3718, ground 994 | belt 3718, ground 994, all 286 motors "Driving 7 tiles, 55 W", items keep moving |
| Motor destroyed mid-flow | belt 40 | items frozen at identical progress after 120 ticks, belt 40 |
| Belt in front of motor destroyed | belt 40 | motor "No belt in front", 5 W; remaining line frozen, belt 40 |
| Upgrade mid-flow (2 fast + 1 express swapped in place, god mode) | belt 40, ground 1170 | belt 40, ground 1171 (refund), motor 85 W (= 20 + 5x5 + 2x10 + 20) |
| Earlier tasks | closed ring, head-on belts, item gone before pawn arrives, upgrade frame across save/load: see `.superpowers/sdd/.../task-*-report.md` | |

## Known limits

- Belt items and port buffers are not counted in colony wealth or the resource readout.
- Port buffers (output / pusher) do not rot while held.
- A downgrade keeps stacks larger than the new tier's limit on that cell.
- Pawns may haul an Output Port's drops back into an Input Port whose filter allows them; choose filters to avoid loops.
- No splitters, filters on belts, or merging priorities beyond round-robin.
- Walls and doors (and other vanilla buildings) can be built over a belt; blocking that needs Harmony. Motors and ports are refused there.
- Belt items don't deteriorate outdoors, and item comps other than rotting (for example egg hatching) are paused while an item is on a belt.
- Input ports only fill gaps in a busy line: the belt simulation ticks before buildings, so items already on the belt take the free space first.
- An idle motor re-faces another adjacent belt when its belt is removed, and doesn't turn back on its own when the belt is rebuilt (use Rotate motor).
- Pusher refuelling ignores the target's auto-refuel toggle.
- A pusher never drops rejected items on the floor of a side cell that holds a belt or a building: a belt there only takes them if it points away from the pusher (an overflow line); otherwise the pusher waits.
- Headless harness: the daily autosave never finishes, so `tick` is refused after about 60000 ticks (save/load resets).
- Manual checks still needed in a real window: two-click ghost preview and right-click cancel, rotate-motor gizmo, Port tab clicks, Select line / Upgrade line gizmo clicks, right-click belt menu.
