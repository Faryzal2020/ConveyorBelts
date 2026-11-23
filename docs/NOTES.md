# Conveyor Belts: developer and testing notes

A mod for RimWorld 1.7. Adds Conveyor Belts and other supporting facilities to make a fully functional automation system as minimum.

Requires Harmony. Core only. Architect tab: **conveyors**.

## What it does

- **Belts:** two clicks lay an L-shaped path (across first, then up/down). After the first click, **Q**, **E** or a middle-click flips it to up/down first. With no click pending, they rotate the single tile. Right-click cancels a pending start. Belts are surface-only: nothing else is built on a belt cell except a floor, a wall-mounted item, a different belt tier, or a **power conduit** (conduits may share belt and port cells, in either build order).
- **Belt motor:** put it next to a line (beside, behind the first tile or past the last). It faces the belt and drives the whole line while powered. A second motor on the same line sits on "Standby" and takes over if the first loses power or is removed. No power, no motor: the line stops and items stay put.
- **Input port:** takes the items its filter allows (haul priority in the tab) and puts them on adjacent belts.
- **Output port:** puts what a belt delivers on the cell it faces. **Hold** keeps the stack until the cell is free; **Spill** drops it nearby.
- **Pusher port:** moves items from the cell behind it to the cell in front, one move per second while powered. It holds nothing, and does nothing until its filter allows something.
  - Pulls from: a belt pointing into it (end item), items lying on the cell (a shelf or other storage building with all its cells, e.g. both cells of a 2-cell shelf; a stockpile zone or plain floor only that cell), a building's or component's container (transport pods, caskets without the person, graves), or a fermenting barrel's finished beer.
  - Pushes to: a belt, fuel users, a fermenting barrel (wort), construction frames, storage and shelves, graves.
  - Never pulls a live pawn, never takes materials back out of a construction frame, never puts items on bare ground, skips items a pawn has reserved.
  - **Pull from forbidden** is on by default. Moved items keep their forbidden state.
  - Moves as much as the target takes, up to one stack. Barrel beer moves only as a whole batch.
- **Filter tab** (Input and Pusher ports): a category tree on the left (tri-state boxes, expand arrows), a search box that lists matching items and categories with "Allow all", and on the right everything currently allowed with an X to remove. **All** / **None** for the whole filter.
- **Upgrade / Downgrade:** select a line, then use the buttons; each shows the target belt. Items stay on their cells.
- Right-click a belt tile holding items for "Pick up" / "Haul to stockpile". Belt items keep rotting, and count toward colony wealth.

## Buildings

| Building | Speed | Max stack | Power | Cost |
|---|---|---|---|---|
| Basic belt (yellow) | 1.5 tiles/s | 10 | 5 W per tile | 1 steel |
| Fast belt (red) | 3 tiles/s | 25 | 10 W per tile | 1 steel |
| Express belt (blue) | 6 tiles/s | 75 | 20 W per tile | 1 steel |
| Belt motor | | | 20 W + line watts driving, 5 W standby | 10 steel, 1 component |
| Input / Pusher / Output port | | | 50 W | 10 steel, 1 component |

Two items per tile. Tiers can be mixed in one line.

## Build and test

```bash
cd Source/ConveyorBelts && dotnet build -c Release     # writes ../../Assemblies/ConveyorBelts.dll
cd ../ConveyorBelts.Tests && dotnet test               # 51 unit tests (Sim/ only, no RimWorld references)
```

Needs the game's `Assembly-CSharp.dll` (see the csproj). No hot reload in 1.7: restart after rebuilding. Dev-mode debug actions (category "Conveyor Belts") seed items, dump state, build pusher scenarios and run the perf layouts; they were driven headless with `RimTestHarness`. Design: `PLAN.md` (v1) and `docs/superpowers/specs/2026-11-23-v1.1-design.md` (v1.1, wins where they differ).

## Verification

Headless, quicktest map, 8 basic belts + motors, `log level=error` empty.

| Check (v1.1 regression) | Result |
|---|---|
| Input port -> belt -> output port | 40 wood put in the port, moved 10 per stack onto the belt, reached the output port; with the front cell blocked the stack waited in the port (Hold); in Spill mode all 40 left, a colonist carried them back to the input port (the known loop below) |
| Motor standby takeover | second motor "Standby"; first powered off: items froze; second powered on: it showed "Driving 8 tiles, 60 W" and items moved on |
| Tier upgrade keeps items | 8 belts to fast in god mode mid-flow: 8 fast, 0 basic, same items on the same cells |
| Save/load mid-flow | `restored 9/9 belt items, deferred 0`, same cells, items keep moving, no errors |
| Wealth | belt items and output buffers counted: item wealth 9682.9 before, 9898.5 with 108.0 on belts and in buffers, and 9898.5 again after load |
| Pusher scenarios (empty filter, stacks over the room, barrel batch, reserved items, forbidden toggle, graves, frames) | details in the local SDD reports (not in the repo) |
| Perf (v1, 2,000 tiles / 4,000 moving items) | Sim.Tick 0.24 ms avg (20 x 100) and 0.43 ms avg (286 x 7); draw 1.6-1.7 ms/frame; target 2 ms |

## Known limits

- Belt items and output-port buffers are not shown in the resource readout (they count toward wealth).
- Items held in an Output Port buffer don't rot.
- Barrel beer moves only as a whole batch: a basic belt (max stack 10) can't take a full barrel; use a fast or express belt, or a storage target.
- The Pusher can pull from any building's or component's container, so test unusual modded buildings.
- The Pusher pulls only from the cell behind it (a storage building there counts with all its cells); a belt entering from the side stops at its end (v1 side-fed layouts stop).
- Fermenting barrels with modded capacity above Beer's stack limit (25) never empty by pusher.
- A part that merges into an existing stack takes that stack's forbidden state (vanilla behaviour).
- A downgrade keeps stacks larger than the new tier's limit on that cell.
- Pawns may haul an Output Port's drops back into an Input Port whose filter allows them; set filters to avoid loops.
- No splitters, belt filters or priorities beyond round-robin.
- Belt items don't deteriorate outdoors; item comps other than rotting (egg hatching) pause on a belt.
- Input ports only fill gaps in a busy line (belts tick before buildings).
- An idle motor re-faces another adjacent belt when its belt is removed and doesn't turn back when the belt is rebuilt (use Rotate motor).
- Pusher refuelling ignores the target's auto-refuel toggle.
- Headless harness: the daily autosave stalls `tick` after about 60000 ticks (save/load resets).

## Manual checks still needed in a real window

- Q/E and middle-click: flip the L after the first click; rotate a single tile before it. The on-screen rotate buttons still rotate while a start is pending (they only change a same-cell tile's direction, not the L).
- A conduit built over a built belt (and a belt over a conduit) joins the power net.
- Filter tab: tree clicks, expand arrows, search, "Allow all", the X on the allowed list, All/None, scrolling.
- Pusher tab: the "Pull from forbidden" checkbox.
- Upgrade / Downgrade buttons and their target-belt icons.
- From v1: two-click ghost preview and right-click cancel, rotate-motor button, Select line gizmo, right-click belt menu.
