using SpaceSnoop.Core.Domain;
using SpaceSnoop.Core.Mft;

namespace SpaceSnoop.Tests;

[TestFixture]
public class MftProgressPathTests
{
    [TestCase(@"C:\", TestName = "Путь прогресса от корня тома собирается как цепочка Path.Join")]
    [TestCase(@"C:\Windows", TestName = "Путь прогресса от подкаталога собирается как цепочка Path.Join")]
    public void Путь_прогресса_совпадает_с_цепочкой_Path_Join(string rootPath)
    {
        var root = new DirectorySpace(Path.GetFileName(rootPath), null, default, default);
        var system = new DirectorySpace("System32", root, default, default);
        var drivers = new DirectorySpace("drivers", system, default, default);

        var path = new MftProgressPath(root, rootPath).For(drivers);

        Assert.That(path, Is.EqualTo(Path.Join(Path.Join(rootPath, "System32"), "drivers")));
    }

    [Test]
    public void Путь_прогресса_корня_равен_пути_цели()
    {
        var root = new DirectorySpace("Windows", null, default, default);

        Assert.That(new MftProgressPath(root, @"C:\Windows").For(root), Is.EqualTo(@"C:\Windows"));
    }

    [Test]
    public void Путь_прогресса_не_пересобирается_на_каждый_каталог()
    {
        var root = new DirectorySpace("Windows", null, default, default);
        var system = new DirectorySpace("System32", root, default, default);
        var paths = new MftProgressPath(root, @"C:\Windows");

        var first = paths.For(root);
        var second = paths.For(system);

        Assert.That(second, Is.SameAs(first));
    }
}
