using System.Runtime.InteropServices;
using System.Security;
using System.Text.Json;

namespace SpaceSnoop.Core.Cleanup;

public sealed class ProjectCleanupService
{
    private readonly Guid _owner = Guid.NewGuid();
    private readonly Action<string> _recycle;
    private static readonly EnumerationOptions Entries = new() { AttributesToSkip = 0, IgnoreInaccessible = false };

    public ProjectCleanupService() : this(ProjectRecycleBin.Delete)
    {
    }

    internal ProjectCleanupService(Action<string> recycle) => _recycle = recycle;

    public Task<ProjectCleanupPlan> ScanAsync(string root, CancellationToken token) => ScanAsync([root], [], token);

    public Task<ProjectCleanupPlan> ScanAsync(IReadOnlyCollection<string> roots, IReadOnlyCollection<string> customOutputs,
        CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(roots);
        ArgumentNullException.ThrowIfNull(customOutputs);
        var rootSnapshot = roots.ToArray();
        var outputSnapshot = customOutputs.ToArray();
        return Task.Run(() => Scan(rootSnapshot, outputSnapshot, token), token);
    }

    private ProjectCleanupPlan Scan(string[] rootSnapshot, string[] outputSnapshot, CancellationToken token)
    {
        var roots = CollapseRoots(rootSnapshot);
        var outputs = CollapseRoots(outputSnapshot);
        if (roots.Count == 0 && outputs.Count == 0) { throw new IOException("Добавьте хотя бы один корень проектов или output-каталог."); }
        var artifacts = new List<ProjectArtifactScan>();
        var errors = new List<string>();
        var leases = new List<CacheFileAccess.DirectoryLease>();
        try
        {
            foreach (var path in roots.Concat(outputs).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                token.ThrowIfCancellationRequested();
                leases.Add(CacheFileAccess.LockDirectory(path));
            }
            var scanOutputs = outputs.Where(output => !roots.Any(root => Inside(root, output))).ToArray();
            foreach (var output in outputs.Except(scanOutputs, StringComparer.OrdinalIgnoreCase))
            {
                AddError(errors, $"«{output}»: output-каталог содержит корень поиска. Выберите отдельный каталог результатов.");
            }
            foreach (var root in roots) { Discover(root, scanOutputs, artifacts, errors, token); }
            foreach (var output in scanOutputs)
            {
                token.ThrowIfCancellationRequested();
                if (Inside(Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory), output))
                {
                    AddError(errors, $"«{output}»: output-каталог содержит запущенное приложение.");
                    continue;
                }
                if (Excluded(Path.GetFileName(output)) || output.Split(Path.DirectorySeparatorChar)
                    .Any(segment => segment.ToLowerInvariant() is ".git" or ".hg" or ".svn"
                        || segment.StartsWith(".SpaceSnoop-", StringComparison.OrdinalIgnoreCase)))
                {
                    AddError(errors, $"«{output}»: служебный каталог не является output-каталогом.");
                    continue;
                }
                if (artifacts.Any(x => x.Kind == ProjectArtifactKind.Coverage
                    ? x.Entries.Any(file => Inside(file.Path, output))
                    : Inside(output, x.Path) || Inside(x.Path, output)))
                {
                    AddError(errors, $"«{output}»: output-каталог пересекается с уже найденной целью. Используйте отдельные строки найденных артефактов.");
                    continue;
                }
                Measure(output, ProjectArtifactKind.CustomOutput, output, artifacts, token);
            }
            return new(_owner, roots.AsReadOnly(), outputs.AsReadOnly(),
                leases.Select(x => x.Stamp).ToList().AsReadOnly(),
                artifacts.OrderBy(x => x.ProjectDirectory, StringComparer.Ordinal).ThenBy(x => x.Path, StringComparer.Ordinal).ToList().AsReadOnly(),
                errors.AsReadOnly());
        }
        finally
        {
            for (var index = leases.Count - 1; index >= 0; index--) { leases[index].Dispose(); }
        }
    }

    private static void Discover(string root, IReadOnlyCollection<string> customOutputs,
        List<ProjectArtifactScan> artifacts, List<string> errors, CancellationToken token)
    {
        var pending = new Stack<(string Path, string? Project)>();
        pending.Push((root, null));
        while (pending.TryPop(out var next))
        {
            token.ThrowIfCancellationRequested();
            var directory = next.Path;
            try
            {
                using var current = CacheFileAccess.LockDirectory(directory);
                var entries = new DirectoryInfo(directory).GetFileSystemInfos("*", Entries);
                var project = entries.Any(x => (x.Attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) == 0
                    && IsProject(x.Extension));
                var solution = entries.Any(x => (x.Attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) == 0
                    && (x.Extension.Equals(".sln", StringComparison.OrdinalIgnoreCase)
                        || x.Extension.Equals(".slnx", StringComparison.OrdinalIgnoreCase)));
                var projectDirectory = project || solution ? directory : next.Project;
                if (projectDirectory is not null)
                {
                    var coverage = entries.Where(x => (x.Attributes & FileAttributes.Directory) == 0 && IsCoverage(x.Name)).ToArray();
                    if (coverage.Length > 0) { MeasureCoverage(directory, projectDirectory, coverage, artifacts, token); }
                }
                foreach (var entry in entries.Where(x => (x.Attributes & FileAttributes.Directory) != 0))
                {
                    token.ThrowIfCancellationRequested();
                    if (entry.Name.StartsWith(".SpaceSnoop-", StringComparison.OrdinalIgnoreCase))
                    {
                        AddError(errors, $"Обнаружен каталог незавершённой очистки «{entry.FullName}». Проверьте files и SpaceSnoop-restore.json для восстановления.");
                        continue;
                    }
                    if ((entry.Attributes & FileAttributes.ReparsePoint) != 0 || Excluded(entry.Name) || Protected(entry.FullName))
                    {
                        continue;
                    }
                    if (customOutputs.Any(output => Inside(entry.FullName, output))) { continue; }

                    var kind = Kind(entry.Name);
                    if (kind != ProjectArtifactKind.None)
                    {
                        if ((project || (solution && kind is ProjectArtifactKind.TestResults or ProjectArtifactKind.Benchmarks))
                            && !Inside(Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory), entry.FullName))
                        {
                            Measure(entry.FullName, kind, projectDirectory!, artifacts, token);
                        }
                        continue;
                    }
                    pending.Push((entry.FullName, projectDirectory));
                }
            }
            catch (Exception exception) when (FileError(exception))
            {
                AddError(errors, exception.Message);
            }
        }

    }

    public Task<CleanupReport> CleanAsync(ProjectCleanupPlan plan, IReadOnlyCollection<string> selected,
        IProgress<OperationProgress>? progress, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(selected);
        if (plan.Owner != _owner || Interlocked.Exchange(ref plan.Used, 1) != 0
            || DateTime.UtcNow - plan.CreatedUtc > TimeSpan.FromMinutes(15))
        {
            return Task.FromResult(new CleanupReport(0, 0, 0, ["План устарел, уже использован или принадлежит другому сеансу. Повторите поиск."]));
        }

        var targets = plan.Artifacts.Where(x => selected.Contains(x.Id, StringComparer.Ordinal)
            || (selected.Contains(x.Path, StringComparer.Ordinal)
                && plan.Artifacts.Count(other => string.Equals(other.Path, x.Path, StringComparison.Ordinal)) == 1)).ToArray();
        if (targets.Length == 0 || targets.Length != selected.Distinct(StringComparer.Ordinal).Count()
            || targets.Any(x => !x.CanClean))
        {
            return Task.FromResult(new CleanupReport(0, 0, 0, ["Выберите доступные непустые каталоги из просмотренного плана."]));
        }

        var completion = new TaskCompletionSource<CleanupReport>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { completion.SetResult(Clean(plan, targets, progress, token)); }
            catch (Exception exception) { completion.SetException(exception); }
        }) { IsBackground = true, Name = "SpaceSnoop project cleanup" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    private CleanupReport Clean(ProjectCleanupPlan plan, ProjectArtifactScan[] targets,
        IProgress<OperationProgress>? progress, CancellationToken token)
    {
        long bytes = 0;
        var deleted = 0;
        var skipped = 0;
        var errors = new List<string>();
        var leases = new List<CacheFileAccess.DirectoryLease>();
        try
        {
            foreach (var stamp in plan.RootStamps)
            {
                ValidateRoot(stamp.Path);
                var lease = CacheFileAccess.LockDirectory(stamp.Path);
                leases.Add(lease);
                if (!SameDirectory(lease.Stamp, stamp))
                {
                    throw new IOException("Выбранный корень заменён после поиска. Повторите поиск.");
                }
            }

            foreach (var target in targets)
            {
                token.ThrowIfCancellationRequested();
                var staged = new List<StagedFile>();
                var createdDirectories = new HashSet<string>(StringComparer.Ordinal);
                CacheFileStamp? manifest = null;
                string? staging = null;
                try
                {
                    if (!plan.RootStamps.Any(root => Inside(target.Path, root.Path)) || Protected(target.Path)
                        || target.Entries.Any(file => Protected(file.Path))
                        || ContainsApplicationFiles(target, Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory)))
                    {
                        throw new IOException("Каталог находится за границами выбранной области или защищён.");
                    }
                    using var targetLease = CacheFileAccess.LockDirectory(target.Path);
                    if (!SameDirectory(targetLease.Stamp, target.Root))
                    {
                        throw new IOException($"«{target.Path}»: каталог заменён после поиска.");
                    }

                    var stagingParent = plan.RootStamps.Any(root => string.Equals(root.Path, target.Path, StringComparison.OrdinalIgnoreCase))
                        ? target.Path : Path.GetDirectoryName(target.Path)!;
                    staging = Path.Combine(stagingParent, $".SpaceSnoop-{Path.GetFileName(target.Path)}-{Guid.NewGuid():N}");
                    Directory.CreateDirectory(staging);
                    createdDirectories.Add(staging);
                    using (var stagingLease = CacheFileAccess.LockDirectory(staging))
                    {
                        File.WriteAllText(Path.Combine(staging, "SpaceSnoop-restore.json"), JsonSerializer.Serialize(new
                        {
                            OriginalDirectory = target.Path,
                            PreviewedFiles = target.Entries.Select(x => Path.GetRelativePath(target.Path, x.Path)).ToArray(),
                        }));
                        using (var handle = CacheFileAccess.OpenFile(Path.Combine(staging, "SpaceSnoop-restore.json"), false))
                        {
                            manifest = CacheFileAccess.Read(handle, Path.Combine(staging, "SpaceSnoop-restore.json"));
                        }
                        foreach (var file in target.Entries)
                        {
                            token.ThrowIfCancellationRequested();
                            progress?.Report(new OperationProgress(deleted + staged.Count, file.Path, bytes));
                            try
                            {
                                using var parent = CacheFileAccess.LockDirectory(Path.GetDirectoryName(file.Path)!);
                                using var handle = CacheFileAccess.OpenFile(file.Path, true);
                                if (CacheFileAccess.Read(handle, file.Path) != file)
                                {
                                    skipped++;
                                    continue;
                                }

                                var destination = Path.Combine(staging, "files", Path.GetRelativePath(target.Path, file.Path));
                                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                                for (var directory = Path.GetDirectoryName(destination)!; !string.Equals(directory, staging, StringComparison.Ordinal);
                                    directory = Path.GetDirectoryName(directory)!)
                                {
                                    createdDirectories.Add(directory);
                                }
                                using var destinationLease = CacheFileAccess.LockDirectory(Path.GetDirectoryName(destination)!);
                                CacheFileAccess.Move(handle, destination);
                                staged.Add(new(file, destination));
                            }
                            catch (Exception exception) when (FileError(exception))
                            {
                                skipped++;
                                AddError(errors, exception.Message);
                            }
                        }
                    }

                    token.ThrowIfCancellationRequested();
                    if (staged.Count > 0)
                    {
                        _recycle(staging);
                        deleted += staged.Count;
                        bytes += staged.Sum(x => x.Stamp.Length);
                        staged.Clear();
                        staging = null;
                    }
                }
                catch (Exception exception) when (FileError(exception) || exception is COMException)
                {
                    AddError(errors, exception.Message);
                }
                finally
                {
                    foreach (var file in staged)
                    {
                        try
                        {
                            using var parent = CacheFileAccess.LockDirectory(Path.GetDirectoryName(file.Stamp.Path)!);
                            using var stagedParent = CacheFileAccess.LockDirectory(Path.GetDirectoryName(file.Destination)!);
                            using var handle = CacheFileAccess.OpenFile(file.Destination, true);
                            if (CacheFileAccess.Read(handle, file.Destination) != file.Stamp with { Path = file.Destination })
                            {
                                throw new IOException("Файл в каталоге восстановления изменился.");
                            }
                            CacheFileAccess.Move(handle, file.Stamp.Path);
                        }
                        catch (Exception exception) when (FileError(exception))
                        {
                            AddError(errors, $"Восстановите «{file.Stamp.Path}» из «{file.Destination}»: {exception.Message}");
                        }
                    }
                    if (staging is not null)
                    {
                        try
                        {
                            var remaining = staged.Any(x => File.Exists(x.Destination));
                            if (remaining)
                            {
                                errors.Add($"Файлы сохранены для восстановления: «{staging}».");
                            }
                            else
                            {
                                if (manifest is { } stamp)
                                {
                                    using var parent = CacheFileAccess.LockDirectory(staging);
                                    using var handle = CacheFileAccess.OpenFile(stamp.Path, true);
                                    if (CacheFileAccess.Read(handle, stamp.Path) == stamp)
                                    {
                                        CacheFileAccess.Delete(handle, stamp.Path);
                                    }
                                }
                                foreach (var directory in createdDirectories.OrderByDescending(x => x.Length))
                                {
                                    using var parent = CacheFileAccess.LockDirectory(Path.GetDirectoryName(directory)!);
                                    using var handle = CacheFileAccess.OpenDirectoryForDelete(directory);
                                    CacheFileAccess.Delete(handle, directory);
                                }
                            }
                        }
                        catch (Exception exception) when (FileError(exception))
                        {
                            errors.Add($"Проверьте каталог восстановления «{staging}»: {exception.Message}");
                        }
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            return new(bytes, deleted, skipped, errors.AsReadOnly(), true);
        }
        catch (Exception exception) when (FileError(exception))
        {
            AddError(errors, exception.Message);
        }
        finally
        {
            for (var index = leases.Count - 1; index >= 0; index--) { leases[index].Dispose(); }
        }
        return new(bytes, deleted, skipped, errors.AsReadOnly());
    }

    private static void Measure(string path, ProjectArtifactKind kind, string projectDirectory,
        List<ProjectArtifactScan> artifacts, CancellationToken token)
    {
        var files = new List<CacheFileStamp>();
        var coverage = new List<CacheFileStamp>();
        var errors = new List<string>();
        CacheFileStamp root = default;
        var directories = new Stack<string>();
        directories.Push(path);
        try
        {
            using var rootLease = CacheFileAccess.LockDirectory(path);
            root = rootLease.Stamp;
            while (directories.TryPop(out var current))
            {
                token.ThrowIfCancellationRequested();
                using var lease = CacheFileAccess.LockDirectory(current);
                foreach (var entry in new DirectoryInfo(current).EnumerateFileSystemInfos("*", Entries))
                {
                    token.ThrowIfCancellationRequested();
                    if ((entry.Attributes & FileAttributes.ReparsePoint) != 0 || Excluded(entry.Name) || Protected(entry.FullName)
                        || IsProject(entry.Extension) || entry.Extension.Equals(".sln", StringComparison.OrdinalIgnoreCase)
                        || entry.Extension.Equals(".slnx", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new IOException($"«{entry.FullName}»: ссылка, защищённый путь или вложенный проект; каталог недоступен для очистки.");
                    }
                    if ((entry.Attributes & FileAttributes.Directory) != 0)
                    {
                        var nestedKind = Kind(entry.Name);
                        if (nestedKind is ProjectArtifactKind.TestResults or ProjectArtifactKind.Benchmarks)
                        {
                            Measure(entry.FullName, nestedKind, projectDirectory, artifacts, token);
                        }
                        else
                        {
                            directories.Push(entry.FullName);
                        }
                    }
                    else
                    {
                        using var handle = CacheFileAccess.OpenFile(entry.FullName, false);
                        var stamp = CacheFileAccess.Read(handle, entry.FullName);
                        if (kind != ProjectArtifactKind.TestResults && IsCoverage(entry.Name)) { coverage.Add(stamp); }
                        else { files.Add(stamp); }
                    }
                }
            }
        }
        catch (Exception exception) when (FileError(exception))
        {
            AddError(errors, exception.Message);
        }
        artifacts.Add(new(kind, path, root, files.AsReadOnly(), errors.AsReadOnly(), projectDirectory));
        if (coverage.Count > 0)
        {
            artifacts.Add(new(ProjectArtifactKind.Coverage, path, root, coverage.AsReadOnly(), errors.AsReadOnly(), projectDirectory));
        }
    }

    private static void MeasureCoverage(string path, string projectDirectory, FileSystemInfo[] entries,
        List<ProjectArtifactScan> artifacts, CancellationToken token)
    {
        using var lease = CacheFileAccess.LockDirectory(path);
        var files = new List<CacheFileStamp>();
        var errors = new List<string>();
        foreach (var entry in entries)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                using var handle = CacheFileAccess.OpenFile(entry.FullName, false);
                files.Add(CacheFileAccess.Read(handle, entry.FullName));
            }
            catch (Exception exception) when (FileError(exception)) { AddError(errors, exception.Message); }
        }
        artifacts.Add(new(ProjectArtifactKind.Coverage, path, lease.Stamp, files.AsReadOnly(), errors.AsReadOnly(), projectDirectory));
    }

    private static List<string> CollapseRoots(IEnumerable<string> paths)
    {
        var result = new List<string>();
        foreach (var path in paths.Select(Normalize).Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x.Length).ThenBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            ValidateRoot(path);
            if (!result.Any(root => Inside(path, root))) { result.Add(path); }
        }
        return result;
    }

    private static bool IsCoverage(string name) => name.Equals("lcov.info", StringComparison.OrdinalIgnoreCase)
        || name.EndsWith(".cobertura.xml", StringComparison.OrdinalIgnoreCase)
        || name.EndsWith(".opencover.xml", StringComparison.OrdinalIgnoreCase)
        || name.EndsWith(".coverage", StringComparison.OrdinalIgnoreCase)
        || (name.StartsWith("coverage", StringComparison.OrdinalIgnoreCase)
            && Path.GetExtension(name).ToLowerInvariant() is ".xml" or ".json" or ".info" or ".lcov");

    private static string Normalize(string path)
    {
        if (!Path.IsPathFullyQualified(path) || path.Length < 3 || path[1] != ':' || path[2] != '\\'
            || path.AsSpan(2).Contains(':') || path.Split('\\').Any(x => x.EndsWith(' ') || x.EndsWith('.')))
        {
            throw new IOException("Выберите локальный каталог с однозначным абсолютным путём.");
        }
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    }

    private static void ValidateRoot(string root)
    {
        if (new DirectoryInfo(root).Parent is null || Protected(root)
            || string.Equals(root, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), StringComparison.OrdinalIgnoreCase))
        {
            throw new IOException("Выберите отдельный каталог проектов, а не диск, домашний или системный каталог.");
        }
    }

    internal static bool ContainsApplicationFiles(ProjectArtifactScan target, string appDirectory) =>
        Inside(target.Path, appDirectory)
        || (target.Kind != ProjectArtifactKind.Coverage && Inside(appDirectory, target.Path))
        || target.Entries.Any(file => Inside(file.Path, appDirectory));

    private static bool Protected(string path)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Path.Combine(home, ".dotnet"), Path.Combine(home, ".nuget"),
            Environment.GetEnvironmentVariable("DOTNET_ROOT") ?? string.Empty,
            Environment.GetEnvironmentVariable("DOTNET_ROOT(x86)") ?? string.Empty,
            Path.Combine(Environment.GetEnvironmentVariable("DOTNET_CLI_HOME") ?? home, ".dotnet"),
            AppContext.BaseDirectory,
        }.Where(x => !string.IsNullOrWhiteSpace(x)).Any(x => Inside(path, Path.TrimEndingDirectorySeparator(x)));
    }

    private static bool Inside(string path, string root) => string.Equals(path, root, StringComparison.OrdinalIgnoreCase)
        || path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    private static bool SameDirectory(CacheFileStamp left, CacheFileStamp right) => left.Volume == right.Volume
        && left.Id == right.Id && left.Created == right.Created;
    private static bool IsProject(string extension) => extension.Equals(".csproj", StringComparison.OrdinalIgnoreCase)
        || extension.Equals(".fsproj", StringComparison.OrdinalIgnoreCase) || extension.Equals(".vbproj", StringComparison.OrdinalIgnoreCase);
    private static bool Excluded(string name) => name.ToLowerInvariant() is ".git" or ".hg" or ".svn" or ".kc" or ".claude" or ".codex" or "node_modules"
        || name.StartsWith(".SpaceSnoop-", StringComparison.OrdinalIgnoreCase);
    private static ProjectArtifactKind Kind(string name) => name.ToLowerInvariant() switch
    {
        "bin" => ProjectArtifactKind.Bin,
        "obj" => ProjectArtifactKind.Obj,
        "testresults" => ProjectArtifactKind.TestResults,
        "benchmarkdotnet.artifacts" => ProjectArtifactKind.Benchmarks,
        _ => ProjectArtifactKind.None,
    };
    private static bool FileError(Exception exception) => exception is IOException or UnauthorizedAccessException or SecurityException;
    private static void AddError(List<string> errors, string error)
    {
        if (errors.Count < 50) { errors.Add(error); }
    }

    private sealed record StagedFile(CacheFileStamp Stamp, string Destination);
}
