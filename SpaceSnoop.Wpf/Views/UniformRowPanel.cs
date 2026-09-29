namespace SpaceSnoop.Wpf.Views;

public sealed class UniformRowPanel : Panel
{
    private bool _stacked;

    public static bool Stacks(double available, int count, double widest)
    {
        return !double.IsPositiveInfinity(available) && count > 1 && widest * count > available;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var children = InternalChildren;
        var widest = 0d;
        var tallest = 0d;

        foreach (UIElement child in children)
        {
            child.Measure(new(double.PositiveInfinity, availableSize.Height));
            widest = Math.Max(widest, child.DesiredSize.Width);
            tallest = Math.Max(tallest, child.DesiredSize.Height);
        }

        _stacked = Stacks(availableSize.Width, children.Count, widest);

        if (!_stacked)
        {
            return new(widest * children.Count, tallest);
        }

        var height = 0d;

        foreach (UIElement child in children)
        {
            child.Measure(new(availableSize.Width, double.PositiveInfinity));
            height += child.DesiredSize.Height;
        }

        return new(availableSize.Width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var children = InternalChildren;

        if (children.Count == 0)
        {
            return finalSize;
        }

        if (!_stacked)
        {
            var cell = finalSize.Width / children.Count;

            for (var i = 0; i < children.Count; i++)
            {
                children[i].Arrange(new(i * cell, 0, cell, finalSize.Height));
            }

            return finalSize;
        }

        var top = 0d;

        foreach (UIElement child in children)
        {
            child.Arrange(new(0, top, finalSize.Width, child.DesiredSize.Height));
            top += child.DesiredSize.Height;
        }

        return finalSize;
    }
}
