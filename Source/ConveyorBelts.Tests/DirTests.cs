using ConveyorBelts.Sim;
using Xunit;

namespace ConveyorBelts.Tests
{
    public class DirTests
    {
        [Fact] public void RoundTrips() {
            foreach (Dir d in new[] { Dir.North, Dir.East, Dir.South, Dir.West }) {
                Assert.Equal(d, d.Opposite().Opposite());
                Assert.Equal(d, d.Left().Right());
                Assert.Equal(d, d.Right().Left());
                Assert.Equal(d, d.Left().Left().Left().Left());
                Assert.Equal(d.Opposite(), d.Left().Left());
            }
        }
        [Fact] public void LeftIsCounterClockwise() {
            Assert.Equal(Dir.West, Dir.North.Left());
            Assert.Equal(Dir.East, Dir.North.Right());
        }
        [Fact] public void Step() {
            Assert.Equal(new Cell(0,1), new Cell(0,0).Step(Dir.North));
            Assert.Equal(new Cell(1,0), new Cell(0,0).Step(Dir.East));
            Assert.Equal(new Cell(0,-1), new Cell(0,0).Step(Dir.South));
            Assert.Equal(new Cell(-1,0), new Cell(0,0).Step(Dir.West));
        }
        [Fact] public void TierValues() {
            Assert.Equal(1.5f / 60f, Tiers.Get(Tiers.Basic).TilesPerTick);
            Assert.Equal(25, Tiers.Get(Tiers.Fast).MaxStack);
            Assert.Equal(20f, Tiers.Get(Tiers.Express).WattsPerTile);
            Assert.Equal(6f / 60f, Tiers.Get(Tiers.Express).TilesPerTick);
        }
    }
}
