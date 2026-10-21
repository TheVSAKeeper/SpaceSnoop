namespace SpaceSnoop.Wpf.ViewModels.Scan;

public sealed class ScanVisibility
{
    private readonly HashSet<SpaceBase> _ignored = [];
    private readonly Dictionary<SpaceBase, long> _excludedBytes = [];
    private readonly Dictionary<SpaceBase, int> _excludedFiles = [];
    private readonly Dictionary<SpaceBase, int> _excludedDirectories = [];

    public int Count => _ignored.Count;

    internal IReadOnlySet<SpaceBase> AffectedSpaces() => _excludedBytes.Keys.ToHashSet();

    public bool IsIgnored(SpaceBase space)
    {
        if (_ignored.Count == 0)
        {
            return false;
        }

        for (SpaceBase? current = space; current is not null; current = current.Parent)
        {
            if (_ignored.Contains(current))
            {
                return true;
            }
        }

        return false;
    }

    public long SizeOf(SpaceBase space) => Math.Max(0, space.TotalSize - _excludedBytes.GetValueOrDefault(space));

    public long OwnSizeOf(DirectorySpace space) => Math.Max(0, space.Size - _ignored
        .Where(item => item is FileSpace && ReferenceEquals(item.Parent, space))
        .Sum(static item => item.TotalSize));

    public int FileCountOf(DirectorySpace space) => Math.Max(0, space.TotalFileCount - _excludedFiles.GetValueOrDefault(space));

    public int DirectoryCountOf(DirectorySpace space) => Math.Max(0, space.TotalDirectoryCount - _excludedDirectories.GetValueOrDefault(space));

    internal bool Ignore(SpaceBase space)
    {
        if (IsIgnored(space))
        {
            return false;
        }

        _ignored.RemoveWhere(item => IsWithin(item, space));
        _ignored.Add(space);
        Rebuild();
        return true;
    }

    internal SpaceBase? IgnoredAncestor(SpaceBase space) => Ancestors(space).FirstOrDefault(_ignored.Contains);

    internal SpaceBase? Restore(SpaceBase space)
    {
        var marked = IgnoredAncestor(space);

        if (marked is not null)
        {
            _ignored.Remove(marked);
            Rebuild();
        }

        return marked;
    }

    internal void Clear()
    {
        _ignored.Clear();
        _excludedBytes.Clear();
        _excludedFiles.Clear();
        _excludedDirectories.Clear();
    }

    internal void RetainRoots(IEnumerable<ScanNodeViewModel> roots)
    {
        if (_ignored.Count == 0)
        {
            return;
        }

        var spaces = roots.Select(static root => root.Space).OfType<SpaceBase>().ToHashSet();
        _ignored.RemoveWhere(item => !Ancestors(item).Any(spaces.Contains));
        Rebuild();
    }

    internal void RemoveDeleted(IReadOnlySet<SpaceBase> deleted)
    {
        _ignored.RemoveWhere(item => Ancestors(item).Any(deleted.Contains));
        Rebuild();
    }

    private void Rebuild()
    {
        _excludedBytes.Clear();
        _excludedFiles.Clear();
        _excludedDirectories.Clear();

        foreach (var item in _ignored)
        {
            var files = item is DirectorySpace dir ? dir.TotalFileCount : 1;
            var directories = item is DirectorySpace directory ? directory.TotalDirectoryCount : 0;

            foreach (var ancestor in Ancestors(item))
            {
                _excludedBytes[ancestor] = _excludedBytes.GetValueOrDefault(ancestor) + item.TotalSize;
                _excludedFiles[ancestor] = _excludedFiles.GetValueOrDefault(ancestor) + files;
                _excludedDirectories[ancestor] = _excludedDirectories.GetValueOrDefault(ancestor)
                    + directories;

                if (item is DirectorySpace && !ReferenceEquals(item, ancestor))
                {
                    _excludedDirectories[ancestor]++;
                }
            }
        }
    }

    private static bool IsWithin(SpaceBase item, SpaceBase parent) => Ancestors(item).Contains(parent);

    private static IEnumerable<SpaceBase> Ancestors(SpaceBase item)
    {
        for (SpaceBase? current = item; current is not null; current = current.Parent)
        {
            yield return current;
        }
    }
}
