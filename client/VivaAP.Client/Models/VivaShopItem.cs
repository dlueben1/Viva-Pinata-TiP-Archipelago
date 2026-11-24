using System;
using System.Collections.Generic;
using System.Text;

namespace VivaAP.Client.Models
{
    public class VivaShopItem
    {
        public string InternalName { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public uint NativeTag { get; set; }
        public uint SourceOrdinal { get; set; }
        public uint ItemClass { get; set; }
        public uint LevelThreshold { get; set; }
    }
}
