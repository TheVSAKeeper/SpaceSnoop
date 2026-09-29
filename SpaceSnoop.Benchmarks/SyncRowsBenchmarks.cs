using BenchmarkDotNet.Attributes;
using SpaceSnoop.Core.Domain;
using SpaceSnoop.Wpf.ViewModels.Sync;

namespace SpaceSnoop.Benchmarks;

[MemoryDiagnoser]
public class SyncRowsBenchmarks
{
    private readonly BenchSyncRowHost _host = new();

    private ComparisonResult _result = null!;
    private Dictionary<DirectoryComparison, (long Left, long Right)> _dirSizes = null!;

    [Params(10_000, 200_000)]
    public int Files { get; set; }

    [Params(false, true)]
    public bool FlatView { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _result = SyntheticTree.BuildComparison(Files);
        _result.ApplyMode(SyncMode.Bidirectional, mirror: true, SyncWinner.Newest);
        _dirSizes = SyncRowsProjector.BuildDirSizeCache(_result.Root);
    }

    [Benchmark]
    public int Build()
    {
        var request = new SyncRowsRequest
        {
            Result = _result,
            FlatView = FlatView,
            ShowIdentical = true,
            DirSizeCache = _dirSizes,
        };

        return SyncRowsProjector.Build(request, _host).Count;
    }
}
