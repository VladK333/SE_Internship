using System;

namespace DocPilot.Client.Services
{
    public enum ChatApiErrorKind
    {
        /// Server nije pokrenut ili nije dostupan na mreži.
        ServerUnavailable,
        /// Server nije odgovorio na vreme.
        Timeout,
        /// Server je odbio zahtev (400/422), npr. prazno ili predugo pitanje.
        InvalidRequest,
        /// Server radi, ali LLM nije dostupan ili je zakasnio (503).
        ServiceUnavailable,
        /// Bilo šta drugo (500, neispravan odgovor...).
        Unexpected,
    }

    /// Greška pri komunikaciji sa serverom; Message je poruka za korisnika.
    public class ChatApiException : Exception
    {
        public ChatApiErrorKind Kind { get; }
        public int? StatusCode { get; }

        public ChatApiException(ChatApiErrorKind kind, string message, int? statusCode = null, Exception inner = null)
            : base(message, inner)
        {
            Kind = kind;
            StatusCode = statusCode;
        }
    }
}
