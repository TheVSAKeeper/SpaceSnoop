using System.Collections.ObjectModel;
using System.IO;

namespace SpaceSnoop.Wpf.ViewModels.Scan;

internal static class ScanTreeEditor
{
    private static readonly char[] Separators = [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar];

    internal static bool HasMarkedSelfOrChild(SpaceBase space)
    {
        if (space.IsDeleted)
        {
            return true;
        }

        if (space is not DirectorySpace dir)
        {
            return false;
        }

        return dir.SubDirectories.Cast<SpaceBase>().Concat(dir.Files).Any(HasMarkedSelfOrChild);
    }

    internal static List<SpaceBase> CollectMarked(IEnumerable<ScanNodeViewModel> roots, ScanNodeViewModel? unmarkedRoot = null)
    {
        var list = new List<SpaceBase>();

        foreach (var root in roots)
        {
            if (ReferenceEquals(root, unmarkedRoot) || root.Space is not DirectorySpace dir)
            {
                continue;
            }

            if (dir.IsDeleted)
            {
                list.Add(dir);
            }
            else
            {
                CollectMarked(dir, list);
            }
        }

        return list;
    }

    internal static void CollectMarked(DirectorySpace dir, List<SpaceBase> list)
    {
        foreach (var sub in dir.SubDirectories)
        {
            if (sub.IsDeleted)
            {
                list.Add(sub);
            }
            else
            {
                CollectMarked(sub, list);
            }
        }

        foreach (var file in dir.Files)
        {
            if (file.IsDeleted)
            {
                list.Add(file);
            }
        }
    }

    internal static void RefreshNodeAfterDeletion(ScanNodeViewModel node, HashSet<SpaceBase> deletedSet)
    {
        if (node.Space is null)
        {
            return;
        }

        var hasDeletedChild = node.Children.Any(c => c.Space is not null && deletedSet.Contains(c.Space));

        if (hasDeletedChild)
        {
            node.ReloadChildren();
            node.NotifyPropertiesChanged();
            return;
        }

        foreach (var child in node.Children)
        {
            RefreshNodeAfterDeletion(child, deletedSet);
        }

        node.NotifyPropertiesChanged();
    }

    internal static bool RefreshNodeAfterAddition(ScanNodeViewModel node, DirectorySpace parent)
    {
        if (ReferenceEquals(node.Space, parent))
        {
            node.ReloadChildren();
            node.NotifyPropertiesChanged();
            return true;
        }

        foreach (var child in node.Children)
        {
            if (RefreshNodeAfterAddition(child, parent))
            {
                node.NotifyPropertiesChanged();
                return true;
            }
        }

        node.NotifyPropertiesChanged();
        return false;
    }

