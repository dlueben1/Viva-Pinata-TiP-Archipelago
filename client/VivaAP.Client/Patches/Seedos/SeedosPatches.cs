namespace VivaAP.Client.Patches.Seedos;

internal static class SeedosPatches
{
    internal static readonly GuestFunction[] Dependencies =
    [ SeedosNative.FindController, SeedosNative.WeedSubstate ];

    internal static Patch[] Create() =>
    [
        new Patch_SeedosSelector(),
        new Patch_SeedosCaller(0x82491C60, SeedosPath.TalkInitialization),
        new Patch_SeedosCaller(0x824924E8, SeedosPath.Hit),
        new Patch_SeedosCaller(0x824937E0, SeedosPath.GardenThrow),
        new Patch_SeedosCaller(0x82493B58, SeedosPath.WeedInitialization),
        new Patch_SeedosConversation(),
        new Patch_SeedosHitGifts(),
        new Patch_SeedosWeedEligibility(),
        new Patch_SeedosWeedStrategy(false),
        new Patch_SeedosWeedStrategy(true),
        new Patch_SeedosWeedUpdate(),
        new Patch_SeedosDelivery(false),
        new Patch_SeedosDelivery(true),
        new Patch_SeedosEnvironmentalBarrier(),
    ];
}
