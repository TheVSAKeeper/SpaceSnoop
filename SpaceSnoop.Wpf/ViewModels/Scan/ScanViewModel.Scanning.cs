using System.Diagnostics;
using System.IO;

namespace SpaceSnoop.Wpf.ViewModels.Scan;

public sealed partial class ScanViewModel
{
    private async Task<bool> ScanAsync(string path, CancellationToken external = default)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var directory = new DirectoryInfo(path);

        if (!directory.Exists)
        {
            _dialogs.Warning("Сканирование", $"Каталог не найден: {path}");
            return false;
        }

        if (DiskSpaceCalculator.DescribeRootRefusal(directory) is { } refusal)
        {
            _logger.ScanRootLinkRejected(path);
            _dialogs.Warning("Сканирование", refusal);
            return false;
        }

        _cts = CancellationTokenSource.CreateLinkedTokenSource(external);
        _scanVersion++;
        var token = _cts.Token;
        IsPickerOpen = false;
        IsScanning = true;
        ScanWasCancelled = false;
        ScanTargetPath = path;
        StatusCaption = $"Сканирование: {path}";

        SelectedNode = null;
        var released = false;
        var resultApplied = false;

        try
        {
            Progress.ReportPhase("Подготовка сканирования");
            released = ReleaseBeforeScan(directory);
            var parallelism = await Preferences.ResolveParallelismAsync(path).WaitAsync(token);
            var progress = Progress.Begin(directory, path, parallelism);

            if (parallelism == 1 && Preferences.UseMultithreading && Preferences.MaxParallelism > 1)
            {
                _logger.ScanMediaLimited(path, Preferences.MaxParallelism);
            }

            _logger.ScanStarted(path, parallelism > 1, parallelism);

            var outcome = await RunTraversalAsync(directory, parallelism, progress, token);

            var elapsed = Progress.Finish(releaseMeasurement: false);
            var applied = Stopwatch.StartNew();

            Progress.ReportPhase("Показ результата сканирования");
            ApplyScanResult(outcome.Root, elapsed, Progress.Traversal, outcome.Notes);
            resultApplied = true;

            _logger.ScanPhases((long)elapsed.TotalMilliseconds, (long)applied.Elapsed.TotalMilliseconds);

            await ReturnScanMemoryAsync(outcome.Root, Progress.ReportPhase, token);

            _notifier.Notify($"Сканирование завершено: {outcome.Root.AbsolutePath} · {outcome.Root.TotalSizeText}", StatusSeverity.Success);
            return true;
        }
        catch (OperationCanceledException)
        {
            CancelledPath = path;
            CancelledReleasedPrevious = released;
            ScanWasCancelled = true;
            _logger.ScanCancelled(path);
            return false;
        }
        catch (Exception exception)
        {
            var cause = exception.Unwrap();
            _logger.ScanFailed(cause, path);
            _notifier.Notify($"Ошибка сканирования: {cause.Message}", StatusSeverity.Error);
            _dialogs.Error("Ошибка сканирования", cause.Message);
            return false;
        }
        finally
        {
            if (resultApplied)
            {
                Progress.ReportPhase("Показ результата сканирования");
            }

            Progress.Finish(releaseMeasurement: false);

            var measurement = Progress.CompleteMeasurementAsync();

            IsScanning = false;
            StatusCaption = null;

            _cts?.Dispose();
            _cts = null;

            await measurement;
        }
    }
}
