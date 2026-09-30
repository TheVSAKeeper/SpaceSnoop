using SpaceSnoop.Wpf.Views;
using System.Threading;
using System.Windows;
using System.Windows.Controls;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
[Apartment(ApartmentState.STA)]
public class FirstFitPanelTests
{
    private static readonly double[] Widths = [360, 30];

    [TestCase(double.PositiveInfinity, 0)]
    [TestCase(500, 0)]
    [TestCase(360, 0)]
    [TestCase(359, 1)]
    [TestCase(30, 1)]
    [TestCase(10, 1)]
    public void Показывается_первый_вариант_который_влезает_иначе_последний(double available, int expected)
    {
        Assert.That(FirstFitPanel.Pick(available, Widths), Is.EqualTo(expected));
    }

    [TestCase(500, Visibility.Visible, Visibility.Hidden)]
    [TestCase(200, Visibility.Hidden, Visibility.Visible)]
    public void Невыбранный_вариант_спрятан_и_не_занимает_места(double available, Visibility full, Visibility compact)
    {
        var wide = new Border { Width = Widths[0], Height = 20 };
        var narrow = new Border { Width = Widths[1], Height = 24 };
        var panel = new FirstFitPanel { Children = { wide, narrow } };

        panel.Measure(new(available, 40));
        panel.Arrange(new(new Size(available, 40)));

        var shown = full == Visibility.Visible ? wide : narrow;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(wide.Visibility, Is.EqualTo(full));
            Assert.That(narrow.Visibility, Is.EqualTo(compact));
            Assert.That(panel.DesiredSize, Is.EqualTo(new Size(shown.Width, shown.Height)), "Панель заняла место не того варианта, что показан.");
        }
    }
}
