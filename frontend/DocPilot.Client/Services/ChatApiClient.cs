using System;
using System.Configuration;
using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DocPilot.Client.Models;

namespace DocPilot.Client.Services
{
    /// Poziva POST /chat i GET /health preko HttpClient-a (async/await, UI se ne blokira).
    public class ChatApiClient : IChatApiClient, IDisposable
    {
        static readonly TimeSpan HealthTimeout = TimeSpan.FromSeconds(5);

        readonly HttpClient http;
        readonly TimeSpan timeout;

        public Uri BaseUrl => http.BaseAddress;

        public ChatApiClient(Uri baseUrl, TimeSpan timeout)
        {
            // HttpClient nema svoj timeout; vreme se meri preko CancellationToken-a,
            // da bi se razlikovalo isteklo vreme od otkazivanja koje traži korisnik
            http = new HttpClient { BaseAddress = baseUrl, Timeout = Timeout.InfiniteTimeSpan };
            this.timeout = timeout;
        }

        /// Pravi klijent sa podešavanjima iz App.config (ApiBaseUrl, ApiTimeoutSeconds).
        public static ChatApiClient FromConfig()
        {
            var url = ConfigurationManager.AppSettings["ApiBaseUrl"] ?? "http://127.0.0.1:8000/";
            var seconds = int.TryParse(ConfigurationManager.AppSettings["ApiTimeoutSeconds"], out var s) ? s : 330;
            return new ChatApiClient(new Uri(url), TimeSpan.FromSeconds(seconds));
        }

        public async Task<ChatResponse> AskAsync(string question, CancellationToken cancellationToken = default)
        {
            var body = JsonSerializer.Serialize(new ChatRequest { Question = question });
            using (var content = new StringContent(body, Encoding.UTF8, "application/json"))
            {
                return await SendAsync<ChatResponse>(HttpMethod.Post, "chat", content, timeout,
                    " Model je možda preopterećen; pokušaj ponovo ili postavi kraće pitanje.", cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        public Task<HealthResponse> GetHealthAsync(CancellationToken cancellationToken = default) =>
            SendAsync<HealthResponse>(HttpMethod.Get, "health", null, HealthTimeout, "", cancellationToken);

        async Task<T> SendAsync<T>(HttpMethod method, string path, HttpContent content, TimeSpan limit,
            string timeoutHint, CancellationToken cancellationToken)
        {
            using (var timeoutCts = new CancellationTokenSource(limit))
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, cancellationToken))
            using (var request = new HttpRequestMessage(method, path) { Content = content })
            {
                HttpResponseMessage response;
                string text;
                try
                {
                    response = await http.SendAsync(request, linked.Token).ConfigureAwait(false);
                    text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;   // korisnik je otkazao; to nije greška servera
                }
                catch (OperationCanceledException ex)
                {
                    throw new ChatApiException(ChatApiErrorKind.Timeout,
                        $"Server nije odgovorio za {limit.TotalSeconds:0} s.{timeoutHint}",
                        inner: ex);
                }
                catch (HttpRequestException ex)
                {
                    throw new ChatApiException(ChatApiErrorKind.ServerUnavailable,
                        $"Server nije dostupan na {http.BaseAddress}. Proveri da li je backend pokrenut.",
                        inner: ex);
                }

                using (response)
                {
                    var status = (int)response.StatusCode;
                    if (!response.IsSuccessStatusCode)
                        throw ErrorFromResponse(status, text);
                    try
                    {
                        return JsonSerializer.Deserialize<T>(text);
                    }
                    catch (JsonException ex)
                    {
                        throw new ChatApiException(ChatApiErrorKind.Unexpected,
                            "Server je vratio odgovor koji klijent ne razume.", status, ex);
                    }
                }
            }
        }

        /// FastAPI greške imaju oblik {"detail": "..."}; za 422 je detail lista.
        static ChatApiException ErrorFromResponse(int status, string body)
        {
            string detail = null;
            try
            {
                using (var doc = JsonDocument.Parse(body))
                {
                    if (doc.RootElement.TryGetProperty("detail", out var d) && d.ValueKind == JsonValueKind.String)
                        detail = d.GetString();
                }
            }
            catch (JsonException) { }

            switch (status)
            {
                case 400:
                case 422:
                    return new ChatApiException(ChatApiErrorKind.InvalidRequest,
                        detail ?? "Server je odbio pitanje kao neispravno.", status);
                case 503:
                    return new ChatApiException(ChatApiErrorKind.ServiceUnavailable,
                        detail ?? "Jezički model trenutno nije dostupan.", status);
                default:
                    return new ChatApiException(ChatApiErrorKind.Unexpected,
                        string.Format(CultureInfo.InvariantCulture, "Greška na serveru (HTTP {0}).", status), status);
            }
        }

        public void Dispose() => http.Dispose();
    }
}
