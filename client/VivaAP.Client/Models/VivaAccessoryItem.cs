namespace VivaAP.Client.Models
{
    /// <summary>
    /// A Paper Pets accessory definition whose purchase permission is controlled by Archipelago.
    /// </summary>
    public class VivaAccessoryItem : VivaShopItem
    {
        public uint ExpectedPendingMask { get; set; }
    }
}
