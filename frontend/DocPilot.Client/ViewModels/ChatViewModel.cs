using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DocPilot.Client.Models;
using DocPilot.Client.Services;

namespace DocPilot.Client.ViewModels
{
    /// Stanje i logika chat prozora: lista poruka, unos pitanja, slanje, otkazivanje, status servera.
    public class ChatViewModel : ViewModelBase
    {
        /// Isto ograničenje kao na serveru (guardrail 4).
        public const int MaxQuestionLength = 1000;

        readonly IChatApiClient api;
        readonly bool useStreaming;
        CancellationTokenSource pending;
        string question = "";
        bool isBusy;
        string serverStatus = "Provera servera...";
        string busyStatus = "";
        bool isServerOk;

        /// useStreaming: odgovor se prikazuje dok ga model piše (POST /chat/stream); inače se čeka ceo (POST /chat).
        public ChatViewModel(IChatApiClient api, bool useStreaming = true)
        {
            this.api = api ?? throw new ArgumentNullException(nameof(api));
            this.useStreaming = useStreaming;
            SendCommand = new AsyncRelayCommand(SendAsync, () => CanSend);
            CancelCommand = new RelayCommand(Cancel, () => IsBusy);
            AskExampleCommand = new RelayCommand<string>(AskExample, () => !IsBusy);
        }

        /// Primeri na praznom ekranu; sva četiri pretraga pouzdano nalazi (videti evaluaciju).
        public string[] ExampleQuestions { get; } =
        {
            "Which CGMES versions are supported?",
            "Which attributes describe a line in the grid model?",
            "What voltage initialization modes does the load flow support?",
            "How can I anonymize the identifiers in CIM files?",
        };

        public RelayCommand<string> AskExampleCommand { get; }

        /// Tekst pored indikatora učitavanja: prvo pretraga i čitanje sekcija, pa pisanje odgovora.
        public string BusyStatus
        {
            get => busyStatus;
            private set => SetProperty(ref busyStatus, value);
        }

        void AskExample(string example)
        {
            Question = example;
            if (SendCommand.CanExecute(null))
                SendCommand.Execute(null);
        }

        /// Poruke, najnovija na dnu.
        public ObservableCollection<MessageViewModel> Messages { get; } = new ObservableCollection<MessageViewModel>();

        public AsyncRelayCommand SendCommand { get; }
        public RelayCommand CancelCommand { get; }

        /// Tekst iz polja za unos.
        public string Question
        {
            get => question;
            set
            {
                if (SetProperty(ref question, value ?? ""))
                {
                    OnPropertyChanged(nameof(IsQuestionTooLong));
                    SendCommand.RaiseCanExecuteChanged();
                }
            }
        }

        public bool IsQuestionTooLong => question.Length > MaxQuestionLength;

        /// true dok se čeka odgovor servera (indikator učitavanja).
        public bool IsBusy
        {
            get => isBusy;
            private set
            {
                if (SetProperty(ref isBusy, value))
                {
                    SendCommand.RaiseCanExecuteChanged();
                    CancelCommand.RaiseCanExecuteChanged();
                    AskExampleCommand.RaiseCanExecuteChanged();
                }
            }
        }

        /// Tekst za statusnu liniju, npr. "Server radi · llama3".
        public string ServerStatus
        {
            get => serverStatus;
            private set => SetProperty(ref serverStatus, value);
        }

        public bool IsServerOk
        {
            get => isServerOk;
            private set => SetProperty(ref isServerOk, value);
        }

        bool CanSend => !IsBusy && !string.IsNullOrWhiteSpace(question) && !IsQuestionTooLong;

        /// Proverava /health; poziva se pri pokretanju prozora.
        public async Task CheckServerAsync()
        {
            try
            {
                var health = await api.GetHealthAsync();
                IsServerOk = health.IsOk;
                ServerStatus = health.IsOk
                    ? $"Server radi · {health.Llm?.Model}"
                    : $"Server radi, ali jezički model nije dostupan ({health.Llm?.Status}). Pitanja van dokumentacije i dalje rade.";
            }
            catch (ChatApiException e)
            {
                IsServerOk = false;
                ServerStatus = e.Message;
            }
        }

        async Task SendAsync()
        {
            var text = question.Trim();
            Messages.Add(MessageViewModel.FromQuestion(text));
            Question = "";
            BusyStatus = "DocPilot pretražuje dokumentaciju i čita pronađene sekcije…";
            IsBusy = true;
            pending = new CancellationTokenSource();
            MessageViewModel streaming = null;
            try
            {
                if (useStreaming)
                {
                    streaming = MessageViewModel.StartStreaming(text, SendRatingAsync);
                    // Progress javlja delove na UI niti, istim redom kojim su stigli
                    var progress = new Progress<ChatStreamUpdate>(update =>
                    {
                        if (update.Sources != null)
                            streaming.SetSources(update.Sources);
                        if (update.Token != null)
                        {
                            // balončić se pojavljuje tek sa prvim delom teksta; do tada radi indikator učitavanja
                            if (!Messages.Contains(streaming))
                            {
                                Messages.Add(streaming);
                                BusyStatus = "DocPilot piše odgovor…";
                            }
                            streaming.AppendText(update.Token);
                        }
                    });
                    var response = await api.AskStreamingAsync(text, progress, pending.Token);
                    if (!Messages.Contains(streaming))
                        Messages.Add(streaming);   // odbijanje bez poziva modelu: nije bilo delova teksta
                    streaming.Complete(response);
                }
                else
                {
                    var response = await api.AskAsync(text, pending.Token);
                    Messages.Add(MessageViewModel.FromResponse(response, text, SendRatingAsync));
                }
                if (!IsServerOk)
                    await CheckServerAsync();   // server je u međuvremenu pokrenut: osveži status
            }
            catch (OperationCanceledException)
            {
                streaming?.StopStreaming();
                Messages.Add(MessageViewModel.FromError("Pitanje je otkazano."));
            }
            catch (ChatApiException e)
            {
                streaming?.StopStreaming();
                Messages.Add(MessageViewModel.FromError(e));
                if (e.Kind == ChatApiErrorKind.ServerUnavailable)
                {
                    IsServerOk = false;
                    ServerStatus = e.Message;
                }
            }
            catch (Exception e)
            {
                // nijedna greška ne sme da sruši aplikaciju (async void komanda)
                streaming?.StopStreaming();
                Messages.Add(MessageViewModel.FromError("Neočekivana greška: " + e.Message));
            }
            finally
            {
                pending.Dispose();
                pending = null;
                IsBusy = false;
            }
        }

        void Cancel() => pending?.Cancel();

        /// Ocena odgovora ide na POST /feedback, zajedno sa pitanjem, odgovorom i izvorima.
        Task SendRatingAsync(MessageViewModel message, FeedbackRating rating) =>
            api.SendFeedbackAsync(new FeedbackRequest
            {
                Question = message.Question,
                Answer = message.Text,
                Rating = rating == FeedbackRating.Up ? "up" : "down",
                Refused = message.Kind == MessageKind.Refused,
                Sources = message.Sources.Select(s => s.HeadingPath).ToList(),
            });
    }
}
