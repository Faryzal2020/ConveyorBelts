using System.Collections.Generic;
using System.Linq;
using ConveyorBelts.Sim;
using Xunit;

namespace ConveyorBelts.Tests
{
    public class ItemTests
    {
        sealed class TestSink : IBeltSink
        {
            public bool Accept = true;
            public readonly List<(long H, int N, Dir Side)> Seen = new();
            public bool TryAccept(long handle, int count, Dir fromSide)
            {
                if (!Accept) return false;
                Seen.Add((handle, count, fromSide)); return true;
            }
        }

        static (BeltSim s, FakeHost h) New(bool motors = false)
        {
            var h = new FakeHost();
            return (new BeltSim(h) { RequireMotors = motors }, h);
        }
        static void Ticks(BeltSim s, int n) { for (int i = 0; i < n; i++) s.Tick(); }
        static void Near(float expected, float actual) => Assert.InRange(actual, expected - 1e-3f, expected + 1e-3f);

        [Fact] public void Item_MovesAtTierSpeed()
        {
            var (s, _) = New(); s.AddBelt(new(0,0), Dir.East, Tiers.Basic);
            Assert.True(s.TryInsert(new(0,0), Dir.West, 1, 1));
            Ticks(s, 20);
            Near(0.5f, s.ItemsAt(new(0,0))[0].Progress);
        }
        [Fact] public void Item_TransfersToNextBelt()
        {
            var (s, _) = New();
            s.AddBelt(new(0,0), Dir.East, 0); s.AddBelt(new(1,0), Dir.East, 0);
            s.TryInsert(new(0,0), Dir.West, 1, 1);
            Ticks(s, 39);
            Assert.Single(s.ItemsAt(new(0,0)));
            Near(0.975f, s.ItemsAt(new(0,0))[0].Progress);
            Ticks(s, 2);
            Assert.Empty(s.ItemsAt(new(0,0)));
            var it = s.ItemsAt(new(1,0)).Single();
            Assert.Equal(Dir.West, it.EnteredFrom);
            Assert.True(it.Progress < 0.05f);
        }
        [Fact] public void Item_StopsAtEndWithNoTarget()
        {
            var (s, _) = New(); s.AddBelt(new(0,0), Dir.East, 0);
            s.TryInsert(new(0,0), Dir.West, 1, 1);
            Ticks(s, 200);
            Assert.Equal(1f, s.ItemsAt(new(0,0)).Single().Progress);
        }
        [Fact] public void Insert_RespectsMinGap()
        {
            var (s, _) = New(); s.AddBelt(new(0,0), Dir.East, 0);
            Assert.True(s.TryInsert(new(0,0), Dir.West, 1, 1));
            Assert.False(s.TryInsert(new(0,0), Dir.West, 2, 1));
            Ticks(s, 20);
            Assert.True(s.TryInsert(new(0,0), Dir.West, 2, 1));
            Assert.False(s.TryInsert(new(0,0), Dir.West, 3, 1));
        }
        [Fact] public void Insert_RefusedFromFrontSide()
        {
            var (s, _) = New(); s.AddBelt(new(0,0), Dir.East, 0);
            Assert.False(s.TryInsert(new(0,0), Dir.East, 1, 1));
            Assert.False(s.TryInsert(new(5,5), Dir.West, 1, 1));
        }
        [Fact] public void Insert_RefusedAboveMaxStack()
        {
            var (s, _) = New(); s.AddBelt(new(0,0), Dir.East, Tiers.Basic);
            Assert.Equal(10, s.MaxStackAt(new(0,0)));
            Assert.False(s.TryInsert(new(0,0), Dir.West, 1, 11));
            Assert.True(s.TryInsert(new(0,0), Dir.West, 1, 10));
        }
        [Fact] public void HeadOn_DoesNotTransfer()
        {
            var (s, _) = New();
            s.AddBelt(new(0,0), Dir.East, 0); s.AddBelt(new(1,0), Dir.West, 0);
            s.TryInsert(new(0,0), Dir.West, 1, 1);
            Ticks(s, 200);
            Assert.Equal(1f, s.ItemsAt(new(0,0)).Single().Progress);
            Assert.Empty(s.ItemsAt(new(1,0)));
        }
        [Fact] public void SideMerge_IsRoundRobin()
        {
            var (s, _) = New();
            s.AddBelt(new(0,0), Dir.East, 0); s.AddBelt(new(1,0), Dir.East, 0); s.AddBelt(new(2,0), Dir.East, 0);
            s.AddBelt(new(1,1), Dir.South, 0);
            s.AddSink(new(3,0), new TestSink());
            int west = 0, north = 0; long id = 1;
            for (int t = 0; t < 1200; t++)
            {
                s.TryInsert(new(0,0), Dir.West, id++, 1);
                s.TryInsert(new(1,1), Dir.North, id++, 1);
                s.Tick();
                foreach (var it in s.ItemsAt(new(1,0)))
                    if (it.Progress == 0f) { if (it.EnteredFrom == Dir.West) west++; else if (it.EnteredFrom == Dir.North) north++; }
            }
            Assert.True(west > 0 && north > 0);
            Assert.True(System.Math.Abs(west - north) <= 1, $"west={west} north={north}");
        }
        [Fact] public void TierSplit_ExpressIntoBasic()
        {
            var (s, h) = New();
            s.AddBelt(new(0,0), Dir.East, Tiers.Express); s.AddBelt(new(1,0), Dir.East, Tiers.Basic);
            Assert.True(s.TryInsert(new(0,0), Dir.West, 5, 75));
            Ticks(s, 11);
            Assert.Equal(10, s.ItemsAt(new(1,0)).Single().Count);
            Assert.Equal(65, s.ItemsAt(new(0,0)).Single().Count);
            Assert.Contains(h.Splits, x => x.Handle == 5 && x.Count == 10);
        }
        [Fact] public void StoppedLine_Freezes_ThenRuns()
        {
            var (s, _) = New(true); s.AddBelt(new(0,0), Dir.East, 0);
            s.TryInsert(new(0,0), Dir.West, 1, 1);
            Ticks(s, 30);
            Assert.Equal(0f, s.ItemsAt(new(0,0))[0].Progress);
            s.AddMotor(new(0,1), Dir.South); s.SetMotorPowered(new(0,1), true);
            Ticks(s, 20);
            Near(0.5f, s.ItemsAt(new(0,0))[0].Progress);
        }
        [Fact] public void StoppedLine_StillAcceptsAtEntry()
        {
            var (s, _) = New(true);
            s.AddBelt(new(0,0), Dir.East, 0); s.AddBelt(new(1,0), Dir.North, 0);
            s.AddMotor(new(0,1), Dir.South); s.SetMotorPowered(new(0,1), true);
            s.TryInsert(new(0,0), Dir.West, 1, 1);
            Ticks(s, 45);
            var it = s.ItemsAt(new(1,0)).Single();
            Assert.Equal(Dir.West, it.EnteredFrom);
            Assert.Equal(0f, it.Progress);
        }
        [Fact] public void Sink_ReceivesWithFromSide()
        {
            var (s, _) = New(); var sink = new TestSink();
            s.AddBelt(new(0,0), Dir.East, 0); s.AddSink(new(1,0), sink);
            s.TryInsert(new(0,0), Dir.West, 9, 4);
            Ticks(s, 45);
            Assert.Equal((9L, 4, Dir.West), sink.Seen.Single());
            Assert.Empty(s.ItemsAt(new(0,0)));
        }
        [Fact] public void Sink_Refusing_BacksUp()
        {
            var (s, _) = New(); var sink = new TestSink { Accept = false };
            s.AddBelt(new(0,0), Dir.East, 0); s.AddSink(new(1,0), sink);
            s.TryInsert(new(0,0), Dir.West, 9, 4);
            Ticks(s, 100);
            Assert.Empty(sink.Seen);
            Assert.Equal(1f, s.ItemsAt(new(0,0)).Single().Progress);
            sink.Accept = true; s.Tick();
            Assert.Single(sink.Seen);
            s.RemoveSink(new(1,0));
        }
        [Fact] public void RemoveBelt_ReturnsItems()
        {
            var (s, _) = New(); s.AddBelt(new(0,0), Dir.East, 0);
            s.TryInsert(new(0,0), Dir.West, 1, 1); Ticks(s, 20); s.TryInsert(new(0,0), Dir.West, 2, 1);
            var dropped = new List<BeltItem>();
            s.RemoveBelt(new(0,0), dropped);
            Assert.Equal(new long[] { 1, 2 }, dropped.Select(i => i.Handle).OrderBy(x => x));
            Assert.Empty(s.ItemsAt(new(0,0)));
        }
        [Fact] public void SetBelt_KeepsItems_NewSpeedApplies()
        {
            var (s, _) = New(); s.AddBelt(new(0,0), Dir.East, Tiers.Basic);
            s.TryInsert(new(0,0), Dir.West, 1, 1); Ticks(s, 10);
            float before = s.ItemsAt(new(0,0))[0].Progress;
            s.SetBelt(new(0,0), Dir.East, Tiers.Express); s.Tick();
            Near(before + 0.1f, s.ItemsAt(new(0,0))[0].Progress);
        }
        [Fact] public void TryRemoveItem_ByHandle()
        {
            var (s, _) = New(); s.AddBelt(new(0,0), Dir.East, 0);
            s.TryInsert(new(0,0), Dir.West, 1, 1); Ticks(s, 20); s.TryInsert(new(0,0), Dir.West, 2, 3);
            Assert.False(s.TryRemoveItem(new(0,0), 99, out _));
            Assert.True(s.TryRemoveItem(new(0,0), 2, out var it));
            Assert.Equal(3, it.Count);
            Assert.Equal(1L, s.ItemsAt(new(0,0)).Single().Handle);
        }
        [Fact] public void RestoreItem_NoBelt_ReturnsFalse()
        {
            var (s, _) = New(); s.AddBelt(new(0,0), Dir.East, 0);
            Assert.False(s.RestoreItem(new(3,3), new BeltItem { Handle = 1, Count = 1 }));
            Assert.True(s.RestoreItem(new(0,0), new BeltItem { Handle = 1, Count = 1, Progress = 0.3f, EnteredFrom = Dir.West }));
            Assert.True(s.RestoreItem(new(0,0), new BeltItem { Handle = 2, Count = 1, Progress = 0.9f, EnteredFrom = Dir.West }));
            Assert.Equal(2L, s.ItemsAt(new(0,0))[0].Handle);   // front item first
        }

