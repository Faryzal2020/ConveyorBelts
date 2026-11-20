using System.Collections.Generic;
using Verse;

namespace ConveyorBelts
{
    /// <summary>
    /// Off-map owner of every Thing riding a belt. Things here are never spawned, so they are not
    /// haulable and not in the resource counter. The handle of a thing is its thingIDNumber.
    /// The store is deliberately not reported by Map.GetChildHolders (belt items are not colony wealth in v1).
    /// A stored thing that leaves the owner other than through Take (it was destroyed: rotted away, damage...)
    /// is recorded in <see cref="Orphans"/> so the map component can drop its sim item.
    /// </summary>
    public class BeltItemStore : IThingHolder, IThingHolderEvents<Thing>, IExposable
    {
        public ThingOwner<Thing> things;
        readonly Map map;
        readonly Dictionary<long, Thing> index = new Dictionary<long, Thing>();
        bool removing;   // set while the store itself removes a thing (Take / Split)

        /// <summary>Handles of things that left the store without Take (destroyed). Consumed by MapComponent_Belts.</summary>
        public readonly HashSet<long> Orphans = new HashSet<long>();

        public void Notify_ItemAdded(Thing item) { }

        public void Notify_ItemRemoved(Thing item)
        {
            index.Remove(item.thingIDNumber);
            if (!removing) Orphans.Add(item.thingIDNumber);
        }

        public BeltItemStore(Map map)
        {
            this.map = map;
            things = new ThingOwner<Thing>(this, oneStackOnly: false, LookMode.Deep);
            things.dontTickContents = true;
        }

        public IThingHolder ParentHolder => map;
        public ThingOwner GetDirectlyHeldThings() => things;
        public void GetChildHolders(List<IThingHolder> outChildren)
            => ThingOwnerUtility.AppendThingHoldersFromThings(outChildren, things);

        public int Count => things.Count;

        /// <summary>Takes ownership of t (despawning it / removing it from its current owner). Returns its handle, or -1 on failure.</summary>
        public long Add(Thing t)
        {
            if (t.Spawned) t.DeSpawn();
            t.holdingOwner?.Remove(t);
            if (!things.TryAdd(t, canMergeWithExistingStacks: false)) return -1;
            index[t.thingIDNumber] = t;
            return t.thingIDNumber;
        }

        /// <summary>The stored thing, or null if it is gone (taken, or destroyed e.g. rotted away).</summary>
        public Thing Get(long handle)
        {
            if (!index.TryGetValue(handle, out var t)) return null;
            if (t.holdingOwner != things || t.Destroyed) { index.Remove(handle); return null; }
            return t;
        }

        /// <summary>Removes the thing from the store and returns it unspawned (null if gone).</summary>
        public Thing Take(long handle)
        {
            var t = Get(handle);
            if (t == null) return null;
            removing = true;
            try { things.Remove(t); } finally { removing = false; }
            index.Remove(handle);
            return t;
        }

        /// <summary>
        /// Splits <paramref name="count"/> off the stored stack into a new stored thing and returns its handle.
        /// Called from inside BeltSim.Tick (via IBeltHost): must not touch the sim.
        /// </summary>
        public long Split(long handle, int count)
        {
            var t = Get(handle);
            if (t == null) { Log.Error($"[CB] Split: no stored thing for handle {handle}"); return -1; }
            if (count >= t.stackCount) Log.Error($"[CB] Split {count} of {t} (stack {t.stackCount}): nothing would remain");
            Thing piece;
            removing = true;
            try { piece = t.SplitOff(count); } finally { removing = false; }
            if (piece == t) { things.TryAdd(t, canMergeWithExistingStacks: false); index[t.thingIDNumber] = t; return handle; }
            return Add(piece);
        }

        public void RebuildIndex()
        {
            index.Clear();
            for (int i = 0; i < things.Count; i++) index[things[i].thingIDNumber] = things[i];
        }

        public void ExposeData()
        {
            Scribe_Deep.Look(ref things, "things", this);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (things == null) things = new ThingOwner<Thing>(this, oneStackOnly: false, LookMode.Deep);
                things.dontTickContents = true;
                RebuildIndex();
            }
        }
    }
}
