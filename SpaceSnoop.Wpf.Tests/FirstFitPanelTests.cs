using SpaceSnoop.Wpf.Views;
using System.Windows;
using System.Windows.Controls;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
[Apartment(ApartmentState.STA)]
public class FirstFitPanelTests
{
    private static readonly double[] Widths = [360, 30];

    [TestCase(300, Visibility.Visible, Visibility.Hidden)]
    [TestCase(120, Visibility.Hidden, Visibility.Visible)]
    public void Вертикальная_панель_выбирает_вариант_по_высоте(double available, Visibility full, Visibility compact)
    {
        var tall = new Border
        {
            Width = 30,
            Height = 240,
        };

        var low = new Border
        {
            Width = 30,
            Height = 34,
        };

        var panel = new FirstFitPanel
        {
            Orientation = Orientation.Vertical,
            Children =
            {
                tall,
                low,
            },
        };

        panel.Measure(new(48, available));
        panel.Arrange(new(new(48, available)));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(tall.Visibility, Is.EqualTo(full));
            Assert.That(low.Visibility, Is.EqualTo(compact));
            Assert.That(panel.DesiredSize.Height, Is.LessThanOrEqualTo(available));
        }
    }

    [TestCase(500, Visibility.Visible, Visibility.Hidden)]
    [TestCase(200, Visibility.Hidden, Visibility.Visible)]
    public void Невыбранный_вариант_спрятан_и_не_занимает_места(double available, Visibility full, Visibility compact)
    {
        var wide = new Border
        {
            Width = Widths[0],
            Height = 20,
        };

        var narrow = new Border
        {
            Width = Widths[1],
            Height = 24,
        };

        var panel = new FirstFitPanel
        {
            Children =
            {
                wide,
                narrow,
            },
        };

        panel.Measure(new(available, 40));
        panel.Arrange(new(new(available, 40)));

        var shown = full == Visibility.Visible ? wide : narrow;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(wide.Visibility, Is.EqualTo(full));
            Assert.That(narrow.Visibility, Is.EqualTo(compact));
            Assert.That(panel.DesiredSize, Is.EqualTo(new Size(shown.Width, shown.Height)), "Панель заняла место не того варианта, что показан.");
        }
    }

    [TestCase(HorizontalAlignment.Stretch, 0, 300)]
    [TestCase(HorizontalAlignment.Right, 200, 100)]
    [TestCase(HorizontalAlignment.Left, 0, 100)]
    public void Показанный_вариант_встаёт_по_своему_выравниванию_во_всю_ширину_панели(HorizontalAlignment alignment, double left, double width)
    {
        var shown = new Border
        {
            HorizontalAlignment = alignment,
            Child = new Border
            {
                Width = 100,
                Height = 20,
            },
        };

        var panel = new FirstFitPanel
        {
            Children =
            {
                shown,
            },
        };

        panel.Measure(new(300, 40));
        panel.Arrange(new(new(300, 40)));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(shown.ActualWidth, Is.EqualTo(width));
            Assert.That(shown.TranslatePoint(default, panel).X, Is.EqualTo(left));
        }
    }

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
}
