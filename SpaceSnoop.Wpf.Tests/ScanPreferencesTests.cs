using KeepShell.Testing;
using SpaceSnoop.Wpf.Bootstrap;
using SpaceSnoop.Wpf.Bootstrap.Storage;
using SpaceSnoop.Wpf.ViewModels.Scan;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
public class ScanPreferencesTests
{
    [Test]
    public void По_умолчанию_потоков_вдвое_больше_чем_ядер()
    {
        var preferences = new ScanPreferences(new MemorySettings());

        Assert.That(preferences.ParallelismCeiling, Is.EqualTo(preferences.ProcessorCount * AppDefaults.ScanParallelismPerCore));
        Assert.That(preferences.MaxParallelism, Is.EqualTo(preferences.ParallelismCeiling));
    }

    [Test]
    public void Сохранённое_число_потоков_выше_ядер_переживает_чтение()
    {
        var settings = new MemorySettings();
        var requested = Environment.ProcessorCount + 1;
        settings.SetValue(SettingsKeys.ScanParallelism, requested.ToString());

        var preferences = new ScanPreferences(settings);

        Assert.That(preferences.MaxParallelism, Is.EqualTo(requested));
    }

    [TestCase(0, 1)]
    [TestCase(-5, 1)]
    public void Число_потоков_ниже_единицы_поднимается(int stored, int expected)
    {
        var settings = new MemorySettings();
        settings.SetValue(SettingsKeys.ScanParallelism, stored.ToString());

        var preferences = new ScanPreferences(settings);

        Assert.That(preferences.MaxParallelism, Is.EqualTo(expected));
    }

    [Test]
    public void Чтение_таблицы_NTFS_по_умолчанию_ограничено_диском_целиком()
    {
        var preferences = new ScanPreferences(new MemorySettings());

        Assert.That(preferences.MftRootOnly, Is.EqualTo(AppDefaults.ScanMftRootOnlyDefault));
    }

    [TestCase("false", false)]
    [TestCase("true", true)]
    public void Сохранённое_ограничение_диском_целиком_переживает_чтение(string stored, bool expected)
    {
        var settings = new MemorySettings();
        settings.SetValue(SettingsKeys.ScanMftRootOnly, stored);

        var preferences = new ScanPreferences(settings);

        Assert.That(preferences.MftRootOnly, Is.EqualTo(expected));
    }

    [TestCase(SettingsKeys.ScanReleaseBeforeRescan, null, true)]
    [TestCase(SettingsKeys.ScanReleaseBeforeRescan, "false", false)]
    [TestCase(SettingsKeys.ScanReleaseBeforeRescan, "true", true)]
    [TestCase(SettingsKeys.ScanReturnMemoryAfterScan, null, true)]
    [TestCase(SettingsKeys.ScanReturnMemoryAfterScan, "false", false)]
    [TestCase(SettingsKeys.ScanReturnMemoryAfterScan, "true", true)]
    public void Настройка_памяти_читается_из_настроек_и_по_умолчанию_включена(string key, string? stored, bool expected)
    {
        var settings = new MemorySettings();

        if (stored is not null)
        {
            settings.SetValue(key, stored);
        }

        var preferences = new ScanPreferences(settings);

        Assert.That(ReadMemoryFlag(preferences, key), Is.EqualTo(expected));
    }

    [TestCase(SettingsKeys.ScanReleaseBeforeRescan)]
    [TestCase(SettingsKeys.ScanReturnMemoryAfterScan)]
    public void Выключенная_настройка_памяти_сохраняется(string key)
    {
        var settings = new MemorySettings();
        var preferences = new ScanPreferences(settings);

        switch (key)
        {
            case SettingsKeys.ScanReleaseBeforeRescan:
                preferences.ReleaseBeforeRescan = false;
                break;
            case SettingsKeys.ScanReturnMemoryAfterScan:
                preferences.ReturnMemoryAfterScan = false;
                break;
        }

        Assert.That(ReadMemoryFlag(new ScanPreferences(settings), key), Is.False);
    }

    private static bool ReadMemoryFlag(ScanPreferences preferences, string key)
    {
        return key switch
        {
            SettingsKeys.ScanReleaseBeforeRescan => preferences.ReleaseBeforeRescan,
            SettingsKeys.ScanReturnMemoryAfterScan => preferences.ReturnMemoryAfterScan,
            _ => throw new ArgumentOutOfRangeException(nameof(key), key, null),
        };
    }

    [Test]
    public void Число_потоков_выше_потолка_урезается()
    {
        var settings = new MemorySettings();
        settings.SetValue(SettingsKeys.ScanParallelism, "4096");

        var preferences = new ScanPreferences(settings);

        Assert.That(preferences.MaxParallelism, Is.EqualTo(preferences.ParallelismCeiling));
    }
}
