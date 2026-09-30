using KeepShell.Bootstrap;
using SpaceSnoop.Wpf.Bootstrap;
using SpaceSnoop.Wpf.Bootstrap.Storage;
using System.IO;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
public class AppStorageTests
{
    [TestCase(false, false, false, true)]
    [TestCase(false, false, true, false)]
    [TestCase(false, true, false, true)]
    [TestCase(false, true, true, true)]
    [TestCase(true, false, false, false)]
    [TestCase(true, true, true, false)]
    public void По_умолчанию_AppData_кроме_явного_portable_или_наследия(bool portableMarker, bool legacyAppDataMarker, bool legacyPortableData, bool expected)
    {
        Assert.That(AppStorage.Resolve(portableMarker, legacyAppDataMarker, () => legacyPortableData), Is.EqualTo(expected));
    }

    [TestCase(true, false, TestName = "Метка portable.flag решает без проверки каталога exe")]
    [TestCase(false, true, TestName = "Старая метка appdata.flag решает без проверки и миграции в каталоге exe")]
    public void Метка_решает_без_проверки_каталога_exe(bool portableMarker, bool legacyAppDataMarker)
    {
        var probed = false;

        AppStorage.Resolve(portableMarker, legacyAppDataMarker, () => probed = true);

        Assert.That(probed, Is.False);
    }

    [TestCase("wpf.shell.font_scale 1.25\nwpf.scan.multithreading True\n", false, true, TestName = "Свой settings.txt без профиля в AppData – каталог переносной, файл мигрирует")]
    [TestCase("\n  wpf.shell.font_scale 1.25\r\n", false, true, TestName = "Свой settings.txt с отступом и CRLF – каталог переносной")]
    [TestCase("volume 7\nlanguage ru\n", false, false, TestName = "Чужой settings.txt – каталог не переносной, миграции нет")]
    [TestCase("", false, false, TestName = "Пустой settings.txt – каталог не переносной")]
    [TestCase("wpf.shell.font_scale 1.25\n", true, false, TestName = "Свой settings.txt при профиле в AppData – остаётся AppData, миграции нет")]
    public void Старый_settings_txt_рядом_с_exe(string legacy, bool appDataProfile, bool expectedPortable)
    {
        var root = Path.Combine(Path.GetTempPath(), "SpaceSnoop.Tests", Guid.NewGuid().ToString("N"));
        var exe = Path.Combine(root, "exe");
        var appData = Path.Combine(root, "appdata");

        try
        {
            Directory.CreateDirectory(exe);
            Directory.CreateDirectory(appData);
            File.WriteAllText(Path.Combine(exe, TomlSettingsFile.LegacyFileName), legacy);

            if (appDataProfile)
            {
                File.WriteAllText(Path.Combine(appData, TomlSettingsFile.PrimaryFileName), string.Empty);
            }

            var portable = AppStorage.HasPortableSettings(exe, appData);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(portable, Is.EqualTo(expectedPortable));
                Assert.That(File.Exists(Path.Combine(exe, TomlSettingsFile.PrimaryFileName)), Is.EqualTo(expectedPortable));
                Assert.That(File.ReadAllText(Path.Combine(exe, TomlSettingsFile.LegacyFileName)), Is.EqualTo(legacy));
            }
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Test]
    public void Сбой_записи_settings_toml_оставляет_settings_txt_и_выбирает_AppData()
    {
        var root = Path.Combine(Path.GetTempPath(), "SpaceSnoop.Tests", Guid.NewGuid().ToString("N"));
        var exe = Path.Combine(root, "exe");
        var appData = Path.Combine(root, "appdata");
        const string legacy = "wpf.shell.font_scale 1.25\n";

        try
        {
            Directory.CreateDirectory(Path.Combine(exe, TomlSettingsFile.PrimaryFileName));
            Directory.CreateDirectory(appData);
            File.WriteAllText(Path.Combine(exe, TomlSettingsFile.LegacyFileName), legacy);

            var useAppData = AppStorage.Resolve(false, false, () => AppStorage.HasPortableSettings(exe, appData));

            using (Assert.EnterMultipleScope())
            {
                Assert.That(useAppData, Is.True);
                Assert.That(File.ReadAllText(Path.Combine(exe, TomlSettingsFile.LegacyFileName)), Is.EqualTo(legacy));
            }
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Test]
    public void Migrate_переносит_файлы_не_оставляя_дубликатов_в_источнике()
    {
        var root = Path.Combine(Path.GetTempPath(), "SpaceSnoop.Tests", Guid.NewGuid().ToString("N"));
        var source = Path.Combine(root, "src");
        var destination = Path.Combine(root, "dst");

        try
        {
            Directory.CreateDirectory(source);
            Directory.CreateDirectory(Path.Combine(source, AppStorage.LogsFolderName));

            File.WriteAllText(Path.Combine(source, TomlSettingsFile.PrimaryFileName), "theme = \"dark\"");
            File.WriteAllText(Path.Combine(source, AppInfo.DeletionLogFileName), "deleted");
            File.WriteAllText(Path.Combine(source, AppInfo.SyncLogFileName), "synced");
            File.WriteAllText(Path.Combine(source, AppStorage.LogsFolderName, "wpf-1.log"), "log");

            AppStorage.Migrate(source, destination);

            Assert.Multiple(() =>
            {
                Assert.That(File.ReadAllText(Path.Combine(destination, TomlSettingsFile.PrimaryFileName)), Is.EqualTo("theme = \"dark\""));
                Assert.That(File.Exists(Path.Combine(destination, AppInfo.DeletionLogFileName)), Is.True);
                Assert.That(File.Exists(Path.Combine(destination, AppStorage.LogsFolderName, "wpf-1.log")), Is.True);

                Assert.That(File.Exists(Path.Combine(source, TomlSettingsFile.PrimaryFileName)), Is.False);
                Assert.That(File.Exists(Path.Combine(source, AppInfo.SyncLogFileName)), Is.False);
                Assert.That(File.Exists(Path.Combine(source, AppStorage.LogsFolderName, "wpf-1.log")), Is.False);
            });
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }
}
