using System.Collections.Generic;

namespace DocPilot.Client.Models
{
    /// Deo odgovora sa POST /chat/stream: ili izvori (stižu prvi), ili novi deo teksta.
    public class ChatStreamUpdate
    {
        public List<Source> Sources { get; set; }

        public string Token { get; set; }
    }
}
