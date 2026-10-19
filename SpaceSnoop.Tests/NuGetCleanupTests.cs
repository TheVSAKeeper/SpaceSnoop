using SpaceSnoop.Core;
using SpaceSnoop.Core.Cleanup;

namespace SpaceSnoop.Tests;

[TestFixture]
public class NuGetCleanupTests
{
    private string _root = null!;
    private Dictionary<NuGetCacheKind, string> _paths = null!;
    private string _context = null!;
    private NuGetCleanupService _service = null!;

    [SetUp]
    public void SetUp()
    {
        var repository = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (repository is not null && !Directory.Exists(Path.Combine(repository.FullName, ".git")))
        {
            repository = repository.Parent;
        }

        Assert.That(repository, Is.Not.Null);
        _root = Path.Combine(repository!.FullName, ".kc", "scratch", "developer-cleanup-tests", Guid.NewGuid().ToString("N"));
        _paths = Enum.GetValues<NuGetCacheKind>().Where(x => x != NuGetCacheKind.None)
            .ToDictionary(x => x, x => Path.Combine(_root, x.ToString()));
        foreach (var path in _paths.Values)
        {
            Directory.CreateDirectory(path);
        }

        _context = "test-user";
        _service = new(_ => Task.FromResult(new NuGetCacheLocations(_context, new Dictionary<NuGetCacheKind, string>(_paths))));
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }

