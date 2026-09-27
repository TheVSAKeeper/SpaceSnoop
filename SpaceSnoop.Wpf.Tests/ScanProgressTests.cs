using SpaceSnoop.Wpf.ViewModels.Scan;
using System.Globalization;
using System.IO;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
public class ScanProgressTests
{
    [SetUp]
    public void SetUp()
    {
        _culture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new("ru-RU");
    }

    [TearDown]
    public void TearDown()
    {
        CultureInfo.CurrentCulture = _culture;
    }

    private CultureInfo _culture = CultureInfo.CurrentCulture;

    [Test]
    public void Оценка_объёма_есть_только_у_корня_диска()
    {
        var directory = Directory.CreateTempSubdirectory("spacesnoop-estimate");

        try
        {
            var root = new DirectoryInfo(Path.GetPathRoot(directory.FullName)!);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(ScanProgressViewModel.EstimateTotalBytes(directory), Is.Null);
                Assert.That(ScanProgressViewModel.EstimateTotalBytes(root), Is.Not.Null.And.GreaterThan(0));
            }
        }
        finally
        {
            directory.Delete(true);
        }
    }

    [Test]
    public void Неготовый_диск_не_даёт_оценку()
    {
        var used = DriveInfo.GetDrives().Select(static drive => char.ToUpperInvariant(drive.Name[0])).ToHashSet();
        var free = "ZYXWVU".Cast<char?>().FirstOrDefault(letter => !used.Contains(letter!.Value));

        Assume.That(free, Is.Not.Null, "на машине нет свободной буквы диска");

        Assert.That(ScanProgressViewModel.EstimateTotalBytes(new($@"{free}:\")), Is.Null);
    }

    [Test]
    public void Строка_фазы_идёт_мимо_пути_и_не_переприсваивается_на_каждом_тике()
    {
        var dispatcher = new FakeUiDispatcher();
        var viewModel = new ScanProgressViewModel(new(TestDiagnostics.Monitor()), dispatcher);
        var timer = dispatcher.Timers.Single();
        var progress = viewModel.Begin(new(Path.GetTempPath()), @"C:\", 1);
        var stageNotifications = 0;

        viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(ScanProgressViewModel.ScanStageText))
            {
                stageNotifications++;
            }
        };

        progress.Announce("Чтение $MFT тома C: 40 %");
        timer.Tick();
        timer.Tick();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(viewModel.ScanShowsStage, Is.True);
            Assert.That(viewModel.ScanStageText, Is.EqualTo("Чтение $MFT тома C: 40 %"));
            Assert.That(viewModel.ScanCurrentPath, Is.EqualTo(@"C:\"));
            Assert.That(stageNotifications, Is.EqualTo(1));
        }

        progress.EnterDirectory(@"C:\Windows");
        timer.Tick();
        progress.Reset();
        timer.Tick();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(viewModel.ScanShowsStage, Is.False);
            Assert.That(viewModel.ScanCurrentPath, Is.EqualTo(@"C:\Windows"));
        }
    }
}
