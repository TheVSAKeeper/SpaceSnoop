using KeepShell.Bootstrap;
using KeepShell.Diagnostics;
using KeepShell.Testing;
using SpaceSnoop.Wpf.Bootstrap.Storage;
using SpaceSnoop.Wpf.Diagnostics;
using System.IO;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
public class DiagnosticsBundleTests
{
    private const string Token = "s3cr3t-live-token";

    private string _root = string.Empty;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "spacesnoop-diagnostics-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
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
    public void Живой_токен_вымарывается_из_строки_журнала_Serilog()
    {
        var line = $"2026-09-20 18:00:11.234 +03:00 [INF] Агент подключился по Bearer {Token} за 12 мс";

        var entries = Build(Secrets(Token), [new("wpf-20260920.log", line)]);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(Text(entries, "logs/wpf-20260920.log"), Does.Not.Contain(Token));
            Assert.That(Text(entries, "logs/wpf-20260920.log"), Does.Contain(DiagnosticsSecrets.Mask));
            Assert.That(Text(entries, "logs/wpf-20260920.log"), Does.Contain("за 12 мс"));
        }
    }

    [Test]
    public void Пара_имя_значение_глушится_и_без_объявленного_токена()
    {
        var entries = Build(Secrets(null), [new("wpf-20260920.log", "2026-09-20 18:00:11.234 +03:00 [INF] Запрос token=eyJhbGciOiJIUzI1 завершился")]);

        Assert.That(Text(entries, "logs/wpf-20260920.log"),
            Is.EqualTo($"2026-09-20 18:00:11.234 +03:00 [INF] Запрос token={DiagnosticsSecrets.Mask} завершился"));
    }

    [Test]
    public void Источник_секретов_объявляет_живой_токен_только_когда_он_задан()
    {
        var settings = new MemorySettings();
        var source = new SpaceSnoopSecretSource(settings);

        var empty = source.Collect();

        settings.SetValue(SettingsKeys.McpToken, "  " + Token + "  ");

        var filled = source.Collect();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(empty.Values, Is.Empty);
            Assert.That(empty.KeyNames, Does.Contain("token"));
            Assert.That(filled.Values, Is.EqualTo(new[] { Token }));
        }
    }

    [Test]
    public void Прикладной_вклад_несёт_настройки_и_прогон()
    {
        var settings = new MemorySettings();
        var path = Path.Combine(_root, TomlSettingsFile.PrimaryFileName);
        File.WriteAllText(path, $"[wpf.mcp]{Environment.NewLine}token = \"{Token}\"");

        var operations = new PerformanceOperations(TestDiagnostics.Monitor());
        operations.ReportRun(new("Сканирование", 120, 4096, TimeSpan.FromSeconds(3)));

        var entries = Build(Secrets(Token), [], new SpaceSnoopBundleSource(settings, operations, path).Collect().ToList());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(entries.Select(static entry => entry.Name),
                Does.Contain(SpaceSnoopBundleSource.SettingsEntry).And.Contain(SpaceSnoopBundleSource.OperationsEntry));
            Assert.That(Text(entries, SpaceSnoopBundleSource.SettingsEntry), Does.Not.Contain(Token));
            Assert.That(Text(entries, SpaceSnoopBundleSource.OperationsEntry), Does.Contain("Сканирование"));
        }
    }

    [Test]
    public void Непрочитанные_настройки_не_роняют_вклад_и_называются_человеку()
    {
        var operations = new PerformanceOperations(TestDiagnostics.Monitor());
        var source = new SpaceSnoopBundleSource(new MemorySettings(), operations, Path.Combine(_root, "нет-такого.toml"));

        var bundle = Bundle(Secrets(null), source.Collect().ToList());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(bundle.Entries.Select(static entry => entry.Name), Does.Not.Contain(SpaceSnoopBundleSource.SettingsEntry));
            Assert.That(bundle.Entries.Select(static entry => entry.Name), Does.Contain(SpaceSnoopBundleSource.OperationsEntry));
            Assert.That(Text(bundle.Entries, SpaceSnoopBundleSource.SettingsFailureEntry),
                Does.Contain("Настройки приложения в пакет не попали").And.Contain("Файла настроек ещё нет"));
            Assert.That(bundle.Describe(), Does.Contain(SpaceSnoopBundleSource.SettingsFailureEntry));
        }
    }

    [Test]
    public void Занятый_файл_настроек_доезжает_до_человека_строкой_об_отказе_чтения()
    {
        var path = Path.Combine(_root, TomlSettingsFile.PrimaryFileName);
        File.WriteAllText(path, "[wpf]");

        var operations = new PerformanceOperations(TestDiagnostics.Monitor());
        var source = new SpaceSnoopBundleSource(new MemorySettings(), operations, path);

        using var holder = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);

        var bundle = Bundle(Secrets(null), source.Collect().ToList());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(bundle.Entries.Select(static entry => entry.Name), Does.Not.Contain(SpaceSnoopBundleSource.SettingsEntry));
            Assert.That(Text(bundle.Entries, SpaceSnoopBundleSource.SettingsFailureEntry),
                Does.Contain("занят другой программой"));
            Assert.That(bundle.Describe(), Does.Contain(SpaceSnoopBundleSource.SettingsFailureEntry));
        }
    }

    private static DiagnosticsSecrets Secrets(string? token)
    {
        var settings = new MemorySettings();

        if (token is not null)
        {
            settings.SetValue(SettingsKeys.McpToken, token);
        }

        return new(DiagnosticsSecretRules.Merge([DiagnosticsSecretRules.Common, new SpaceSnoopSecretSource(settings).Collect()]));
    }

    private static IReadOnlyList<DiagnosticsEntry> Build(
        DiagnosticsSecrets secrets,
        IReadOnlyList<DiagnosticsEntry> logs,
        IReadOnlyList<DiagnosticsEntry>? contributed = null)
    {
        var payload = new DiagnosticsPayload(
            MachineProfile.Capture(new()),
            PerformanceSnapshot.Empty,
            PerformanceHistory.Empty,
            PerformanceHitches.Empty,
            "сводка",
            logs,
            contributed ?? []);

        return DiagnosticsBundle.Build(payload, secrets, null).Entries;
    }

    private static DiagnosticsBundle Bundle(DiagnosticsSecrets secrets, IReadOnlyList<DiagnosticsEntry> contributed)
    {
        var payload = new DiagnosticsPayload(
            MachineProfile.Capture(new()),
            PerformanceSnapshot.Empty,
            PerformanceHistory.Empty,
            PerformanceHitches.Empty,
            "сводка",
            [],
            contributed);

        return DiagnosticsBundle.Build(payload, secrets, null);
    }

    private static string Text(IReadOnlyList<DiagnosticsEntry> entries, string name)
    {
        return entries.Single(entry => entry.Name == name).Text;
    }
}
