using ConveyorBelts.Sim;
using Xunit;

namespace ConveyorBelts.Tests
{
    public class RenderPosTests
    {
        static void Near((float Dx, float Dz) a, float x, float z)
        {
            Assert.InRange(a.Dx, x - 1e-4f, x + 1e-4f);
            Assert.InRange(a.Dz, z - 1e-4f, z + 1e-4f);
        }
        [Fact] public void LocalPos_Straight()
        {
            Near(BeltSim.LocalPos(Dir.West, Dir.East, 0f), -0.5f, 0f);
            Near(BeltSim.LocalPos(Dir.West, Dir.East, 0.5f), 0f, 0f);
            Near(BeltSim.LocalPos(Dir.West, Dir.East, 1f), 0.5f, 0f);
        }
        [Fact] public void LocalPos_SideEntry()
        {
            Near(BeltSim.LocalPos(Dir.North, Dir.East, 0.25f), 0f, 0.25f);
            Near(BeltSim.LocalPos(Dir.North, Dir.East, 0.75f), 0.25f, 0f);
        }
    }
}
