using KeepShell.Services;
using System.Collections.ObjectModel;

namespace SpaceSnoop.Wpf.ViewModels.Scan;

public sealed partial class ScanMarksViewModel : ObservableObject
{
    private readonly IDialogService _dialogs;
    private readonly DeleteProgressDialogFactory _deleteDialogFactory;
    private readonly OperationPreferences _operations;
    private readonly ILogger _logger;
    private readonly ScanNodeFactory _nodeFactory;
    private readonly ObservableCollection<ScanNodeViewModel> _roots;
    private readonly ScanSummaryViewModel _summary;
    private readonly ScanTreemapViewModel _treemap;
    private readonly Func<ScanNodeViewModel?> _selectedNode;
    private readonly Action<ScanNodeViewModel?> _setSelectedNode;
    private readonly Action _clearHasResult;
    private readonly Func<bool> _isScanning;
    private readonly Action _reloadDrives;
    private long _markedBytes;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMarked))]
    [NotifyPropertyChangedFor(nameof(DeleteHint))]
    [NotifyCanExecuteChangedFor(nameof(DeleteMarkedCommand))]
    private int _markedCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DeleteHint))]
    private string _markedSizeText = string.Empty;

    internal ScanMarksViewModel(
        IDialogService dialogs,
        DeleteProgressDialogFactory deleteDialogFactory,
        OperationPreferences operations,
        ILogger logger,
        ScanNodeFactory nodeFactory,
        ObservableCollection<ScanNodeViewModel> roots,
        ScanSummaryViewModel summary,
        ScanTreemapViewModel treemap,
        Func<ScanNodeViewModel?> selectedNode,
        Action<ScanNodeViewModel?> setSelectedNode,
        Action clearHasResult,
        Func<bool> isScanning,
        Action reloadDrives)
    {
        _dialogs = dialogs;
        _deleteDialogFactory = deleteDialogFactory;
        _operations = operations;
        _logger = logger;
        _nodeFactory = nodeFactory;
        _roots = roots;
        _summary = summary;
        _treemap = treemap;
        _selectedNode = selectedNode;
        _setSelectedNode = setSelectedNode;
        _clearHasResult = clearHasResult;
        _isScanning = isScanning;
        _reloadDrives = reloadDrives;

        nodeFactory.MarkSnapshot = CaptureMarks;
        nodeFactory.MarksChanged += RecountMarked;
    }

    internal event Action<IReadOnlySet<SpaceBase>>? ItemsDeleted;

    public bool HasMarked => MarkedCount > 0;

    public string DeleteHint => HasMarked
        ? $"Переместить в корзину: {MarkedCount} {Plural.Word(MarkedCount, "объект", "объекта", "объектов")}, ≈{MarkedSizeText}"
        : "Пометьте файлы и папки через меню правой кнопки мыши или панель «Детали», чтобы удалить их в корзину";

    internal void RecountMarked()
    {
        Recount(ScanTreeEditor.CollectMarked(_roots));
    }

    private ScanMarkSnapshot CaptureMarks(SpaceBase root, SpaceBase space)
    {
        var scope = ScanTreeEditor.FindMarkScope(root, space);

        if (ReferenceEquals(scope, root) && !scope.IsDeleted)
        {
            var occurrences = _roots.Count(node => ReferenceEquals(node.Space, root));

            if (occurrences == 0)
            {
                return new(root, scope, 0, 0);
            }

            var others = ScanTreeEditor.CollectMarked(_roots.Where(node => !ReferenceEquals(node.Space, root)));
            return new(root, scope, (MarkedCount - others.Count) / occurrences,
                (_markedBytes - others.Sum(static item => item.TotalSize)) / occurrences);
        }

        return ScanTreeEditor.CaptureMarks(root, scope, _nodeFactory.MarksPresent);
    }

    private void RecountMarked(ScanMarkSnapshot before)
    {
        var occurrences = _roots.Count(root => ReferenceEquals(root.Space, before.Root));

        if (occurrences == 0)
        {
            return;
        }

        var marked = ScanTreeEditor.CollectMarked(before.Scope);
        var bytes = marked.Sum(static item => item.TotalSize);
        ApplyMarkedTotals(MarkedCount + ((marked.Count - before.Count) * occurrences),
            _markedBytes + ((bytes - before.Bytes) * occurrences));
    }

    internal void RecountMarkedAfterScan(ScanNodeViewModel freshRoot, List<SpaceBase> transferred)
    {
        var marked = ScanTreeEditor.CollectMarked(_roots, freshRoot);
        marked.AddRange(transferred);
        Recount(marked);
    }

    private void Recount(List<SpaceBase> marked)
    {
        ApplyMarkedTotals(marked.Count, marked.Sum(static item => item.TotalSize));
    }

    private void ApplyMarkedTotals(int count, long bytes)
    {
        _markedBytes = bytes;
        _nodeFactory.MarksPresent = count > 0;
        MarkedCount = count;
        MarkedSizeText = count > 0 ? SizeFormatter.Format(bytes) : string.Empty;
    }

    internal void ApplyDeletionResult(IReadOnlyList<SpaceBase> deletedItems)
    {
        if (deletedItems.Count == 0)
        {
            return;
        }

        _logger.DeletionResultApplied(deletedItems.Count);

        var deletedSet = new HashSet<SpaceBase>(ReferenceEqualityComparer.Instance);

        foreach (var item in deletedItems)
        {
            deletedSet.Add(item);
        }

        var selected = _selectedNode();

        if (selected?.Space is not null && deletedSet.Contains(selected.Space))
        {
            _setSelectedNode(null);
        }

        foreach (var item in deletedItems)
        {
            if (item.Parent is DirectorySpace parentDir)
            {
                parentDir.Remove(item);
            }
        }

        var rootsToRemove = new List<ScanNodeViewModel>();

        foreach (var rootVm in _roots)
        {
            if (rootVm.Space is not null && deletedSet.Contains(rootVm.Space))
            {
                rootsToRemove.Add(rootVm);
            }
            else
            {
                ScanTreeEditor.RefreshNodeAfterDeletion(rootVm, deletedSet);
            }
        }

        foreach (var rootVm in rootsToRemove)
        {
            _roots.Remove(rootVm);
        }

        var resultPath = ScanTreeEditor.NormalizePath(_summary.ResultPath);
        var resultRoot = _roots.FirstOrDefault(r => ScanTreeEditor.SamePath(ScanTreeEditor.NormalizePath(r.AbsolutePath), resultPath));

        if (resultRoot?.Space is DirectorySpace resultDir)
        {
            _summary.Refresh(resultDir);
        }
        else if (rootsToRemove.Any(r => ScanTreeEditor.SamePath(ScanTreeEditor.NormalizePath(r.AbsolutePath), resultPath)))
        {
            _clearHasResult();
        }

        _treemap.RefreshAfterDeletion(deletedSet);
        ItemsDeleted?.Invoke(deletedSet);
        RecountMarked();
        _reloadDrives();
    }

    private bool CanDeleteMarked()
    {
        return !_isScanning() && MarkedCount > 0;
    }

    [RelayCommand(CanExecute = nameof(CanDeleteMarked))]
    private async Task DeleteMarkedAsync()
    {
        var marked = ScanTreeEditor.CollectMarked(_roots);

        if (marked.Count == 0)
        {
            _logger.NothingMarkedForDeletion();
            _dialogs.Info("Удаление", "Нет элементов, помеченных на удаление. Пометьте их через контекстное меню узла.");
            return;
        }

        var permanent = _operations.DeleteMode == DeleteMode.Permanent;
        _logger.DeletionRequested(marked.Count, permanent);
        var dialog = _deleteDialogFactory.Create(marked, permanent);

        if (!_operations.ConfirmBeforeDelete)
        {
            dialog.StartCommand.Execute(null);
        }

        try
        {
            await _dialogs.ShowAsync(dialog);
        }
        finally
        {
            await dialog.StopAsync();
        }

        ApplyDeletionResult(dialog.DeletedItems);
    }
}
