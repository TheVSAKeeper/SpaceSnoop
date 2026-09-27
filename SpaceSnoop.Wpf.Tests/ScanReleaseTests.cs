using KeepShell.Bootstrap;
using KeepShell.Testing;
using Microsoft.Extensions.DependencyInjection;
using SpaceSnoop.Core.Domain;
using SpaceSnoop.Wpf.Bootstrap;
using SpaceSnoop.Wpf.Bootstrap.Storage;
using SpaceSnoop.Wpf.ViewModels.Scan;
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

    private DirectorySpace Arrange(string name)
    {
        var path = Path.Combine(_root, name);
        var space = new DirectorySpace(path, null, DateTime.Now, DateTime.Now);
        space.Add(new("branch", space, DateTime.Now, DateTime.Now));

        _scan.ApplyScanResult(space, TimeSpan.FromSeconds(1), null, ScanNotes.ForTraversal(1));

        return space;
    }
}
