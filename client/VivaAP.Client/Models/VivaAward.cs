using System;
using System.Collections.Generic;
using System.Text;

namespace VivaAP.Client.Models
{
    /// <summary>
    /// Represents a Viva award.
    /// </summary>
    public class VivaAward
    {
        public string EventId { get; set; }
        public string Subject { get; set; }
        public string Category { get; set; }
        public int XP { get; set; }
    }
}
