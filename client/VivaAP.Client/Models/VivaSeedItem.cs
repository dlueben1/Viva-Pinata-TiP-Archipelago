namespace VivaAP.Client.Models
{
    /// <summary>A seed type whose permanent purchase permission is controlled by Archipelago.</summary>
    public class VivaSeedItem : VivaShopItem
    {
        public uint AcquisitionTriggerTag { get; set; }
        public uint ExpectedPendingMask { get; set; }
    }
}
