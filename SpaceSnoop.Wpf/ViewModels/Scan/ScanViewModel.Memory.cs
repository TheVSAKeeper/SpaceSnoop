using System.Diagnostics;
using System.IO;

namespace SpaceSnoop.Wpf.ViewModels.Scan;

public sealed partial class ScanViewModel
{
    private void ReleaseBeforeScan(DirectoryInfo directory)
    {
        if (!ReleasePreviousResult(directory))
        {
            return;
        }

        var collect = Stopwatch.StartNew();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: false);
        _logger.ScanPreviousReleased(directory.FullName, collect.ElapsedMilliseconds);
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

    private void ReturnScanMemory(DirectorySpace result)
    {
        if (!Preferences.ReturnMemoryAfterScan
            || (long)result.TotalFileCount + result.TotalDirectoryCount < AppDefaults.ScanMemoryReturnMinNodes)
        {
            return;
        }

        // TODO: уплотняющая сборка замораживает окно на 0,6–0,85 с вскоре после скана C: (3,5 млн файлов) и растёт с деревом.
        // Станет мешать (просадка в журнале на слабом железе) – звать её только при высокой нагрузке на память (GCMemoryInfo.MemoryLoadBytes)
        _ = Task.Run(async () =>
        {
            await Task.Delay(AppDefaults.ScanMemoryReturnDelayMs, CancellationToken.None).ConfigureAwait(false);

            if (IsScanning)
            {
                return;
            }

            var collect = Stopwatch.StartNew();
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
            var memory = GC.GetGCMemoryInfo();

            _logger.ScanMemoryReturned(collect.ElapsedMilliseconds, memory.HeapSizeBytes, memory.TotalCommittedBytes);
        }, CancellationToken.None);
    }
}
