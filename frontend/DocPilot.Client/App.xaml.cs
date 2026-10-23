using System.Windows;
using System.Windows.Threading;
using DocPilot.Client.Services;
using DocPilot.Client.ViewModels;

namespace DocPilot.Client
{
    public partial class App : Application
    {
        ChatApiClient api;

        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            DispatcherUnhandledException += OnUnhandledException;
            api = ChatApiClient.FromConfig();
            var viewModel = new ChatViewModel(api);
            new MainWindow { DataContext = viewModel }.Show();
            await viewModel.CheckServerAsync();
        }

        /// Neočekivana greška u prikazu: poruka umesto tihog gašenja aplikacije.
        void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            MessageBox.Show(e.Exception.Message, "DocPilot – neočekivana greška",
                MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        }

        protected override void OnExit(ExitEventArgs e)
        {
            api?.Dispose();
            base.OnExit(e);
        }
    }
}
