using System.Collections.ObjectModel;
using System.IO;

namespace SpaceSnoop.Wpf.ViewModels.Scan;

internal static class ScanTreeEditor
{
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

    internal static void RemoveRoot(ObservableCollection<ScanNodeViewModel> roots, string path)
    {
        var normalized = NormalizePath(path);

        for (var i = roots.Count - 1; i >= 0; i--)
        {
            if (IsRootOf(roots[i], normalized))
            {
                roots.RemoveAt(i);
            }
        }
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
        return string.Equals(NormalizePath(root.AbsolutePath), normalizedPath, StringComparison.OrdinalIgnoreCase);
    }
}
