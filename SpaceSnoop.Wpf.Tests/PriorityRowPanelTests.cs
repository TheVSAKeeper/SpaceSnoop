using SpaceSnoop.Wpf.Views;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
public class PriorityRowPanelTests
{
    private const double NameMin = 96;
    private const double BarMin = 96;
    private const double BarMax = 172;
    private static readonly double[] Meta = [90, 75];

    [TestCase(800, true, true, BarMax)]
    [TestCase(433, true, true, BarMax)]
    [TestCase(400, false, true, BarMax)]
    [TestCase(300, false, false, BarMax)]
    [TestCase(220, false, false, 124)]
    [TestCase(150, false, false, BarMin)]
    [TestCase(80, false, false, 80)]
    public void Узкая_строка_сначала_теряет_счётчики_потом_сужает_полосу(double available, bool keepsFiles, bool keepsShare, double barWidth)
    {
        var plan = PriorityRowPanel.Plan(available, NameMin, Meta, BarMin, BarMax);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.Kept, Is.EqualTo(new[] { keepsFiles, keepsShare }));
            Assert.That(plan.TailWidth, Is.EqualTo(barWidth));
        }
    }

    [TestCase(800)]
    [TestCase(433)]
    [TestCase(300)]
    [TestCase(200)]
    [TestCase(80)]
    public void Полоса_с_размером_не_выходит_за_правый_край(double available)
    {
        var plan = PriorityRowPanel.Plan(available, NameMin, Meta, BarMin, BarMax);
        var kept = Meta.Where((_, i) => plan.Kept[i]).Sum();

        Assert.That(plan.FillWidth + kept + plan.TailWidth, Is.EqualTo(available).Within(0.001));
    }

    [TestCase(300, 60, BarMax)]
    [TestCase(180, 120, 120)]
    [TestCase(80, 60, 80)]
    [Apartment(ApartmentState.STA)]
    public void Настоящая_панель_держит_полосу_в_своих_границах_и_прячет_счётчики(double width, double sizeTextWidth, double barWidth)
    {
        var name = new Border { MinWidth = NameMin };
        var files = new Border { Width = Meta[0] };
        var share = new Border { Width = Meta[1] };
        var bar = new Border { MinWidth = BarMin, MaxWidth = BarMax, Child = new Border { Width = sizeTextWidth } };
        var panel = new PriorityRowPanel { Children = { name, files, share, bar } };

        panel.Measure(new(width, 20));
        panel.Arrange(new(0, 0, width, 20));

        var barSlot = LayoutInformation.GetLayoutSlot(bar);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(barSlot.Right, Is.LessThanOrEqualTo(width + 0.001));
            Assert.That(barSlot.Width, Is.EqualTo(barWidth).Within(0.001));
            Assert.That(LayoutInformation.GetLayoutSlot(files), Is.EqualTo(new Rect(0, 0, 0, 0)));
            Assert.That(LayoutInformation.GetLayoutSlot(share), Is.EqualTo(new Rect(0, 0, 0, 0)));
        }
    }
}
