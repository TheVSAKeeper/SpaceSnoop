using KeepShell.Services;

namespace SpaceSnoop.Wpf.ViewModels.Cleanup;

internal sealed class CleanupOperation : IDisposable
{
    private CancellationTokenSource? _cancellation;
    private Action<bool>? _setBusy;
    private bool _confirming;

    public bool CanCancel => _cancellation is not null && !_confirming;
    public CancellationToken Token => _cancellation?.Token ?? throw new InvalidOperationException("Операция не запущена.");

    public CleanupOperation Begin(Action<bool> setBusy)
    {
        if (_cancellation is not null)
        {
            throw new InvalidOperationException("Операция уже выполняется.");
        }

        _cancellation = new();
        _setBusy = setBusy;
        setBusy(true);
        return this;
    }

    public void RestartCancellation()
    {
        if (_cancellation is null)
        {
            throw new InvalidOperationException("Операция не запущена.");
        }

        _cancellation.Dispose();
        _cancellation = new();
    }

    public void Cancel()
    {
        _cancellation?.Cancel();
    }

    public async Task<bool> ConfirmAsync(IDialogService dialogs, ConfirmDialogViewModel confirmation)
    {
        _confirming = true;
        try
        {
            return await dialogs.ShowAsync(confirmation) && !Token.IsCancellationRequested;
        }
        finally
        {
            _confirming = false;
        }
    }

    public void Dispose()
    {
        _cancellation?.Dispose();
        _cancellation = null;
        var setBusy = _setBusy;
        _setBusy = null;
        setBusy?.Invoke(false);
    }

    public static string Outcome(CleanupReport report)
    {
        return report.Cancelled ? "Отменено"
            : report.Errors.Count > 0 ? "Завершено с ошибками"
            : report.Skipped > 0 ? "Завершено с пропусками" : "Завершено";
    }

    public static string Outcome(DotNetCliOutcome outcome)
    {
        return outcome switch
        {
            DotNetCliOutcome.Cancelled => "Отменено",
            DotNetCliOutcome.TimedOut => "Истекло время ожидания",
            DotNetCliOutcome.Succeeded => "Завершено",
            _ => "Завершено с ошибкой",
        };
    }
}
