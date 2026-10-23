using System.Text.Json.Serialization;

namespace DocPilot.Client.Models
{
    /// Telo zahteva za POST /chat.
    public class ChatRequest
    {
        [JsonPropertyName("question")]
        public string Question { get; set; }
    }
}
