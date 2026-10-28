using System;
using System.Threading;
using System.Threading.Tasks;
using DocPilot.Client.Models;

namespace DocPilot.Client.Services
{
    /// Komunikacija sa DocPilot serverom. Sve greške se prijavljuju kao ChatApiException
    /// sa porukom spremnom za prikaz korisniku.
    public interface IChatApiClient
    {
        Task<ChatResponse> AskAsync(string question, CancellationToken cancellationToken = default);

        /// Isto kao AskAsync, ali delovi odgovora stižu kroz progress dok ih model piše (POST /chat/stream).
        Task<ChatResponse> AskStreamingAsync(string question, IProgress<ChatStreamUpdate> progress,
            CancellationToken cancellationToken = default);

        Task<HealthResponse> GetHealthAsync(CancellationToken cancellationToken = default);

        Task SendFeedbackAsync(FeedbackRequest feedback, CancellationToken cancellationToken = default);
    }
}
