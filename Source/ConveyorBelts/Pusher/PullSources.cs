using System;
using System.Collections.Generic;
using ConveyorBelts.Sim;
using RimWorld;
using Verse;

namespace ConveyorBelts
{
    /// <summary>One item a pusher could pull right now.</summary>
    public sealed class PullCandidate
    {
        /// <summary>The item (for a product: a stand-in with the product's def and count; never move it yourself).</summary>
        public Thing Thing;

        /// <summary>How many can be taken.</summary>
        public int Available;

        /// <summary>What it comes from: "Belt", the building, the zone, or the item itself.</summary>
        public string SourceLabel;

        /// <summary>
        /// True for count-based products (product adapters): Take only succeeds with n == Available, so the move must
        /// wait until the target takes the whole batch. False for every other source.
        /// </summary>
        public bool WholeBatch;

        /// <summary>
        /// Removes exactly n (1..Available) from the source and returns them as an unspawned, unowned thing (null if the
        /// source changed). Products only come out whole: n must equal Available, otherwise nothing happens (null).
        /// Call outside Sim.Tick, and only once per candidate list (the list is stale after a take).
        /// </summary>
        public Func<int, Thing> Take;

        /// <summary>
        /// Puts a taken part (or what a target refused of it) back into the source: merged into the belt item it came
        /// from, placed back on the cell, or added back to the container. Returns false when the source can't take it
        /// (products, a belt item that was taken whole); the caller then drops it nearby. Null = no way back.
        /// </summary>
        public Func<Thing, bool> PutBack;
    }

    /// <summary>
    /// What a pusher can pull from the cell behind it, in the spec order: belt end, items lying on the cell, building
    /// IThingHolder contents, product adapters. Never live pawns; skips reserved things, and forbidden ones unless asked.
    /// </summary>
    public static class PullSources
    {
        /// <summary>The cell behind a pusher at pos facing facing.</summary>
        public static IntVec3 CellBehind(IntVec3 pos, Dir facing) => pos - facing.ToRot4().FacingCell;

        /// <summary>The belt on cell when it points into the pusher (its Dir equals the pusher's facing).</summary>
        static bool BeltPointsIn(MapComponent_Belts comp, IntVec3 cell, Dir facing)
            => comp != null && comp.Sim.TryGetBelt(cell.ToCell(), out var d, out _) && d == facing;

        static bool Skip(Thing t, Map map, bool includeForbidden)
        {
            if (t == null || t.Destroyed || t is Pawn) return true;   // live pawns never; corpses are Corpse items
            if (t.def.category != ThingCategory.Item) return true;
            if (map.reservationManager.IsReserved(t)) return true;      // a pawn is on its way to haul / use it
            return !includeForbidden && t.IsForbidden(Faction.OfPlayer);
        }

        public static IEnumerable<PullCandidate> At(IntVec3 cell, Map map, Dir pusherFacing, bool includeForbidden)
        {
            var result = new List<PullCandidate>();
            if (map == null || !cell.InBounds(map)) return result;
            var comp = MapComponent_Belts.For(map);

            // 1. Belt pointing into the pusher: its front item, once at the tile's end.
            if (BeltPointsIn(comp, cell, pusherFacing) && comp.Sim.TryGetEndItem(cell.ToCell(), out var end))
            {
                var t = comp.Store.Get(end.Handle);
                if (!Skip(t, map, includeForbidden))
                {
                    long handle = end.Handle;
                    result.Add(new PullCandidate
                    {
                        Thing = t, Available = end.Count, SourceLabel = BeltLabel,
                        Take = n => n <= 0 ? null : comp.TakeFromBelt(cell, handle, n),
                        PutBack = part => comp.ReturnToBelt(cell, handle, part),
                    });
                }
            }

            var list = map.thingGrid.ThingsListAt(cell);

            // 2. Items lying on the cell (shelves, stockpiles, plain floor).
            for (int i = 0; i < list.Count; i++)
            {
                var t = list[i];
                if (!t.Spawned || !t.def.EverHaulable || Skip(t, map, includeForbidden)) continue;
                var label = SpotLabel(cell, map) ?? t.LabelCapNoCount;
                result.Add(new PullCandidate
                {
                    Thing = t, Available = t.stackCount, SourceLabel = label, Take = n => TakeSpawned(t, n),
                    PutBack = part => PutBackOnCell(part, cell, map),
                });
            }

            // 3. Building containers (IThingHolder building or comp): graves, caskets, transport pods, machines.
            for (int i = 0; i < list.Count; i++)
            {
                if (!(list[i] is Building b)) continue;   // blueprints are not buildings; frames have no Holders
                if (!includeForbidden && b.IsForbidden(Faction.OfPlayer)) continue;
                foreach (var h in Holders(b))
                {
                    var owner = h.GetDirectlyHeldThings();
                    if (owner == null) continue;
                    for (int k = 0; k < owner.Count; k++)
                    {
                        var t = owner[k];
                        if (Skip(t, map, includeForbidden)) continue;
                        result.Add(new PullCandidate
                        {
                            Thing = t, Available = t.stackCount, SourceLabel = b.LabelCap,
                            Take = n => TakeHeld(b, owner, t, n),
                            PutBack = part => !b.Destroyed && owner.TryAdd(part, canMergeWithExistingStacks: true),
                        });
                    }
                }
            }

            // 4. Product adapters: count-based products, whole batch only.
            for (int i = 0; i < list.Count; i++)
            {
                var b = list[i];
                var a = ProductAdapters.For(b);
                if (a == null) continue;
                if (map.reservationManager.IsReserved(b) || (!includeForbidden && b.IsForbidden(Faction.OfPlayer))) continue;
                var peek = a.Peek(b);
                if (peek == null || peek.stackCount <= 0) continue;
                int count = peek.stackCount;
                result.Add(new PullCandidate
                {
                    Thing = peek, Available = count, SourceLabel = b.LabelCap, WholeBatch = true,
                    Take = n => n == count ? a.TakeAll(b) : null,   // safety net: a partial take does nothing
                });
            }
            return result;
        }

