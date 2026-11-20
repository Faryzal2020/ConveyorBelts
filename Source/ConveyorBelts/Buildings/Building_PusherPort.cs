using System.Collections.Generic;
using ConveyorBelts.Sim;
using RimWorld;
using UnityEngine;
using Verse;

namespace ConveyorBelts
{
    /// <summary>The pusher's two settings, copied and pasted together.</summary>
    public struct PusherSettings
    {
        public PusherRejectMode RejectMode;
        public EjectSides EjectSides;
    }

    /// <summary>
    /// Belt sink that feeds the machine it faces. Takes the stack a belt delivers into a one-item buffer; in its own
    /// Tick it asks the machine's adapter how many it takes now and pushes that many. What the machine won't take is
    /// held or thrown to a side cell (onto a belt pointing away, else onto the ground), as its reject mode says.
    /// </summary>
    public class Building_PusherPort : Building, IBeltSink, IPortTabOwner
    {
        const int StalledInterval = 15;   // ticks between retries while nothing could be pushed or ejected

        PusherRejectMode rejectMode = PusherRejectMode.EjectIfNeverAccepted;
        EjectSides ejectSides = EjectSides.LeftThenRight;
        Thing buffer;
        bool stalled;   // the last attempt moved nothing: retry only every StalledInterval ticks
        CompPowerTrader power;
        List<PortTabSection> sections;

        public bool Powered => power == null || power.PowerOn;
        public PusherRejectMode RejectMode => rejectMode;
        public EjectSides EjectSides => ejectSides;
        public Thing Buffer => buffer;
        public IntVec3 FrontCell => Position + Rotation.FacingCell;

        public void SetRejectMode(PusherRejectMode m) { rejectMode = m; stalled = false; }
        public void SetEjectSides(EjectSides s) { ejectSides = s; stalled = false; }

        public PusherSettings Settings
        {
            get => new PusherSettings { RejectMode = rejectMode, EjectSides = ejectSides };
            set { SetRejectMode(value.RejectMode); SetEjectSides(value.EjectSides); }
        }

        /// <summary>The machine in front that an adapter can feed, or null.</summary>
        public Thing Target => Spawned ? PushAdapters.FindTarget(FrontCell, Map) : null;

        public IEnumerable<PortTabSection> TabSections
        {
            get { return sections ??= new List<PortTabSection> { new Section_PusherModes(this) }; }
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
            // Never lose the held stack: it lies where the pusher stood.
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
            stalled = false;
            return true;
        }

        protected override void Tick()
        {
            base.Tick();
            if (buffer == null || !Powered) return;
            if (stalled && !this.IsHashIntervalTick(StalledInterval)) return;
            stalled = !TryHandleBuffer();
        }

        /// <summary>One push/eject attempt for the buffer. Returns true if anything moved.</summary>
        public bool TryHandleBuffer()
        {
            if (buffer == null || !Spawned) return false;
            if (buffer.Destroyed || buffer.stackCount <= 0) { buffer = null; return true; }
            var target = Target;
            var adapter = PushAdapters.For(target);
            int acceptNow = adapter == null ? 0 : Mathf.Min(buffer.stackCount, adapter.CanAccept(target, buffer));
            bool ever = adapter != null && adapter.EverAccepts(target, buffer.def);
            switch (PusherLogic.Decide(rejectMode, acceptNow, ever))
            {
                case PusherAction.Push: return Push(adapter, target, acceptNow);
                case PusherAction.Eject: return Eject();
                default: return false;
            }
        }

        bool Push(IPushAdapter adapter, Thing target, int n)
        {
            int before = buffer.stackCount;
            var piece = n >= buffer.stackCount ? buffer : buffer.SplitOff(n);
            if (piece == buffer) buffer = null;
            adapter.Accept(target, piece);
            // Whatever the machine did not take comes back to the buffer: nothing lost, nothing duplicated.
            if (!piece.Destroyed && !piece.Spawned && piece.holdingOwner == null && piece.stackCount > 0)
            {
                if (buffer == null) buffer = piece;
                else if (!buffer.TryAbsorbStack(piece, respectStackLimit: false))
                    PortGizmos.DropBuffer(piece, Position, Map);   // can't happen (same def), but never lose it
            }
            int after = buffer?.stackCount ?? 0;
            return after < before;
        }

        /// <summary>Throws the buffer to the side cells in eject order. Returns true if anything moved.</summary>
        bool Eject()
        {
            var (first, second) = PusherLogic.EjectOrder(Rotation.ToDir(), ejectSides);
            return EjectTo(first) || EjectTo(second);
        }

        bool EjectTo(Dir side)
        {
            var map = Map;
            var (dx, dz) = side.Offset();
            var cell = new IntVec3(Position.x + dx, 0, Position.z + dz);
            if (!cell.InBounds(map)) return false;
            var comp = MapComponent_Belts.For(map);
            if (comp != null && comp.Sim.TryGetBelt(cell.ToCell(), out var beltDir, out _))
            {
                // A belt cell only takes the item onto the belt, and only if the belt points away from the pusher
                // (an overflow line). Never drop items on the floor under a belt: a full or wrong-way belt is blocked.
                if (beltDir != side) return false;
                var fromSide = side.Opposite();   // from the belt toward the pusher
                int n = Mathf.Min(buffer.stackCount, comp.Sim.MaxStackAt(cell.ToCell()));
                if (n <= 0 || !comp.Sim.CanInsert(cell.ToCell(), fromSide)) return false;
                var piece = n >= buffer.stackCount ? buffer : buffer.SplitOff(n);
                if (comp.TryPutOnBelt(cell, fromSide.ToRot4(), piece))
                {
                    if (piece == buffer) buffer = null;
                    return true;
                }
                if (piece != buffer && !buffer.TryAbsorbStack(piece, respectStackLimit: false))
                    PortGizmos.DropBuffer(piece, Position, map);
                return false;
            }
            // Ground: only an open cell (no wall, door, machine or other edifice underneath).
            if (!cell.Walkable(map) || cell.GetEdifice(map) != null) return false;
            int before = buffer.stackCount;
            GenPlace.TryPlaceThing(buffer, cell, map, ThingPlaceMode.Direct);
            if (buffer.Destroyed || buffer.Spawned || buffer.stackCount <= 0) { buffer = null; return true; }
            return buffer.stackCount < before;
        }

        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (var g in base.GetGizmos()) yield return g;
            yield return PortGizmos.OpenTab("CB_ConfigurePusher", "CB_ConfigurePusherDesc");
            foreach (var g in PortGizmos.CopyPaste(() => Settings, s => Settings = s, "CB_CopyPusherDesc", "CB_PastePusherDesc"))
                yield return g;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref rejectMode, "rejectMode", PusherRejectMode.EjectIfNeverAccepted);
            Scribe_Values.Look(ref ejectSides, "ejectSides", EjectSides.LeftThenRight);
            Scribe_Deep.Look(ref buffer, "buffer");
        }

        public override string GetInspectString()
        {
            var s = base.GetInspectString();
            void Add(string line) { if (s.Length > 0) s += "\n"; s += line; }
            if (power != null && !power.PowerOn) Add("CB_PusherNoPower".Translate());
            if (Spawned)
            {
                var target = Target;
                Add(target == null ? "CB_PusherFacingNothing".Translate().ToString() : "CB_PusherFacing".Translate(target.LabelCap).ToString());
            }
            if (buffer != null) Add("CB_OutputHolding".Translate(buffer.LabelCap));
            return s;
        }
    }
}
