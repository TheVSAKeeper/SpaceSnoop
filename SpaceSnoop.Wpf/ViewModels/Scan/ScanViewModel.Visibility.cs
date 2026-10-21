namespace SpaceSnoop.Wpf.ViewModels.Scan;

public sealed partial class ScanViewModel
{
    public bool HasIgnoredItems => _nodeFactory.Visibility.Count > 0;

    public string ResetIgnoredCaption => $"Снять игнорирование ({_nodeFactory.Visibility.Count})";
    internal bool IsRefreshingVisibility { get; private set; }

    private void OnIgnoreRequested(ScanNodeViewModel node)
    {
        if (!node.CanIgnore || node.Space is not { } space || !Roots.Any(root => ReferenceEquals(root.Space, node.Root)))
        {
            return;
        }

        if (_nodeFactory.Visibility.Ignore(space))
        {
            RefreshVisibility(AffectedPath(space));
        }
    }

    private void OnUnignoreRequested(ScanNodeViewModel node)
    {
        if (node.Space is { } space
            && Roots.Any(root => ReferenceEquals(root.Space, node.Root))
            && _nodeFactory.Visibility.Restore(space) is { } restored)
        {
            RefreshVisibility(AffectedPath(restored));
        }
    }

    private static IReadOnlySet<SpaceBase> AffectedPath(SpaceBase space)
    {
        var affected = new HashSet<SpaceBase>();

        for (var current = space; current is not null; current = current.Parent)
        {
            affected.Add(current);
        }

        return affected;
    }

    [RelayCommand]
    private void ResetIgnored()
    {
        var affected = _nodeFactory.Visibility.AffectedSpaces();
        _nodeFactory.Visibility.Clear();
        RefreshVisibility(affected);
    }

    private void RefreshVisibility(IReadOnlySet<SpaceBase> affected)
    {
        IsRefreshingVisibility = true;

        try
        {
            foreach (var root in Roots)
            {
                root.RefreshVisibility(affected);
            }
        }
        finally
        {
            IsRefreshingVisibility = false;
        }

        if (SelectedNode is { } node)
        {
            Inspector.RefreshVisibility(node, affected);
        }

        Treemap.RefreshVisibility(_nodeFactory.Visibility, affected);
        OnPropertyChanged(nameof(HasIgnoredItems));
        OnPropertyChanged(nameof(ResetIgnoredCaption));
    }
}
