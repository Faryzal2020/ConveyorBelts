using ConveyorBelts.Sim;
using Verse;

namespace ConveyorBelts
{
    /// <summary>Conversions between game types and the game-free Sim types. Dir ints equal Rot4.AsInt.</summary>
    public static class Conv
    {
        public static Cell ToCell(this IntVec3 c) => new Cell(c.x, c.z);
        public static IntVec3 ToIntVec3(this Cell c) => new IntVec3(c.X, 0, c.Z);
        public static Dir ToDir(this Rot4 r) => (Dir)r.AsInt;
        public static Rot4 ToRot4(this Dir d) => new Rot4((int)d);
    }
}
