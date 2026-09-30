namespace SpaceSnoop.Wpf.Views;

public sealed class FirstFitPanel : Panel
{
    private int _shown;

    public static int Pick(double available, IReadOnlyList<double> widths)
    {
        for (var i = 0; i < widths.Count; i++)
        {
            if (widths[i] <= available)
            {
                return i;
            }
        }

        return widths.Count - 1;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var children = InternalChildren;

        if (children.Count == 0)
        {
            return default;
        }

        var widths = new double[children.Count];

        for (var i = 0; i < children.Count; i++)
        {
            children[i].Measure(new(double.PositiveInfinity, availableSize.Height));
            widths[i] = children[i].DesiredSize.Width;
        }

        _shown = Pick(availableSize.Width, widths);

        for (var i = 0; i < children.Count; i++)
        {
            children[i].SetCurrentValue(VisibilityProperty, i == _shown ? Visibility.Visible : Visibility.Hidden);
        }

        var shown = children[_shown].DesiredSize;

        return new(Math.Min(shown.Width, availableSize.Width), shown.Height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var children = InternalChildren;

        for (var i = 0; i < children.Count; i++)
        {
            var size = i == _shown
                ? new Size(Math.Min(children[i].DesiredSize.Width, finalSize.Width), finalSize.Height)
                : default;

            children[i].Arrange(new(size));
        }

        return finalSize;
    }
}
