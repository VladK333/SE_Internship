using System;
using System.Collections.Generic;
using System.Configuration;
using System.Globalization;
using System.IO;
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

        public async Task SendFeedbackAsync(FeedbackRequest feedback, CancellationToken cancellationToken = default)
        {
            var body = JsonSerializer.Serialize(feedback);
            using (var content = new StringContent(body, Encoding.UTF8, "application/json"))
            {
                // server vraća 204 bez tela, pa nema šta da se čita
                await SendRawAsync(HttpMethod.Post, "feedback", content, HealthTimeout, "", cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        async Task<T> SendAsync<T>(HttpMethod method, string path, HttpContent content, TimeSpan limit,
            string timeoutHint, CancellationToken cancellationToken)
        {
            var text = await SendRawAsync(method, path, content, limit, timeoutHint, cancellationToken)
                .ConfigureAwait(false);
            try
            {
                return JsonSerializer.Deserialize<T>(text);
            }
            catch (JsonException ex)
            {
                throw new ChatApiException(ChatApiErrorKind.Unexpected,
                    "Server je vratio odgovor koji klijent ne razume.", inner: ex);
            }
        }

        /// Šalje zahtev i vraća telo odgovora; mrežne greške, isteklo vreme i HTTP greške pretvara u ChatApiException.
        async Task<string> SendRawAsync(HttpMethod method, string path, HttpContent content, TimeSpan limit,
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
                    throw TimeoutError(limit, timeoutHint, ex);
                }
                catch (HttpRequestException ex)
                {
                    throw UnavailableError(ex);
                }

                using (response)
                {
                    if (!response.IsSuccessStatusCode)
                        throw ErrorFromResponse((int)response.StatusCode, text);
                    return text;
                }
            }
        }

        /// POST /chat/stream (bonus: streaming): server šalje Server-Sent Events — prvo izvore, pa delove
        /// odgovora kako ih model piše, i na kraju ceo odgovor (isti oblik kao /chat). Izvori i delovi se
        /// javljaju kroz progress; vraća se završni odgovor.
        ///
        /// Vreme se ovde meri od poslednjeg primljenog dela, ne za ceo odgovor: dok model piše, odgovor
        /// sme da traje duže od ApiTimeoutSeconds, ali ako server toliko dugo ne pošalje ništa, to je greška.
        public async Task<ChatResponse> AskStreamingAsync(string question, IProgress<ChatStreamUpdate> progress,
            CancellationToken cancellationToken = default)
        {
            const string hint = " Model je možda preopterećen; pokušaj ponovo ili postavi kraće pitanje.";
            var body = JsonSerializer.Serialize(new ChatRequest { Question = question });
            using (var idle = new CancellationTokenSource(timeout))
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(idle.Token, cancellationToken))
            using (var request = new HttpRequestMessage(HttpMethod.Post, "chat/stream"))
            {
                request.Content = new StringContent(body, Encoding.UTF8, "application/json");
                HttpResponseMessage response;
                try
                {
                    // ResponseHeadersRead: telo se čita kako stiže, ne čeka se kraj odgovora
                    response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, linked.Token)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (OperationCanceledException ex)
                {
                    throw TimeoutError(timeout, hint, ex);
                }
                catch (HttpRequestException ex)
                {
                    throw UnavailableError(ex);
                }

                using (response)
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        var text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        throw ErrorFromResponse((int)response.StatusCode, text);
                    }

                    var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
                    // ReadLineAsync na net48 ne prima CancellationToken: otkazivanje i isteklo vreme zatvaraju vezu
                    using (linked.Token.Register(response.Dispose))
                    using (var reader = new StreamReader(stream, Encoding.UTF8))
                    {
                        string eventName = null;
                        var data = new StringBuilder();
                        while (true)
                        {
                            string line;
                            try
                            {
                                line = await reader.ReadLineAsync().ConfigureAwait(false);
                            }
                            catch (Exception ex) when (ex is IOException || ex is ObjectDisposedException
                                                       || ex is HttpRequestException)
                            {
                                cancellationToken.ThrowIfCancellationRequested();
                                if (idle.IsCancellationRequested)
                                    throw TimeoutError(timeout, hint, ex);
                                throw new ChatApiException(ChatApiErrorKind.ServerUnavailable,
                                    "Veza sa serverom je prekinuta usred odgovora.", inner: ex);
                            }
                            if (line == null)
                                throw new ChatApiException(ChatApiErrorKind.Unexpected,
                                    "Server je prekinuo odgovor pre kraja.");
                            idle.CancelAfter(timeout);   // stigao je novi deo: vreme se meri ispočetka

                            if (line.StartsWith("event:", StringComparison.Ordinal))
                                eventName = line.Substring(6).Trim();
                            else if (line.StartsWith("data:", StringComparison.Ordinal))
                                data.Append(line.Substring(5).Trim());
                            else if (line.Length == 0 && eventName != null)
                            {
                                var done = HandleEvent(eventName, data.ToString(), progress);
                                if (done != null)
                                    return done;
                                eventName = null;
                                data.Clear();
                            }
                        }
                    }
                }
            }
        }

        /// Jedan SSE događaj; vraća završni odgovor za "done", inače null.
        static ChatResponse HandleEvent(string eventName, string data, IProgress<ChatStreamUpdate> progress)
        {
            try
            {
                switch (eventName)
                {
                    case "sources":
                        progress?.Report(new ChatStreamUpdate { Sources = JsonSerializer.Deserialize<ChatResponse>(data).Sources });
                        return null;
                    case "token":
                        using (var doc = JsonDocument.Parse(data))
                            progress?.Report(new ChatStreamUpdate { Token = doc.RootElement.GetProperty("text").GetString() });
                        return null;
                    case "done":
                        return JsonSerializer.Deserialize<ChatResponse>(data);
                    case "error":
                        using (var doc = JsonDocument.Parse(data))
                            throw new ChatApiException(ChatApiErrorKind.ServiceUnavailable,
                                doc.RootElement.GetProperty("detail").GetString());
                    default:
                        return null;   // nepoznat događaj (novija verzija servera) se preskače
                }
            }
            catch (Exception ex) when (ex is JsonException || ex is KeyNotFoundException || ex is InvalidOperationException)
            {
                throw new ChatApiException(ChatApiErrorKind.Unexpected,
                    "Server je vratio odgovor koji klijent ne razume.", inner: ex);
            }
        }

        ChatApiException TimeoutError(TimeSpan limit, string hint, Exception inner) =>
            new ChatApiException(ChatApiErrorKind.Timeout, $"Server nije odgovorio za {limit.TotalSeconds:0} s.{hint}",
                inner: inner);

        ChatApiException UnavailableError(Exception inner) =>
            new ChatApiException(ChatApiErrorKind.ServerUnavailable,
                $"Server nije dostupan na {http.BaseAddress}. Proveri da li je backend pokrenut.", inner: inner);

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
