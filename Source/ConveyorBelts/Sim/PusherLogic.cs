namespace ConveyorBelts.Sim
{
    /// <summary>What a pusher does with a buffered item its machine won't take right now.</summary>
    public enum PusherRejectMode { EjectIfNeverAccepted, EjectIfNotAcceptedNow, AlwaysWait }

    /// <summary>Which side cell an ejected item tries first (left/right as seen facing the machine).</summary>
    public enum EjectSides { LeftThenRight, RightThenLeft }

    public enum PusherAction { Push, Wait, Eject }

    public static class PusherLogic
    {
        /// <summary>
        /// acceptNow: how many of the buffered item the machine takes right now (0 with no machine);
        /// everAccepts: whether the machine would ever take this kind of item (false with no machine).
        /// </summary>
        public static PusherAction Decide(PusherRejectMode mode, int acceptNow, bool everAccepts)
        {
            if (acceptNow > 0) return PusherAction.Push;
            switch (mode)
            {
                case PusherRejectMode.EjectIfNotAcceptedNow: return PusherAction.Eject;
                case PusherRejectMode.AlwaysWait: return PusherAction.Wait;
                default: return everAccepts ? PusherAction.Wait : PusherAction.Eject;
            }
        }

        /// <summary>The side directions to try for an eject, in order, for a pusher facing <paramref name="facing"/>.</summary>
        public static (Dir First, Dir Second) EjectOrder(Dir facing, EjectSides sides)
            => sides == EjectSides.LeftThenRight ? (facing.Left(), facing.Right()) : (facing.Right(), facing.Left());
    }
}
