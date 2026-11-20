using System.Collections.Generic;

namespace ConveyorBelts.Sim
{
    public enum MotorState { Idle, Standby, Driving }
    public readonly record struct MotorStatus(MotorState State, int LineTiles, float Watts);

    public sealed partial class BeltSim
    {
        public const float MotorBaseWatts = 20f;
        public const float MotorStandbyWatts = 5f;

        sealed class Motor { public Cell Pos; public Dir Facing; public bool Powered; }
        readonly List<Motor> motors = new List<Motor>();                       // registration order
        readonly Dictionary<Cell, Motor> motorAt = new Dictionary<Cell, Motor>();

        // Line -> index into motors of its driver (-1: none). Rebuilt in one pass over the motors whenever the lines,
        // the motor list, a facing or a power state changed, so per-line lookups during Tick are O(1).
        int[] lineDriver = new int[0];
        bool driversDirty = true;

        public void AddMotor(Cell motor, Dir facing)
        {
            if (motorAt.ContainsKey(motor)) { SetMotorFacing(motor, facing); return; }
            var m = new Motor { Pos = motor, Facing = facing };
            motors.Add(m);
            motorAt[motor] = m;
            driversDirty = true;
        }

        public void SetMotorFacing(Cell motor, Dir facing)
        {
            if (motorAt.TryGetValue(motor, out var m) && m.Facing != facing) { m.Facing = facing; driversDirty = true; }
        }

        public void SetMotorPowered(Cell motor, bool powered)
        {
            if (motorAt.TryGetValue(motor, out var m) && m.Powered != powered) { m.Powered = powered; driversDirty = true; }
        }

        public void RemoveMotor(Cell motor)
        {
            if (!motorAt.TryGetValue(motor, out var m)) return;
            motorAt.Remove(motor);
            motors.Remove(m);
            driversDirty = true;
        }

        /// <summary>Removes every motor (callers re-add them in the order that defines "first registered").</summary>
        public void ClearMotors()
        {
            motors.Clear();
            motorAt.Clear();
            driversDirty = true;
        }

        int LineFacedBy(Motor m)
        {
            EnsureLines();
            return Belts.TryGetValue(m.Pos.Step(m.Facing), out var b) ? b.LineId : -1;
        }

        void EnsureDrivers()
        {
            EnsureLines();   // a rebuild sets driversDirty
            if (!driversDirty) return;
            driversDirty = false;
            if (lineDriver.Length < lines.Count) lineDriver = new int[lines.Count];
            System.Array.Fill(lineDriver, -1, 0, lines.Count);
            // First registered powered motor facing the line, else the first registered one.
            for (int i = 0; i < motors.Count; i++)
            {
                var m = motors[i];
                if (!Belts.TryGetValue(m.Pos.Step(m.Facing), out var b)) continue;
                int d = lineDriver[b.LineId];
                if (d < 0 || (!motors[d].Powered && m.Powered)) lineDriver[b.LineId] = i;
            }
        }

        /// <summary>Index into motors of the line's driver, or -1.</summary>
        int DriverOf(int lineId)
        {
            if (lineId < 0) return -1;
            EnsureDrivers();
            return lineId < lines.Count ? lineDriver[lineId] : -1;
        }

        public MotorStatus GetMotorStatus(Cell motor)
        {
            if (!motorAt.TryGetValue(motor, out var m)) return new MotorStatus(MotorState.Idle, 0, MotorStandbyWatts);
            int line = LineFacedBy(m);
            if (line < 0) return new MotorStatus(MotorState.Idle, 0, MotorStandbyWatts);
            var cells = LineCells(line);
            int d = DriverOf(line);
            if (d < 0 || motors[d] != m) return new MotorStatus(MotorState.Standby, cells.Count, MotorStandbyWatts);
            float w = MotorBaseWatts;
            foreach (var c in cells) w += Tiers.Get(Belts[c].Tier).WattsPerTile;
            return new MotorStatus(MotorState.Driving, cells.Count, w);
        }

        public IEnumerable<Dir> AdjacentBeltDirs(Cell c)
        {
            for (int d = 0; d < 4; d++)
                if (Belts.ContainsKey(c.Step((Dir)d))) yield return (Dir)d;
        }
    }
}
