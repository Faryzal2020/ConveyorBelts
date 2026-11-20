using ConveyorBelts.Sim;
using Xunit;

namespace ConveyorBelts.Tests
{
    public class BeltPathTests
    {
        [Fact] public void LPath_XFirst_ExampleFromSpec() {
            var p = BeltPath.Compute(new(0,0), new(5,5), zFirst:false, Dir.North);
            Assert.Equal(11, p.Count);
            for (int x = 0; x <= 4; x++) Assert.Equal((new Cell(x,0), Dir.East), p[x]);
            for (int z = 0; z <= 5; z++) Assert.Equal((new Cell(5,z), Dir.North), p[5+z]);
        }
        [Fact] public void LPath_Reversed_GoesWestThenSouth() {
            var p = BeltPath.Compute(new(5,5), new(0,0), false, Dir.North);
            Assert.Equal((new Cell(1,5), Dir.West), p[4]);
            Assert.Equal((new Cell(0,5), Dir.South), p[5]);
            Assert.Equal((new Cell(0,0), Dir.South), p[^1]);
        }
        [Fact] public void LPath_ZFirst() {
            var p = BeltPath.Compute(new(0,0), new(5,5), true, Dir.North);
            Assert.Equal((new Cell(0,4), Dir.North), p[4]);
            Assert.Equal((new Cell(0,5), Dir.East), p[5]);
            Assert.Equal((new Cell(5,5), Dir.East), p[^1]);
        }
        [Fact] public void Straight_AllSameDir() =>
            Assert.All(BeltPath.Compute(new(0,0), new(3,0), false, Dir.North), t => Assert.Equal(Dir.East, t.Dir));
        [Fact] public void Vertical_South() {
            var p = BeltPath.Compute(new(0,0), new(0,-2), false, Dir.North);
            Assert.Equal(3, p.Count); Assert.All(p, t => Assert.Equal(Dir.South, t.Dir));
        }
        [Fact] public void SingleCell_UsesGivenDir() =>
            Assert.Equal(new[] { (new Cell(2,2), Dir.West) }, BeltPath.Compute(new(2,2), new(2,2), false, Dir.West));
    }
}
