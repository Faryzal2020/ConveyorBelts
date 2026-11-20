using System.Linq;
using ConveyorBelts.Sim;
using Xunit;

namespace ConveyorBelts.Tests
{
    public class LineTests
    {
        static BeltSim New() => new BeltSim(new FakeHost());

        [Fact] public void Lines_LPathMakesTwoLines() {
            var s = New();
            foreach (var (c, d) in BeltPath.Compute(new(0,0), new(5,5), false, Dir.North)) s.AddBelt(c, d, Tiers.Basic);
            var a = s.LineCells(s.LineIdAt(new(0,0))).ToList();
            Assert.Equal(Enumerable.Range(0,5).Select(x => new Cell(x,0)).ToList(), a);
            var b = s.LineCells(s.LineIdAt(new(5,0))).ToList();
            Assert.Equal(Enumerable.Range(0,6).Select(z => new Cell(5,z)).ToList(), b);
            Assert.NotEqual(s.LineIdAt(new(0,0)), s.LineIdAt(new(5,0)));
        }
        [Fact] public void Lines_SideMergeIsSeparateLine() {
            var s = New();
            for (int x = 0; x < 3; x++) s.AddBelt(new(x,0), Dir.East, 0);
            s.AddBelt(new(1,1), Dir.South, 0);
            Assert.NotEqual(s.LineIdAt(new(1,1)), s.LineIdAt(new(1,0)));
            Assert.Equal(3, s.LineCells(s.LineIdAt(new(0,0))).Count);
        }
        [Fact] public void Lines_MixedTiersAreOneLine() {
            var s = New();
            s.AddBelt(new(0,0), Dir.East, Tiers.Basic);
            s.AddBelt(new(1,0), Dir.East, Tiers.Express);
            s.AddBelt(new(2,0), Dir.East, Tiers.Fast);
            Assert.Equal(3, s.LineCells(s.LineIdAt(new(0,0))).Count);
            Assert.Equal(s.LineIdAt(new(0,0)), s.LineIdAt(new(2,0)));
        }
        [Fact] public void Lines_RemovingMiddleSplits() {
            var s = New();
            for (int x = 0; x < 5; x++) s.AddBelt(new(x,0), Dir.East, 0);
            s.RemoveBelt(new(2,0), new());
            Assert.Equal(2, s.LineCells(s.LineIdAt(new(0,0))).Count);
            Assert.Equal(2, s.LineCells(s.LineIdAt(new(3,0))).Count);
            Assert.Equal(-1, s.LineIdAt(new(2,0)));
            Assert.Equal(4, s.BeltCount);
        }
        [Fact] public void Lines_RingTerminates() {
            var s = New();
            s.AddBelt(new(0,0), Dir.East, 0); s.AddBelt(new(1,0), Dir.North, 0);
            s.AddBelt(new(1,1), Dir.West, 0); s.AddBelt(new(0,1), Dir.South, 0);
            var ids = new[] { new Cell(0,0), new Cell(1,0), new Cell(1,1), new Cell(0,1) }.Select(c => s.LineIdAt(c)).ToList();
            Assert.Equal(4, ids.Distinct().Count());
            foreach (var id in ids) Assert.Single(s.LineCells(id));
        }
        [Fact] public void RemoveBelt_ReturnsItems_SetBeltKeepsThem() {
            var s = New();
            s.AddBelt(new(0,0), Dir.East, 0);
            s.Belts[new Cell(0,0)].Items.Add(new BeltItem { Handle = 7, Count = 3 });
            s.SetBelt(new(0,0), Dir.North, Tiers.Fast);
            Assert.True(s.TryGetBelt(new(0,0), out var d, out var t));
            Assert.Equal(Dir.North, d); Assert.Equal(Tiers.Fast, t);
            var dropped = new System.Collections.Generic.List<BeltItem>();
            s.RemoveBelt(new(0,0), dropped);
            Assert.Single(dropped); Assert.Equal(7, dropped[0].Handle);
            Assert.False(s.TryGetBelt(new(0,0), out _, out _));
        }
    }
}
