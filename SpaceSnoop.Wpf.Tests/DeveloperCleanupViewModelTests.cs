using Microsoft.Extensions.Logging.Abstractions;
using SpaceSnoop.Core.Cleanup;
using KeepShell.Services;
using KeepShell.Services.Modal;
using KeepShell.Services.Platform;
using SpaceSnoop.Wpf.ViewModels.Cleanup;
using SpaceSnoop.Wpf.ViewModels.Dialogs;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
public sealed class DeveloperCleanupViewModelTests
{
    private string _root = null!;
    private Dictionary<NuGetCacheKind, string> _paths = null!;

    [SetUp]
    public void SetUp()
    {
        var repository = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (repository is not null && !Directory.Exists(Path.Combine(repository.FullName, ".git")))
        {
            repository = repository.Parent;
        }

        Assert.That(repository, Is.Not.Null);
        _root = Path.Combine(repository!.FullName, ".kc", "scratch", "developer-cleanup-vm-tests", Guid.NewGuid().ToString("N"));
        _paths = Enum.GetValues<NuGetCacheKind>().Where(x => x != NuGetCacheKind.None)
            .ToDictionary(x => x, x => Path.Combine(_root, x.ToString()));
        foreach (var (kind, path) in _paths)
        {
            Directory.CreateDirectory(path);
            if (kind != NuGetCacheKind.GlobalPackages)
            {
                File.WriteAllText(Path.Combine(path, "cache.dat"), "cache");
            }
        }

        var package = Path.Combine(_paths[NuGetCacheKind.GlobalPackages], "package", "1.0.0");
        Directory.CreateDirectory(package);
        foreach (var name in new[] { ".nupkg.metadata", "package.nuspec", "package.1.0.0.nupkg" })
        {
            File.WriteAllText(Path.Combine(package, name), "package");
        }
    }

    [TearDown]
    public void TearDown() => Directory.Delete(_root, true);

    private DeveloperCleanupViewModel Create(bool confirm = false) => new(
        new NuGetCleanupService(_ => Task.FromResult(new NuGetCacheLocations("test-user", _paths))),
        new NoopDialogs(showResult: confirm), new FakeUiDispatcher(), NullLogger<DeveloperCleanupViewModel>.Instance);

    [Test]
    public async Task После_замера_выбраны_только_HTTP_и_plugins()
    {
        var vm = Create();
        await vm.RefreshCommand.ExecuteAsync(null);
        Assert.That(vm.Caches.Where(x => x.IsSelected).Select(x => x.Kind),
            Is.EquivalentTo(new[] { NuGetCacheKind.HttpCache, NuGetCacheKind.PluginsCache }));
        Assert.That(vm.CanClean, Is.True);
    }

    [Test]
    public async Task Отказ_от_подтверждения_сохраняет_файлы_и_план()
    {
        var vm = Create();
        await vm.RefreshCommand.ExecuteAsync(null);
        await vm.CleanCommand.ExecuteAsync(null);
        Assert.That(File.Exists(Path.Combine(_paths[NuGetCacheKind.HttpCache], "cache.dat")), Is.True);
        Assert.That(vm.CanClean, Is.True);
        Assert.That(vm.ResultText, Is.Null);
    }

    [Test]
    public async Task Снятие_и_рекомендуемый_выбор_обновляют_итог_без_выбора_пакетов()
    {
        var vm = Create();
        await vm.RefreshCommand.ExecuteAsync(null);
        vm.Caches.Single(x => x.Kind == NuGetCacheKind.GlobalPackages).IsSelected = true;
        vm.ClearSelectionCommand.Execute(null);
        Assert.Multiple(() =>
        {
            Assert.That(vm.HasSelectedBytes, Is.False);
            Assert.That(vm.CanClean, Is.False);
            Assert.That(vm.SelectionText, Is.EqualTo("Категорий: 0 из 4"));
        });
        vm.SelectRecommendedCommand.Execute(null);
        Assert.Multiple(() =>
        {
            Assert.That(vm.Caches.Where(x => x.IsSelected).Select(x => x.Kind),
                Is.EquivalentTo(new[] { NuGetCacheKind.HttpCache, NuGetCacheKind.PluginsCache }));
            Assert.That(vm.HasSelectedBytes, Is.True);
            Assert.That(vm.SelectionText, Is.EqualTo("Категорий: 2 из 4"));
            Assert.That(vm.CanClean, Is.True);
        });
    }

