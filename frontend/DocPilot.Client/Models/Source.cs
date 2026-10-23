using System.Text.Json.Serialization;

namespace DocPilot.Client.Models
{
    /// Jedan izvor (sekcija dokumentacije) uz odgovor.
    public class Source
    {
        [JsonPropertyName("title")]
        public string Title { get; set; }

        /// npr. "Grid exchange formats > CIM-CGMES > Import"
        [JsonPropertyName("heading_path")]
        public string HeadingPath { get; set; }

        /// Link ka sekciji na powsybl.readthedocs.io.
        [JsonPropertyName("url")]
        public string Url { get; set; }

        /// Kosinusna sličnost pitanja i sekcije (0-1).
        [JsonPropertyName("score")]
        public double Score { get; set; }
    }
}
