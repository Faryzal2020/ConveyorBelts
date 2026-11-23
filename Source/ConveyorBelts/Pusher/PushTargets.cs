using System.Collections.Generic;
using ConveyorBelts.Sim;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace ConveyorBelts
{
    /// <summary>
    /// Where a pusher can put an item on the cell in front, in the spec order: a belt not pointing into the pusher,
    /// the v1 machine adapters (frame, fuel, wort), a haul destination (the slot group at the cell, or an
    /// IHaulDestination that is also an IThingHolder, e.g. a grave). Plain ground is never a target.
    /// </summary>
    public static class PushTargets
    {
        interface ITarget
        {
            string Label { get; }
            int Room(Thing item);
            void Give(Thing part);
        }

        public static IntVec3 CellInFront(IntVec3 pos, Dir facing) => pos + facing.ToRot4().FacingCell;

        /// <summary>
        /// How many of item the cell in front takes right now (may exceed item.stackCount; 0 = cannot take now), and
        /// the target to pass to Give (null when 0). The caller still caps the move by the source count and the stack limit.
        /// </summary>
        public static int Room(IntVec3 cell, Map map, Dir pusherFacing, Thing item, out object target)
        {
            target = null;
            if (item == null) return 0;
            foreach (var t in Targets(cell, map, pusherFacing))
            {
                int room = t.Room(item);
                if (room > 0) { target = t; return room; }
            }
            return 0;
        }

        /// <summary>
        /// Gives part (unspawned, unowned, at most the Room reported for this target) to the target. The target absorbs,
        /// stores, consumes or spawns all of it; anything left in part (not destroyed, not spawned, not held, count &gt; 0)
        /// was refused and stays the caller's to put back.
        /// </summary>
        public static void Give(object target, Thing part)
        {
            if (target is ITarget t && part != null && !part.Destroyed && part.stackCount > 0) t.Give(part);
        }

        /// <summary>The target the pusher recognises in front, whether or not it has room now; null for none.</summary>
        public static string Describe(IntVec3 cell, Map map, Dir facing)
        {
            foreach (var t in Targets(cell, map, facing)) return t.Label;
            return null;
        }

        static IEnumerable<ITarget> Targets(IntVec3 cell, Map map, Dir facing)
        {
            if (map == null || !cell.InBounds(map)) yield break;

            // 1. Belt: pointing anywhere but into the pusher. A belt cell is only ever a belt target (never the floor under it).
            var comp = MapComponent_Belts.For(map);
            if (comp != null && comp.Sim.TryGetBelt(cell.ToCell(), out var beltDir, out _))
            {
                var fromSide = facing.Opposite();   // the side of the belt cell facing the pusher
                if (beltDir != fromSide) yield return new BeltTarget(comp, cell, fromSide);
                yield break;
            }

            // 2. v1 machine adapters.
            var list = map.thingGrid.ThingsListAt(cell);
            foreach (var a in PushAdapters.All)
                for (int i = 0; i < list.Count; i++)
                    if (a.Applies(list[i])) { yield return new AdapterTarget(a, list[i]); break; }

            // 3a. The slot group at the cell (shelf, hopper, stockpile zone).
            var sg = cell.GetSlotGroup(map);
            if (sg != null) yield return new SlotGroupTarget(sg, cell, map);

            // 3b. Haul destinations that hold things themselves (graves).
            for (int i = 0; i < list.Count; i++)
                if (list[i] is IHaulDestination hd && list[i] is IThingHolder && !(list[i] is ISlotGroupParent))
                    yield return new HolderTarget(hd, list[i]);
        }

        sealed class BeltTarget : ITarget
        {
            readonly MapComponent_Belts comp; readonly IntVec3 cell; readonly Dir fromSide;
            public BeltTarget(MapComponent_Belts comp, IntVec3 cell, Dir fromSide) { this.comp = comp; this.cell = cell; this.fromSide = fromSide; }
            public string Label => PullSources.BeltLabel;
            public int Room(Thing item) => comp.Sim.CanInsert(cell.ToCell(), fromSide) ? comp.Sim.MaxStackAt(cell.ToCell()) : 0;
            /// <summary>On refusal TryPutOnBelt leaves part untouched (and unstored).</summary>
            public void Give(Thing part) => comp.TryPutOnBelt(cell, fromSide.ToRot4(), part);
        }

        sealed class AdapterTarget : ITarget
        {
            readonly IPushAdapter adapter; readonly Thing machine;
            public AdapterTarget(IPushAdapter adapter, Thing machine) { this.adapter = adapter; this.machine = machine; }
            public string Label => machine.LabelCap;
            public int Room(Thing item) => machine.Destroyed ? 0 : Mathf.Max(0, adapter.CanAccept(machine, item));
            public void Give(Thing part) { if (!machine.Destroyed) adapter.Accept(machine, part); }
        }

        /// <summary>
        /// Vanilla storage rules per cell (IsValidStorageFor: filter, blockers, item slots), room = free item slots ×
        /// stack limit + room in stacks it merges with. A storage building counts with all its cells (it is one
        /// machine); a stockpile zone only with the cell in front. Cells a pawn has reserved are left alone.
        /// </summary>
        sealed class SlotGroupTarget : ITarget
        {
            readonly SlotGroup sg; readonly IntVec3 front; readonly Map map;
            public SlotGroupTarget(SlotGroup sg, IntVec3 front, Map map) { this.sg = sg; this.front = front; this.map = map; }

            public string Label => sg.parent is Thing th ? (string)th.LabelCap : sg.parent.SlotYielderLabel();

            IEnumerable<IntVec3> Cells()
            {
                if (sg.parent is Thing) { foreach (var c in sg.CellsList) yield return c; }
                else yield return front;
            }

            int CellRoom(IntVec3 c, Thing item)
            {
                if (!c.InBounds(map) || !c.IsValidStorageFor(map, item)) return 0;
                if (map.reservationManager.IsReserved(c)) return 0;
                var list = map.thingGrid.ThingsListAt(c);
                int items = 0, room = 0;
                for (int i = 0; i < list.Count; i++)
                {
                    var t = list[i];
                    if (t.def.category != ThingCategory.Item) continue;
                    items++;
                    if (t.CanStackWith(item)) room += Mathf.Max(0, t.def.stackLimit - t.stackCount);
                }
                return room + Mathf.Max(0, c.GetMaxItemsAllowedInCell(map) - items) * item.def.stackLimit;
            }

            public int Room(Thing item)
            {
                int total = 0;
                foreach (var c in Cells()) total += CellRoom(c, item);
                return total;
            }

            /// <summary>Places part cell by cell (ThingPlaceMode.Direct, merging into stacks there) up to each cell's room.</summary>
            public void Give(Thing part)
            {
                foreach (var c in Cells())
                {
                    if (part.Destroyed || part.Spawned || part.stackCount <= 0) return;
                    int space = CellRoom(c, part);
                    if (space <= 0) continue;
                    var piece = part.stackCount <= space ? part : part.SplitOff(space);
                    GenPlace.TryPlaceThing(piece, c, map, ThingPlaceMode.Direct);
                    // A split piece that did not fully fit goes back into part (Direct spawned or absorbed the rest).
                    if (piece != part && !piece.Destroyed && !piece.Spawned && piece.stackCount > 0)
                        part.TryAbsorbStack(piece, respectStackLimit: false);
                }
            }
        }

        /// <summary>An IHaulDestination holding things itself (graves): Accepts + the owner's capacity, minus what pawns are bringing.</summary>
        sealed class HolderTarget : ITarget
        {
            readonly IHaulDestination dest; readonly Thing thing;
            public HolderTarget(IHaulDestination dest, Thing thing) { this.dest = dest; this.thing = thing; }
            public string Label => thing.LabelCap;

            public int Room(Thing item)
            {
                if (thing.Destroyed || !dest.HaulDestinationEnabled) return 0;
                if (thing is Building_Grave && !item.CanBeBuried()) return 0;   // vanilla TryFindBestBetterNonSlotGroupStorageFor
                if (!dest.Accepts(item)) return 0;
                int room = ((IThingHolder)thing).GetDirectlyHeldThings()?.GetCountCanAccept(item) ?? 0;
                if (thing is IHaulEnroute e) room = Mathf.Min(room, e.GetSpaceRemainingWithEnroute(item.def));
                return room;
            }

            public void Give(Thing part)
            {
                if (thing.Destroyed) return;
                if (thing is Building_Casket casket) casket.TryAcceptThing(part);   // grave: unclaims, redraws
                else ((IThingHolder)thing).GetDirectlyHeldThings()?.TryAdd(part, part.stackCount);
            }
        }
    }
}
