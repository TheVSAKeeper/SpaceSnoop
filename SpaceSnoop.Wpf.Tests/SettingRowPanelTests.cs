using SpaceSnoop.Wpf.Views;
using SpaceSnoop.Wpf.Views.Settings;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
public class SettingRowPanelTests
{
    private const double Trailing = 326;

    [TestCase(760, false, 434)]
    [TestCase(526, false, 200)]
    [TestCase(525, true, 525)]
    [TestCase(320, true, 320)]
    [TestCase(double.PositiveInfinity, false, double.PositiveInfinity)]
    public void Контрол_уходит_под_подпись_когда_подписи_остаётся_меньше_минимума(double available, bool stacked, double labelWidth)
    {
        var plan = SettingRowPanel.Plan(available, SettingRowPanel.LabelMinWidth, Trailing);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.Stacked, Is.EqualTo(stacked));
            Assert.That(plan.LabelWidth, Is.EqualTo(labelWidth));
        }
    }

    [TestCase(510, 3, 170, false)]
    [TestCase(509, 3, 170, true)]
    [TestCase(100, 1, 170, false)]
    [TestCase(double.PositiveInfinity, 3, 170, false)]
    public void Сегменты_встают_столбиком_только_когда_не_влезают_в_ряд(double available, int count, double widest, bool stacked)
    {
        Assert.That(UniformRowPanel.Stacks(available, count, widest), Is.EqualTo(stacked));
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void Столбиком_последний_контрол_не_выходит_за_правый_край()
    {
        var control = new Border { Child = new Border { Width = 280, Height = 20 } };
        var reset = new Border { Child = new Border { Width = 24, Height = 20 } };
        var panel = new SettingRowPanel();
        panel.Children.Add(new Border { Height = 20 });
        panel.Children.Add(control);
        panel.Children.Add(reset);

        panel.Measure(new(300, double.PositiveInfinity));
        panel.Arrange(new(0, 0, 300, panel.DesiredSize.Height));

        Assert.That(LayoutInformation.GetLayoutSlot(reset).Right, Is.LessThanOrEqualTo(300));
    }
}
