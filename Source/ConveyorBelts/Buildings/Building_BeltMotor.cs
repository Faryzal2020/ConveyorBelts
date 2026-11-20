using System.Collections.Generic;
using System.Linq;
using System.Text;
using ConveyorBelts.Sim;
using RimWorld;
using UnityEngine;
using Verse;

namespace ConveyorBelts
{
    /// <summary>Drives the belt line it faces. Registration with the sim is ordered by thingIDNumber (see MapComponent_Belts).</summary>
    public class Building_BeltMotor : Building
    {
        CompPowerTrader power;
        MotorStatus status = new MotorStatus(MotorState.Idle, 0, BeltSim.MotorStandbyWatts);
        internal bool SimPowered;   // last power state pushed to the sim

        static Texture2D rotateIcon;

        public Dir Facing => Rotation.ToDir();

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            power = GetComp<CompPowerTrader>();
            var comp = MapComponent_Belts.For(map);
            var sim = comp.Sim;
            var cell = Position.ToCell();
            if (!sim.AdjacentBeltDirs(cell).Contains(Facing))
            {
                var first = sim.AdjacentBeltDirs(cell).Cast<Dir?>().FirstOrDefault();
                if (first.HasValue) Rotation = first.Value.ToRot4();
            }
            comp.RegisterMotor(this);
            Refresh(true);
        }

        public override void DeSpawn(DestroyMode mode = DestroyMode.Vanish)
        {
            MapComponent_Belts.For(Map)?.UnregisterMotor(this);
            base.DeSpawn(mode);
        }

        protected override void Tick()
        {
            base.Tick();
            if (power == null) return;
            if (power.PowerOn != SimPowered)
            {
                SimPowered = power.PowerOn;
                MapComponent_Belts.For(Map).Sim.SetMotorPowered(Position.ToCell(), SimPowered);
            }
            if (this.IsHashIntervalTick(30)) Refresh(false);
        }

        void Refresh(bool pushPower)
        {
            var sim = MapComponent_Belts.For(Map).Sim;
            var cell = Position.ToCell();
            if (pushPower && power != null)
            {
                SimPowered = power.PowerOn;
                sim.SetMotorPowered(cell, SimPowered);
            }
            status = sim.GetMotorStatus(cell);
            if (status.State == MotorState.Idle)
            {
                var first = sim.AdjacentBeltDirs(cell).Cast<Dir?>().FirstOrDefault();
                if (first.HasValue)
                {
                    SetFacing(first.Value);
                    status = sim.GetMotorStatus(cell);
                }
            }
            if (power != null) power.PowerOutput = -status.Watts;
        }

        void SetFacing(Dir d)
        {
            Rotation = d.ToRot4();
            MapComponent_Belts.For(Map).Sim.SetMotorFacing(Position.ToCell(), d);
            Map.mapDrawer.MapMeshDirty(Position, MapMeshFlagDefOf.Things);
        }

        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (var g in base.GetGizmos()) yield return g;
            rotateIcon ??= ContentFinder<Texture2D>.Get("ConveyorBelts/UI/RotateMotor");
            yield return new Command_Action
            {
                defaultLabel = "CB_RotateMotor".Translate(),
                defaultDesc = "CB_RotateMotorDesc".Translate(),
                icon = rotateIcon,
                action = RotateToNextBelt,
            };
        }

        void RotateToNextBelt()
        {
            var sim = MapComponent_Belts.For(Map).Sim;
            var dirs = sim.AdjacentBeltDirs(Position.ToCell()).ToList();
            if (dirs.Count == 0) return;
            Dir? next = null;
            for (int i = 1; i <= 4 && next == null; i++)
            {
                var d = (Dir)(((int)Facing + i) % 4);
                if (dirs.Contains(d)) next = d;
            }
            if (next.HasValue && next.Value != Facing) { SetFacing(next.Value); Refresh(false); }
        }

        public override string GetInspectString()
        {
            var sb = new StringBuilder(base.GetInspectString());
            var comp = MapComponent_Belts.For(Map);
            if (comp == null) return sb.ToString();
            var st = comp.Sim.GetMotorStatus(Position.ToCell());
            string line = st.State switch
            {
                MotorState.Driving => "CB_MotorDriving".Translate(st.LineTiles, st.Watts.ToString("0")).ToString(),
                MotorState.Standby => "CB_MotorStandby".Translate().ToString(),
                _ => "CB_MotorIdle".Translate().ToString(),
            };
            if (sb.Length > 0) sb.AppendLine();
            sb.Append(line);
            return sb.ToString();
        }
    }
}
