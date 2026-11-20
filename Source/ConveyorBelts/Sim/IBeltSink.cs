namespace ConveyorBelts.Sim
{
    public interface IBeltSink
    {
        /// <summary>Offer a stack arriving through <paramref name="fromSide"/> (side of the sink cell, toward the source belt).</summary>
        bool TryAccept(long handle, int count, Dir fromSide);
    }
}
