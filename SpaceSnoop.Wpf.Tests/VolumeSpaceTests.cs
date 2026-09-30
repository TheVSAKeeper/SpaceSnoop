using SpaceSnoop.Wpf.Bootstrap.Platform;
using System.IO;

namespace SpaceSnoop.Wpf.Tests;

[NonParallelizable]
public class VolumeSpaceTests
{
    [Test]
    public async Task Замер_после_тайм_аута_ждёт_висящий_а_не_заводит_новый()
    {
        using var reader = new BlockingVolumeReader(42);
        var path = UniquePath();

        var timedOut = await VolumeSpace.ReadFreeAsync(path);
        var retry = VolumeSpace.ReadFreeAsync(path);

        Assert.That(reader.Calls, Is.EqualTo(1));

        reader.Release();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(timedOut, Is.Null);
            Assert.That(await retry, Is.EqualTo(42));
            Assert.That(reader.Calls, Is.EqualTo(1));
        }
    }

    [Test]
    public async Task Замер_другого_пути_на_том_же_томе_ждёт_конца_висящего()
    {
        using var reader = new BlockingVolumeReader(42);
        var first = VolumeSpace.ReadFreeAsync(UniquePath());
        reader.WaitEntered();

        var second = VolumeSpace.ReadFreeAsync(UniquePath());
        await Task.Delay(200);

        Assert.That(reader.Calls, Is.EqualTo(1));

        reader.Release();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(await first, Is.EqualTo(42));
            Assert.That(await second, Is.EqualTo(42));
            Assert.That(reader.Calls, Is.EqualTo(2));
        }
    }

    [Test]
    public async Task За_висящим_замером_ждёт_не_больше_одного_пути_остальные_сразу_без_ответа()
    {
        using var reader = new BlockingVolumeReader(42);
        var hung = VolumeSpace.ReadFreeAsync(UniquePath());
        reader.WaitEntered();

        var waitingPath = UniquePath();
        var waiting = VolumeSpace.ReadFreeAsync(waitingPath);
        var sameAsWaiting = VolumeSpace.ReadFreeAsync(waitingPath);
        var third = VolumeSpace.ReadFreeAsync(UniquePath());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(third.IsCompleted, Is.True);
            Assert.That(await third, Is.Null);
        }

        reader.Release();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(await hung, Is.EqualTo(42));
            Assert.That(await waiting, Is.EqualTo(42));
            Assert.That(await sameAsWaiting, Is.EqualTo(42));
            Assert.That(reader.Calls, Is.EqualTo(2));
        }
    }

    private static string UniquePath()
    {
        return Path.Combine(Path.GetTempPath(), "spacesnoop-volume", Guid.NewGuid().ToString("N"));
    }
}
