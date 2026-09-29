using KeepShell.Bootstrap;
using KeepShell.Testing;
using SpaceSnoop.Core.Domain;
using SpaceSnoop.Wpf.Bootstrap;
using SpaceSnoop.Wpf.Bootstrap.Storage;
using SpaceSnoop.Wpf.ViewModels.Scan;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
public class ScanInspectorCollapseTests
{
    [SetUp]
    public void SetUp()
    {
        _settings = new MemorySettings();
        _factory = new(new(_settings), new(_settings), new FakeShellLauncher());
    }

    private ISettingsStore _settings = null!;
    private ScanNodeFactory _factory = null!;

    [Test]
    public void До_выбора_узла_детали_свёрнуты()
    {
        Assert.That(Create().IsInspectorCollapsed, Is.True);
    }

    [Test]
    public void Первый_выбор_раскрывает_детали()
    {
        var inspector = Create();

        inspector.Show(Node());

        Assert.That(inspector.IsInspectorCollapsed, Is.False);
    }

    [Test]
    public void Явное_сворачивание_переживает_следующий_выбор_и_сохраняется()
    {
        var inspector = Create();
        inspector.Show(Node());

        inspector.ToggleInspectorCollapsedCommand.Execute(null);
        inspector.Show(Node());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(inspector.IsInspectorCollapsed, Is.True);
            Assert.That(_settings.GetBool(SettingsKeys.ScanInspectorCollapsed), Is.True);
        }
    }

    [Test]
    public void После_перезапуска_сохранённое_сворачивание_не_даёт_раскрыть_при_выборе()
    {
        _settings.SetBool(SettingsKeys.ScanInspectorCollapsed, true);
        var inspector = Create();

        inspector.Show(Node());

        Assert.That(inspector.IsInspectorCollapsed, Is.True);
    }

    [Test]
    public void Снятие_выбора_не_сворачивает_раскрытые_детали()
    {
        var inspector = Create();
        inspector.Show(Node());

        inspector.Clear();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(inspector.IsInspectorCollapsed, Is.False);
            Assert.That(_settings.GetBool(SettingsKeys.ScanInspectorCollapsed), Is.False);
        }
    }

    [Test]
    public void Явное_раскрытие_снимает_запрет_на_раскрытие_при_выборе()
    {
        _settings.SetBool(SettingsKeys.ScanInspectorCollapsed, true);
        var inspector = Create();

        inspector.ToggleInspectorCollapsedCommand.Execute(null);
        inspector.Show(Node());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(inspector.IsInspectorCollapsed, Is.False);
            Assert.That(_settings.GetBool(SettingsKeys.ScanInspectorCollapsed), Is.False);
        }
    }

    private ScanInspectorViewModel Create()
    {
        return new(_settings, new FakeClipboard());
    }

    private ScanNodeViewModel Node()
    {
        var space = new DirectorySpace(@"C:\inspector", null, DateTime.Now, DateTime.Now);
        return _factory.CreateRoot(space, new());
    }
}
