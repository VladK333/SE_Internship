using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace DocPilot.Client.Models
{
    /// Telo zahteva za POST /feedback (ocena odgovora palcem gore ili dole).
    public class FeedbackRequest
    {
        [JsonPropertyName("question")]
        public string Question { get; set; }

        [JsonPropertyName("answer")]
        public string Answer { get; set; }

        /// "up" ili "down".
        [JsonPropertyName("rating")]
        public string Rating { get; set; }

        [JsonPropertyName("refused")]
        public bool Refused { get; set; }

        [JsonPropertyName("sources")]
        public List<string> Sources { get; set; } = new List<string>();
    }
}
