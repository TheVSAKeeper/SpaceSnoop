using SpaceSnoop.Core;

namespace SpaceSnoop.Tests;

[TestFixture]
[NonParallelizable]
public class StorageMediaTests
{
    private static readonly TimeSpan Ceiling = TimeSpan.FromSeconds(10);

    private Func<string, StorageKind> _originalProbe = null!;

    [SetUp]
    public void SetUp()
    {
        _originalProbe = StorageMedia.Probe;
        StorageMedia.ResetCache();
    }

    [TearDown]
    public void TearDown()
    {
        StorageMedia.Probe = _originalProbe;
        StorageMedia.ResetCache();
    }

    [Test]
    public async Task Повторные_проверки_медленного_тома_делят_одну_пробу_в_полёте()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var calls = 0;

        StorageMedia.Probe = _ =>
        {
            Interlocked.Increment(ref calls);
            entered.Set();
            release.Wait(Ceiling);
            return StorageKind.Rotational;
        };

        var first = StorageMedia.LimitParallelismAsync(@"Z:\first", 8, TimeSpan.FromMilliseconds(50));
        Assert.That(entered.Wait(Ceiling), Is.True, "проба носителя так и не началась");

        var firstResult = await first;
        var secondResult = await StorageMedia.LimitParallelismAsync(@"z:\second", 8, TimeSpan.FromMilliseconds(50));
        var concurrentDetect = Task.Run(() => StorageMedia.Detect(@"Z:\third"));

        Assert.That(Volatile.Read(ref calls), Is.EqualTo(1));

        release.Set();

        var detectResult = await concurrentDetect;
        var afterRelease = await StorageMedia.LimitParallelismAsync(@"Z:\fourth", 8, TimeSpan.FromMilliseconds(50));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(firstResult, Is.EqualTo(8));
            Assert.That(secondResult, Is.EqualTo(8));
            Assert.That(detectResult, Is.EqualTo(StorageKind.Rotational));
            Assert.That(afterRelease, Is.EqualTo(1));
            Assert.That(Volatile.Read(ref calls), Is.EqualTo(1));
        }
    }
}
