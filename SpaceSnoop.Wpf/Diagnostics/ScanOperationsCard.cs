using KeepShell.Diagnostics;
using System.Windows.Input;

namespace SpaceSnoop.Wpf.Diagnostics;

public sealed partial class ScanOperationsCard : ObservableObject, IDiagnosticsCard
{
    private readonly PerformanceOperations _operations;

    private readonly IUiTimer _timer;

    private PerformanceOperationTile _tile;

    public ScanOperationsCard(PerformanceOperations operations, IUiDispatcher dispatcher)
    {
        _operations = operations;
        _timer = dispatcher.CreateTimer(TimeSpan.FromMilliseconds(AppDefaults.PerformanceOperationRefreshMs), Refresh);
        _tile = PerformanceText.TileOperation(operations.Current, operations.Last);
        Rows = BuildRows();

        _operations.Changed += OnOperationsChanged;
    }

    public string Title => _tile.Caption;

    public DiagnosticsCardState State =>
        _operations.Current is null && _operations.Last is null
            ? DiagnosticsCardState.Unknown
            : DiagnosticsCardState.Ok;

    public IReadOnlyList<DiagnosticsCardRow> Rows { get; private set; } = [];

    public ICommand? Command => ClearRunCommand;

    public string? CommandCaption => "Забыть прогон";

    public void SetActive(bool active)
    {
        if (!active)
        {
            _timer.Stop();

            return;
        }

        Refresh();
        _timer.Start();
    }

    [RelayCommand(CanExecute = nameof(CanClearRun))]
    private void ClearRun()
    {
        _operations.ClearRun();
    }

    private bool CanClearRun()
    {
        return _operations.Last is not null;
    }

    private void OnOperationsChanged(object? sender, EventArgs e)
    {
        Refresh();
    }

    private void Refresh()
    {
        _tile = PerformanceText.TileOperation(_operations.Current, _operations.Last);
        Rows = BuildRows();

        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(State));
        OnPropertyChanged(nameof(Rows));
        ClearRunCommand.NotifyCanExecuteChanged();
    }

    private IReadOnlyList<DiagnosticsCardRow> BuildRows()
    {
        if (_operations.Current is null && _operations.Last is null)
        {
            return
            [
                new("Прогоны", "не запускались")
                {
                    Hint = "после сканирования или синхронизации здесь встанут длительность, объём, скорость и обход",
                },
            ];
        }

        var rows = new List<DiagnosticsCardRow>(4)
        {
            new("Длительность", _tile.Value),
            new("Объём", _tile.Volume),
            new("Скорость", _tile.Rate),
        };

        if (_tile.Traversal is { } traversal)
        {
            rows.Add(new("Обход", traversal) { Hint = _tile.TraversalDetail });
        }

        return rows;
    }
}
