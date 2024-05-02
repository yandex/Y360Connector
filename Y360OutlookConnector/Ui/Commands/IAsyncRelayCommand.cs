using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

namespace Y360OutlookConnector.Ui.Commands
{
    public interface IAsyncRelayCommand : ICommand
    {
        bool IsExecuting { get; }

        bool CanExecute();
        Task ExecuteAsync();
        Task ExecuteAsync(CancellationToken cancellationToken);
        Task ExecuteAsync(object parameter);
        Task ExecuteAsync(object parameter, CancellationToken cancellationToken);

        void InvalidateCommand();
    }
}