        /// <summary>
        /// The standard containers of a building: itself when it is an IThingHolder (graves, caskets), and each comp
        /// that is one (CompTransporter on transport pods, modded machine comps). Construction frames are never a source:
        /// their resourceContainer holds delivered materials, and pulling them would stall construction.
        /// </summary>
        static IEnumerable<IThingHolder> Holders(Building b)
        {
            if (b is Frame) yield break;
            if (b is IThingHolder self) yield return self;
            var comps = b.AllComps;
            for (int i = 0; i < comps.Count; i++)
                if (comps[i] is IThingHolder h) yield return h;
        }

        static bool IsContainer(Building b)
        {
            foreach (var _ in Holders(b)) return true;
            return false;
        }

        static Thing TakeSpawned(Thing t, int n)
        {
            if (n <= 0 || t.Destroyed || !t.Spawned) return null;
            if (n >= t.stackCount) { t.DeSpawn(); return t; }
            return t.SplitOff(n);   // unspawned, unowned; keeps the forbidden state (CompForbiddable.PostSplitOff)
        }

        /// <summary>Places part back on cell (merging with the stack it came from); true if all of it is back.</summary>
        static bool PutBackOnCell(Thing part, IntVec3 cell, Map map)
        {
            if (part.Destroyed || part.Spawned || part.stackCount <= 0) return true;
            GenPlace.TryPlaceThing(part, cell, map, ThingPlaceMode.Direct);
            return part.Destroyed || part.Spawned || part.stackCount <= 0;
        }

        static Thing TakeHeld(Building b, ThingOwner owner, Thing t, int n)
        {
            if (n <= 0 || t.Destroyed || !owner.Contains(t)) return null;
            var taken = owner.Take(t, System.Math.Min(n, t.stackCount));
            if (b.Spawned) b.Map.mapDrawer.MapMeshDirty(b.Position, (ulong)MapMeshFlagDefOf.Buildings | (ulong)MapMeshFlagDefOf.Things);
            return taken;
        }

        internal static string BeltLabel => "CB_PusherBelt".Translate();

        /// <summary>The storage the cell belongs to (shelf, stockpile zone), or null.</summary>
        static string SpotLabel(IntVec3 cell, Map map)
        {
            var sg = cell.GetSlotGroup(map);
            if (sg == null) return null;
            return sg.parent is Thing th ? (string)th.LabelCap : sg.parent.SlotYielderLabel();
        }

        /// <summary>
        /// The source the pusher recognises on cell, whether or not anything can move now: "Belt" (pointing in), a
        /// container / product building, the storage (shelf, zone), else the first item lying there; null for none.
        /// </summary>
        public static string Describe(IntVec3 cell, Map map, Dir facing)
        {
            if (map == null || !cell.InBounds(map)) return null;
            if (BeltPointsIn(MapComponent_Belts.For(map), cell, facing)) return BeltLabel;
            var list = map.thingGrid.ThingsListAt(cell);
            for (int i = 0; i < list.Count; i++)
                if (list[i] is Building b && (IsContainer(b) || ProductAdapters.For(b) != null)) return b.LabelCap;
            var spot = SpotLabel(cell, map);
            if (spot != null) return spot;
            for (int i = 0; i < list.Count; i++)
                if (list[i].def.category == ThingCategory.Item && list[i].def.EverHaulable) return list[i].LabelCapNoCount;
            return null;
        }
    }
}
