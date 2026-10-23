using System;
using System.Collections.Generic;
using DocPilot.Client.Models;
using DocPilot.Client.Services;

namespace DocPilot.Client.ViewModels
{
    public enum MessageKind
    {
        /// Pitanje korisnika.
        User,
        /// Odgovor asistenta (Markdown) sa izvorima.
        Assistant,
        /// Sistem je odbio da odgovori ("Not covered in the documentation.").
        Refused,
        /// Greška u komunikaciji (server ne radi, isteklo vreme...).
        Error,
    }

    /// Jedna poruka u listi; prozor je prikazuje različito u zavisnosti od vrste (Kind).
    public class MessageViewModel : ViewModelBase
    {
        public MessageKind Kind { get; }

        /// Tekst poruke; za odgovor asistenta je Markdown.
        public string Text { get; }

        /// Izvori ispod odgovora; prazno za ostale vrste poruka.
        public IReadOnlyList<Source> Sources { get; }

        public DateTime Time { get; }

        public bool IsUser => Kind == MessageKind.User;
        public bool HasSources => Sources.Count > 0;

        MessageViewModel(MessageKind kind, string text, IReadOnlyList<Source> sources = null)
        {
            Kind = kind;
            Text = text;
            Sources = sources ?? Array.Empty<Source>();
            Time = DateTime.Now;
        }

        public static MessageViewModel FromQuestion(string question) =>
            new MessageViewModel(MessageKind.User, question);

        public static MessageViewModel FromResponse(ChatResponse response) =>
            response.Refused
                ? new MessageViewModel(MessageKind.Refused, response.Answer)
                : new MessageViewModel(MessageKind.Assistant, response.Answer, response.Sources);

        public static MessageViewModel FromError(ChatApiException error) =>
            new MessageViewModel(MessageKind.Error, error.Message);

        public static MessageViewModel FromError(string message) =>
            new MessageViewModel(MessageKind.Error, message);
    }
}
