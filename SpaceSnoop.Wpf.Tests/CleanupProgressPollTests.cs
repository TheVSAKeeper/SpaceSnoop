using Microsoft.Extensions.Logging.Abstractions;
using SpaceSnoop.Core;
using SpaceSnoop.Core.Cleanup;
using SpaceSnoop.Wpf.ViewModels.Dialogs;
using System.IO;
using System.Runtime.CompilerServices;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
public class CleanupProgressPollTests
{
    private const int FilesPerTarget = 60;
    private const int Targets = 2;

    private string _root = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), $"SpaceSnoopCleanupPoll_{Guid.NewGuid():N}");

        for (var target = 0; target < Targets; target++)
        {
            var directory = Path.Combine(_root, $"цель-{target}");
            Directory.CreateDirectory(directory);

            for (var index = 0; index < FilesPerTarget; index++)
            {
                File.WriteAllText(Path.Combine(directory, $"файл-{index:D3}.tmp"), new string('о', 512));
            }
        }
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }

    [Test]
    public async Task Экран_очистки_обновляется_тиком_таймера_а_не_каждым_файлом()
    {
        var dispatcher = new FakeUiDispatcher();
        var dialog = CreateDialog(dispatcher);
        var updates = Watch(dialog);

        await dialog.StartCommand.ExecuteAsync(null);

        var total = FilesPerTarget * Targets;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(dialog.Deleted, Is.EqualTo(total), "Очистка не дошла до всех файлов – проверять нечего.");
            Assert.That(updates.Value, Is.EqualTo(1), $"Экран обновился {updates.Value} раз на {total} файлах – прогресс снова пушится на каждый объект.");
            Assert.That(dialog.CountText, Is.EqualTo($"{total:N0} / {total:N0}"), "Счётчик второй цели начался заново вместо продолжения первой.");
        }
    }

    [Test]
    public async Task Таймер_опроса_заводится_один_и_останавливается_после_очистки()
    {
        var dispatcher = new FakeUiDispatcher();
        var dialog = CreateDialog(dispatcher);

        await dialog.StartCommand.ExecuteAsync(null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(dispatcher.Timers, Has.Count.EqualTo(1));
            Assert.That(dispatcher.Timers[0].Interval, Is.EqualTo(CleanupProgressDialogViewModel.ProgressPollInterval));
            Assert.That(dispatcher.Timers[0].IsRunning, Is.False, "Таймер опроса пережил операцию.");
        }
    }

    [Test]
    public async Task Тик_после_конца_очистки_не_переписывает_итог()
    {
        var dispatcher = new FakeUiDispatcher();
        var dialog = CreateDialog(dispatcher);

        await dialog.StartCommand.ExecuteAsync(null);

        var updates = Watch(dialog);
        dispatcher.Timers[0].Tick();

        Assert.That(updates.Value, Is.Zero, "Тик после конца операции переписал итоговый срез.");
    }

    [TestCase(CleanupTargetKind.RecycleBin, 5, 0, true, ExpectedResult = true)]
    [TestCase(CleanupTargetKind.RecycleBin, 0, 0, true, ExpectedResult = false)]
    [TestCase(CleanupTargetKind.RecycleBin, 0, 5, true, ExpectedResult = false)]
    [TestCase(CleanupTargetKind.RecycleBin, 5, 0, false, ExpectedResult = false)]
    [TestCase(CleanupTargetKind.Directory, 5, 0, true, ExpectedResult = false)]
    public bool Фраза_про_неделимый_шаг_только_когда_корзина_очищена(CleanupTargetKind kind, int deleted, int skipped, bool cancelRequested)
    {
        var target = new CleanupTarget { Id = "t", Name = "t", Description = "Тест", Kind = kind, Path = _root };
        string[] errors = skipped > 0 ? ["Корзина занята другим процессом"] : [];

        return CleanupProgressDialogViewModel.ClearedDespiteCancel(target, new(deleted * 512L, deleted, skipped, errors), cancelRequested);
    }

    [TestCase(false, false, 0, "", ExpectedResult = "Готово: {0}.")]
    [TestCase(false, true, 0, "", ExpectedResult = "Готово: {0}. Корзину Windows отмена не остановила: этот шаг не прерывается.")]
    [TestCase(true, true, 0, "", ExpectedResult = "Отменено: {0}. Корзину Windows отмена не остановила: этот шаг не прерывается.")]
    [TestCase(false, true, 2, "Нет доступа", ExpectedResult = "Готово: {0}. Пропущено 2 – Нет доступа. Корзину Windows отмена не остановила: этот шаг не прерывается.")]
    [TestCase(false, false, 0, "Каталог не найден.", ExpectedResult = "Готово: {0}. Каталог не найден.")]
    [TestCase(true, false, 2, "Нет доступа", ExpectedResult = "Отменено: {0}. Пропущено 2 – Нет доступа")]
    [TestCase(true, false, 0, "Каталог не найден.", ExpectedResult = "Отменено: {0}. Каталог не найден.")]
    public string Итог_очистки_не_прячет_ошибку_за_фразой_про_отмену(bool cancelled, bool binCleared, int skipped, string firstError)
    {
        var summary = CleanupProgressDialogViewModel.Summarize(5, 2048, cancelled, binCleared, skipped, firstError);

        return summary.Replace($"удалено {5:N0}, освобождено {SizeFormatter.Format(2048)}", "{0}");
    }

    private CleanupProgressDialogViewModel CreateDialog(FakeUiDispatcher dispatcher)
    {
        var targets = Directory.GetDirectories(_root)
            .Order(StringComparer.Ordinal)
            .Select(path => new CleanupTarget
            {
                Id = Path.GetFileName(path),
                Name = Path.GetFileName(path),
                Description = "Тест",
                Kind = CleanupTargetKind.Directory,
                Path = path,
                MinimumAge = TimeSpan.Zero,
            })
            .ToList();

        CleanupRequest request = new(targets, 512L * FilesPerTarget * Targets, FilesPerTarget * Targets);

        return new(request, new(), dispatcher, NullLogger.Instance);
    }

    private static StrongBox<int> Watch(CleanupProgressDialogViewModel dialog)
    {
        var updates = new StrongBox<int>(0);

        dialog.PropertyChanged += (_, arguments) =>
        {
            if (arguments.PropertyName == nameof(dialog.CountText))
            {
                updates.Value++;
            }
        };

        return updates;
    }
}
