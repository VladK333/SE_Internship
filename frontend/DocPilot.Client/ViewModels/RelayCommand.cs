using System;
using System.Threading.Tasks;
using System.Windows.Input;

namespace DocPilot.Client.ViewModels
{
    // Komanda za dugme koja poziva obicnu metodu
    public class RelayCommand : ICommand
    {
        readonly Action execute;
        readonly Func<bool> canExecute;

        public RelayCommand(Action execute, Func<bool> canExecute = null)
        {
            this.execute = execute ?? throw new ArgumentNullException(nameof(execute));
            this.canExecute = canExecute;
        }

        public event EventHandler CanExecuteChanged;

        public bool CanExecute(object parameter) => canExecute?.Invoke() ?? true;

        public void Execute(object parameter) => execute();

        // Poziva se kada se promeni nešto od cega zavisi CanExecute (npr. prazno polje)
        public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }

    // Komanda sa parametrom iz CommandParameter (npr. tekst primera pitanja)
    public class RelayCommand<T> : ICommand
    {
        readonly Action<T> execute;
        readonly Func<bool> canExecute;

        public RelayCommand(Action<T> execute, Func<bool> canExecute = null)
        {
            this.execute = execute ?? throw new ArgumentNullException(nameof(execute));
            this.canExecute = canExecute;
        }

        public event EventHandler CanExecuteChanged;

        public bool CanExecute(object parameter) => canExecute?.Invoke() ?? true;

        public void Execute(object parameter) => execute(parameter is T value ? value : default);

        public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }

    /// Komanda za dugme koja poziva async metodu. Dok metoda traje, komanda je onemogućena,
    /// pa korisnik ne može dva puta da pošalje isto pitanje.
    public class AsyncRelayCommand : ICommand
    {
        readonly Func<Task> execute;
        readonly Func<bool> canExecute;
        bool isRunning;

        public AsyncRelayCommand(Func<Task> execute, Func<bool> canExecute = null)
        {
            this.execute = execute ?? throw new ArgumentNullException(nameof(execute));
            this.canExecute = canExecute;
        }

        public event EventHandler CanExecuteChanged;

        public bool CanExecute(object parameter) => !isRunning && (canExecute?.Invoke() ?? true);

        /// ICommand.Execute je void; greške hvata sama metoda (ChatViewModel ih prikazuje kao poruke).
        public async void Execute(object parameter) => await ExecuteAsync();

        public async Task ExecuteAsync()
        {
            if (!CanExecute(null))
                return;
            isRunning = true;
            RaiseCanExecuteChanged();
            try
            {
                await execute();
            }
            finally
            {
                isRunning = false;
                RaiseCanExecuteChanged();
            }
        }

        public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}
