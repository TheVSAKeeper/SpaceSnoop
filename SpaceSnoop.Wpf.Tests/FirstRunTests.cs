using KeepShell.Bootstrap;
using KeepShell.Testing;
using SpaceSnoop.Wpf.Bootstrap;
using SpaceSnoop.Wpf.Bootstrap.Storage;
using SpaceSnoop.Wpf.ViewModels.Dialogs;
using SpaceSnoop.Wpf.ViewModels.Scan;
using SpaceSnoop.Wpf.ViewModels.Settings;
using System.IO;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
public class FirstRunTests
{
    [TestCase(false, true, true, StartupAdminAction.WelcomeCard, TestName = "Первый запуск без прав – карточка вместо вопроса")]
    [TestCase(true, true, true, StartupAdminAction.WelcomeCard, TestName = "Первый запуск от администратора – карточка")]
    [TestCase(false, false, true, StartupAdminAction.Question, TestName = "Повторный запуск без прав – вопрос о правах")]
    [TestCase(false, false, false, StartupAdminAction.None, TestName = "Повторный запуск после «Больше не спрашивать» – ни карточки, ни вопроса")]
    [TestCase(true, false, true, StartupAdminAction.None, TestName = "Повторный запуск от администратора – ни карточки, ни вопроса")]
    public void Решение_на_старте(bool isElevated, bool welcomePending, bool warnIfNotAdmin, StartupAdminAction expected)
    {
        Assert.That(AdminStartupPrompt.Decide(isElevated, welcomePending, warnIfNotAdmin), Is.EqualTo(expected));
    }

    [TestCase(false, false, true, 1, TestName = "Вопрос задаётся на повторном запуске без прав")]
    [TestCase(false, true, true, 0, TestName = "Вопрос не задаётся, пока ждёт карточка")]
    [TestCase(true, false, true, 0, TestName = "Вопрос не задаётся администратору")]
    [TestCase(false, false, false, 0, TestName = "Вопрос не задаётся после «Больше не спрашивать»")]
    public async Task Вопрос_о_правах_показывается_только_по_решению(bool isElevated, bool welcomePending, bool warnIfNotAdmin, int expectedShows)
    {
        var settings = new MemorySettings();
        ((ISettingsStore)settings).SetBool(SettingsKeys.WarnIfNotAdmin, warnIfNotAdmin);
        var dialogs = new NoopDialogs();
        var prompt = new AdminStartupPrompt(dialogs, new(settings), () => Task.CompletedTask);

        await prompt.RunAsync(isElevated, welcomePending);

        Assert.That(dialogs.ShowCount, Is.EqualTo(expectedShows));
    }

    [TestCase(0, false, 0, TestName = "«Не перезапускать» – спросит и в следующий раз")]
    [TestCase(1, true, 0, TestName = "«Больше не спрашивать» – вопрос выключен")]
    [TestCase(2, false, 1, TestName = "«Перезапустить» – перезапуск")]
    public async Task Ответ_на_вопрос_о_правах(int choiceIndex, bool expectStopAsking, int expectedRestarts)
    {
        var settings = new MemorySettings();
        var restarts = 0;
        var dialogs = new NoopDialogs(show: viewModel =>
        {
            var confirm = (ConfirmDialogViewModel)viewModel;
            var choice = confirm.Choices[choiceIndex];
            confirm.ChooseCommand.Execute(choice);

            return !choice.IsDismissive;
        });

        var prompt = new AdminStartupPrompt(dialogs, new(settings), () =>
        {
            restarts++;
            return Task.CompletedTask;
        });

        await prompt.RunAsync(false, false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(((ISettingsStore)settings).GetBool(SettingsKeys.WarnIfNotAdmin, AppDefaults.WarnIfNotAdminDefault), Is.EqualTo(!expectStopAsking));
            Assert.That(restarts, Is.EqualTo(expectedRestarts));
        }
    }

    [TestCase(null, false, TestName = "Ключа нет – карточки нет (профиль старше карточки)")]
    [TestCase("true", true, TestName = "Отметка первого запуска – карточка видна")]
    [TestCase("false", false, TestName = "Карточку закрывали – карточки нет")]
    public void Карточка_видна_по_отметке(string? stored, bool expected)
    {
        var settings = new MemorySettings();

        if (stored is not null)
        {
            settings.SetValue(SettingsKeys.WelcomePending, stored);
        }

        Assert.That(Card(settings, isElevated: false, () => Task.FromResult(false)).IsVisible, Is.EqualTo(expected));
    }

    [Test]
    public void Закрытие_карточки_снимает_отметку_и_не_трогает_быстрый_скан()
    {
        var settings = PendingSettings();
        var card = Card(settings, isElevated: false, () => Task.FromResult(false));

        card.DismissCommand.Execute(null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(card.IsVisible, Is.False);
            Assert.That(((ISettingsStore)settings).GetBool(SettingsKeys.WelcomePending, true), Is.False);
            Assert.That(((ISettingsStore)settings).GetBool(SettingsKeys.ScanMftEnabled, AppDefaults.ScanMftEnabledDefault), Is.EqualTo(AppDefaults.ScanMftEnabledDefault));
            Assert.That(Card(settings, isElevated: false, () => Task.FromResult(false)).IsVisible, Is.False);
        }
    }

