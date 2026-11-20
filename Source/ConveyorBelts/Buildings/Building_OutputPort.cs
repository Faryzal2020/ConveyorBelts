using System.Collections.Generic;
using ConveyorBelts.Sim;
using RimWorld;
using UnityEngine;
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
        OutputFullMode fullMode = OutputFullMode.Hold;
        Thing buffer;
        CompPowerTrader power;
        List<PortTabSection> sections;

        static OutputFullMode? clipboard;
        static Texture2D tabIcon;

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
            if (buffer != null)
            {
                var t = buffer;
                buffer = null;
                if (!t.Destroyed && t.stackCount > 0 && !GenPlace.TryPlaceThing(t, cell, map, ThingPlaceMode.Near))
                    t.Destroy();   // only if the map has no room at all
            }
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
            if (buffer != null) TryPlaceBuffer();
        }

        /// <summary>Places the buffer on the front cell (merging stacks); in Spill mode the rest goes to nearby cells.</summary>
        public void TryPlaceBuffer()
        {
            if (buffer == null || !Spawned) return;
            if (buffer.Destroyed) { buffer = null; return; }
            var front = FrontCell;
            if (front.InBounds(Map)) GenPlace.TryPlaceThing(buffer, front, Map, ThingPlaceMode.Direct);
            if (Placed()) return;
            if (fullMode == OutputFullMode.Spill)
            {
                GenPlace.TryPlaceThing(buffer, front, Map, ThingPlaceMode.Near);
                Placed();
            }
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
            tabIcon ??= ContentFinder<Texture2D>.Get("ConveyorBelts/UI/TabPort");
            yield return new Command_Action
            {
                defaultLabel = "CB_ConfigureOutput".Translate(),
                defaultDesc = "CB_ConfigureOutputDesc".Translate(),
                icon = tabIcon,
                action = Building_InputPort.OpenPortTab,
            };
            yield return new Command_Action
            {
                defaultLabel = "CB_CopyMode".Translate(),
                defaultDesc = "CB_CopyModeDesc".Translate(),
                icon = TexButton.Copy,
                action = () => clipboard = fullMode,
            };
            if (clipboard.HasValue)
                yield return new Command_Action
                {
                    defaultLabel = "CB_PasteMode".Translate(),
                    defaultDesc = "CB_PasteModeDesc".Translate(),
                    icon = TexButton.Paste,
                    action = () => SetFullMode(clipboard.Value),
                };
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
            if (power != null && !power.PowerOn) Add("CB_OutputNoPower".Translate());
            if (buffer != null) Add("CB_OutputHolding".Translate(buffer.LabelCap));
            return s;
        }
    }
}
