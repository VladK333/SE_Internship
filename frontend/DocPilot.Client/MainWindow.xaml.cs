using System;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Navigation;
using System.Windows.Threading;
using DocPilot.Client.ViewModels;

namespace DocPilot.Client
{
    /// Samo ponašanje prikaza (tastatura, skrol, linkovi); logika je u ChatViewModel.
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            Loaded += (s, e) => QuestionBox.Focus();
            DataContextChanged += OnDataContextChanged;
        }

        ChatViewModel ViewModel => DataContext as ChatViewModel;

        void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.OldValue is ChatViewModel oldVm)
                oldVm.Messages.CollectionChanged -= OnMessagesChanged;
            if (e.NewValue is ChatViewModel newVm)
                newVm.Messages.CollectionChanged += OnMessagesChanged;
        }

        /// Nova poruka: skrol na dno (posle iscrtavanja, da bi visina bila poznata).
        void OnMessagesChanged(object sender, NotifyCollectionChangedEventArgs e) =>
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(MessagesScroll.ScrollToEnd));

        /// Enter šalje pitanje, Shift+Enter dodaje novi red.
        void OnQuestionKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
                return;
            e.Handled = true;
            if (ViewModel?.SendCommand.CanExecute(null) == true)
                ViewModel.SendCommand.Execute(null);
        }

        /// Izvori se otvaraju u podrazumevanom browseru, i to samo HTTPS linkovi.
        void OnSourceLinkClicked(object sender, RequestNavigateEventArgs e)
        {
            e.Handled = true;
            if (e.Uri != null && e.Uri.IsAbsoluteUri && e.Uri.Scheme == Uri.UriSchemeHttps)
                Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        }
    }
}
