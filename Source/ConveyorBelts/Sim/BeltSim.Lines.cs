using System.Collections.Generic;

namespace ConveyorBelts.Sim
{
    public sealed partial class BeltSim
    {
        readonly List<List<Cell>> lines = new List<List<Cell>>();
        bool linesDirty = true;

        void EnsureLines()
        {
            if (!linesDirty) return;
            linesDirty = false;
            driversDirty = true;
            lines.Clear();
            foreach (var b in Belts.Values) b.LineId = -1;

            // Heads: belts whose rear neighbour is not a same-direction belt feeding into them.
            foreach (var kv in Belts)
            {
                var rear = kv.Key.Step(kv.Value.Dir.Opposite());
                if (Belts.TryGetValue(rear, out var rb) && rb.Dir == kv.Value.Dir) continue;
                Walk(kv.Key);
            }
            // Anything unreached (cycles of same-dir belts are impossible, but guard) is its own line.
            foreach (var kv in Belts)
                if (kv.Value.LineId < 0) { var l = new List<Cell> { kv.Key }; kv.Value.LineId = lines.Count; lines.Add(l); }
        }

        void Walk(Cell head)
        {
            var line = new List<Cell>();
            int id = lines.Count;
            var cur = head;
            while (Belts.TryGetValue(cur, out var b) && b.LineId < 0 && (line.Count == 0 || b.Dir == Belts[head].Dir))
            {
                b.LineId = id;
                line.Add(cur);
                cur = cur.Step(b.Dir);
            }
            lines.Add(line);
        }

        public int LineIdAt(Cell c)
        {
            EnsureLines();
            return Belts.TryGetValue(c, out var b) ? b.LineId : -1;
        }

        public IReadOnlyList<Cell> LineCells(int lineId)
        {
            EnsureLines();
            return lineId >= 0 && lineId < lines.Count ? lines[lineId] : new List<Cell>();
        }

        public bool IsLineRunning(int lineId)
        {
            if (RequireMotors == false) return true;
            int d = DriverOf(lineId);
            return d >= 0 && motors[d].Powered;
        }
    }
}
