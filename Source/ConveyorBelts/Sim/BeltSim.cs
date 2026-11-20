using System.Collections.Generic;

namespace ConveyorBelts.Sim
{
    public sealed partial class BeltSim
    {
        public const float Eps = 1e-4f;

        internal sealed class BeltCell
        {
            public Dir Dir;
            public int Tier;
            public readonly List<BeltItem> Items = new List<BeltItem>(2);
            public int LineId = -1;
            public Dir LastFrom;   // round-robin cursor for merging inputs (valid when HasLast)
            public bool HasLast;
        }

        readonly IBeltHost host;
        internal readonly Dictionary<Cell, BeltCell> Belts = new Dictionary<Cell, BeltCell>();

        public BeltSim(IBeltHost host) { this.host = host; }

        public bool RequireMotors { get; set; } = true;
        public int BeltCount => Belts.Count;

        public void AddBelt(Cell c, Dir dir, int tier) => SetBelt(c, dir, tier);

        public void SetBelt(Cell c, Dir dir, int tier)
        {
            if (Belts.TryGetValue(c, out var b)) { b.Dir = dir; b.Tier = tier; }
            else Belts[c] = new BeltCell { Dir = dir, Tier = tier };
            linesDirty = true;
        }

        public void RemoveBelt(Cell c, List<BeltItem> dropped)
        {
            if (!Belts.TryGetValue(c, out var b)) return;
            if (dropped != null) dropped.AddRange(b.Items);
            Belts.Remove(c);
            linesDirty = true;
        }

        public bool TryGetBelt(Cell c, out Dir dir, out int tier)
        {
            if (Belts.TryGetValue(c, out var b)) { dir = b.Dir; tier = b.Tier; return true; }
            dir = default; tier = 0; return false;
        }
    }
}
