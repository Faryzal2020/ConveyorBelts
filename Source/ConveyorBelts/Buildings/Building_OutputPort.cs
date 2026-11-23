using System.Collections.Generic;
using ConveyorBelts.Sim;
using RimWorld;
using Verse;

namespace ConveyorBelts
{
    public enum OutputFullMode { Hold, Spill }

    /// <summary>
    /// Belt sink: takes the stack a belt delivers into a one-item buffer and places it on the cell it faces.
    /// While the front cell can't take it, Hold keeps the buffer (the belt backs up); Spill puts it on a nearby cell.
    /// </summary>
    public class Building_OutputPort : Building, IBeltSink, IPortTabOwner
    {
        const int BlockedInterval = 15;   // ticks between placement retries while the front cell is full

        OutputFullMode fullMode = OutputFullMode.Hold;
        Thing buffer;
        bool blocked;   // the last placement attempt failed: retry only every BlockedInterval ticks
        CompPowerTrader power;
        List<PortTabSection> sections;

        public bool Powered => power == null || power.PowerOn;
        public OutputFullMode FullMode => fullMode;
        public Thing Buffer => buffer;
        public IntVec3 FrontCell => Position + Rotation.FacingCell;

        public void SetFullMode(OutputFullMode m) => fullMode = m;

        public IEnumerable<PortTabSection> TabSections
        {
            get { return sections ??= new List<PortTabSection> { new Section_OutputModes(this) }; }
        }

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            power = GetComp<CompPowerTrader>();
            MapComponent_Belts.For(map)?.Sim.AddSink(Position.ToCell(), this);
        }

        public override void DeSpawn(DestroyMode mode = DestroyMode.Vanish)
        {
            var map = Map;
            var cell = Position;
            MapComponent_Belts.For(map)?.Sim.RemoveSink(cell.ToCell());
            base.DeSpawn(mode);
            // Never lose the held stack: it lies where the port stood.
            var t = buffer;
            buffer = null;
            PortGizmos.DropBuffer(t, cell, map);
        }

        /// <summary>Called from inside Sim.Tick: only moves the thing from the store to the buffer; never touches the sim.</summary>
        public bool TryAccept(long handle, int count, Dir fromSide)
        {
            if (!Spawned || !Powered || buffer != null || fromSide == Rotation.ToDir()) return false;
            var comp = MapComponent_Belts.For(Map);
            if (comp == null) return false;
            var t = comp.Store.Take(handle);
            if (t == null) return false;
            buffer = t;
            return true;
        }

        protected override void Tick()
        {
            base.Tick();
            if (buffer == null) return;
            if (blocked && !this.IsHashIntervalTick(BlockedInterval)) return;
            TryPlaceBuffer();
            blocked = buffer != null;
        }

        /// <summary>Places the buffer on the front cell (merging stacks); in Spill mode the rest goes to nearby cells.</summary>
        public void TryPlaceBuffer()
        {
            if (buffer == null || !Spawned) return;
            if (buffer.Destroyed) { buffer = null; return; }
            var front = FrontCell;
            if (front.InBounds(Map) && front.Walkable(Map) && CellHasRoomFor(front, buffer))
                GenPlace.TryPlaceThing(buffer, front, Map, ThingPlaceMode.Direct);
            if (Placed()) return;
            if (fullMode == OutputFullMode.Spill)
            {
                GenPlace.TryPlaceThing(buffer, front, Map, ThingPlaceMode.Near);
                Placed();
            }
        }

        /// <summary>
        /// Whether c can take (part of) t without exceeding its item limit. Vanilla Direct placement spawns a
        /// non-stackable item on a full cell anyway (a pile), which would defeat Hold.
        /// </summary>
        bool CellHasRoomFor(IntVec3 c, Thing t)
        {
            int items = 0;
            foreach (var x in c.GetThingList(Map))
            {
                if (x.def.category != ThingCategory.Item) continue;
                items++;
                if (t.def.stackLimit > 1 && x.CanStackWith(t) && x.stackCount < x.def.stackLimit) return true;
            }
            return items < c.GetMaxItemsAllowedInCell(Map);
        }

        bool Placed()
        {
            if (buffer == null || buffer.Destroyed || buffer.stackCount <= 0 || buffer.Spawned)
            {
                buffer = null;
                return true;
            }
            return false;
        }

        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (var g in base.GetGizmos()) yield return g;
            yield return PortGizmos.OpenTab();
            foreach (var g in PortGizmos.CopyPaste(() => fullMode, SetFullMode))
                yield return g;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref fullMode, "fullMode", OutputFullMode.Hold);
            Scribe_Deep.Look(ref buffer, "buffer");
        }

        public override string GetInspectString()
        {
            var s = base.GetInspectString();
            void Add(string line) { if (s.Length > 0) s += "\n"; s += line; }
            if (buffer != null) Add("CB_OutputHolding".Translate(buffer.LabelCap));
            return s;
        }
    }
}
