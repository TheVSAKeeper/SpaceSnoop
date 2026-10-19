using SpaceSnoop.Core;
using SpaceSnoop.Core.Cleanup;

namespace SpaceSnoop.Tests;

[TestFixture]
public sealed class ProjectCleanupTests
{
    private string _root = null!;
    private ProjectCleanupService _service = null!;
    private List<string> _recycled = null!;

    [SetUp]
    public void SetUp()
    {
        var repository = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (repository is not null && !Directory.Exists(Path.Combine(repository.FullName, ".git")))
        {
            repository = repository.Parent;
        }
        Assert.That(repository, Is.Not.Null);
        _root = Path.Combine(repository!.FullName, ".kc", "scratch", "project-cleanup-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _recycled = [];
        _service = new(path =>
        {
            var destination = path + "-recycled";
            Directory.Move(path, destination);
            _recycled.Add(destination);
        });
    }

    [TearDown]
    public void TearDown() => Directory.Delete(_root, true);

    private string Write(string relative, string content = "data")
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    private Task<CleanupReport> Clean(ProjectCleanupPlan plan, params ProjectArtifactKind[] kinds) =>
        _service.CleanAsync(plan, plan.Artifacts.Where(x => kinds.Contains(x.Kind)).Select(x => x.Path).ToArray(), null, default);

    [Test]
    public async Task Finds_only_project_outputs_and_separates_nested_results()
    {
        Write("app/app.csproj");
        Write("app/bin/Debug/app.dll");
        Write("app/bin/Debug/TestResults/test.trx");
        Write("app/obj/project.assets.json");
        Write("app/BenchmarkDotNet.Artifacts/report.md");
        Write("other/bin/important.txt");
        Write(".git/inner/app.csproj");
        Write(".git/inner/bin/important.txt");
        Write("app/publish/app.dll");
        var plan = await _service.ScanAsync(_root, default);
        Assert.Multiple(() =>
        {
            Assert.That(plan.Errors, Is.Empty);
            Assert.That(plan.Artifacts.Select(x => x.Kind), Is.EquivalentTo(new[]
            {
                ProjectArtifactKind.Bin, ProjectArtifactKind.Obj, ProjectArtifactKind.TestResults, ProjectArtifactKind.Benchmarks,
            }));
            Assert.That(plan.Artifacts.Single(x => x.Kind == ProjectArtifactKind.Bin).Files, Is.EqualTo(1));
            Assert.That(plan.Artifacts.All(x => x.CanClean), Is.True);
        });
    }

    [Test]
    public async Task Recycles_only_selected_previewed_unchanged_files()
    {
        Write("app/app.csproj");
        var old = Write("app/bin/old.dll");
        var changed = Write("app/bin/changed.dll");
        var other = Write("app/obj/data.cache");
        var plan = await _service.ScanAsync(_root, default);
        var added = Write("app/bin/new.dll");
        File.WriteAllText(changed, "changed after preview");
        var result = await Clean(plan, ProjectArtifactKind.Bin);
        Assert.Multiple(() =>
        {
            Assert.That(result.Errors, Is.Empty);
            Assert.That(result.Deleted, Is.EqualTo(1));
            Assert.That(result.FreedBytes, Is.EqualTo(4));
            Assert.That(result.Skipped, Is.EqualTo(1));
            Assert.That(File.Exists(old), Is.False);
            Assert.That(File.Exists(changed) && File.Exists(added) && File.Exists(other), Is.True);
            Assert.That(_recycled, Has.Count.EqualTo(1));
        });
        Assert.That(File.ReadAllText(Path.Combine(_recycled.Single(), "files", "old.dll")), Is.EqualTo("data"));
        Assert.That(File.Exists(Path.Combine(_recycled.Single(), "SpaceSnoop-restore.json")), Is.True);
    }

    [Test]
    public async Task Failed_recycle_restores_files_without_overwriting_new_files()
    {
        Write("app/app.csproj");
        var source = Write("app/bin/app.dll");
        _service = new(_ =>
        {
            File.WriteAllText(source, "new build");
            throw new IOException("Recycle Bin unavailable");
        });
        var plan = await _service.ScanAsync(_root, default);
        var result = await Clean(plan, ProjectArtifactKind.Bin);
        Assert.Multiple(() =>
        {
            Assert.That(result.Deleted, Is.Zero);
            Assert.That(result.Errors.Any(x => x.Contains("восстановления")), Is.True);
            Assert.That(File.ReadAllText(source), Is.EqualTo("new build"));
            Assert.That(Directory.GetFiles(_root, "app.dll", SearchOption.AllDirectories)
                .Any(x => File.ReadAllText(x) == "data"), Is.True);
        });
    }

    [Test]
    public async Task Cancel_during_staging_restores_originals()
    {
        Write("app/app.csproj");
        var first = Write("app/bin/first.dll");
        var second = Write("app/bin/second.dll");
        var plan = await _service.ScanAsync(_root, default);
        using var cancellation = new CancellationTokenSource();
        var result = await _service.CleanAsync(plan, [plan.Artifacts.Single().Path],
            new CancelProgress(cancellation), cancellation.Token);
        Assert.Multiple(() =>
        {
            Assert.That(result.Cancelled, Is.True);
            Assert.That(result.Deleted, Is.Zero);
            Assert.That(File.Exists(first) && File.Exists(second), Is.True);
            Assert.That(_recycled, Is.Empty);
        });
    }

    [Test]
    public async Task Foreign_used_and_forged_targets_are_rejected()
    {
        Write("app/app.csproj");
        var file = Write("app/bin/app.dll");
        var plan = await _service.ScanAsync(_root, default);
        var foreign = await new ProjectCleanupService(_ => Assert.Fail("foreign recycle")).CleanAsync(plan,
            [plan.Artifacts.Single().Path], null, default);
        var forged = await _service.CleanAsync(await _service.ScanAsync(_root, default), [Path.GetDirectoryName(file)! + "-other"], null, default);
        await _service.CleanAsync(plan, ["invalid"], null, default);
        var used = await Clean(plan, ProjectArtifactKind.Bin);
        Assert.Multiple(() =>
        {
            Assert.That(foreign.Errors, Is.Not.Empty);
            Assert.That(forged.Errors, Is.Not.Empty);
            Assert.That(used.Errors, Is.Not.Empty);
            Assert.That(File.Exists(file), Is.True);
        });
    }

    [Test]
    public async Task Link_in_output_disables_category_and_root_links_are_rejected()
    {
        Write("app/app.csproj");
        Write("app/bin/app.dll");
        var outside = Write("outside/important.txt");
        var link = Path.Combine(_root, "app", "bin", "linked");
        Directory.CreateSymbolicLink(link, Path.GetDirectoryName(outside)!);
        try
        {
            var plan = await _service.ScanAsync(_root, default);
            Assert.That(plan.Artifacts.Single().CanClean, Is.False);
            Assert.ThrowsAsync<IOException>(() => _service.ScanAsync(link, default));
            Assert.That(File.ReadAllText(outside), Is.EqualTo("data"));
        }
        finally
        {
            Directory.Delete(link);
        }
    }

    [Test]
    public async Task Replaced_output_directory_is_not_recycled()
    {
        Write("app/app.csproj");
        var file = Write("app/bin/app.dll");
        var plan = await _service.ScanAsync(_root, default);
        Directory.Move(Path.GetDirectoryName(file)!, Path.Combine(_root, "saved-bin"));
        Write("app/bin/app.dll", "replacement");
        var result = await Clean(plan, ProjectArtifactKind.Bin);
        Assert.Multiple(() =>
        {
            Assert.That(result.Errors, Is.Not.Empty);
            Assert.That(result.Deleted, Is.Zero);
            Assert.That(File.ReadAllText(file), Is.EqualTo("replacement"));
        });
    }

    [Test]
    public void Volume_and_home_roots_are_rejected()
    {
        Assert.ThrowsAsync<IOException>(() => _service.ScanAsync(Path.GetPathRoot(_root)!, default));
        Assert.ThrowsAsync<IOException>(() => _service.ScanAsync(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), default));
    }

