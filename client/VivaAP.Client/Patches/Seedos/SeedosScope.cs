namespace VivaAP.Client.Patches.Seedos;

internal enum SeedosPath { None, TalkInitialization, Hit, GardenThrow, WeedInitialization, WeedDelivery, Environment }

internal struct SeedosScopeState
{
    internal SeedosPath Path;
    internal uint Scene;
}

internal readonly ref struct SeedosScope
{
    [ThreadStatic] private static SeedosScopeState state;
    private readonly SeedosScopeState previous;
    internal static SeedosScopeState Current => state;
    internal SeedosScope(SeedosPath path, uint scene = 0)
    {
        previous = state;
        state = new() { Path = path, Scene = scene };
    }
    public void Dispose() => state = previous;
}
