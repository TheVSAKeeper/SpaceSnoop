using System.Diagnostics;
using System.IO;

namespace SpaceSnoop.Wpf.ViewModels.Scan;

public sealed partial class ScanViewModel
{
    internal static bool ShouldReturnScanMemory(long loadBytes, long thresholdBytes)
    {
        return thresholdBytes > 0 && loadBytes >= thresholdBytes;
    }

    private async Task ScanAfterRescanPromptAsync(string path)
    {
        var prompt = new RescanPrompt(_dialogs, Preferences);
        var confirmed = !Directory.Exists(path) || await prompt.ConfirmAsync(Roots, path, Marks.HasMarked);

        if (!confirmed || IsScanning)
        {
            return;
        }

        if (await ScanAsync(path, CancellationToken.None))
        {
            Tips.OnScanCompleted();
        }
    }

    private bool ReleaseBeforeScan(DirectoryInfo directory)
    {
        if (!ReleasePreviousResult(directory))
        {
            return false;
        }

        var collect = Stopwatch.StartNew();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true, false);
        _logger.ScanPreviousReleased(directory.FullName, collect.ElapsedMilliseconds);

        return true;
    }

    private bool ReleasePreviousResult(DirectoryInfo directory)
    {
        if (!Preferences.ReleaseBeforeRescan)
        {
            return false;
        }

        var current = CurrentRoot;
        var released = ScanTreeEditor.ReleaseUnmarkedRoot(Roots, directory.FullName, Marks.HasMarked, out var keptMarked);

        if (keptMarked)
        {
            _logger.ScanPreviousKeptForMarks(directory.FullName);
        }

        if (released.Count == 0)
        {
            return false;
        }

        var spaces = released.Select(static root => root.Space).OfType<SpaceBase>().ToHashSet<SpaceBase>(ReferenceEqualityComparer.Instance);

        Treemap.RefreshAfterDeletion(spaces);

        if (current is not null && spaces.Contains(current))
        {
            Duplicates.Clear();
            HasResult = false;
        }

        return true;
    }

    private async Task ReturnScanMemoryAsync(DirectorySpace result, Action<string> reportPhase, CancellationToken cancellationToken)
    {
        if (!Preferences.ReturnMemoryAfterScan
            || ((long)result.TotalFileCount + result.TotalDirectoryCount) < AppDefaults.ScanMemoryReturnMinNodes)
        {
            return;
        }

        var pressure = GC.GetGCMemoryInfo();

        if (!ShouldReturnScanMemory(pressure.MemoryLoadBytes, pressure.HighMemoryLoadThresholdBytes))
        {
            _logger.ScanMemoryReturnSkipped(pressure.MemoryLoadBytes, pressure.HighMemoryLoadThresholdBytes);
            return;
        }

        var version = _scanVersion;
        reportPhase("Ожидание возврата памяти");

        try
        {
            await Task.Delay(AppDefaults.ScanMemoryReturnDelayMs, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        pressure = GC.GetGCMemoryInfo();

        if (version != _scanVersion
            || cancellationToken.IsCancellationRequested
            || !ShouldReturnScanMemory(pressure.MemoryLoadBytes, pressure.HighMemoryLoadThresholdBytes))
        {
            return;
        }

        reportPhase("Возврат памяти после сканирования");

        await Task.Run(
            () =>
            {
                var collect = Stopwatch.StartNew();
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, true, true);
                var memory = GC.GetGCMemoryInfo();

                _logger.ScanMemoryReturned(collect.ElapsedMilliseconds, memory.HeapSizeBytes, memory.TotalCommittedBytes);
            },
            CancellationToken.None);

        await _performance.FlushFramesAsync();
    }
}
