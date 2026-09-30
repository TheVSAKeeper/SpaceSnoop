using Microsoft.Extensions.Logging;
using SpaceSnoop.Core;
using System.Collections.Concurrent;

namespace SpaceSnoop.Tests;

[TestFixture]
public class NetworkDrivesTests
{
    private const int BadNetPath = 53;

    [TestCase("z", @"\\srv\share", "DOMAIN\\user", "Microsoft Windows Network", "Z:", "DOMAIN\\user", "Microsoft Windows Network")]
    [TestCase("Y", @"\\srv\share", "", null, "Y:", null, null)]
    [TestCase("Y", @"\\srv\share", null, "  ", "Y:", null, null)]
    public void Подключение_читается_из_ключа_буквы(
        string keyName,
        string remotePath,
        string? userName,
        string? providerName,
        string expectedLocalName,
        string? expectedUserName,
        string? expectedProviderName)
    {
        var values = new Dictionary<string, string?>
        {
            ["RemotePath"] = remotePath,
            ["UserName"] = userName,
            ["ProviderName"] = providerName,
        };

        var drive = NetworkDrives.Parse(keyName, name => values.GetValueOrDefault(name));

        Assert.That(drive, Is.EqualTo(new PersistentDrive(expectedLocalName, remotePath, expectedUserName, expectedProviderName)));
    }

    [TestCase("ZZ", @"\\srv\share")]
    [TestCase("1", @"\\srv\share")]
    [TestCase("", @"\\srv\share")]
    [TestCase("Z", "")]
    [TestCase("Z", null)]
    public void Ключ_не_буквы_или_без_пути_пропускается(string keyName, string? remotePath)
    {
        var drive = NetworkDrives.Parse(keyName, name => name == "RemotePath" ? remotePath : null);

        Assert.That(drive, Is.Null);
    }

    [Test]
    public void Буква_занятая_в_сеансе_не_выбирается_без_учёта_регистра()
    {
        PersistentDrive[] persistent = [Drive("Z:"), Drive("Y:"), Drive("X:")];

        var missing = NetworkDrives.SelectMissing(persistent, [@"C:\", @"z:\", @"X:\"]);

        Assert.That(missing.Select(drive => drive.LocalName), Is.EqualTo(new[] { "Y:" }));
    }

    [Test]
    public async Task Без_повышения_ни_реестр_ни_сеть_не_трогаются()
    {
        var touched = false;

        var results = await NetworkDrives.RestoreAsync(false,
            () =>
            {
                touched = true;
                return [Drive("Z:")];
            },
            () =>
            {
                touched = true;
                return [];
            },
            _ =>
            {
                touched = true;
                return 0;
            },
            new CapturingLogger());

        Assert.Multiple(() =>
        {
            Assert.That(results, Is.Empty);
            Assert.That(touched, Is.False);
        });
    }

    [Test]
    public async Task Отказ_одной_буквы_пишется_строкой_и_не_мешает_остальным()
    {
        var logger = new CapturingLogger();
        var connected = new ConcurrentBag<string>();

        var results = await NetworkDrives.RestoreAsync(true,
            () => [Drive("Z:", @"\\down\share"), Drive("Y:"), Drive("C:"), Drive("X:")],
            () => [@"C:\"],
            drive =>
            {
                connected.Add(drive.LocalName);
                return drive.LocalName == "Z:" ? BadNetPath : 0;
            },
            logger);

        var failure = logger.Entries.Single(entry => entry.Level == LogLevel.Warning);

        Assert.Multiple(() =>
        {
            Assert.That(connected, Is.EquivalentTo(new[] { "Z:", "Y:", "X:" }));
            Assert.That(results.Where(result => result.Succeeded).Select(result => result.Drive.LocalName), Is.EquivalentTo(new[] { "Y:", "X:" }));
            Assert.That(failure.Message, Does.Contain("Z:").And.Contain(@"\\down\share").And.Contain(BadNetPath.ToString()));
            Assert.That(logger.Entries.Count(entry => entry.Level == LogLevel.Information), Is.EqualTo(2));
        });
    }

    private static PersistentDrive Drive(string localName, string remotePath = @"\\srv\share")
    {
        return new(localName, remotePath, null, null);
    }

    private sealed record LogEntry(LogLevel Level, string Message);

    private sealed class CapturingLogger : ILogger
    {
        private readonly ConcurrentQueue<LogEntry> _entries = new();

        public IReadOnlyList<LogEntry> Entries => [.. _entries];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            _entries.Enqueue(new(logLevel, formatter(state, exception)));
        }
    }
}
