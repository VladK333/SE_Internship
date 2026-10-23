using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace DocPilot.Client.Models
{
    /// Odgovor sa POST /chat.
    public class ChatResponse
    {
        /// Odgovor u Markdown formatu.
        [JsonPropertyName("answer")]
        public string Answer { get; set; }

        /// Sekcije dokumentacije koje su korišćene; [1] u odgovoru je prvi izvor.
        [JsonPropertyName("sources")]
        public List<Source> Sources { get; set; } = new List<Source>();

        /// true kada sistem odbije da odgovori ("Not covered in the documentation.").
        [JsonPropertyName("refused")]
        public bool Refused { get; set; }
    }
}
