namespace ConveyorBelts.Sim
{
    public interface IBeltHost
    {
        /// <summary>Returns a new handle carrying <paramref name="count"/>; the old handle keeps the rest.</summary>
        long Split(long handle, int count);
    }
}