        static BeltSim Ring(int items)
        {
            var (s, _) = New();
            s.AddBelt(new(0,0), Dir.East, 0); s.AddBelt(new(1,0), Dir.North, 0);
            s.AddBelt(new(1,1), Dir.West, 0); s.AddBelt(new(0,1), Dir.South, 0);
            var cells = new[] { new Cell(0,0), new Cell(1,0), new Cell(1,1), new Cell(0,1) };
            var from = new[] { Dir.West, Dir.South, Dir.East, Dir.North };
            long h = 1; int left = items;
            for (int k = 0; k < 2 && left > 0; k++)
                for (int i = 0; i < 4 && left > 0; i++, left--)
                    s.RestoreItem(cells[i], new BeltItem { Handle = h++, Count = 1, Progress = k == 0 ? 0.9f : 0.4f, EnteredFrom = from[i] });
            return s;
        }
        [Fact] public void Ring_FullRingKeepsItems()
        {
            var s = Ring(8);
            Ticks(s, 600);
            Assert.Equal(8, s.AllItems().Count());
        }
        [Fact] public void Ring_SevenItemsCirculate()
        {
            var s = Ring(7);
            var before = s.AllItems().ToDictionary(x => x.Item.Handle, x => x.Cell);
            Ticks(s, 600);
            var after = s.AllItems().ToList();
            Assert.Equal(7, after.Count);
            Assert.Contains(after, x => before[x.Item.Handle] != x.Cell);
        }
    }
}
