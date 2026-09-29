using KeepShell.Bootstrap;
using KeepShell.Testing;
using Microsoft.Extensions.DependencyInjection;
using SpaceSnoop.Wpf.Bootstrap;
using SpaceSnoop.Wpf.Bootstrap.Storage;
using SpaceSnoop.Wpf.ViewModels.Scan;
using System.IO;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
public class ScanTipsTests
{
    [Test]
    public void Советы_идут_по_порядку_по_одному_на_завершённый_скан()
    {
        var settings = FreshProfile();
        var tips = new ScanTipsViewModel(settings, () => false);
        var seen = new List<ScanTip?>();

        for (var scan = 0; scan <= ScanTipsViewModel.All.Count; scan++)
        {
            tips.OnScanCompleted();
            seen.Add(tips.Current);
            tips.DismissCommand.Execute(null);
        }

        using (Assert.EnterMultipleScope())
        {
            Assert.That(seen, Is.EqualTo(ScanTipsViewModel.All.Append(null)));
            Assert.That(settings.GetInt(SettingsKeys.ScanTipsShown, -1), Is.EqualTo(ScanTipsViewModel.All.Count));
        }
    }

    [Test]
    public void Пока_совет_не_закрыт_следующий_не_показывается()
    {
        var settings = FreshProfile();
        var tips = new ScanTipsViewModel(settings, () => false);

        tips.OnScanCompleted();
        tips.OnScanCompleted();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(tips.Current, Is.SameAs(ScanTipsViewModel.All[0]));
            Assert.That(tips.Counter, Is.EqualTo($"Совет 1 из {ScanTipsViewModel.All.Count}"));
            Assert.That(settings.GetInt(SettingsKeys.ScanTipsShown, -1), Is.EqualTo(1));
        }
    }

    [Test]
    public void Отметка_переживает_перезапуск_и_показанный_совет_не_повторяется()
    {
        var settings = FreshProfile();
        new ScanTipsViewModel(settings, () => false).OnScanCompleted();

        var restarted = new ScanTipsViewModel(settings, () => false);

        Assert.That(restarted.IsVisible, Is.False);

        restarted.OnScanCompleted();

        Assert.That(restarted.Current, Is.SameAs(ScanTipsViewModel.All[1]));
    }

    [Test]
    public void Совет_ждёт_закрытия_карточки_первого_запуска()
    {
        var settings = FreshProfile();
        var welcome = true;
        var tips = new ScanTipsViewModel(settings, () => welcome);

        tips.OnScanCompleted();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(tips.IsVisible, Is.False);
            Assert.That(settings.GetInt(SettingsKeys.ScanTipsShown, -1), Is.Zero);
        }

        welcome = false;
        tips.OnScanCompleted();

        Assert.That(tips.Current, Is.SameAs(ScanTipsViewModel.All[0]));
    }

    [Test]
    public void Профиль_без_отметки_советов_не_получает()
    {
        var tips = new ScanTipsViewModel(new MemorySettings(), () => false);

        tips.OnScanCompleted();

        Assert.That(tips.IsVisible, Is.False);
    }

    private static ISettingsStore FreshProfile()
    {
        ISettingsStore settings = new MemorySettings();
        settings.SetInt(SettingsKeys.ScanTipsShown, 0);

        return settings;
    }
}

[TestFixture]
[Apartment(ApartmentState.STA)]
[NonParallelizable]
public class ScanTipsPageTests
{
    private string _root = null!;
    private KeepShellLogging _logging = null!;
    private ServiceProvider _services = null!;
    private ScanViewModel _scan = null!;

    [SetUp]
    public void SetUp()
    {
        _ = TestApplication.Ensure(AppResources.Sources);

        _root = Path.Combine(Path.GetTempPath(), "spacesnoop-scan-tips", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_root, "branch"));
        File.WriteAllText(Path.Combine(_root, "branch", "file.txt"), "tip");

        ISettingsStore settings = new MemorySettings();
        settings.SetBool(SettingsKeys.UpdateCheckOnStartup, false);
        settings.SetInt(SettingsKeys.ScanTipsShown, 0);

        _logging = KeepShellLogging.Bootstrap(new()
        {
            LogsDirectory = Path.Combine(_root, "logs"),
            FileNamePrefix = AppInfo.LogFilePrefix,
        });

        _services = App.ConfigureServices(settings, _logging);
        _scan = _services.GetRequiredService<ScanViewModel>();
        _scan.SelectedDrive = _root;
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

    [Test]
    public async Task Первый_завершённый_скан_со_страницы_показывает_первый_совет()
    {
        await _scan.StartCommand.ExecuteAsync(null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(_scan.HasResult, Is.True);
            Assert.That(_scan.Tips.Current, Is.SameAs(ScanTipsViewModel.All[0]));
        }
    }

    [Test]
    public async Task Скан_при_незакрытой_карточке_первого_запуска_показывает_совет_над_результатом()
    {
        _scan.FirstRun.ShowForAutomation(false);

        await _scan.StartCommand.ExecuteAsync(null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(_scan.FirstRun.IsVisible, Is.True);
            Assert.That(_scan.ShowTargetPicker, Is.False);
            Assert.That(_scan.Tips.Current, Is.SameAs(ScanTipsViewModel.All[0]));
        }
    }

    [Test]
    public async Task Отменённый_скан_совета_не_даёт()
    {
        var run = _scan.StartCommand.ExecuteAsync(null);
        Assert.That(_scan.IsScanning, Is.True, "Скан должен идти к моменту отмены.");

        _scan.StopCommand.Execute(null);
        await run;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(_scan.ScanWasCancelled, Is.True);
            Assert.That(_scan.Tips.IsVisible, Is.False);
        }
    }

    [Test]
    public async Task Скан_агента_совета_не_даёт()
    {
        await _scan.ScanFromAutomationAsync(_root, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(_scan.HasResult, Is.True);
            Assert.That(_scan.Tips.IsVisible, Is.False);
        }
    }
}
