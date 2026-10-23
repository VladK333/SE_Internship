using System.Text.Json.Serialization;

namespace DocPilot.Client.Models
{
    /// Odgovor sa GET /health.
    public class HealthResponse
    {
        /// "ok" ili "degraded" (LLM ne radi).
        [JsonPropertyName("status")]
        public string Status { get; set; }

        [JsonPropertyName("index")]
        public IndexStatus Index { get; set; }

        [JsonPropertyName("llm")]
        public LlmStatus Llm { get; set; }

        [JsonPropertyName("uptime_seconds")]
        public int UptimeSeconds { get; set; }

        public bool IsOk => Status == "ok";
    }

    public class IndexStatus
    {
        [JsonPropertyName("chunks")]
        public int Chunks { get; set; }

        [JsonPropertyName("embedding_model")]
        public string EmbeddingModel { get; set; }
    }

    public class LlmStatus
    {
        /// "ok", "unavailable" ili "model not pulled".
        [JsonPropertyName("status")]
        public string Status { get; set; }

        [JsonPropertyName("model")]
        public string Model { get; set; }

        [JsonPropertyName("url")]
        public string Url { get; set; }
    }
}
