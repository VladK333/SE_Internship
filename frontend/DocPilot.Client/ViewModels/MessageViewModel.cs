using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
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

    public enum FeedbackRating
    {
        None,
        Up,
        Down,
    }

    /// Jedna poruka u listi; prozor je prikazuje različito u zavisnosti od vrste (Kind).
    public class MessageViewModel : ViewModelBase
    {
        readonly Func<MessageViewModel, FeedbackRating, Task> sendRating;
        FeedbackRating rating;
        string feedbackStatus = "";
        MessageKind kind;
        string text;
        IReadOnlyList<Source> sources;
        IReadOnlyList<SourceLinkViewModel> sourceLinks;
        bool isStreaming;

        /// Vrsta se menja samo na kraju strima (odgovor može da se završi odbijanjem).
        public MessageKind Kind
        {
            get => kind;
            private set => SetProperty(ref kind, value);
        }

        /// Tekst poruke; za odgovor asistenta je Markdown. Tokom strima raste kako model piše.
        public string Text
        {
            get => text;
            private set => SetProperty(ref text, value);
        }

        /// Pitanje na koje je ova poruka odgovor (šalje se uz ocenu); prazno za pitanje i grešku.
        public string Question { get; }

        /// Izvori ispod odgovora; prazno za ostale vrste poruka.
        public IReadOnlyList<Source> Sources
        {
            get => sources;
            private set
            {
                sources = value ?? Array.Empty<Source>();
                sourceLinks = sources.Select((s, i) => new SourceLinkViewModel(i + 1, s)).ToList();
                OnPropertyChanged();
                OnPropertyChanged(nameof(SourceLinks));
                OnPropertyChanged(nameof(HasSources));
            }
        }

        /// Izvori sa brojevima za prikaz ([1] Naslov, [2] ...).
        public IReadOnlyList<SourceLinkViewModel> SourceLinks => sourceLinks;

        /// true dok model još piše ovaj odgovor (bonus: streaming).
        public bool IsStreaming
        {
            get => isStreaming;
            private set
            {
                if (SetProperty(ref isStreaming, value))
                {
                    OnPropertyChanged(nameof(CanRate));
                    RateUpCommand.RaiseCanExecuteChanged();
                    RateDownCommand.RaiseCanExecuteChanged();
                }
            }
        }

        public DateTime Time { get; }

        /// Naslov iznad poruke, npr. "DocPilot · 14:32".
        public string Header => (Kind == MessageKind.User ? "Ti" : "DocPilot") + " · " + Time.ToString("HH:mm");

        public bool IsUser => Kind == MessageKind.User;
        public bool HasSources => Sources.Count > 0;

        /// Odgovori i odbijanja mogu da se ocene palcem gore ili dole (bonus: feedback dugmad), kad su završeni.
        public bool CanRate => sendRating != null && !IsStreaming;

        public AsyncRelayCommand RateUpCommand { get; }
        public AsyncRelayCommand RateDownCommand { get; }

        public FeedbackRating Rating
        {
            get => rating;
            private set
            {
                if (SetProperty(ref rating, value))
                {
                    OnPropertyChanged(nameof(IsRatedUp));
                    OnPropertyChanged(nameof(IsRatedDown));
                }
            }
        }

        public bool IsRatedUp => rating == FeedbackRating.Up;
        public bool IsRatedDown => rating == FeedbackRating.Down;

        /// "Hvala na oceni." ili poruka o grešci pored dugmadi.
        public string FeedbackStatus
        {
            get => feedbackStatus;
            private set => SetProperty(ref feedbackStatus, value);
        }

        MessageViewModel(MessageKind kind, string text, IReadOnlyList<Source> sources = null,
            string question = "", Func<MessageViewModel, FeedbackRating, Task> sendRating = null)
        {
            this.kind = kind;
            this.text = text;
            Question = question;
            Sources = sources;
            Time = DateTime.Now;
            this.sendRating = sendRating;
            RateUpCommand = new AsyncRelayCommand(() => RateAsync(FeedbackRating.Up), () => CanRate);
            RateDownCommand = new AsyncRelayCommand(() => RateAsync(FeedbackRating.Down), () => CanRate);
        }

        /// Ocena se odmah prikazuje; ako slanje ne uspe, vraća se prethodna i prikazuje se greška.
        /// Nova ocena (predomišljanje) šalje se kao nova linija u log na serveru.
        async Task RateAsync(FeedbackRating value)
        {
            if (value == rating)
                return;
            var previous = rating;
            Rating = value;
            FeedbackStatus = "";
            try
            {
                await sendRating(this, value);
                FeedbackStatus = "Hvala na oceni.";
            }
            catch (ChatApiException e)
            {
                Rating = previous;
                FeedbackStatus = "Ocena nije sačuvana: " + e.Message;
            }
        }

        /// Prazan odgovor koji se puni dok model piše (AppendText), pa se završava sa Complete.
        public static MessageViewModel StartStreaming(string question, Func<MessageViewModel, FeedbackRating, Task> sendRating) =>
            new MessageViewModel(MessageKind.Assistant, "", null, question, sendRating) { IsStreaming = true };

        public void SetSources(IReadOnlyList<Source> value) => Sources = value;

        public void AppendText(string part) => Text += part;

        /// Kraj strima: konačan odgovor sa servera (isti oblik kao /chat), može biti i odbijanje.
        public void Complete(ChatResponse response)
        {
            Kind = response.Refused ? MessageKind.Refused : MessageKind.Assistant;
            Text = response.Answer;
            Sources = response.Refused ? null : response.Sources;
            IsStreaming = false;
        }

        /// Strim je prekinut (otkazivanje ili greška): ostaje ono što je model stigao da napiše.
        public void StopStreaming() => IsStreaming = false;

        public static MessageViewModel FromQuestion(string question) =>
            new MessageViewModel(MessageKind.User, question);

        public static MessageViewModel FromResponse(ChatResponse response, string question,
            Func<MessageViewModel, FeedbackRating, Task> sendRating) =>
            response.Refused
                ? new MessageViewModel(MessageKind.Refused, response.Answer, null, question, sendRating)
                : new MessageViewModel(MessageKind.Assistant, response.Answer, response.Sources, question, sendRating);

        public static MessageViewModel FromError(ChatApiException error) =>
            new MessageViewModel(MessageKind.Error, error.Message);

        public static MessageViewModel FromError(string message) =>
            new MessageViewModel(MessageKind.Error, message);
    }
}
