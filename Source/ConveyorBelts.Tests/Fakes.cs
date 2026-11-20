using System.Collections.Generic;
using ConveyorBelts.Sim;

namespace ConveyorBelts.Tests
{
    public sealed class FakeHost : IBeltHost
    {
        public long NextHandle = 1000;
        public readonly List<(long Handle, int Count, long NewHandle)> Splits = new();
        public long Split(long handle, int count)
        {
            long h = ++NextHandle;
            Splits.Add((handle, count, h));
            return h;
        }
    }
}
