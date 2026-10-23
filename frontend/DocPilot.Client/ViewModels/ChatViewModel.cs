using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using DocPilot.Client.Services;

namespace DocPilot.Client.ViewModels
{
    /// Stanje i logika chat prozora: lista poruka, unos pitanja, slanje, otkazivanje, status servera.
    public class ChatViewModel : ViewModelBase
    {
        /// Isto ograničenje kao na serveru (guardrail 4).
        public const int MaxQuestionLength = 1000;

        readonly IChatApiClient api;
        CancellationTokenSource pending;
        string question = "";
        bool isBusy;
        string serverStatus = "Provera servera...";
        bool isServerOk;

        public ChatViewModel(IChatApiClient api)
        {
            this.api = api ?? throw new ArgumentNullException(nameof(api));
            SendCommand = new AsyncRelayCommand(SendAsync, () => CanSend);
            CancelCommand = new RelayCommand(Cancel, () => IsBusy);
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
            IsBusy = true;
            pending = new CancellationTokenSource();
            try
            {
                var response = await api.AskAsync(text, pending.Token);
                Messages.Add(MessageViewModel.FromResponse(response));
                if (!IsServerOk)
                    await CheckServerAsync();   // server je u međuvremenu pokrenut: osveži status
            }
            catch (OperationCanceledException)
            {
                Messages.Add(MessageViewModel.FromError("Pitanje je otkazano."));
            }
            catch (ChatApiException e)
            {
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
    }
}
