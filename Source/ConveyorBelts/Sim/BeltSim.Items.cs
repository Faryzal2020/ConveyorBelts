using System.Collections.Generic;

namespace ConveyorBelts.Sim
{
    public sealed partial class BeltSim
    {
        const float MinGap = 0.5f;
        const int ItemsPerTile = 2;

        readonly Dictionary<Cell, IBeltSink> sinks = new Dictionary<Cell, IBeltSink>();
        readonly Dictionary<Cell, Dir> sinkLast = new Dictionary<Cell, Dir>();
        readonly Dictionary<Cell, int> pending = new Dictionary<Cell, int>();   // receiving cell -> bitmask of fromSide dirs
        byte[] lineRun = new byte[0];                                            // 0 unknown, 1 running, 2 stopped (per tick)

        public void AddSink(Cell c, IBeltSink sink) { sinks[c] = sink; sinkLast.Remove(c); }
        public void RemoveSink(Cell c) { sinks.Remove(c); sinkLast.Remove(c); }

        public int MaxStackAt(Cell belt) => Belts.TryGetValue(belt, out var b) ? Tiers.Get(b.Tier).MaxStack : 0;

        static bool HasEntrySpace(BeltCell b)
        {
            int n = b.Items.Count;
            return n < ItemsPerTile && (n == 0 || b.Items[n - 1].Progress >= MinGap - Eps);
        }

        public bool CanInsert(Cell belt, Dir fromSide)
            => Belts.TryGetValue(belt, out var b) && b.Dir != fromSide && HasEntrySpace(b);

        public bool TryInsert(Cell belt, Dir fromSide, long handle, int count)
        {
            if (!Belts.TryGetValue(belt, out var b)) return false;
            if (b.Dir == fromSide || count > Tiers.Get(b.Tier).MaxStack || !HasEntrySpace(b)) return false;
            b.Items.Add(new BeltItem { Handle = handle, Count = count, Progress = 0f, EnteredFrom = fromSide });
            return true;
        }

        public bool TryRemoveItem(Cell belt, long handle, out BeltItem item)
        {
            if (Belts.TryGetValue(belt, out var b))
                for (int i = 0; i < b.Items.Count; i++)
                    if (b.Items[i].Handle == handle) { item = b.Items[i]; b.Items.RemoveAt(i); return true; }
            item = default; return false;
        }

        public bool RestoreItem(Cell belt, BeltItem item)
        {
            if (!Belts.TryGetValue(belt, out var b)) return false;
            int i = 0;   // keep front (highest progress) first
            while (i < b.Items.Count && b.Items[i].Progress >= item.Progress) i++;
            b.Items.Insert(i, item);
            return true;
        }

        static readonly IReadOnlyList<BeltItem> NoItems = new BeltItem[0];
        public IReadOnlyList<BeltItem> ItemsAt(Cell c) => Belts.TryGetValue(c, out var b) ? b.Items : NoItems;

        public IEnumerable<(Cell Cell, BeltItem Item)> AllItems()
        {
            foreach (var kv in Belts)
                foreach (var it in kv.Value.Items) yield return (kv.Key, it);
        }

        public static (float Dx, float Dz) LocalPos(Dir enteredFrom, Dir beltDir, float progress)
        {
            Dir edge = progress < 0.5f ? enteredFrom : beltDir;
            float k = progress < 0.5f ? 1f - progress * 2f : (progress - 0.5f) * 2f;
            var (dx, dz) = edge.Offset();
            return (dx * 0.5f * k, dz * 0.5f * k);
        }

        bool LineRunning(int lineId)
        {
            if (RequireMotors == false) return true;
            if (lineId < 0) return false;
            if (lineId >= lineRun.Length) return IsLineRunning(lineId);
            if (lineRun[lineId] == 0) lineRun[lineId] = (byte)(IsLineRunning(lineId) ? 1 : 2);
            return lineRun[lineId] == 1;
        }

        public void Tick()
        {
            EnsureLines();
            if (RequireMotors)
            {
                if (lineRun.Length < lines.Count) lineRun = new byte[lines.Count];
                else System.Array.Clear(lineRun, 0, lineRun.Length);
            }

            // Phase 1: advance items on running lines; collect front items waiting at the end.
            pending.Clear();
            foreach (var kv in Belts)
            {
                var b = kv.Value;
                if (b.Items.Count == 0 || !LineRunning(b.LineId)) continue;
                float speed = Tiers.Get(b.Tier).TilesPerTick;
                float limit = 1f;
                for (int i = 0; i < b.Items.Count; i++)
                {
                    var it = b.Items[i];
                    float p = it.Progress + speed;
                    if (p > limit) p = limit;
                    if (p >= 1f - Eps) p = 1f;
                    if (p < it.Progress) p = it.Progress;
                    it.Progress = p;
                    b.Items[i] = it;
                    limit = p - MinGap;
                }
                if (b.Items[0].Progress >= 1f - Eps)
                {
                    var dir = b.Dir;
                    var r = kv.Key.Step(dir);
                    pending.TryGetValue(r, out int mask);
                    pending[r] = mask | (1 << (int)dir.Opposite());   // fromSide as seen by the receiver
                }
            }

            // Phase 2: each receiving cell accepts at most one candidate, round-robin.
            foreach (var kv in pending)
            {
                var r = kv.Key;
                int mask = kv.Value;
                Belts.TryGetValue(r, out var target);
                Dir last = Dir.North; bool hasLast = false;
                if (target != null) { last = target.LastFrom; hasLast = target.HasLast; }
                else if (sinks.ContainsKey(r) && sinkLast.TryGetValue(r, out var sl)) { last = sl; hasLast = true; }

                int start = hasLast ? (int)last + 1 : 0;
                for (int k = 0; k < 4; k++)
                {
                    var d = (Dir)((start + k) & 3);
                    if ((mask & (1 << (int)d)) == 0) continue;
                    if (!TryTransfer(r.Step(d), d, r, target)) continue;
                    if (target != null) { target.LastFrom = d; target.HasLast = true; }
                    else sinkLast[r] = d;
                    break;
                }
            }
        }

        bool TryTransfer(Cell src, Dir fromSide, Cell r, BeltCell target)
        {
            var sb = Belts[src];
            if (sb.Items.Count == 0 || sb.Items[0].Progress < 1f - Eps) return false;
            var item = sb.Items[0];

            if (target != null)
            {
                if (target.Dir == fromSide || !HasEntrySpace(target)) return false;
                int max = Tiers.Get(target.Tier).MaxStack;
                if (item.Count > max)
                {
                    long nh = host.Split(item.Handle, max);
                    item.Count -= max;
                    sb.Items[0] = item;
                    target.Items.Add(new BeltItem { Handle = nh, Count = max, Progress = 0f, EnteredFrom = fromSide });
                }
                else
                {
                    sb.Items.RemoveAt(0);
                    target.Items.Add(new BeltItem { Handle = item.Handle, Count = item.Count, Progress = 0f, EnteredFrom = fromSide });
                }
                return true;
            }
            if (sinks.TryGetValue(r, out var sink) && sink.TryAccept(item.Handle, item.Count, fromSide))
            {
                sb.Items.RemoveAt(0);
                return true;
            }
            return false;
        }
    }
}
