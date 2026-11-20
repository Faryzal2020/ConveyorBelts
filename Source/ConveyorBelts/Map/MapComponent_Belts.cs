using System.Collections.Generic;
using ConveyorBelts.Sim;
using RimWorld;
using UnityEngine;
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
            CB_DebugActions.ApplyHeldTemperature(map);   // test-only; no-op unless a debug action set it
            if (Store.Orphans.Count > 0) PurgeOrphans();
            if (Sim.BeltCount > 0) Sim.Tick();
            if (Find.TickManager.TicksGame % RotInterval == 0 && Store.Count > 0) TickRot();
        }

        readonly List<(Cell Cell, long Handle)> rotScratch = new List<(Cell, long)>();

        /// <summary>
        /// Vanilla CompRottable.TickInterval, but at the temperature of the belt cell the item sits on
        /// (an unspawned thing would otherwise use the world tile's temperature). The vanilla "rotted away in
        /// storage" message and rot-stink gas only apply to spawned things, so they are skipped here as vanilla
        /// would. Runs outside Sim.Tick; things destroyed here are removed from the sim in the same pass.
        /// </summary>
        void TickRot()
        {
            rotScratch.Clear();
            foreach (var (cell, item) in Sim.AllItems()) rotScratch.Add((cell, item.Handle));
            foreach (var (cell, handle) in rotScratch)
            {
                var t = Store.Get(handle);
                var rot = t?.TryGetComp<CompRottable>();
                if (rot == null || !rot.Active) continue;
                float temp = GenTemperature.GetTemperatureForCell(cell.ToIntVec3(), map);
                float before = rot.RotProgress;
                rot.RotProgress += GenTemperature.RotRateAtTemperature(temp) * RotInterval;
                if (rot.Stage == RotStage.Rotting && rot.PropsRot.rotDestroys)
                {
                    t.Destroy();
                }
                else if (Mathf.FloorToInt(before / 60000f) != Mathf.FloorToInt(rot.RotProgress / 60000f))
                {
                    if (rot.Stage == RotStage.Rotting && rot.PropsRot.rotDamagePerDay > 0f)
                        t.TakeDamage(new DamageInfo(DamageDefOf.Rotting, GenMath.RoundRandom(rot.PropsRot.rotDamagePerDay)));
                    else if (rot.Stage == RotStage.Dessicated && rot.PropsRot.dessicatedDamagePerDay > 0f)
                        t.TakeDamage(new DamageInfo(DamageDefOf.Rotting, GenMath.RoundRandom(rot.PropsRot.dessicatedDamagePerDay)));
                }
            }
            if (Store.Orphans.Count > 0) PurgeOrphans();
        }

        /// <summary>Removes the sim items of stored things that were destroyed (never call from inside Sim.Tick).</summary>
        void PurgeOrphans()
        {
            var gone = new List<(Cell, long)>();
            foreach (var (cell, item) in Sim.AllItems())
                if (Store.Orphans.Contains(item.Handle)) gone.Add((cell, item.Handle));
            foreach (var (cell, handle) in gone) Sim.TryRemoveItem(cell, handle, out _);
            Store.Orphans.Clear();
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
