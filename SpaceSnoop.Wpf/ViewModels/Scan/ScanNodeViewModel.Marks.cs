namespace SpaceSnoop.Wpf.ViewModels.Scan;

public sealed partial class ScanNodeViewModel
{
    internal IEnumerable<ScanNodeViewModel> RealizedChildren => _children?.Where(static child => !ReferenceEquals(child, Dummy)) ?? [];

    internal void RefreshOwnMarks()
    {
        if (Space is null)
        {
            return;
        }

        IsMarkedDeleted = Space.IsDeleted;
        OnPropertyChanged(nameof(CanMarkContentsDeleted));
        OnPropertyChanged(nameof(CanUnmarkContents));
    }

    [RelayCommand]
    private void MarkDeleted()
    {
        if (Space is null || Space.IsDeleted)
        {
            return;
        }

        var before = _factory?.CaptureMarks(Space, Root ?? Space);
        Space.Delete();
        ApplyMarkChange(before);
    }

    [RelayCommand]
    private void UnmarkDeleted()
    {
        if (Space is null)
        {
            return;
        }

        var before = _factory?.CaptureMarks(Space, Root ?? Space);
        RestoreRecursive(Space);
        ApplyMarkChange(before);
    }

    [RelayCommand]
    private void MarkContentsDeleted()
    {
        if (Space is not DirectorySpace dir)
        {
            return;
        }

        var before = _factory?.CaptureMarks(dir, Root ?? dir);

        foreach (var child in EnumerateChildren(dir))
        {
            child.Delete();
        }

        ApplyMarkChange(before);
    }

    [RelayCommand]
    private void UnmarkContents()
    {
        if (Space is not DirectorySpace dir)
        {
            return;
        }

        var before = _factory?.CaptureMarks(dir, Root ?? dir);

        foreach (var child in EnumerateChildren(dir))
        {
            RestoreRecursive(child);
        }

        ApplyMarkChange(before);
    }

    private void ApplyMarkChange(ScanMarkSnapshot? before)
    {
        if (before is { } snapshot)
        {
            _factory?.RaiseMarksChanged(snapshot);
        }

        RefreshMarkRecursive();

        for (var parent = _parent; parent is not null; parent = parent._parent)
        {
            parent.RefreshOwnMarks();
        }
    }

    private void RefreshMarkRecursive()
    {
        RefreshOwnMarks();

        foreach (var child in RealizedChildren)
        {
            child.RefreshMarkRecursive();
        }
    }
}