    [TestCase(true, false, TestName = "Перезапуск из карточки включает быстрый скан и снимает отметку ещё до перезапуска")]
    [TestCase(false, false, TestName = "Несостоявшийся перезапуск возвращает карточку, отметку и выключенный быстрый скан")]
    [TestCase(false, true, TestName = "Несостоявшийся перезапуск оставляет включённый заранее быстрый скан")]
    public async Task Перезапуск_из_карточки(bool restarted, bool fastScanBefore)
    {
        var settings = PendingSettings();
        ((ISettingsStore)settings).SetBool(SettingsKeys.ScanMftEnabled, fastScanBefore);
        bool? pendingAtRestart = null;
        bool? fastScanAtRestart = null;
        var card = Card(settings, isElevated: false, () =>
        {
            pendingAtRestart = ((ISettingsStore)settings).GetBool(SettingsKeys.WelcomePending, true);
            fastScanAtRestart = ((ISettingsStore)settings).GetBool(SettingsKeys.ScanMftEnabled);
            return Task.FromResult(restarted);
        });

        await card.RestartAsAdminCommand.ExecuteAsync(null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(pendingAtRestart, Is.False);
            Assert.That(fastScanAtRestart, Is.True);
            Assert.That(card.IsVisible, Is.EqualTo(!restarted));
            Assert.That(((ISettingsStore)settings).GetBool(SettingsKeys.WelcomePending, false), Is.EqualTo(!restarted));
            Assert.That(((ISettingsStore)settings).GetBool(SettingsKeys.ScanMftEnabled), Is.EqualTo(restarted || fastScanBefore));
            Assert.That(card.FastScan, Is.EqualTo(restarted || fastScanBefore));
        }
    }

    [TestCase(true, false, true, TestName = "Проверка обновлений на старте идёт, когда карточка закрыта")]
    [TestCase(true, true, false, TestName = "Проверка обновлений на старте ждёт закрытия карточки первого запуска")]
    [TestCase(false, false, false, TestName = "Выключенная проверка обновлений на старте не идёт")]
    public void Проверка_обновлений_на_старте(bool checkOnStartup, bool welcomePending, bool expected)
    {
        Assert.That(AppUpdateViewModel.ChecksOnStartup(checkOnStartup, welcomePending), Is.EqualTo(expected));
    }

    [Test]
    public void Галки_карточки_пишут_те_же_настройки_что_и_раздел_настроек()
    {
        var settings = PendingSettings();
        var card = Card(settings, isElevated: true, () => Task.FromResult(false));

        card.FastScan = true;
        card.CheckUpdates = false;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(((ISettingsStore)settings).GetBool(SettingsKeys.ScanMftEnabled), Is.True);
            Assert.That(((ISettingsStore)settings).GetBool(SettingsKeys.UpdateCheckOnStartup, true), Is.False);
            Assert.That(card.IsVisible, Is.True);
        }
    }

    [Test]
    public void Профиль_без_файла_настроек_считается_новым()
    {
        var directory = Path.Combine(Path.GetTempPath(), "SpaceSnoopFirstRun", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        try
        {
            var fresh = AppStorage.HasSettings(directory);
            File.WriteAllText(Path.Combine(directory, TomlSettingsFile.LegacyFileName), string.Empty);
            var legacy = AppStorage.HasSettings(directory);
            File.Delete(Path.Combine(directory, TomlSettingsFile.LegacyFileName));
            File.WriteAllText(Path.Combine(directory, TomlSettingsFile.PrimaryFileName), string.Empty);
            var primary = AppStorage.HasSettings(directory);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(fresh, Is.False);
                Assert.That(legacy, Is.True);
                Assert.That(primary, Is.True);
            }
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Test]
    public void Переносной_каталог_со_старым_settings_txt_открывается_без_карточки()
    {
        var root = Path.Combine(Path.GetTempPath(), "SpaceSnoopFirstRun", Guid.NewGuid().ToString("N"));
        var exe = Path.Combine(root, "exe");
        var appData = Path.Combine(root, "appdata");
        Directory.CreateDirectory(exe);
        Directory.CreateDirectory(appData);

        try
        {
            File.WriteAllText(Path.Combine(exe, TomlSettingsFile.LegacyFileName), $"{SettingsKeys.FontScale} 1.25\n");

            var useAppData = AppStorage.Resolve(false, false, () => AppStorage.HasPortableSettings(exe, appData));
            var dataDirectory = useAppData ? appData : exe;
            var freshProfile = !AppStorage.HasSettings(dataDirectory);

            using var store = new SettingsStore(Path.Combine(dataDirectory, TomlSettingsFile.PrimaryFileName));
            ISettingsStore settings = store;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(useAppData, Is.False);
                Assert.That(freshProfile, Is.False);
                Assert.That(settings.GetDouble(SettingsKeys.FontScale, 1.0), Is.EqualTo(1.25));
                Assert.That(Card(settings, isElevated: false, () => Task.FromResult(false)).IsVisible, Is.False);
            }
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static MemorySettings PendingSettings()
    {
        var settings = new MemorySettings();
        ((ISettingsStore)settings).SetBool(SettingsKeys.WelcomePending, true);

        return settings;
    }

    private static FirstRunViewModel Card(ISettingsStore settings, bool isElevated, Func<Task<bool>> restart)
    {
        return new(settings, new(settings), new(settings), new UpdatePreferences(settings), isElevated, restart);
    }
}