    [Test]
    public async Task Native_recycler_moves_own_fixture_to_Windows_Recycle_Bin()
    {
        Write("app/app.csproj");
        var file = Write("app/bin/app.dll");
        _service = new ProjectCleanupService();
        var plan = await _service.ScanAsync(_root, default);
        var result = await Clean(plan, ProjectArtifactKind.Bin);
        Assert.Multiple(() =>
        {
            Assert.That(result.Errors, Is.Empty, string.Join(Environment.NewLine, result.Errors));
            Assert.That(result.Deleted, Is.EqualTo(1));
            Assert.That(File.Exists(file), Is.False);
            Assert.That(Directory.GetDirectories(Path.GetDirectoryName(Path.GetDirectoryName(file)!)!, ".SpaceSnoop-*"), Is.Empty);
        });
    }

    [Test]
    public async Task Recovery_path_is_reported_even_after_many_file_errors()
    {
        Write("app/app.csproj");
        var blocked = Enumerable.Range(0, 50).Select(x => Write($"app/bin/locked-{x}.dll")).ToArray();
        var source = Write("app/bin/app.dll");
        _service = new(_ =>
        {
            File.WriteAllText(source, "new build");
            throw new IOException("Recycle Bin unavailable");
        });
        var plan = await _service.ScanAsync(_root, default);
        var handles = blocked.Select(x => File.Open(x, FileMode.Open, FileAccess.ReadWrite, FileShare.None)).ToArray();
        try
        {
            var result = await Clean(plan, ProjectArtifactKind.Bin);
            Assert.That(result.Errors.Any(x => x.Contains("Файлы сохранены для восстановления")), Is.True,
                "Ошибки занятых файлов скрыли путь оставшегося файла.");
            Assert.That(File.ReadAllText(source), Is.EqualTo("new build"));
        }
        finally
        {
            foreach (var handle in handles) { handle.Dispose(); }
        }
    }

    private sealed class CancelProgress(CancellationTokenSource cancellation) : IProgress<OperationProgress>
    {
        public void Report(OperationProgress value) => cancellation.Cancel();
    }
}