    private string Write(string name, string content = "cache", NuGetCacheKind kind = NuGetCacheKind.HttpCache)
    {
        var path = Path.Combine(_paths[kind], name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    private Task<CleanupReport> Clean(NuGetCleanupPlan plan, NuGetCacheKind kind = NuGetCacheKind.HttpCache,
        IProgress<OperationProgress>? progress = null, CancellationToken token = default) =>
        _service.CleanAsync(plan, [kind], progress, token);

    [Test]
    public async Task Deletes_only_selected_previewed_files_and_leaves_new_files()
    {
        var selected = Write("old.dat");
        var other = Write("other.dat", kind: NuGetCacheKind.Temp);
        var plan = await _service.ScanAsync(default);
        var added = Write("new.dat");
        var result = await Clean(plan);
        Assert.Multiple(() =>
        {
            Assert.That(result.Errors, Is.Empty);
            Assert.That(result.Deleted, Is.EqualTo(1));
            Assert.That(result.FreedBytes, Is.EqualTo(5));
            Assert.That(File.Exists(selected), Is.False);
            Assert.That(File.Exists(other), Is.True);
            Assert.That(File.Exists(added), Is.True);
        });
    }

    [Test]
    public async Task Changed_file_is_skipped_and_not_counted_as_deleted()
    {
        var file = Write("cache.dat");
        var plan = await _service.ScanAsync(default);
        File.WriteAllText(file, "updated cache");
        var result = await Clean(plan);
        Assert.Multiple(() =>
        {
            Assert.That(result.Skipped, Is.EqualTo(1));
            Assert.That(result.Deleted, Is.Zero);
            Assert.That(result.FreedBytes, Is.Zero);
            Assert.That(File.Exists(file), Is.True);
        });
    }

    [Test]
    public async Task Plan_is_single_use_even_when_new_files_appear()
    {
        Write("old.dat");
        var plan = await _service.ScanAsync(default);
        await Clean(plan);
        var added = Write("new.dat");
        var result = await Clean(plan);
        Assert.That(result.Errors, Is.Not.Empty);
        Assert.That(File.Exists(added), Is.True);
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task Changed_user_or_cache_path_invalidates_entire_plan(bool changeUser)
    {
        var file = Write("cache.dat");
        var plan = await _service.ScanAsync(default);
        if (changeUser)
        {
            _context = "other-user";
        }
        else
        {
            _paths[NuGetCacheKind.Temp] += "-changed";
        }

        var result = await Clean(plan);
        Assert.That(result.Errors, Is.Not.Empty);
        Assert.That(File.Exists(file), Is.True);
    }

    [Test]
    public async Task Replaced_root_is_rejected_by_identity()
    {
        Write("cache.dat");
        var plan = await _service.ScanAsync(default);
        Directory.Move(_paths[NuGetCacheKind.HttpCache], _paths[NuGetCacheKind.HttpCache] + "-old");
        var replacement = Write("cache.dat");
        var result = await Clean(plan);
        Assert.That(result.Deleted, Is.Zero);
        Assert.That(result.Errors, Is.Not.Empty);
        Assert.That(File.Exists(replacement), Is.True);
    }

    [Test]
    public async Task Locked_file_is_not_forced_or_counted()
    {
        var file = Write("cache.dat");
        var plan = await _service.ScanAsync(default);
        using var reader = File.Open(file, FileMode.Open, FileAccess.Read, FileShare.Read);
        var result = await Clean(plan);
        Assert.That(result.Deleted, Is.Zero);
        Assert.That(result.Skipped, Is.EqualTo(1));
    }

    [Test]
    public async Task Hidden_and_system_entries_are_measured_and_cleaned()
    {
        var hidden = Write("hidden.dat");
        var nested = Write("hidden-directory/system.dat");
        File.SetAttributes(hidden, FileAttributes.Hidden);
        File.SetAttributes(nested, FileAttributes.System);
        File.SetAttributes(Path.GetDirectoryName(nested)!, FileAttributes.Hidden | FileAttributes.Directory);
        var plan = await _service.ScanAsync(default);
        Assert.That(plan.Caches.Single(x => x.Kind == NuGetCacheKind.HttpCache).Files, Is.EqualTo(2));
        var result = await Clean(plan);
        Assert.That(result.Deleted, Is.EqualTo(2));
        Assert.That(File.Exists(hidden) || File.Exists(nested), Is.False);
    }

    [Test]
    public async Task Cancellation_preserves_partial_counts()
    {
        Write("first.dat");
        Write("second.dat");
        using var cancel = new CancellationTokenSource();
        var plan = await _service.ScanAsync(default);
        var result = await Clean(plan, progress: new CancelAfterProgress(cancel), token: cancel.Token);
        Assert.Multiple(() =>
        {
            Assert.That(result.Cancelled, Is.True);
            Assert.That(result.Deleted, Is.EqualTo(1));
            Assert.That(result.FreedBytes, Is.EqualTo(5));
            Assert.That(Directory.GetFiles(_paths[NuGetCacheKind.HttpCache]).Length, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task Global_packages_require_complete_package_structure()
    {
        var file = Write("package/1.0.0/lib/a.dll", kind: NuGetCacheKind.GlobalPackages);
        var plan = await _service.ScanAsync(default);
        Assert.That(plan.Caches.Single(x => x.Kind == NuGetCacheKind.GlobalPackages).CanClean, Is.False);
        var result = await Clean(plan, NuGetCacheKind.GlobalPackages);
        Assert.That(result.Errors, Is.Not.Empty);
        Assert.That(File.Exists(file), Is.True);
    }

    [Test]
    public async Task Package_marker_is_removed_even_when_a_payload_file_cannot_be_deleted()
    {
        Write("package/1.0.0/.nupkg.metadata", kind: NuGetCacheKind.GlobalPackages);
        Write("package/1.0.0/package.nuspec", kind: NuGetCacheKind.GlobalPackages);
        Write("package/1.0.0/package.1.0.0.nupkg", kind: NuGetCacheKind.GlobalPackages);
        Write("package/1.0.0/nested/.nupkg.metadata", kind: NuGetCacheKind.GlobalPackages);
        var file = Write("package/1.0.0/lib/a.dll", kind: NuGetCacheKind.GlobalPackages);
        var plan = await _service.ScanAsync(default);
        using var reader = File.Open(file, FileMode.Open, FileAccess.Read, FileShare.Read);
        var result = await Clean(plan, NuGetCacheKind.GlobalPackages);
        Assert.Multiple(() =>
        {
            Assert.That(result.Skipped, Is.EqualTo(1));
            Assert.That(result.Deleted, Is.EqualTo(4));
            Assert.That(File.Exists(Path.Combine(_paths[NuGetCacheKind.GlobalPackages], "package/1.0.0/.nupkg.metadata")), Is.False);
            Assert.That(File.Exists(file), Is.True);
        });
    }

    [TestCase("project.csproj")]
    [TestCase("app.cs")]
    [TestCase("build.proj")]
    [TestCase("project.vcxproj")]
    public async Task Project_inside_a_cache_disables_the_category(string name)
    {
        Write("work/" + name);
        var plan = await _service.ScanAsync(default);
        Assert.That(plan.Caches.Single(x => x.Kind == NuGetCacheKind.HttpCache).CanClean, Is.False);
    }

    [Test]
    public async Task Overlapping_categories_are_not_cleanable()
    {
        Write("cache.dat");
        _paths[NuGetCacheKind.Temp] = Path.Combine(_paths[NuGetCacheKind.HttpCache], "nested");
        var plan = await _service.ScanAsync(default);
        Assert.That(plan.Caches.Where(x => x.Kind is NuGetCacheKind.HttpCache or NuGetCacheKind.Temp).All(x => !x.CanClean), Is.True);
    }

    [Test]
    public async Task Protected_installation_root_is_rejected_without_scanning()
    {
        _paths[NuGetCacheKind.HttpCache] = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var plan = await _service.ScanAsync(default);
        var cache = plan.Caches.Single(x => x.Kind == NuGetCacheKind.HttpCache);
        Assert.That(cache.Files, Is.Zero);
        Assert.That(cache.Errors, Is.Not.Empty);
    }

    [Test]
    public async Task Reparse_point_is_not_followed_during_scan_or_after_parent_replacement()
    {
        var file = Write("nested/cache.dat");
        var plan = await _service.ScanAsync(default);
        var parent = Path.GetDirectoryName(file)!;
        Directory.Move(parent, parent + "-old");
        var outside = Path.Combine(_root, "outside");
        Directory.CreateDirectory(outside);
        var protectedFile = Path.Combine(outside, "cache.dat");
        File.WriteAllText(protectedFile, "important");
        try
        {
            Directory.CreateSymbolicLink(parent, outside);
        }
        catch (UnauthorizedAccessException)
        {
            Assert.Ignore("Для создания тестовой ссылки нужны права или режим разработчика.");
        }

        try
        {
            var result = await Clean(plan);
            var next = await _service.ScanAsync(default);
            Assert.Multiple(() =>
            {
                Assert.That(result.Deleted, Is.Zero);
                Assert.That(result.Errors, Is.Not.Empty);
                Assert.That(next.Caches.Single(x => x.Kind == NuGetCacheKind.HttpCache).CanClean, Is.False);
                Assert.That(File.ReadAllText(protectedFile), Is.EqualTo("important"));
            });
        }
        finally
        {
            Directory.Delete(parent);
        }
    }

    [Test]
    public void Cli_parser_requires_all_four_distinct_absolute_paths()
    {
        var output = string.Join('\n', NuGetCacheDiscovery.Names.Select(x => $"{x.Value}: {_paths[x.Key]}"));
        Assert.That(NuGetCacheDiscovery.Parse(output, "user").Paths.Count, Is.EqualTo(4));
        Assert.Throws<InvalidOperationException>(() => NuGetCacheDiscovery.Parse("http-cache: C:\\cache", "user"));
        Assert.Throws<InvalidOperationException>(() => NuGetCacheDiscovery.Parse(output + "\n" + output, "user"));
        Assert.Throws<IOException>(() => NuGetCleanupService.Normalize("C:\\cache:file"));
        Assert.Throws<IOException>(() => NuGetCleanupService.Normalize("relative"));
        Assert.Throws<IOException>(() => NuGetCleanupService.Normalize("C:\\Program Files. \\dotnet"));
    }

    private sealed class CancelAfterProgress(CancellationTokenSource cancel) : IProgress<OperationProgress>
    {
        public void Report(OperationProgress value) => cancel.Cancel();
    }
}
