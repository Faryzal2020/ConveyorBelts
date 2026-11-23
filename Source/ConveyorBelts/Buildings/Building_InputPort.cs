using System.Collections.Generic;
using ConveyorBelts.Sim;
using RimWorld;
using UnityEngine;
using Verse;

namespace ConveyorBelts
{
    /// <summary>
    /// One-cell storage that pawns haul into (vanilla AI); while powered it feeds the stack it holds onto adjacent
    /// belts, taking turns over N, E, S, W. Its settings are shown in our Port tab instead of the vanilla Storage tab.
    /// </summary>
    public class Building_InputPort : Building_Storage, IPortTabOwner
    {
        CompPowerTrader power;
        int nextDir;   // round-robin cursor (Dir int) — where the next feed attempt starts
        List<PortTabSection> sections;

        public bool Powered => power == null || power.PowerOn;

        /// <summary>The Port tab's haul priority row.</summary>
        public Section_HaulPriority PrioritySection
        {
            get { EnsureSections(); return (Section_HaulPriority)sections[0]; }
        }

        /// <summary>The Port tab's item filter; its operations are what the tab's widgets call.</summary>
        public Section_ItemFilter FilterSection
        {
            get { EnsureSections(); return (Section_ItemFilter)sections[1]; }
        }

        // GetStoreSettings() rather than the settings field: with linked storage groups the group's settings are the live ones.
        void EnsureSections()
        {
            sections ??= new List<PortTabSection>
            {
                new Section_HaulPriority(this),
                new Section_ItemFilter(() => GetStoreSettings().filter, () => def.building.fixedStorageSettings?.filter),
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
        }

        protected override void Tick()
        {
            base.Tick();
            if (Powered) TryFeed();
        }

        /// <summary>The stack held on the port cell, or null.</summary>
        public Thing HeldThing
        {
            get
            {
                if (!Spawned) return null;
                var list = Map.thingGrid.ThingsListAtFast(Position);
                for (int i = 0; i < list.Count; i++)
                    if (list[i].def.category == ThingCategory.Item) return list[i];
                return null;
            }
        }

        /// <summary>Puts (part of) the held stack on the next adjacent belt that has room. Returns true if something moved.</summary>
        public bool TryFeed()
        {
            var t = HeldThing;
            if (t == null) return false;
            var comp = MapComponent_Belts.For(Map);
            if (comp == null) return false;
            var sim = comp.Sim;
            for (int i = 0; i < 4; i++)
            {
                var d = (Dir)((nextDir + i) & 3);
                var (dx, dz) = d.Offset();
                var belt = new IntVec3(Position.x + dx, 0, Position.z + dz);
                var bc = belt.ToCell();
                var fromSide = d.Opposite();   // from the belt toward the port: the side the item enters through
                if (!sim.CanInsert(bc, fromSide)) continue;
                int n = Mathf.Min(t.stackCount, sim.MaxStackAt(bc));
                if (n <= 0) continue;
                var piece = n >= t.stackCount ? t : t.SplitOff(n);
                if (comp.TryPutOnBelt(belt, fromSide.ToRot4(), piece))
                {
                    nextDir = ((int)d + 1) & 3;
                    return true;
                }
                // Refused: merge the split piece back (a whole stack was left untouched by TryPutOnBelt).
                if (piece != t && !t.TryAbsorbStack(piece, respectStackLimit: false))
                    GenPlace.TryPlaceThing(piece, Position, Map, ThingPlaceMode.Near);
                return false;
            }
            return false;
        }

        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (var g in base.GetGizmos()) yield return g;
            yield return PortGizmos.OpenTab("CB_ConfigureItems", "CB_ConfigureItemsDesc");
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref nextDir, "nextDir");
        }

        public override string GetInspectString()
        {
            var s = base.GetInspectString();
            if (power != null && !power.PowerOn)
            {
                if (s.Length > 0) s += "\n";
                s += "CB_PortNotFeeding".Translate();
            }
            return s;
        }
    }
}
