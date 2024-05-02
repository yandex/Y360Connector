using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

namespace Y360OutlookConnector.Ui.Commands
{
    public class AsyncRelayCommand : IAsyncRelayCommand
    {
        public bool IsExecuting => executionCount > 0;

        protected readonly Func<Task> ExecuteAsyncNoParam;
        protected readonly Action ExecuteNoParam;
        protected readonly Func<bool> CanExecuteNoParam;

        private readonly Func<object, Task> executeAsync;
        private readonly Action<object> execute;
        private readonly Predicate<object> canExecute;
        private EventHandler canExecuteChangedDelegate;
        private int executionCount;

        public event EventHandler CanExecuteChanged
        {
            add
            {
                CommandManager.RequerySuggested += value;
                canExecuteChangedDelegate = (EventHandler)Delegate.Combine(canExecuteChangedDelegate, value);
            }
            remove
            {
                CommandManager.RequerySuggested -= value;
                canExecuteChangedDelegate = (EventHandler)Delegate.Remove(canExecuteChangedDelegate, value);
            }
        }

        #region Constructors

        public AsyncRelayCommand(Action<object> execute)
          : this(execute, param => true)
        {
        }

        public AsyncRelayCommand(Action executeNoParam)
          : this(executeNoParam, () => true)
        {
        }

        public AsyncRelayCommand(Func<object, Task> executeAsync)
          : this(executeAsync, param => true)
        {
        }

        public AsyncRelayCommand(Func<Task> executeAsyncNoParam)
          : this(executeAsyncNoParam, () => true)
        {
        }

        public AsyncRelayCommand(Action executeNoParam, Func<bool> canExecuteNoParam)
        {
            ExecuteNoParam = executeNoParam ?? throw new ArgumentNullException(nameof(executeNoParam));
            CanExecuteNoParam = canExecuteNoParam ?? (() => true);
        }

        public AsyncRelayCommand(Action<object> execute, Predicate<object> canExecute)
        {
            this.execute = execute ?? throw new ArgumentNullException(nameof(execute));
            this.canExecute = canExecute ?? (param => true); ;
        }

        public AsyncRelayCommand(Func<Task> executeAsyncNoParam, Func<bool> canExecuteNoParam)
        {
            ExecuteAsyncNoParam = executeAsyncNoParam ?? throw new ArgumentNullException(nameof(executeAsyncNoParam));
            CanExecuteNoParam = canExecuteNoParam ?? (() => true);
        }

        public AsyncRelayCommand(Func<object, Task> executeAsync, Predicate<object> canExecute)
        {
            this.executeAsync = executeAsync ?? throw new ArgumentNullException(nameof(executeAsync));
            this.canExecute = canExecute ?? (param => true); ;
        }

        #endregion Constructors


        public bool CanExecute() => CanExecute(null);

        public bool CanExecute(object parameter) => canExecute?.Invoke(parameter)
                                                    ?? CanExecuteNoParam?.Invoke()
                                                    ?? true;

        async void ICommand.Execute(object parameter) => await ExecuteAsync(parameter, CancellationToken.None);

        public async Task ExecuteAsync() => await ExecuteAsync(null, CancellationToken.None);

        public async Task ExecuteAsync(CancellationToken cancellationToken) => await ExecuteAsync(null, cancellationToken);

        public async Task ExecuteAsync(object parameter) => await ExecuteAsync(parameter, CancellationToken.None);

        public async Task ExecuteAsync(object parameter, CancellationToken cancellationToken)
        {
            try
            {
                Interlocked.Increment(ref executionCount);
                cancellationToken.ThrowIfCancellationRequested();

                if (executeAsync != null)
                {
                    await executeAsync.Invoke(parameter).ConfigureAwait(false);
                    return;
                }
                if (ExecuteAsyncNoParam != null)
                {
                    await ExecuteAsyncNoParam.Invoke().ConfigureAwait(false);
                    return;
                }
                if (ExecuteNoParam != null)
                {
                    ExecuteNoParam.Invoke();
                    return;
                }

                execute?.Invoke(parameter);
            }
            finally
            {
                Interlocked.Decrement(ref executionCount);
            }
        }

        public void InvalidateCommand() => OnCanExecuteChanged();

        protected virtual void OnCanExecuteChanged() => canExecuteChangedDelegate?.Invoke(this, EventArgs.Empty);
    }
}
