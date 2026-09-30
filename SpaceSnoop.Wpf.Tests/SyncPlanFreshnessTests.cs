using KeepShell.Bootstrap;
using KeepShell.Testing;
using KeepShell.ViewModels;
using MahApps.Metro.IconPacks;
using Microsoft.Extensions.Logging.Abstractions;
using SpaceSnoop.Core;
using SpaceSnoop.Core.Domain;
using SpaceSnoop.Core.UseCases;
using SpaceSnoop.Wpf.Bootstrap;
using SpaceSnoop.Wpf.Diagnostics;
using SpaceSnoop.Wpf.Mcp;
using SpaceSnoop.Wpf.ViewModels;
using SpaceSnoop.Wpf.ViewModels.Dialogs;
using SpaceSnoop.Wpf.ViewModels.Settings;
using SpaceSnoop.Wpf.ViewModels.Sync;
using System.IO;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
[NonParallelizable]
public class SyncPlanFreshnessTests
{
    private string _root = string.Empty;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "spacesnoop-freshness", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_root, "left"));
        Directory.CreateDirectory(Path.Combine(_root, "right"));
        File.WriteAllText(Path.Combine(_root, "left", "a.txt"), "данные");
    }

    [TearDown]
    public void TearDown()
    {
        try
        {
            Directory.Delete(_root, true);
        }
        catch (IOException exception)
        {
            TestContext.Out.WriteLine(exception.Message);
        }
    }

    [Test]
    public async Task Исполненный_план_повторно_не_запускается()
    {
        var page = await RunSyncAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(page.Operations.PlanFreshness, Is.EqualTo(SyncPlanFreshness.Applied));
            Assert.That(page.Operations.SyncCommand.CanExecute(null), Is.False);
            Assert.That(File.Exists(Path.Combine(_root, "right", "a.txt")), Is.True);
        }

        Assert.ThrowsAsync<SyncPlanStaleException>(() => ((ISyncAutomation)page).SyncFromAutomationAsync(CancellationToken.None));
    }

    [Test]
    public async Task Хеширование_не_возвращает_исполненному_плану_исполнимость()
    {
        var page = await RunSyncAsync();

        await page.Operations.HashCommand.ExecuteAsync(null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(page.Operations.PlanFreshness, Is.EqualTo(SyncPlanFreshness.Applied));
            Assert.That(page.Operations.SyncCommand.CanExecute(null), Is.False);
        }
    }

    [Test]
    public async Task Смена_режима_не_возвращает_исполненному_плану_исполнимость()
    {
        var page = await RunSyncAsync();

        page.Operations.ReapplyMode();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(page.Operations.PlanFreshness, Is.EqualTo(SyncPlanFreshness.Applied));
            Assert.That(page.Operations.SyncCommand.CanExecute(null), Is.False);
        }
    }

    [Test]
    public async Task Новое_сравнение_возвращает_плану_исполнимость()
    {
        var page = await RunSyncAsync();

        await File.WriteAllTextAsync(Path.Combine(_root, "left", "b.txt"), "ещё данные");
        await ((ISyncAutomation)page).CompareFromAutomationAsync(CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(page.Operations.PlanFreshness, Is.EqualTo(SyncPlanFreshness.Fresh));
            Assert.That(page.Operations.SyncCommand.CanExecute(null), Is.True);
        }
    }

    [TestCase(0, 0, false, SyncPlanFreshness.Applied)]
    [TestCase(1, 0, false, SyncPlanFreshness.PartiallyApplied)]
    [TestCase(0, 1, false, SyncPlanFreshness.PartiallyApplied)]
    [TestCase(0, 0, true, SyncPlanFreshness.PartiallyApplied)]
    public void Результат_прогона_выбирает_полное_или_частичное_исполнение(
        int errors,
        int mismatches,
        bool cancelled,
        SyncPlanFreshness expected)
    {
        var report = new SyncReport();

        for (var index = 0; index < errors; index++)
        {
            report.Errors.Add(new($"e{index}.txt", SyncAction.CopyToRight, "нет доступа"));
        }

        for (var index = 0; index < mismatches; index++)
        {
            report.Mismatches.Add(new($"m{index}.txt", SyncAction.CopyToRight, "приёмник не найден"));
        }

        var state = new SyncPlanFreshnessState(SyncPlanFreshness.Fresh).AfterSync(report, cancelled);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(state.Freshness, Is.EqualTo(expected));
            Assert.That(state.IsExecutable, Is.False);
        }
    }

    [Test]
    public void Оборванный_прогон_не_сообщает_о_нуле_ошибок()
    {
        var state = new SyncPlanFreshnessState(SyncPlanFreshness.Fresh).AfterSync(null, true);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(state.Freshness, Is.EqualTo(SyncPlanFreshness.Interrupted));
            Assert.That(state.RefusalMessage, Does.Not.Contain("ошибок"));
            Assert.That(state.RefusalMessage, Does.Contain("выполните сравнение заново"));
        }
    }

    [Test]
    public void Частичный_отказ_называет_число_ошибок()
    {
        var report = new SyncReport();
        report.Errors.Add(new("a.txt", SyncAction.CopyToRight, "нет доступа"));
        report.Errors.Add(new("b.txt", SyncAction.CopyToRight, "нет доступа"));

        var state = new SyncPlanFreshnessState(SyncPlanFreshness.Fresh).AfterSync(report, false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(state.IsExecutable, Is.False);
            Assert.That(state.RefusalMessage, Does.Contain("ошибок: 2"));
            Assert.That(state.RefusalMessage, Does.Contain("выполните сравнение заново"));
        }
    }

    [Test]
    public void Только_сравнение_возвращает_состояние_в_свежее()
    {
        var transitions = typeof(SyncPlanFreshnessState)
            .GetMethods()
            .Where(static method => method.ReturnType == typeof(SyncPlanFreshnessState))
            .Select(static method => method.Name)
            .ToList();

        Assert.That(transitions, Is.EquivalentTo(new[] { nameof(SyncPlanFreshnessState.AfterComparison), nameof(SyncPlanFreshnessState.AfterSync) }));
    }

    [Test]
    public async Task Правка_исключений_во_время_сравнения_отбрасывает_его_результат()
    {
        var page = CreatePage();
        var automation = (ISyncAutomation)page;
        automation.LeftPath = Path.Combine(_root, "left");
        automation.RightPath = Path.Combine(_root, "right");
        page.Session.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SyncSessionViewModel.IsBusy) && page.Session.IsBusy)
            {
                page.Setup.Exclusions = "*.txt";
            }
        };

        await automation.CompareFromAutomationAsync(CancellationToken.None);

        Assert.That(page.Operations.Result, Is.Null);
    }

    [Test]
    public async Task Правка_исключений_во_время_синхронизации_сбрасывает_сравнение_только_по_её_завершении()
    {
        var page = CreatePage();
        var automation = (ISyncAutomation)page;
        automation.LeftPath = Path.Combine(_root, "left");
        automation.RightPath = Path.Combine(_root, "right");
        automation.Mode = SyncMode.LeftToRight;

        await automation.CompareFromAutomationAsync(CancellationToken.None);

        var keptWhileBusy = false;
        page.Session.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SyncSessionViewModel.IsBusy) && page.Session.IsBusy)
            {
                page.Setup.Exclusions = "*.txt";
                keptWhileBusy = page.Operations.Result is not null;
            }
        };

        var run = await automation.SyncFromAutomationAsync(CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(keptWhileBusy, Is.True);
            Assert.That(run?.Report.SuccessCount, Is.EqualTo(1));
            Assert.That(page.Operations.Result, Is.Null);
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task План_сменившийся_за_замер_места_не_доходит_до_подтверждения(bool replaceResult)
    {
        using var reader = new BlockingVolumeReader(1_000_000);
        var compare = new CompareDirectoriesUseCase(NullLogger<DirectoryComparer>.Instance);
        var request = new CompareDirectoriesRequest(Path.Combine(_root, "left"), Path.Combine(_root, "right"), string.Empty, SyncMode.LeftToRight, SyncWinner.Newest, false);
        var result = compare.Execute(request, CancellationToken.None);
        var ledger = new SyncLedgerViewModel(new(new MemorySettings(), new NoopDialogs(), NullLogger.Instance),
            () => new(PackIconLucideKind.ArrowRight, "слева направо", false));

        ledger.Update(result, false);

        Assume.That(result.CountPlannedActions().RequiredRightBytes, Is.GreaterThan(0));

        var confirmation = ledger.BuildSyncConfirmationAsync(result, new("Сверить хеши", ConfirmChoiceKind.Secondary));
        reader.WaitEntered();

        if (replaceResult)
        {
            ledger.Update(compare.Execute(request, CancellationToken.None), false);
        }
        else
        {
            result.Root.Files.Single().Action = SyncAction.Skip;
        }

        reader.Release();

        Assert.That(await confirmation, Is.Null);
    }

    private async Task<SyncViewModel> RunSyncAsync()
    {
        var page = CreatePage();
        var automation = (ISyncAutomation)page;
        automation.LeftPath = Path.Combine(_root, "left");
        automation.RightPath = Path.Combine(_root, "right");
        automation.Mode = SyncMode.LeftToRight;

        await automation.CompareFromAutomationAsync(CancellationToken.None);

        Assume.That(page.Operations.SyncCommand.CanExecute(null), Is.True);

        await automation.SyncFromAutomationAsync(CancellationToken.None);

        return page;
    }

    private static SyncViewModel CreatePage()
    {
        var settings = new MemorySettings();
        var dialogs = new NoopDialogs();
        var shell = new ShellPreferences(settings);

        return new(settings,
            dialogs,
            new OperationPreferences(settings),
            new AgentPreferences(settings),
            NullLogger<SyncViewModel>.Instance,
            new CompareDirectoriesUseCase(NullLogger<DirectoryComparer>.Instance),
            new ExecuteSyncUseCase(NullLogger<SyncEngine>.Instance),
            new ToastNotifier(new ToastHostViewModel(), shell),
            new PerformanceOperations(TestDiagnostics.Monitor()),
            new FakeFilePicker(),
            new FakeUiDispatcher(),
            new FakeAppNavigator());
    }
}