    internal static string NormalizePath(string path)
    {
        return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    internal static bool AddArchiveToTree(ObservableCollection<ScanNodeViewModel> roots, DirectorySpace source, string archivePath)
    {
        if (source.Parent is not DirectorySpace parent || !File.Exists(archivePath))
        {
            return false;
        }

        parent.AddFile(new(archivePath));

        foreach (var root in roots)
        {
            RefreshNodeAfterAddition(root, parent);
        }

        return true;
    }

    internal static List<ScanNodeViewModel> RemoveRoot(ObservableCollection<ScanNodeViewModel> roots, string path)
    {
        var normalized = NormalizePath(path);
        var removed = new List<ScanNodeViewModel>();

        for (var i = roots.Count - 1; i >= 0; i--)
        {
            if (IsRootOf(roots[i], normalized))
            {
                removed.Add(roots[i]);
                roots.RemoveAt(i);
            }
        }

        return removed;
    }

    internal static (int Transferred, int Lost) TransferMarks(IEnumerable<ScanNodeViewModel> previousRoots, DirectorySpace fresh, bool marksPresent)
    {
        var transferred = 0;
        var lost = 0;

        if (!marksPresent)
        {
            return (transferred, lost);
        }

        var lookups = new Dictionary<DirectorySpace, ILookup<string, SpaceBase>>(ReferenceEqualityComparer.Instance);

        foreach (var root in previousRoots)
        {
            if (root.Space is not DirectorySpace previous)
            {
                continue;
            }

            var marked = new List<SpaceBase>();

            if (previous.IsDeleted)
            {
                marked.Add(previous);
            }
            else
            {
                CollectMarked(previous, marked);
            }

            foreach (var item in marked)
            {
                switch (FindCounterpart(previous, item, fresh, lookups))
                {
                    case null:
                        lost++;
                        break;

                    case { IsDeleted: false } counterpart:
                        counterpart.Delete();
                        transferred++;
                        break;
                }
            }
        }

        return (transferred, lost);
    }

    internal static bool SamePath(string left, string right)
    {
        if (string.Equals(left, right, StringComparison.Ordinal))
        {
            return true;
        }

        if (!string.Equals(left, right, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var start = Path.GetPathRoot(right)?.Length ?? 0;

        while (start < right.Length)
        {
            var end = right.IndexOfAny(Separators, start);

            if (end < 0)
            {
                end = right.Length;
            }

            if (!left.AsSpan(start, end - start).SequenceEqual(right.AsSpan(start, end - start))
                && PathCase.IsCaseSensitive(right[..start]))
            {
                return false;
            }

            start = end + 1;
        }

        return true;
    }

    private static SpaceBase? FindCounterpart(
        DirectorySpace previousRoot,
        SpaceBase item,
        DirectorySpace fresh,
        Dictionary<DirectorySpace, ILookup<string, SpaceBase>> lookups)
    {
        var steps = new Stack<SpaceBase>();

        for (var current = item; current is not null && !ReferenceEquals(current, previousRoot); current = current.Parent)
        {
            steps.Push(current);
        }

        SpaceBase match = fresh;

        while (steps.TryPop(out var step))
        {
            if (match is not DirectorySpace dir || FindChild(dir, step, lookups) is not { } child)
            {
                return null;
            }

            match = child;
        }

        return match;
    }

    private static SpaceBase? FindChild(DirectorySpace dir, SpaceBase step, Dictionary<DirectorySpace, ILookup<string, SpaceBase>> lookups)
    {
        if (!lookups.TryGetValue(dir, out var children))
        {
            children = dir.SubDirectories.Cast<SpaceBase>()
                .Concat(dir.Files)
                .ToLookup(static child => child.Name, StringComparer.OrdinalIgnoreCase);

            lookups[dir] = children;
        }

        var candidates = children[step.Name].Where(child => (child is DirectorySpace) == (step is DirectorySpace)).ToList();
        var exact = candidates.Find(child => string.Equals(child.Name, step.Name, StringComparison.Ordinal));

        if (exact is not null || candidates.Count == 0 || PathCase.IsCaseSensitive(dir.AbsolutePath))
        {
            return exact;
        }

        return candidates[0];
    }

    internal static List<ScanNodeViewModel> ReleaseUnmarkedRoot(
        ObservableCollection<ScanNodeViewModel> roots,
        string path,
        bool marksPresent,
        out bool keptMarked)
    {
        var normalized = NormalizePath(path);
        var released = new List<ScanNodeViewModel>();
        keptMarked = false;

        for (var i = roots.Count - 1; i >= 0; i--)
        {
            var root = roots[i];

            if (!IsRootOf(root, normalized))
            {
                continue;
            }

            if (KeepsMarks(root, marksPresent))
            {
                keptMarked = true;
                continue;
            }

            roots.RemoveAt(i);
            released.Add(root);
        }

        return released;
    }

    internal static bool HasReleasableRoot(IEnumerable<ScanNodeViewModel> roots, string path, bool marksPresent)
    {
        var normalized = NormalizePath(path);

        return roots.Any(root => IsRootOf(root, normalized) && !KeepsMarks(root, marksPresent));
    }

    private static bool KeepsMarks(ScanNodeViewModel root, bool marksPresent)
    {
        return marksPresent && root.Space is { } space && HasMarkedSelfOrChild(space);
    }

    private static bool IsRootOf(ScanNodeViewModel root, string normalizedPath)
    {
        return SamePath(NormalizePath(root.AbsolutePath), normalizedPath);
    }
}
