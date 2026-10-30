using System.Diagnostics;

namespace SpaceSnoop.Wpf.ViewModels.Dialogs;

public readonly record struct CleanupRequest(
    IReadOnlyList<CleanupTarget> Targets,
    long EstimatedBytes,
    int EstimatedFiles,
    CancellationToken External = default);

public sealed partial class CleanupProgressDialogViewModel : OperationDialogViewModelBase
{
    internal const int ProgressPollIntervalMs = 120;

    internal static readonly TimeSpan ProgressPollInterval = TimeSpan.FromMilliseconds(ProgressPollIntervalMs);

    private readonly CleanupRequest _request;
    private readonly CleanupService _service;
    private readonly ILogger _logger;
    private readonly OperationProgressState _progress = new();
    private readonly IUiTimer _progressTimer;
    private readonly PerformanceOperations _operations;
    private readonly List<string> _reportedErrors = [];
    private PerformanceOperation? _reported;
    private long _started;
    private DateTime _startedAtUtc;

    private bool _cancelMissedIndivisible;

    [ObservableProperty]
    private string _targetName = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCancelStep))]
    private bool _isStepIndivisible;

    public override string Title => "Очистка диска";

    public string ActionText => "Очистить";

    public string PlanText { get; }

    public string FateText => "Файлы удаляются безвозвратно, мимо корзины – перенос временных файлов в корзину не освободил бы места.";

    public long FreedBytes { get; private set; }

    public int Deleted { get; private set; }

    public int Skipped { get; private set; }

    public string FirstError { get; private set; } = string.Empty;

    public bool WasCancelled => Cancelled;

    public bool HasFailure => Failure is not null;

    public bool IsIndeterminate => _request.EstimatedFiles == 0;

    public bool CanCancelStep => !IsStepIndivisible;

    public string IndivisibleText => "Корзина Windows очищается одним системным вызовом – этот шаг не прерывается.";

    protected override string RunningStatus => "Очистка…";

    protected override bool CloseResult => FreedBytes > 0;

    protected override bool HasFailedItems => Skipped > 0 || FirstError.Length > 0;

    public CleanupProgressDialogViewModel(CleanupRequest request, CleanupService service, IUiDispatcher uiDispatcher, ILogger logger, PerformanceOperations operations)
    {
        _request = request;
        _operations = operations;
        _service = service;
        _logger = logger;
        _progressTimer = uiDispatcher.CreateTimer(ProgressPollInterval, OnProgressTick);

        TargetName = request.Targets.Count == 1
            ? request.Targets[0].Name
            : $"Корзин выбрано: {request.Targets.Count}";

        PlanText = $"≈ {SizeFormatter.Format(request.EstimatedBytes)} · ≈ {request.EstimatedFiles:N0} файлов";
    }

    internal static string Summarize(int deleted, long freed, bool cancelled, bool binClearedDespiteCancel, int skipped, string firstError)
    {
        var done = $"{(cancelled ? "Отменено" : "Готово")}: удалено {deleted:N0}, освобождено {SizeFormatter.Format(freed)}";

        var text = skipped > 0 ? $"{done}. Пропущено {skipped:N0} – {firstError}"
            : firstError.Length > 0 ? $"{done}. {firstError}"
            : $"{done}.";

        return binClearedDespiteCancel
            ? $"{text.TrimEnd('.')}. Корзину Windows отмена не остановила: этот шаг не прерывается."
            : text;
    }

    internal static bool ClearedDespiteCancel(CleanupTarget target, CleanupReport report, bool cancelRequested)
    {
        return target.IsIndivisible && cancelRequested && !report.Cancelled && report.Deleted > 0;
    }

    protected override async Task ExecuteAsync(CancellationToken token)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _request.External);

        _progress.Reset();
        _progressTimer.Start();

        try
        {
            foreach (var target in _request.Targets)
            {
                linked.Token.ThrowIfCancellationRequested();

                IsStepIndivisible = target.IsIndivisible;
                ReportOperation($"Очистка: {target.Name}", Deleted, FreedBytes);

                var progress = new OffsetProgress(_progress, Deleted, FreedBytes);
                var report = await Task.Run(() => _service.Clean(target, progress, linked.Token), linked.Token);

                IsStepIndivisible = false;
                Tally(target, report, linked.Token.IsCancellationRequested);

                if (report.Cancelled)
                {
                    throw new OperationCanceledException(linked.Token);
                }
            }
        }
        finally
        {
            _progressTimer.Stop();
            IsStepIndivisible = false;
            Apply(_progress.CreateSnapshot());
        }
    }

    protected override void OnStarting()
    {
        _started = Stopwatch.GetTimestamp();
        _startedAtUtc = DateTime.UtcNow;
        ReportOperation("Подготовка очистки", 0, 0);
        _logger.CleanupRunStarted(_request.Targets.Count, _request.EstimatedBytes);
    }

    protected override void OnFinished()
    {
        _operations.ReportCleanup(new(_startedAtUtc, DateTime.UtcNow, Deleted, Skipped, FreedBytes, Cancelled, Failure?.Message, _reportedErrors.ToArray()));
        _operations.Release(_reported);
        _reported = null;
        if (Cancelled)
        {
            _logger.CleanupRunCancelled(Deleted, FreedBytes);
            return;
        }

        if (Failure is { } failure)
        {
            _logger.CleanupRunFailed(failure);
            return;
        }

        _logger.CleanupRunFinished(Deleted, FreedBytes, Skipped);
    }

    protected override string BuildSummary()
    {
        if (Failure is { } failure)
        {
            return $"Ошибка: {failure.Message}";
        }

        return Summarize(Deleted, FreedBytes, Cancelled, _cancelMissedIndivisible, Skipped, FirstError);
    }

    private void Tally(CleanupTarget target, CleanupReport report, bool cancelRequested)
    {
        _cancelMissedIndivisible |= ClearedDespiteCancel(target, report, cancelRequested);

        FreedBytes += report.FreedBytes;
        Deleted += report.Deleted;
        Skipped += report.Skipped;
        _reportedErrors.AddRange(report.Errors);

        if (FirstError.Length == 0 && report.Errors.Count > 0)
        {
            FirstError = report.Errors[0];
        }
    }

    private void OnProgressTick()
    {
        Apply(_progress.CreateSnapshot());
    }

    private void Apply(OperationProgress update)
    {
        if (IsFinished)
        {
            return;
        }

        var done = update.Completed;
        var total = _request.EstimatedFiles;

        CurrentPath = update.Current;
        CountText = total > 0 ? $"{done:N0} / {total:N0}" : done.ToString("N0");
        ProgressValue = total > 0 ? Math.Clamp((double)done / total, 0d, 1d) : 0d;
        ReportOperation(_reported?.Name ?? "Очистка диска", done, update.Bytes);
    }

    private void ReportOperation(string name, int completed, long bytes)
    {
        var operation = new PerformanceOperation(name, completed, bytes, Stopwatch.GetElapsedTime(_started), LogicalBytes: true);
        if (_operations.TryReport(operation, _reported))
        {
            _reported = operation;
        }
    }

    private sealed class OffsetProgress(OperationProgressState state, int completed, long bytes) : IProgress<OperationProgress>
    {
        public void Report(OperationProgress value)
        {
            state.Report(new(completed + value.Completed, value.Current, bytes + value.Bytes));
        }
    }
}
