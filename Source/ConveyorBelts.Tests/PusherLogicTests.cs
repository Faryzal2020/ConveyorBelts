using ConveyorBelts.Sim;
using Xunit;

namespace ConveyorBelts.Tests
{
    public class PusherLogicTests
    {
        [Theory]
        [InlineData(PusherRejectMode.EjectIfNeverAccepted, 5, true,  PusherAction.Push)]
        [InlineData(PusherRejectMode.EjectIfNeverAccepted, 0, true,  PusherAction.Wait)]
        [InlineData(PusherRejectMode.EjectIfNeverAccepted, 0, false, PusherAction.Eject)]
        [InlineData(PusherRejectMode.EjectIfNotAcceptedNow, 0, true, PusherAction.Eject)]
        [InlineData(PusherRejectMode.AlwaysWait, 0, false, PusherAction.Wait)]
        [InlineData(PusherRejectMode.AlwaysWait, 3, false, PusherAction.Push)]
        public void Decide(PusherRejectMode m, int now, bool ever, PusherAction expected) =>
            Assert.Equal(expected, PusherLogic.Decide(m, now, ever));

        [Theory]
        [InlineData(Dir.North, EjectSides.LeftThenRight, Dir.West, Dir.East)]
        [InlineData(Dir.North, EjectSides.RightThenLeft, Dir.East, Dir.West)]
        [InlineData(Dir.East, EjectSides.LeftThenRight, Dir.North, Dir.South)]
        public void EjectOrder(Dir facing, EjectSides sides, Dir first, Dir second) =>
            Assert.Equal((first, second), PusherLogic.EjectOrder(facing, sides));
    }
}
