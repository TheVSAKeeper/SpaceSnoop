using SpaceSnoop.Core.Cleanup;
using SpaceSnoop.Wpf.ViewModels.Cleanup;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
public sealed class CleanupOutcomeTextTests
{
    [TestCase(DotNetCliOutcome.None, "Завершено с ошибкой")]
    [TestCase(DotNetCliOutcome.Succeeded, "Завершено")]
    [TestCase(DotNetCliOutcome.Failed, "Завершено с ошибкой")]
    [TestCase(DotNetCliOutcome.Cancelled, "Отменено")]
    [TestCase(DotNetCliOutcome.TimedOut, "Истекло время ожидания")]
    [TestCase(DotNetCliOutcome.LaunchFailed, "Не удалось выполнить")]
    [TestCase(DotNetCliOutcome.LoggingFailed, "Не удалось выполнить")]
    public void Незапущенная_команда_не_называется_завершённой(DotNetCliOutcome outcome, string expected)
    {
        Assert.That(CleanupOutcomeText.For(outcome), Is.EqualTo(expected));
    }
}
