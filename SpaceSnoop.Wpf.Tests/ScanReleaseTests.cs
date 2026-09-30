using KeepShell.Bootstrap;
using KeepShell.Testing;
using KeepShell.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SpaceSnoop.Core.Domain;
using SpaceSnoop.Wpf.Bootstrap;
using SpaceSnoop.Wpf.Bootstrap.Storage;
using SpaceSnoop.Wpf.ViewModels.Scan;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
[Apartment(ApartmentState.STA)]
[NonParallelizable]
public class ScanReleaseTests
{
    private string _root = null!;
    private KeepShellLogging _logging = null!;
    private ServiceProvider _services = null!;
    private ScanViewModel _scan = null!;

    [SetUp]
    public void SetUp()
    {
        _ = TestApplication.Ensure(AppResources.Sources);

        _root = Path.Combine(Path.GetTempPath(), "spacesnoop-scan-release", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_root, "target"));
        Directory.CreateDirectory(Path.Combine(_root, "other"));

        ISettingsStore settings = new MemorySettings();
        settings.SetBool(SettingsKeys.UpdateCheckOnStartup, false);

        _logging = KeepShellLogging.Bootstrap(new()
        {
            LogsDirectory = Path.Combine(_root, "logs"),
            FileNamePrefix = AppInfo.LogFilePrefix,
        });

