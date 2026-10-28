using System.Configuration;
using System.Windows;
using System.Windows.Threading;
using DocPilot.Client.Services;
using DocPilot.Client.Themes;
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
            ThemeManager.ApplySaved();
            api = ChatApiClient.FromConfig();
            var useStreaming = !bool.TryParse(ConfigurationManager.AppSettings["UseStreaming"], out var stream) || stream;
            var viewModel = new ChatViewModel(api, useStreaming);
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
