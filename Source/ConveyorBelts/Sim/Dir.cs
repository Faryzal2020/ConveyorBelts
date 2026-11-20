namespace ConveyorBelts.Sim
{
    public enum Dir : byte { North = 0, East = 1, South = 2, West = 3 }

    public static class DirExt
    {
        public static Dir Opposite(this Dir d) => (Dir)(((int)d + 2) & 3);
        public static Dir Left(this Dir d) => (Dir)(((int)d + 3) & 3);
        public static Dir Right(this Dir d) => (Dir)(((int)d + 1) & 3);
        public static (int dx, int dz) Offset(this Dir d)
        {
            switch (d)
            {
                case Dir.North: return (0, 1);
                case Dir.East: return (1, 0);
                case Dir.South: return (0, -1);
                default: return (-1, 0);
            }
        }
    }
}
