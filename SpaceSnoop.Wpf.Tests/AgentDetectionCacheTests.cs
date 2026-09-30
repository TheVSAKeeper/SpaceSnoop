using KeepShell.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using SpaceSnoop.Wpf.Agent;
using SpaceSnoop.Wpf.ViewModels.Settings;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
public class AgentDetectionCacheTests
{
    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(10);

    [Test]
    public async Task В_кэше_остаётся_результат_пробы_стартовавшей_последней_даже_если_ранняя_финишировала_позже()
    {
        var early = new AgentCliInfo(@"C:\ранний\claude.exe", "1.0.0");
        var late = new AgentCliInfo(@"C:\поздний\claude.exe", "2.0.0");

        using var earlyEntered = new ManualResetEventSlim();
        using var releaseEarly = new ManualResetEventSlim();
        var probes = 0;

        var preferences = new AgentPreferences(new MemorySettings());

        using var backend = new ClaudeAgentBackend(preferences, NullLogger<ClaudeAgentBackend>.Instance)
        {
            DetectCli = (_, _, _) =>
            {
                if (Interlocked.Increment(ref probes) > 1)
                {
                    return late;
                }

                earlyEntered.Set();
                releaseEarly.Wait(WaitLimit);
                return early;
            },
        };

        var earlyProbe = Task.Run(backend.Detect);
        Assert.That(earlyEntered.Wait(WaitLimit), Is.True);

        var lateResult = backend.Detect();

        releaseEarly.Set();
        var earlyResult = await earlyProbe.WaitAsync(WaitLimit);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(earlyResult, Is.SameAs(early));
            Assert.That(lateResult, Is.SameAs(late));
            Assert.That(backend.Detect(), Is.SameAs(late));
            Assert.That(probes, Is.EqualTo(2));
        }
    }
}
