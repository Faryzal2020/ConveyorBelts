namespace ConveyorBelts.Sim
{
    public sealed record TierSpec(float TilesPerTick, int MaxStack, float WattsPerTile);

    public static class Tiers
    {
        public const int Basic = 0, Fast = 1, Express = 2;

        static readonly TierSpec[] Specs =
        {
            new TierSpec(1.5f / 60f, 10, 5f),
            new TierSpec(3f / 60f, 25, 10f),
            new TierSpec(6f / 60f, 75, 20f),
        };

        public static TierSpec Get(int tier) => Specs[tier];
    }
}
