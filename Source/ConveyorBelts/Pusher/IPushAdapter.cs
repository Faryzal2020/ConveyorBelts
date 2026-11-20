using Verse;

namespace ConveyorBelts
{
    /// <summary>How a pusher feeds one kind of machine. One small class per kind; PushAdapters picks one by the target.</summary>
    public interface IPushAdapter
    {
        bool Applies(Thing target);

        /// <summary>Whether the machine would ever take this kind of item (ignoring how full it is now).</summary>
        bool EverAccepts(Thing target, ThingDef def);

        /// <summary>How many of item the machine takes right now (may exceed item.stackCount; 0 = none).</summary>
        int CanAccept(Thing target, Thing item);

        /// <summary>
        /// Gives item (unspawned, holding exactly the count to give) to the machine. The adapter consumes, absorbs,
        /// stores or spawns what it takes; whatever is left in item (not destroyed, not spawned, not held) goes back
        /// to the pusher's buffer.
        /// </summary>
        void Accept(Thing target, Thing item);

        /// <summary>For the Port tab: what the machine takes right now, or null for nothing.</summary>
        string DescribeAcceptsNow(Thing target);
    }
}
