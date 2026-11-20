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
