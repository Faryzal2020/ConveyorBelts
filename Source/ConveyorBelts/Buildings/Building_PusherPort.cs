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
        /// <summary>A copy, never the live filter of a pusher.</summary>
        public ThingFilter Filter;
        public bool PullForbidden;
    }

    /// <summary>
    /// Moves items from the cell behind it to the cell in front, holding nothing itself. Every MoveInterval ticks while
    /// powered it looks for an item behind that its filter allows and that the front can take right now, and moves as
    /// many as the front takes (at most one stack) in one step. Pull and push sides: see PullSources / PushTargets.
    /// </summary>
    public class Building_PusherPort : Building, IPortTabOwner
    {
        public const int MoveInterval = 60;

        /// <summary>Test-only (debug action): log every move. Never saved.</summary>
        internal static bool LogMoves;

        ThingFilter filter = new ThingFilter();   // starts empty: a new pusher does nothing until configured
        bool pullForbidden = true;
        Thing legacyBuffer;                       // v1 saves only: the held stack, dropped on the cell once loaded
        CompPowerTrader power;
        List<PortTabSection> sections;

        public bool Powered => power == null || power.PowerOn;
        public ThingFilter Filter => filter;
        public bool PullForbidden { get => pullForbidden; set => pullForbidden = value; }
        public Dir Facing => Rotation.ToDir();
        public IntVec3 BehindCell => PullSources.CellBehind(Position, Facing);
        public IntVec3 FrontCell => PushTargets.CellInFront(Position, Facing);

        /// <summary>What the pusher recognises behind it (whether or not anything can move now), or null.</summary>
        public string PullingFrom => Spawned ? PullSources.Describe(BehindCell, Map, Facing) : null;

        /// <summary>What the pusher recognises in front of it (whether or not it has room now), or null.</summary>
        public string PushingTo => Spawned ? PushTargets.Describe(FrontCell, Map, Facing) : null;

        public PusherSettings Settings
        {
            get
            {
                var copy = new ThingFilter();
                copy.CopyAllowancesFrom(filter);
                return new PusherSettings { Filter = copy, PullForbidden = pullForbidden };
            }
            set
            {
                if (value.Filter != null) filter.CopyAllowancesFrom(value.Filter);
                pullForbidden = value.PullForbidden;
            }
        }

        /// <summary>The Port tab's item filter; its operations are what the tab's widgets call.</summary>
        public Section_ItemFilter FilterSection
        {
            get { EnsureSections(); return (Section_ItemFilter)sections[1]; }
        }

        void EnsureSections()
        {
            sections ??= new List<PortTabSection>
            {
                new Section_PusherLinks(this),
                new Section_ItemFilter(() => filter, null),   // no fixed filter: every haulable def, corpses included
            };
        }

        public IEnumerable<PortTabSection> TabSections
        {
            get { EnsureSections(); return sections; }
        }

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            power = GetComp<CompPowerTrader>();
            if (respawningAfterLoad && legacyBuffer != null)
            {
                // A v1 pusher's held stack. Dropped once loading has finished: buildings spawn after items and in no
                // particular order, so the cells around may not show their walls and machines yet.
                var t = legacyBuffer;
                legacyBuffer = null;
                var cell = Position;
                LongEventHandler.ExecuteWhenFinished(() =>
                {
                    PortGizmos.DropBuffer(t, cell, map);
                    Log.Message($"[CB] v1 pusher at {cell}: dropped its held {t.LabelCap}");
                });
            }
        }

        protected override void Tick()
        {
            base.Tick();
            if (Powered && this.IsHashIntervalTick(MoveInterval)) TryMoveOnce();
        }

        /// <summary>
        /// One move attempt: the first pull candidate behind (in PullSources order) that the filter allows and the front
        /// can take moves, min(available, stack limit, room) of it; a whole-batch product only when the front takes all
        /// of it. Returns true if something moved. Never holds anything: what the target refuses goes back to the source.
        /// </summary>
        public bool TryMoveOnce()
        {
            if (!Spawned || filter.AllowedDefCount == 0) return false;
            var map = Map;
            var facing = Facing;
            var front = FrontCell;
            // The candidate list is good for one take only: use each candidate right away, stop after a take.
            foreach (var c in PullSources.At(BehindCell, map, facing, pullForbidden))
            {
                if (c.Thing == null || c.Available <= 0 || !filter.Allows(c.Thing)) continue;
                int room = PushTargets.Room(front, map, facing, c.Thing, out var target);
                if (room <= 0) continue;
                int limit = c.Thing.def.stackLimit;
                int amount;
                if (c.WholeBatch)
                {
                    if (room < c.Available || c.Available > limit) continue;   // no holding: all or nothing
                    amount = c.Available;
                }
                else amount = Mathf.Min(c.Available, Mathf.Min(limit, room));
                if (amount <= 0) continue;

                var part = c.Take(amount);
                if (part == null) return false;   // the source changed under us; try again next interval
                int taken = part.stackCount;
                PushTargets.Give(target, part);
                int left = Leftover(part);
                if (LogMoves)
                    Log.Message($"[CB] pusher {thingIDNumber} moved {c.Thing.def.defName} x{taken - left} of {c.Available} from {c.SourceLabel} " +
                                $"-> {PushTargets.Describe(front, map, facing)} (room {room}{(left > 0 ? $", {left} refused, put back" : "")})");
                if (left > 0) PutBack(c, part);
                return left < taken;
            }
            return false;
        }

        /// <summary>How many of part the target did not take (part not destroyed, spawned or held).</summary>
        static int Leftover(Thing part)
            => part.Destroyed || part.Spawned || part.holdingOwner != null || part.stackCount <= 0 ? 0 : part.stackCount;

        /// <summary>Returns what the target refused to its source; if the source can't take it back, drops it here.</summary>
        void PutBack(PullCandidate c, Thing part)
        {
            if (c.PutBack != null && c.PutBack(part) && Leftover(part) == 0) return;
            if (Leftover(part) > 0) PortGizmos.DropBuffer(part, Position, Map);
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
            Scribe_Deep.Look(ref filter, "filter");
            Scribe_Values.Look(ref pullForbidden, "pullForbidden", true);
            // v1 saves: the one-item buffer (read only; v1 rejectMode / ejectSides are ignored).
            if (Scribe.mode != LoadSaveMode.Saving) Scribe_Deep.Look(ref legacyBuffer, "buffer");
            if (Scribe.mode == LoadSaveMode.PostLoadInit && filter == null) filter = new ThingFilter();
        }
    }
}
