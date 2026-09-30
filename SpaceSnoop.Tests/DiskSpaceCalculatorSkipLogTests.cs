using Microsoft.Extensions.Logging;
using SpaceSnoop.Core;
using System.Security.AccessControl;
using System.Security.Principal;

namespace SpaceSnoop.Tests;

[TestFixture]
public class DiskSpaceCalculatorSkipLogTests
{
    private const int LockedCount = 3;

    private readonly List<DirectoryInfo> _locked = [];
    private FileSystemAccessRule _deny = null!;
    private string _root = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), $"SpaceSnoopSkipLog_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);

        _deny = new(WindowsIdentity.GetCurrent().User!, FileSystemRights.ListDirectory, AccessControlType.Deny);

        for (var i = 0; i < LockedCount; i++)
        {
            var directory = Directory.CreateDirectory(Path.Combine(_root, $"locked{i}"));
            var security = directory.GetAccessControl();
            security.AddAccessRule(_deny);
            directory.SetAccessControl(security);
            _locked.Add(directory);
        }

        Assume.That(() => Directory.EnumerateFileSystemEntries(_locked[0].FullName).Any(),
            NUnit.Framework.Throws.InstanceOf<UnauthorizedAccessException>(),
            "запрет чтения каталога в этом окружении не действует (процесс обходит ACL)");
    }

    [TearDown]
    public void TearDown()
    {
        foreach (var directory in _locked)
        {
            var security = directory.GetAccessControl();
            security.RemoveAccessRule(_deny);
            directory.SetAccessControl(security);
        }

        _locked.Clear();
        Directory.Delete(_root, true);
    }

    [TestCase(true)]
    [TestCase(false)]
    public void Стек_пропуска_пишется_один_раз_за_скан(bool multithreaded)
    {
        var logger = new CapturingLogger<DiskSpaceCalculator>();
        var calculator = new DiskSpaceCalculator(logger);
        var progress = new ScanProgress();

        _ = multithreaded
            ? calculator.CalculateMultithreaded(new(_root), 4, progress, CancellationToken.None)
            : calculator.Calculate(new(_root), progress, CancellationToken.None);

        var skips = logger.Entries.Where(entry => entry.Level == LogLevel.Warning).ToList();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(progress.CreateSnapshot().DirectoriesFailed, Is.EqualTo(LockedCount));
            Assert.That(skips, Has.Count.EqualTo(LockedCount));
            Assert.That(skips.Count(entry => entry.Exception is not null), Is.EqualTo(1));
        }
    }

    [Test]
    public void Каждый_скан_получает_свой_стек()
    {
        var logger = new CapturingLogger<DiskSpaceCalculator>();
        var calculator = new DiskSpaceCalculator(logger);

        calculator.Calculate(new(_root));
        calculator.Calculate(new(_root));

        Assert.That(logger.Entries.Count(entry => entry.Exception is not null), Is.EqualTo(2));
    }

    [Test]
    public void Сравнение_пишет_стек_один_раз_и_итог_числом()
    {
        var right = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"SpaceSnoopSkipLogRight_{Guid.NewGuid():N}"));

        try
        {
            var logger = new CapturingLogger<DirectoryComparer>();
            var comparer = new DirectoryComparer(new(""), logger);

            comparer.Compare(_root, right.FullName, CancellationToken.None);
            comparer.Compare(_root, right.FullName, CancellationToken.None);

            var skips = logger.Entries.Where(entry => entry.EventId is 1512 or 1524).ToList();
            var totals = logger.Entries.Where(entry => entry.EventId == 1525).ToList();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(skips, Has.Count.EqualTo(2 * LockedCount));
                Assert.That(skips.Count(entry => entry.Exception is not null), Is.EqualTo(2));
                Assert.That(totals.Select(entry => entry.Message), Is.All.Contains(LockedCount.ToString()));
                Assert.That(totals, Has.Count.EqualTo(2));
            }
        }
        finally
        {
            right.Delete(true);
        }
    }

    private sealed record LogEntry(LogLevel Level, int EventId, Exception? Exception, string Message);

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        private readonly Lock _lock = new();
        private readonly List<LogEntry> _entries = [];

        public IReadOnlyList<LogEntry> Entries
        {
            get
            {
                lock (_lock)
                {
                    return [.. _entries];
                }
            }
        }

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
            lock (_lock)
            {
                _entries.Add(new(logLevel, eventId.Id, exception, formatter(state, exception)));
            }
        }
    }
}
