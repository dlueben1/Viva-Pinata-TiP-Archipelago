using System;
using System.Collections.Generic;
using System.Text;

namespace VivaAP.Client.Models
{
    public class VivaBlueprints
    {
        public const int ExpectedPendingMask = 0x20;
        public string DisplayName { get; set; }
        public uint HouseblockTag { get; set; }
        public uint SourceOrdinal { get; set; }
        public uint ResidentSpeciesTag { get; set; }
        public uint UnlockQueueTriggerTag { get; set; }
    }
}
