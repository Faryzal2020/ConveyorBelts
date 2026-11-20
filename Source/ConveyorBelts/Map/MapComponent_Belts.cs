using System.Collections.Generic;
using ConveyorBelts.Sim;
using RimWorld;
using Verse;

namespace ConveyorBelts
{
    /// <summary>
    /// Owns the belt simulation and the off-map item store for one map.
    /// IBeltHost/IBeltSink callbacks run inside Sim.Tick(): they must never mutate the sim
    /// (no AddBelt/SetBelt/RemoveBelt/TryInsert/TryRemoveItem from inside them).
    /// </summary>
    public class MapComponent_Belts : MapComponent, IBeltHost
    {
        public const int RotInterval = 250;

        public BeltSim Sim;
        public BeltItemStore Store;

        List<SavedBeltItem> savedItems;
        List<SavedBeltItem> deferredItems;   // loaded items whose belt was not registered yet at FinalizeInit

        public MapComponent_Belts(Map map) : base(map)
        {
            Sim = new BeltSim(this) { RequireMotors = false };   // motors arrive in Task 8
            Store = new BeltItemStore(map);
        }

        public static MapComponent_Belts For(Map map) => map?.GetComponent<MapComponent_Belts>();

        long IBeltHost.Split(long handle, int count) => Store.Split(handle, count);

        /// <summary>Puts t on the belt entering through fromSide. On refusal t is left untouched and unstored.</summary>
        public bool TryPutOnBelt(IntVec3 belt, Rot4 fromSide, Thing t)
        {
            if (t == null || t.Destroyed) return false;
            var cell = belt.ToCell();
            var from = fromSide.ToDir();
            if (!Sim.CanInsert(cell, from) || t.stackCount > Sim.MaxStackAt(cell)) return false;

            bool wasSpawned = t.Spawned;
            IntVec3 oldPos = t.Position;
            long handle = Store.Add(t);
            if (handle < 0)
            {
                Log.Error($"[CB] could not store {t}");
                if (wasSpawned && !t.Spawned && t.holdingOwner == null) GenSpawn.Spawn(t, oldPos, map);
                return false;
            }
            if (Sim.TryInsert(cell, from, handle, t.stackCount)) return true;

            // CanInsert agreed but TryInsert refused: undo.
            Store.Take(handle);
            if (wasSpawned) GenSpawn.Spawn(t, oldPos, map);
            return false;
        }

        /// <summary>Removes the item from the belt and returns its thing unspawned, or null if it is gone.</summary>
        public Thing TakeFromBelt(IntVec3 belt, long handle)
        {
            if (!Sim.TryRemoveItem(belt.ToCell(), handle, out _)) return null;
            return Store.Take(handle);
        }

        /// <summary>Drops the given belt items' things on/near cell as normal map items.</summary>
        public void DropItems(IntVec3 cell, List<BeltItem> items)
        {
            if (items == null) return;
            foreach (var it in items) DropHandle(cell, it.Handle);
        }

        void DropHandle(IntVec3 cell, long handle)
        {
            var t = Store.Take(handle);
            if (t == null) return;
            if (!GenPlace.TryPlaceThing(t, cell, map, ThingPlaceMode.Near))
            {
                Log.Warning($"[CB] no room near {cell} for {t}; spawning on the cell");
                GenSpawn.Spawn(t, cell, map);
            }
        }

        public override void MapComponentTick()
        {
            if (deferredItems != null) RestoreDeferred();
            if (Sim.BeltCount > 0) Sim.Tick();
            if (Find.TickManager.TicksGame % RotInterval == 0 && Store.Count > 0) TickRot();
        }

        void TickRot()
        {
            bool anyGone = false;
            var list = Store.things.InnerListForReading;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                if (i >= list.Count) continue;   // a destroyed thing removes itself from the owner
                var t = list[i];
                var rot = t.TryGetComp<CompRottable>();
                if (rot == null) continue;
                rot.CompTickInterval(RotInterval);
                if (t.Destroyed) anyGone = true;
            }
            if (anyGone) PurgeMissingItems();
        }

        /// <summary>Removes sim items whose thing no longer exists in the store (rotted away etc.).</summary>
        void PurgeMissingItems()
        {
            var gone = new List<(Cell, long)>();
            foreach (var (cell, item) in Sim.AllItems())
                if (Store.Get(item.Handle) == null) gone.Add((cell, item.Handle));
            foreach (var (cell, handle) in gone) Sim.TryRemoveItem(cell, handle, out _);
        }

        public override void MapComponentDraw()
        {
            BeltRenderer.Draw(this);
        }

        public override void ExposeData()
        {
            Scribe_Deep.Look(ref Store, "store", map);
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                savedItems = new List<SavedBeltItem>();
                // AllItems yields each cell's items front-first; RestoreItem relies on that order.
                foreach (var (cell, item) in Sim.AllItems())
                    savedItems.Add(new SavedBeltItem
                    {
                        cell = cell.ToIntVec3(), handle = item.Handle, count = item.Count,
                        progress = item.Progress, enteredFrom = item.EnteredFrom.ToRot4(),
                    });
            }
            Scribe_Collections.Look(ref savedItems, "items", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.Saving) savedItems = null;
            if (Scribe.mode == LoadSaveMode.PostLoadInit && Store == null) Store = new BeltItemStore(map);
        }

        public override void FinalizeInit()
        {
            if (savedItems == null) return;
            int restored = 0;
            foreach (var s in savedItems)
            {
                if (RestoreOne(s)) restored++;
                else (deferredItems ??= new List<SavedBeltItem>()).Add(s);
            }
            Log.Message($"[CB] FinalizeInit: restored {restored}/{savedItems.Count} belt items, deferred {deferredItems?.Count ?? 0}");
            savedItems = null;
            if (CB_DebugActions.DumpOnLoad) CB_DebugActions.Dump(map);
        }

        bool RestoreOne(SavedBeltItem s)
        {
            if (Store.Get(s.handle) == null)
            {
                Log.Warning($"[CB] saved belt item {s.handle} at {s.cell} has no stored thing; skipped");
                return true;
            }
            return Sim.RestoreItem(s.cell.ToCell(), new BeltItem
            {
                Handle = s.handle, Count = s.count, Progress = s.progress, EnteredFrom = s.enteredFrom.ToDir(),
            });
        }

        void RestoreDeferred()
        {
            var list = deferredItems;
            deferredItems = null;
            int restored = 0, dropped = 0;
            foreach (var s in list)
            {
                if (RestoreOne(s)) restored++;
                else { DropHandle(s.cell, s.handle); dropped++; }
            }
            Log.Message($"[CB] first tick: restored {restored} deferred belt items, dropped {dropped}");
        }
    }
}
