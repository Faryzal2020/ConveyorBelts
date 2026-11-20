using System.Linq;
using ConveyorBelts.Sim;
using Xunit;

namespace ConveyorBelts.Tests
{
    public class MotorTests
    {
        static BeltSim New() => new BeltSim(new FakeHost());
        static BeltSim Row() {
            var s = New();
            for (int x = 0; x <= 4; x++) s.AddBelt(new(x,0), Dir.East, Tiers.Basic);
            s.AddBelt(new(5,0), Dir.East, Tiers.Fast);
            return s;
        }

        [Fact] public void Motor_DrivesFacedLine_WattsScaleWithTiers() {
            var s = Row();
            s.AddMotor(new(2,1), Dir.South); s.SetMotorPowered(new(2,1), true);
            Assert.Equal(new MotorStatus(MotorState.Driving, 6, 55f), s.GetMotorStatus(new(2,1)));
            Assert.True(s.IsLineRunning(s.LineIdAt(new(0,0))));
        }
        [Fact] public void Motor_Unpowered_LineStopped_StillReportsFullDraw() {
            var s = Row();
            s.AddMotor(new(2,1), Dir.South); s.SetMotorPowered(new(2,1), true);
            s.SetMotorPowered(new(2,1), false);
            Assert.Equal(new MotorStatus(MotorState.Driving, 6, 55f), s.GetMotorStatus(new(2,1)));
            Assert.False(s.IsLineRunning(s.LineIdAt(new(0,0))));
        }
        [Fact] public void Motor_SecondMotorStandby_TakesOverWhenFirstUnpowered() {
            var s = Row();
            var a = new Cell(2,1); var b = new Cell(3,-1);
            s.AddMotor(a, Dir.South); s.AddMotor(b, Dir.North);
            s.SetMotorPowered(a, true); s.SetMotorPowered(b, true);
            Assert.Equal(new MotorStatus(MotorState.Driving, 6, 55f), s.GetMotorStatus(a));
            Assert.Equal(new MotorStatus(MotorState.Standby, 6, 5f), s.GetMotorStatus(b));
            s.SetMotorPowered(a, false);
            Assert.Equal(new MotorStatus(MotorState.Driving, 6, 55f), s.GetMotorStatus(b));
            Assert.Equal(MotorState.Standby, s.GetMotorStatus(a).State);
            Assert.True(s.IsLineRunning(s.LineIdAt(new(0,0))));
        }
        [Fact] public void Motor_FacingNoBelt_IsIdle() {
            var s = New();
            s.AddMotor(new(2,1), Dir.South);
            Assert.Equal(new MotorStatus(MotorState.Idle, 0, 5f), s.GetMotorStatus(new(2,1)));
        }
        [Fact] public void Motor_BeltRemovedUnderFacing_BecomesIdle() {
            var s = Row();
            s.AddMotor(new(2,1), Dir.South); s.SetMotorPowered(new(2,1), true);
            s.RemoveBelt(new(2,0), new());
            Assert.Equal(new MotorStatus(MotorState.Idle, 0, 5f), s.GetMotorStatus(new(2,1)));
        }
        [Fact] public void LShape_NeedsTwoMotors() {
            var s = New();
            foreach (var (c, d) in BeltPath.Compute(new(0,0), new(5,5), false, Dir.North)) s.AddBelt(c, d, 0);
            s.AddMotor(new(2,1), Dir.South); s.SetMotorPowered(new(2,1), true);
            Assert.True(s.IsLineRunning(s.LineIdAt(new(0,0))));
            Assert.False(s.IsLineRunning(s.LineIdAt(new(5,3))));
        }
        [Fact] public void RequireMotorsOff_EverythingRuns() {
            var s = Row();
            Assert.False(s.IsLineRunning(s.LineIdAt(new(0,0))));
            s.RequireMotors = false;
            Assert.True(s.IsLineRunning(s.LineIdAt(new(0,0))));
        }
        // 300 seven-tile lines (rows z = 2k), two motors per line: A behind the head (x=-1, facing East),
        // B past the end (x=7, facing West). Registration is interleaved across lines; per line k:
        // k%3==0: only B powered -> B drives; k%3==1: both powered -> the first registered drives;
        // k%3==2: neither powered -> the first registered "drives" (full draw) but the line is stopped.
        [Fact] public void ManyShortLines_ManyMotors_ElectDriversCorrectly() {
            const int N = 300;
            var s = New();
            for (int k = 0; k < N; k++)
                for (int x = 0; x < 7; x++) s.AddBelt(new(x, 2 * k), Dir.East, Tiers.Basic);
            Cell A(int k) => new(-1, 2 * k); Cell B(int k) => new(7, 2 * k);
            bool bFirst(int k) => k % 2 == 0;   // which motor of line k registers first
            for (int k = 0; k < N; k++) s.AddMotor(bFirst(k) ? B(k) : A(k), bFirst(k) ? Dir.West : Dir.East);
            for (int k = N - 1; k >= 0; k--) s.AddMotor(bFirst(k) ? A(k) : B(k), bFirst(k) ? Dir.East : Dir.West);
            for (int k = 0; k < N; k++) {
                s.SetMotorPowered(A(k), k % 3 == 1);
                s.SetMotorPowered(B(k), k % 3 != 2);
            }
            for (int k = 0; k < N; k++) {
                Cell first = bFirst(k) ? B(k) : A(k), second = bFirst(k) ? A(k) : B(k);
                Cell driver = k % 3 == 0 ? B(k) : first;
                Cell standby = driver == first ? second : first;
                Assert.Equal(new MotorStatus(MotorState.Driving, 7, 55f), s.GetMotorStatus(driver));
                Assert.Equal(new MotorStatus(MotorState.Standby, 7, 5f), s.GetMotorStatus(standby));
                Assert.Equal(k % 3 != 2, s.IsLineRunning(s.LineIdAt(new(3, 2 * k))));
            }
        }
        // The election must follow every change that can move it: power, facing, motor removal, belt edits.
        [Fact] public void DriverElection_FollowsPowerFacingRemovalAndBeltChanges() {
            var s = Row();
            var a = new Cell(2,1); var b = new Cell(3,-1);
            s.AddMotor(a, Dir.South); s.AddMotor(b, Dir.North);
            s.SetMotorPowered(b, true);
            int line = s.LineIdAt(new(0,0));
            Assert.Equal(MotorState.Driving, s.GetMotorStatus(b).State);       // only b powered
            s.SetMotorPowered(a, true);
            Assert.Equal(MotorState.Driving, s.GetMotorStatus(a).State);       // a registered first
            Assert.Equal(MotorState.Standby, s.GetMotorStatus(b).State);
            s.SetMotorFacing(a, Dir.North);                                    // a turns away (no belt there)
            Assert.Equal(MotorState.Idle, s.GetMotorStatus(a).State);
            Assert.Equal(MotorState.Driving, s.GetMotorStatus(b).State);
            s.SetMotorFacing(a, Dir.South);
            s.RemoveMotor(a);
            Assert.Equal(MotorState.Driving, s.GetMotorStatus(b).State);
            s.SetMotorPowered(b, false);
            Assert.False(s.IsLineRunning(line));
            s.SetMotorPowered(b, true);
            Assert.True(s.IsLineRunning(line));
            s.SetBelt(new(3,0), Dir.North, Tiers.Basic);                       // splits the line under b
            Assert.Equal(new MotorStatus(MotorState.Driving, 1, 25f), s.GetMotorStatus(b));
            Assert.False(s.IsLineRunning(s.LineIdAt(new(0,0))));
            s.RemoveBelt(new(3,0), new());
            Assert.Equal(MotorState.Idle, s.GetMotorStatus(b).State);
            s.AddBelt(new(3,0), Dir.East, Tiers.Basic);
            Assert.Equal(new MotorStatus(MotorState.Driving, 6, 55f), s.GetMotorStatus(b));
            s.AddMotor(a, Dir.South); s.SetMotorPowered(a, true);              // re-added: registers after b
            Assert.Equal(MotorState.Standby, s.GetMotorStatus(a).State);
        }
        [Fact] public void AdjacentBeltDirs_OrderIsNESW() {
            var s = New();
            var c = new Cell(0,0);
            s.AddBelt(new(-1,0), Dir.East, 0); s.AddBelt(new(0,-1), Dir.North, 0);
            s.AddBelt(new(1,0), Dir.West, 0); s.AddBelt(new(0,1), Dir.South, 0);
            Assert.Equal(new[] { Dir.North, Dir.East, Dir.South, Dir.West }, s.AdjacentBeltDirs(c).ToArray());
            var s2 = New();
            s2.AddBelt(new(-1,0), Dir.East, 0); s2.AddBelt(new(0,1), Dir.South, 0);
            Assert.Equal(new[] { Dir.North, Dir.West }, s2.AdjacentBeltDirs(c).ToArray());
        }
    }
}
