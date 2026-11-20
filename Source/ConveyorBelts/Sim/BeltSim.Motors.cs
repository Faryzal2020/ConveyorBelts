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
        readonly List<Motor> motors = new List<Motor>();   // registration order

        int FindMotor(Cell c)
        {
            for (int i = 0; i < motors.Count; i++) if (motors[i].Pos == c) return i;
            return -1;
        }

        public void AddMotor(Cell motor, Dir facing)
        {
            if (FindMotor(motor) >= 0) { SetMotorFacing(motor, facing); return; }
            motors.Add(new Motor { Pos = motor, Facing = facing });
        }

        public void SetMotorFacing(Cell motor, Dir facing) { int i = FindMotor(motor); if (i >= 0) motors[i].Facing = facing; }
        public void SetMotorPowered(Cell motor, bool powered) { int i = FindMotor(motor); if (i >= 0) motors[i].Powered = powered; }
        public void RemoveMotor(Cell motor) { int i = FindMotor(motor); if (i >= 0) motors.RemoveAt(i); }

        int LineFacedBy(Motor m)
        {
            return Belts.TryGetValue(m.Pos.Step(m.Facing), out var b) ? (EnsureLinesThen(b)) : -1;
        }
        int EnsureLinesThen(BeltCell b) { EnsureLines(); return b.LineId; }

        /// <summary>Index into motors of the line's driver, or -1.</summary>
        int DriverOf(int lineId)
        {
            if (lineId < 0) return -1;
            int firstAny = -1;
            for (int i = 0; i < motors.Count; i++)
            {
                if (LineFacedBy(motors[i]) != lineId) continue;
                if (motors[i].Powered) return i;
                if (firstAny < 0) firstAny = i;
            }
            return firstAny;
        }

        public MotorStatus GetMotorStatus(Cell motor)
        {
            int i = FindMotor(motor);
            if (i < 0) return new MotorStatus(MotorState.Idle, 0, MotorStandbyWatts);
            int line = LineFacedBy(motors[i]);
            if (line < 0) return new MotorStatus(MotorState.Idle, 0, MotorStandbyWatts);
            var cells = LineCells(line);
            if (DriverOf(line) != i) return new MotorStatus(MotorState.Standby, cells.Count, MotorStandbyWatts);
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
