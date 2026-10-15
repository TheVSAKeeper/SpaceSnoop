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
    public async Task Измерение_начинается_сразу_и_покрывает_показ_и_возврат_памяти()
    {
        using var monitor = TestDiagnostics.Monitor();
        var operations = new SpaceSnoop.Wpf.Diagnostics.PerformanceOperations(monitor);
        var progress = new ScanProgressViewModel(operations, new FakeUiDispatcher());
        progress.ReportPhase("Подготовка сканирования");
        Assert.That(operations.Current?.Name, Is.EqualTo("Подготовка сканирования"));

        progress.Begin(new(Path.GetTempPath()), @"C:\", 4);
        Assert.That(operations.Current?.Name, Is.EqualTo("Сканирование"));

        progress.Finish(releaseMeasurement: false);
        progress.ReportPhase("Показ результата сканирования");
        Assert.That(operations.Current?.Name, Is.EqualTo("Показ результата сканирования"));

        progress.ReportPhase("Возврат памяти после сканирования");
        await Task.Yield();
        Assert.That(operations.Current?.Name, Is.EqualTo("Возврат памяти после сканирования"));

        await progress.CompleteMeasurementAsync();
        Assert.That(operations.Current, Is.Null);
    }

    [Test]
    public async Task Завершение_измерения_не_освобождает_чужой_слот()
    {
        using var monitor = TestDiagnostics.Monitor();
        var operations = new SpaceSnoop.Wpf.Diagnostics.PerformanceOperations(monitor);
        var owner = new SpaceSnoop.Wpf.Diagnostics.PerformanceOperation("Синхронизация", 0, 0, TimeSpan.Zero);
        operations.TryReport(owner, null);
        var progress = new ScanProgressViewModel(operations, new FakeUiDispatcher());

        progress.Begin(new(Path.GetTempPath()), @"C:\", 1);
        progress.Finish(releaseMeasurement: false);
        progress.ReportPhase("Показ результата сканирования");
        await progress.CompleteMeasurementAsync();

        Assert.That(operations.Current, Is.SameAs(owner));
        operations.Release(owner);
    }

    [TestCase(0L, 0L, false)]
    [TestCase(100L, 0L, false)]
    [TestCase(0L, 100L, false)]
    [TestCase(99L, 100L, false)]
    [TestCase(100L, 100L, true)]
    [TestCase(101L, 100L, true)]
    public void Уплотнение_после_скана_требует_высокой_нагрузки_памяти(long load, long threshold, bool expected)
    {
        Assert.That(ScanViewModel.ShouldReturnScanMemory(load, threshold), Is.EqualTo(expected));
    }

    [Test]
    public void Неготовый_диск_не_даёт_оценку()
    {
        var used = DriveInfo.GetDrives().Select(static drive => char.ToUpperInvariant(drive.Name[0])).ToHashSet();
        var free = "ZYXWVU".Cast<char?>().FirstOrDefault(letter => !used.Contains(letter!.Value));

        Assume.That(free, Is.Not.Null, "на машине нет свободной буквы диска");

        Assert.That(ScanProgressViewModel.EstimateTotalBytes(new($@"{free}:\")), Is.Null);
    }

    [TestCase(0, 0, ScanProgressViewModel.MeasuringText)]
    [TestCase(3, 0, "0 байт")]
    [TestCase(1, 1536, "1,5 КБ")]
    public void Объём_до_первого_замера_не_выдаёт_себя_за_ноль(long files, long bytes, string expected)
    {
        Assert.That(ScanProgressViewModel.BytesText(files, bytes), Is.EqualTo(expected));
    }

    [Test]
    public void Строка_хода_говорит_считаем_от_старта_до_первого_учтённого_файла()
    {
        var dispatcher = new FakeUiDispatcher();
        var viewModel = new ScanProgressViewModel(new(TestDiagnostics.Monitor()), dispatcher);
        var timer = dispatcher.Timers.Single();
        var progress = viewModel.Begin(new(Path.GetTempPath()), @"C:\", 1);

        var atStart = viewModel.ScanBytesText;

        progress.EnterDirectory(@"C:\Windows");
        timer.Tick();

        var beforeFiles = viewModel.ScanBytesText;

        progress.AddFiles(1, 1536);
        timer.Tick();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(atStart, Is.EqualTo(ScanProgressViewModel.MeasuringText));
            Assert.That(beforeFiles, Is.EqualTo(ScanProgressViewModel.MeasuringText));
            Assert.That(viewModel.ScanBytesText, Is.EqualTo("1,5 КБ"));
        }
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
