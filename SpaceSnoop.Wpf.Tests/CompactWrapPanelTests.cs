using SpaceSnoop.Wpf.Views;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
[Apartment(ApartmentState.STA)]
public class CompactWrapPanelTests
{
    private const CompactSlot N = CompactSlot.None;
    private const CompactSlot F = CompactSlot.First;
    private const CompactSlot S = CompactSlot.Second;

    private static readonly double[] Widths = [100, 80, 60, 70];
    private static readonly bool[] Optional = [false, true, false, true];
    private static readonly bool[] NoKeep = [false, false, false, false];

    [TestCase(double.PositiveInfinity, new[] { F, F, F, F })]
    [TestCase(310, new[] { F, F, F, F })]
    [TestCase(309, new[] { F, F, F, N })]
    [TestCase(240, new[] { F, F, F, N })]
    [TestCase(239, new[] { F, N, F, N })]
    [TestCase(159, new[] { F, N, N, N })]
    [TestCase(10, new[] { F, N, N, N })]
    public void В_одну_строку_первыми_уходят_необязательные_с_конца_потом_остальные_с_конца(double available, CompactSlot[] expected)
    {
        Assert.That(CompactWrapPanel.Plan(available, Widths, Optional, NoKeep), Is.EqualTo(expected));
    }

    [TestCase(310, new[] { F, F, F, F })]
    [TestCase(229, new[] { F, N, N, F })]
    [TestCase(169, new[] { F, N, F, S })]
    [TestCase(90, new[] { F, N, N, S })]
    public void Предупреждение_не_снимается_а_если_не_влезает_уходит_на_вторую_строку(double available, CompactSlot[] expected)
    {
        bool[] optional = [false, true, false, false];
        bool[] keep = [false, false, false, true];

        Assert.That(CompactWrapPanel.Plan(available, Widths, optional, keep), Is.EqualTo(expected));
    }

    [Test]
    public void Скрытое_предупреждение_второй_строки_не_заводит()
    {
        double[] widths = [100, 80, 60, 0];
        bool[] keep = [false, false, false, true];

        Assert.That(CompactWrapPanel.Plan(150, widths, NoKeep, keep), Is.EqualTo(new[] { F, N, N, F }));
    }

    [TestCase(false, 2, true)]
    [TestCase(true, 1, false)]
    public void Не_влезшее_в_одну_строку_не_видно_и_недоступно(bool singleLine, int rows, bool lastEnabled)
    {
        var first = new Border { Width = 100, Height = 20 };
        var last = new Border { Width = 100, Height = 20 };
        var panel = new CompactWrapPanel { SingleLine = singleLine, Children = { first, last } };

        panel.Measure(new(150, double.PositiveInfinity));
        panel.Arrange(new(panel.DesiredSize));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(panel.DesiredSize.Height, Is.EqualTo(20 * rows));
            Assert.That(last.IsEnabled, Is.EqualTo(lastEnabled));
            Assert.That(LayoutInformation.GetLayoutSlot(last).Width > 0, Is.EqualTo(lastEnabled));
        }
    }

    [Test]
    public void Предупреждение_в_сжатом_виде_стоит_второй_строкой_и_доступно()
    {
        var first = new Border { Width = 100, Height = 20 };
        var metric = new Border { Width = 60, Height = 20 };
        var warning = new Border { Width = 120, Height = 24 };
        CompactWrapPanel.SetKeep(warning, true);
        var panel = new CompactWrapPanel { SingleLine = true, Children = { first, metric, warning } };

        panel.Measure(new(150, double.PositiveInfinity));
        panel.Arrange(new(panel.DesiredSize));

        var slot = LayoutInformation.GetLayoutSlot(warning);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(panel.DesiredSize.Height, Is.EqualTo(44), "Строк не две.");
            Assert.That(warning.IsEnabled, Is.True, "Предупреждение снято.");
            Assert.That(slot, Is.EqualTo(new Rect(0, 20, 120, 24)), "Предупреждение не на второй строке.");
            Assert.That(metric.IsEnabled, Is.False, "Метрика, не влезшая рядом с первой, осталась.");
        }
    }

    [Test]
    public void Первый_элемент_шире_панели_не_выходит_за_её_край()
    {
        var first = new TextBlock { Text = new string('ж', 80), TextTrimming = TextTrimming.CharacterEllipsis };
        var panel = new CompactWrapPanel { SingleLine = true, Children = { first } };

        panel.Measure(new(120, double.PositiveInfinity));
        panel.Arrange(new(new Size(120, panel.DesiredSize.Height)));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(panel.DesiredSize.Width, Is.LessThanOrEqualTo(120));
            Assert.That(LayoutInformation.GetLayoutSlot(first).Right, Is.LessThanOrEqualTo(120), "Первый элемент вышел за край панели.");
            Assert.That(first.RenderSize.Width, Is.LessThanOrEqualTo(120), "Первый элемент не ужат до ширины панели.");
        }
    }
}
