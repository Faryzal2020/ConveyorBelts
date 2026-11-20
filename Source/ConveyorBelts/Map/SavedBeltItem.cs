using Verse;

namespace ConveyorBelts
{
    /// <summary>Save-file form of one BeltItem (the Thing itself is deep-saved in BeltItemStore).</summary>
    public class SavedBeltItem : IExposable
    {
        public IntVec3 cell;
        public long handle;
        public int count;
        public float progress;
        public Rot4 enteredFrom;

        public void ExposeData()
        {
            Scribe_Values.Look(ref cell, "cell");
            Scribe_Values.Look(ref handle, "handle");
            Scribe_Values.Look(ref count, "count");
            Scribe_Values.Look(ref progress, "progress");
            Scribe_Values.Look(ref enteredFrom, "enteredFrom");
        }
    }
}