    [Test]
    public async Task Подтверждённая_очистка_не_удаляет_невыбранные_категории()
    {
        var vm = Create(confirm: true);
        await vm.RefreshCommand.ExecuteAsync(null);
        await vm.CleanCommand.ExecuteAsync(null);
        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(Path.Combine(_paths[NuGetCacheKind.HttpCache], "cache.dat")), Is.False);
            Assert.That(File.Exists(Path.Combine(_paths[NuGetCacheKind.PluginsCache], "cache.dat")), Is.False);
            Assert.That(File.Exists(Path.Combine(_paths[NuGetCacheKind.Temp], "cache.dat")), Is.True);
            Assert.That(Directory.GetFiles(_paths[NuGetCacheKind.GlobalPackages], "*", SearchOption.AllDirectories).Length, Is.EqualTo(3));
            Assert.That(vm.ResultText, Does.Contain("Удалено файлов: 2"));
            Assert.That(vm.IsBusy, Is.False);
        });
    }

    [Test]
    public async Task Ошибка_определения_путей_не_оставляет_исполнимый_план()
    {
        var vm = new DeveloperCleanupViewModel(new NuGetCleanupService(_ => throw new InvalidOperationException("SDK недоступен")),
            new NoopDialogs(), new FakeUiDispatcher(), NullLogger<DeveloperCleanupViewModel>.Instance);
        await vm.RefreshCommand.ExecuteAsync(null);
        Assert.That(vm.CanClean, Is.False);
        Assert.That(vm.IsBusy, Is.False);
        Assert.That(vm.StatusText, Does.Contain("SDK недоступен"));
    }

    [Test]
    public async Task Ошибка_повторного_замера_не_теряет_результат_удаления()
    {
        var calls = 0;
        var service = new NuGetCleanupService(_ => ++calls == 3
            ? throw new InvalidOperationException("повторный замер недоступен")
            : Task.FromResult(new NuGetCacheLocations("user", _paths)));
        var vm = new DeveloperCleanupViewModel(service, new NoopDialogs(showResult: true), new FakeUiDispatcher(), NullLogger<DeveloperCleanupViewModel>.Instance);
        await vm.RefreshCommand.ExecuteAsync(null);
        await vm.CleanCommand.ExecuteAsync(null);
        Assert.That(vm.ResultText, Does.Contain("Удалено файлов: 2"));
        Assert.That(vm.StatusText, Does.Contain("повторный замер недоступен"));
        Assert.That(vm.CanClean, Is.False);
    }

    [Test]
    public async Task Подтверждение_содержит_безвозвратность_удаления()
    {
        ConfirmDialogViewModel? confirmation = null;
        var dialogs = new NoopDialogs(show: model =>
        {
            confirmation = model as ConfirmDialogViewModel;
            return false;
        });
        var vm = new DeveloperCleanupViewModel(new NuGetCleanupService(_ => Task.FromResult(new NuGetCacheLocations("user", _paths))),
            dialogs, new FakeUiDispatcher(), NullLogger<DeveloperCleanupViewModel>.Instance);
        await vm.RefreshCommand.ExecuteAsync(null);
        await vm.CleanCommand.ExecuteAsync(null);
        Assert.That(confirmation, Is.Not.Null);
        Assert.That(confirmation!.Warning, Does.Contain("безвозвратно"));
    }

    [Test]
    public async Task Обновление_из_шапки_не_заменяет_план_во_время_подтверждения()
    {
        var dialogs = new PausedDialogs();
        var vm = new DeveloperCleanupViewModel(new NuGetCleanupService(_ => Task.FromResult(new NuGetCacheLocations("user", _paths))),
            dialogs, new FakeUiDispatcher(), NullLogger<DeveloperCleanupViewModel>.Instance);
        await vm.RefreshCommand.ExecuteAsync(null);
        var rows = vm.Caches.ToArray();
        foreach (var row in rows)
        {
            row.IsSelected = true;
        }

        var cleanup = vm.CleanCommand.ExecuteAsync(null);
        try
        {
            await vm.RefreshCommand.ExecuteAsync(null);
            vm.ClearSelectionCommand.Execute(null);
            vm.SelectRecommendedCommand.Execute(null);
            Assert.Multiple(() =>
            {
                Assert.That(vm.IsBusy, Is.True);
                Assert.That(vm.Caches, Is.EqualTo(rows));
                Assert.That(vm.Caches.All(x => x.IsSelected), Is.True);
                Assert.That(vm.ClearSelectionCommand.CanExecute(null), Is.False);
                Assert.That(vm.SelectRecommendedCommand.CanExecute(null), Is.False);
            });
        }
        finally
        {
            dialogs.Confirmation.SetResult(false);
            await cleanup;
        }
    }

    private sealed class PausedDialogs : IDialogService
    {
        internal TaskCompletionSource<bool> Confirmation { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<bool> ShowAsync(IDialogViewModel viewModel) => Confirmation.Task;
        public Task<bool> ReplaceAsync(IDialogViewModel viewModel) => throw new NotSupportedException();
        public bool Confirm(string title, string message, bool defaultYes = false) => throw new NotSupportedException();
        public bool ConfirmWarning(string title, string message, bool defaultYes = false) => throw new NotSupportedException();
        public void Info(string title, string message) => throw new NotSupportedException();
        public void Warning(string title, string message) => throw new NotSupportedException();
        public void Error(string title, string message) => throw new NotSupportedException();
    }

    [Test]
    public async Task Фоновый_вызов_меняет_коллекцию_только_через_диспетчер()
    {
        var dispatcher = new TrackingDispatcher();
        var vm = new DeveloperCleanupViewModel(new NuGetCleanupService(_ => Task.FromResult(new NuGetCacheLocations("user", _paths))),
            new NoopDialogs(), dispatcher, NullLogger<DeveloperCleanupViewModel>.Instance);
        var changedOutsideDispatcher = false;
        vm.Caches.CollectionChanged += (_, _) => changedOutsideDispatcher |= !dispatcher.InsideInvoke;
        await Task.Run(() => vm.RefreshCommand.ExecuteAsync(null));
        Assert.That(vm.Caches.Count, Is.EqualTo(4));
        Assert.That(changedOutsideDispatcher, Is.False);
    }

    private sealed class TrackingDispatcher : IUiDispatcher
    {
        internal bool InsideInvoke { get; private set; }
        public bool HasAccess => InsideInvoke;
        public void Invoke(Action action)
        {
            var previous = InsideInvoke;
            InsideInvoke = true;
            try
            {
                action();
            }
            finally
            {
                InsideInvoke = previous;
            }
        }

        public IUiTimer CreateTimer(TimeSpan interval, Action tick) => throw new NotSupportedException();
    }
}