        _services = App.ConfigureServices(settings, _logging);
        _scan = _services.GetRequiredService<ScanViewModel>();
    }

    [TearDown]
    public void TearDown()
    {
        _services?.Dispose();
        _logging?.Dispose();

        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, true);
            }
        }
        catch (IOException exception)
        {
            TestContext.Out.WriteLine($"Не удалось удалить каталог стенда {_root}: {exception.Message}");
        }
    }

    [TestCase(true, false, false, TestName = "Включённая настройка снимает прежний результат папки до скана")]
    [TestCase(true, true, true, TestName = "Прежний результат с пометками остаётся при включённой настройке")]
    [TestCase(false, false, true, TestName = "Выключенная настройка оставляет прежний результат до конца скана")]
    [TestCase(false, true, true, TestName = "Пометки переживают отменённый скан при выключенной настройке")]
    public async Task Отменённый_повторный_скан(bool release, bool marked, bool kept)
    {
        var other = Arrange("other");
        var target = Arrange("target");
        var branch = target.SubDirectories.Single();
        _scan.Preferences.ReleaseBeforeRescan = release;

        if (marked)
        {
            _scan.MarkForAutomation([branch], true);
        }

        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await _scan.ScanFromAutomationAsync(target.AbsolutePath, cancelled.Token);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(_scan.ScanWasCancelled, Is.True);
            Assert.That(_scan.Roots.Any(root => ReferenceEquals(root.Space, other)), Is.True);
            Assert.That(_scan.Roots.Any(root => ReferenceEquals(root.Space, target)), Is.EqualTo(kept));
            Assert.That(_scan.HasResult, Is.EqualTo(kept));
            Assert.That(branch.IsDeleted, Is.EqualTo(marked));
            Assert.That(_scan.Marks.MarkedCount, Is.EqualTo(marked ? 1 : 0));
        }
    }

    [TestCase(true, TestName = "Повторный скан переносит пометки на новое дерево и отбрасывает пропавшие пути при включённой настройке")]
    [TestCase(false, TestName = "Повторный скан переносит пометки на новое дерево и отбрасывает пропавшие пути при выключенной настройке")]
    public async Task Повторный_скан_переносит_пометки(bool release)
    {
        var target = Path.Combine(_root, "target");
        Directory.CreateDirectory(Path.Combine(target, "branch", "leaf"));
        await File.WriteAllTextAsync(Path.Combine(target, "keep.bin"), "keep");
        await File.WriteAllTextAsync(Path.Combine(target, "gone.bin"), "gone");
        await File.WriteAllTextAsync(Path.Combine(target, "plain.bin"), "plain");
        _scan.Preferences.ReleaseBeforeRescan = release;
        var toasts = _services.GetRequiredService<ToastHostViewModel>();

        await SettleDriveLabelsAsync();
        _scan.SelectPathForAutomation(target);
        await SettleDriveLabelsAsync();

        await _scan.ScanFromAutomationAsync(target, CancellationToken.None);
        await SettleDriveLabelsAsync();
        var previous = (DirectorySpace)_scan.Roots.Single().Space!;
        _scan.MarkForAutomation([Child(previous, "branch"), Child(previous, "keep.bin"), Child(previous, "gone.bin")], true);
        File.Delete(Path.Combine(target, "gone.bin"));

        await _scan.ScanFromAutomationAsync(target, CancellationToken.None);
        await SettleDriveLabelsAsync();
        var fresh = (DirectorySpace)_scan.Roots.Single().Space!;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(fresh, Is.Not.SameAs(previous));
            Assert.That(Child(fresh, "branch").IsDeleted, Is.True);
            Assert.That(Child((DirectorySpace)Child(fresh, "branch"), "leaf").IsDeleted, Is.True);
            Assert.That(Child(fresh, "keep.bin").IsDeleted, Is.True);
            Assert.That(Child(fresh, "plain.bin").IsDeleted, Is.False);
            Assert.That(_scan.Marks.MarkedCount, Is.EqualTo(2));
            Assert.That(toasts.Toasts.Select(static toast => toast.Message), Has.Some.StartsWith("Не перенесено пометок на удаление: 1"));
        }
    }

    [Test]
    public void Повторный_скан_без_пометок_не_ищет_их_в_прежнем_дереве()
    {
        var previous = Tree(Path.Combine(_root, "target"), "branch");
        _scan.ApplyScanResult(previous, TimeSpan.FromSeconds(1), null, ScanNotes.ForTraversal(1));
        previous.SubDirectories.Single().Delete();
        var fresh = Tree(Path.Combine(_root, "target"), "branch");

        var result = ScanTreeEditor.TransferMarks(_scan.Roots, fresh, marksPresent: false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.EqualTo((0, 0)));
            Assert.That(fresh.SubDirectories.Single().IsDeleted, Is.False);
        }
    }

    private async Task SettleDriveLabelsAsync()
    {
        await Task.WhenAll(_scan.Drives.Items.Select(static drive => drive.LoadAsync()));
    }

    [TestCase(false, true, TestName = "Пометка переходит на узел, переименованный только регистром, в обычном каталоге")]
    [TestCase(true, false, TestName = "Пометка не переходит на узел, переименованный только регистром, в регистрозависимом каталоге")]
    public void Пометка_при_смене_регистра_имени(bool caseSensitive, bool transferred)
    {
        var target = Path.Combine(_root, "target");
        PrepareCase(target, caseSensitive);
        Directory.CreateDirectory(Path.Combine(target, "branch"));

        var previous = Tree(target, "Branch");
        _scan.ApplyScanResult(previous, TimeSpan.FromSeconds(1), null, ScanNotes.ForTraversal(1));
        _scan.MarkForAutomation([previous.SubDirectories.Single()], true);

        var fresh = Tree(target, "branch");
        _scan.ApplyScanResult(fresh, TimeSpan.FromSeconds(1), null, ScanNotes.ForTraversal(1));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(fresh.SubDirectories.Single().IsDeleted, Is.EqualTo(transferred));
            Assert.That(_scan.Marks.MarkedCount, Is.EqualTo(transferred ? 1 : 0));
        }
    }

    [TestCase(false, true, TestName = "Повторный скан папки с другим регистром имени снимает прежний корень в обычном каталоге")]
    [TestCase(true, false, TestName = "Повторный скан папки с другим регистром имени не трогает соседний корень в регистрозависимом каталоге")]
    public void Корень_с_другим_регистром_имени(bool caseSensitive, bool sameRoot)
    {
        var parent = Path.Combine(_root, "cased");
        PrepareCase(parent, caseSensitive);
        Directory.CreateDirectory(Path.Combine(parent, "Foo"));

        if (caseSensitive)
        {
            Directory.CreateDirectory(Path.Combine(parent, "foo"));
        }

        var upper = Tree(Path.Combine(parent, "Foo"), "branch");
        _scan.ApplyScanResult(upper, TimeSpan.FromSeconds(1), null, ScanNotes.ForTraversal(1));
        var lowerPath = Path.Combine(parent, "foo");

        var releasable = ScanTreeEditor.HasReleasableRoot(_scan.Roots, lowerPath, marksPresent: false);
        _scan.ApplyScanResult(Tree(lowerPath, "branch"), TimeSpan.FromSeconds(1), null, ScanNotes.ForTraversal(1));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(releasable, Is.EqualTo(sameRoot));
            Assert.That(_scan.Roots.Any(root => ReferenceEquals(root.Space, upper)), Is.EqualTo(!sameRoot));
        }
    }

    [TestCase(false, true, TestName = "Поиск узла и список целей сливают имена, различающиеся регистром, в обычном каталоге")]
    [TestCase(true, false, TestName = "Поиск узла и список целей различают имена, различающиеся регистром, в регистрозависимом каталоге")]
    public void Регистр_в_поиске_узла_и_списке_целей(bool caseSensitive, bool merged)
    {
        var parent = Path.Combine(_root, "cased");
        PrepareCase(parent, caseSensitive);
        Directory.CreateDirectory(Path.Combine(parent, "Foo"));
        var lowerPath = Path.Combine(parent, "foo");
        var root = Tree(parent, "Foo");

        var catalog = new DriveCatalog(NullLogger.Instance);
        catalog.AddDrive(Path.Combine(parent, "Foo"));
        catalog.AddDrive(lowerPath);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(ScanLookup.Find([root], lowerPath) is not null, Is.EqualTo(merged));
            Assert.That(catalog.RecentDirectories, Has.Count.EqualTo(merged ? 1 : 2));
        }
    }

    private static SpaceBase Child(DirectorySpace dir, string name)
    {
        return dir.SubDirectories.Cast<SpaceBase>().Concat(dir.Files).Single(child => child.Name == name);
    }

    private static DirectorySpace Tree(string path, string branch)
    {
        var space = new DirectorySpace(path, null, DateTime.Now, DateTime.Now);
        space.Add(new(branch, space, DateTime.Now, DateTime.Now));

        return space;
    }

    private static void PrepareCase(string directory, bool caseSensitive)
    {
        Directory.CreateDirectory(directory);

        if (caseSensitive)
        {
            Assume.That(TrySetCaseSensitive(directory), "fsutil не включает регистрозависимость каталога на этой машине");
        }
    }

    private static bool TrySetCaseSensitive(string directory)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("fsutil", ["file", "setCaseSensitiveInfo", directory, "enable"])
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });

            if (process is null)
            {
                return false;
            }

            process.WaitForExit(10_000);

            return process.HasExited && process.ExitCode == 0;
        }
        catch (Win32Exception)
        {
            return false;
        }
    }

    private DirectorySpace Arrange(string name)
    {
        var path = Path.Combine(_root, name);
        var space = new DirectorySpace(path, null, DateTime.Now, DateTime.Now);
        space.Add(new("branch", space, DateTime.Now, DateTime.Now));

        _scan.ApplyScanResult(space, TimeSpan.FromSeconds(1), null, ScanNotes.ForTraversal(1));

        return space;
    }
}
