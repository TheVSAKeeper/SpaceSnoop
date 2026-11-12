using SpaceSnoop.Wpf.Views.Behaviors;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
[NonParallelizable]
public class PathSuggestTests
{
    private string _tempDir = null!;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"SpaceSnoopSuggest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(_tempDir, "Projects"));
        Directory.CreateDirectory(Path.Combine(_tempDir, "Pictures"));
        Directory.CreateDirectory(Path.Combine(_tempDir, "Music"));
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void Ввод_сетевого_пути_в_поле_не_блокирует_поток_интерфейса_и_отбрасывает_устаревшую_подсказку()
    {
        var previous = PathSuggest.DirectoryReader;
        var uiThread = Environment.CurrentManagedThreadId;
        var readerThread = 0;

        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();

        PathSuggest.DirectoryReader = (directory, prefix) =>
        {
            Interlocked.Exchange(ref readerThread, Environment.CurrentManagedThreadId);
            entered.Set();
            release.Wait(TimeSpan.FromSeconds(10));
            return [directory + prefix + "jects"];
        };

        try
        {
            var box = new TextBox();
            PathSuggest.SetEnabled(box, true);

            const string initial = @"\\hung-server\share\Pr";
            box.Text = initial;
            box.CaretIndex = initial.Length;
            TextCompositionManager.StartComposition(new(InputManager.Current, box, "o"));

            Assert.That(entered.Wait(TimeSpan.FromSeconds(5)), Is.True);
            Assert.That(Volatile.Read(ref readerThread), Is.Not.EqualTo(uiThread));

            box.Text = @"\\hung-server\share\Other";
            box.CaretIndex = box.Text.Length;

            release.Set();
            PumpFor(TimeSpan.FromMilliseconds(60));

            Assert.That(box.Text, Is.EqualTo(@"\\hung-server\share\Other"));
            PathSuggest.SetEnabled(box, false);
        }
        finally
        {
            release.Set();
            PathSuggest.DirectoryReader = previous;
        }
    }

    [TestCase("Mu", "Music")]
    [TestCase("mus", "Music")]
    public void Единственное_совпадение_по_префиксу_без_учёта_регистра(string typed, string expected)
    {
        var matches = PathSuggest.Matches(Path.Combine(_tempDir, typed));

        Assert.That(matches, Is.EqualTo([Path.Combine(_tempDir, expected)]));
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void Завершение_фонового_чтения_применяет_подсказку_если_ввод_не_менялся()
    {
        var previous = PathSuggest.DirectoryReader;
        PathSuggest.DirectoryReader = (directory, prefix) => [directory + prefix + "jects"];

        try
        {
            var box = new TextBox();
            PathSuggest.SetEnabled(box, true);

            const string initial = @"\\fast-server\share\Pr";
            box.Text = initial;
            box.CaretIndex = initial.Length;
            TextCompositionManager.StartComposition(new(InputManager.Current, box, "o"));

            var completed = PumpUntil(() => box.SelectionLength > 0, TimeSpan.FromSeconds(5));

            using (Assert.EnterMultipleScope())
            {
                Assert.That(completed, Is.True);
                Assert.That(box.Text, Is.EqualTo(@"\\fast-server\share\Projects"));
                Assert.That(box.SelectedText, Is.EqualTo("jects"));
            }

            PathSuggest.SetEnabled(box, false);
        }
        finally
        {
            PathSuggest.DirectoryReader = previous;
        }
    }

    [TestCase(@"\\server\", ExpectedResult = "server")]
    [TestCase("//server/", ExpectedResult = "server")]
    [TestCase(@"\\server\share\", ExpectedResult = null)]
    [TestCase(@"C:\", ExpectedResult = null)]
    [TestCase(@"\\", ExpectedResult = null)]
    public string? Корень_сервера_отличается_от_обычного_каталога(string directory)
    {
        return PathSuggest.ServerName(directory);
    }

    [Test]
    public void Несколько_совпадений_возвращаются_по_порядку_для_перебора()
    {
        var matches = PathSuggest.Matches(Path.Combine(_tempDir, "P"));

        Assert.That(matches, Is.EqualTo([Path.Combine(_tempDir, "Pictures"), Path.Combine(_tempDir, "Projects")]));
    }

    [Test]
    public void Первый_запрос_к_недоступному_серверу_не_блокирует_ввод()
    {
        Assert.That(PathSuggest.Matches($@"\\{Guid.NewGuid():N}\sh"), Is.Empty);
    }

    [TestCase("nosep")]
    [TestCase("emptyprefix")]
    [TestCase("nomatch")]
    public void Совпадений_нет(string scenario)
    {
        var input = scenario switch
        {
            "nosep" => "Music",
            "emptyprefix" => _tempDir + Path.DirectorySeparatorChar,
            _ => Path.Combine(_tempDir, "zzz"),
        };

        Assert.That(PathSuggest.Matches(input), Is.Empty);
    }

    private static void PumpFor(TimeSpan duration)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(
            duration,
            DispatcherPriority.Background,
            (_, _) => frame.Continue = false,
            Dispatcher.CurrentDispatcher);

        timer.Start();
        Dispatcher.PushFrame(frame);
        timer.Stop();
    }

    private static bool PumpUntil(Func<bool> condition, TimeSpan timeout)
    {
        if (condition())
        {
            return true;
        }

        var deadline = DateTime.UtcNow + timeout;
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(10),
            DispatcherPriority.Background,
            (_, _) =>
            {
                if (condition() || DateTime.UtcNow >= deadline)
                {
                    frame.Continue = false;
                }
            },
            Dispatcher.CurrentDispatcher);

        timer.Start();
        Dispatcher.PushFrame(frame);
        timer.Stop();
        return condition();
    }
}
