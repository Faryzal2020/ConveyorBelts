namespace ConveyorBelts.Sim
{
    public readonly record struct Cell(int X, int Z)
    {
        public Cell Step(Dir d)
        {
            var (dx, dz) = d.Offset();
            return new Cell(X + dx, Z + dz);
        }
    }
}
