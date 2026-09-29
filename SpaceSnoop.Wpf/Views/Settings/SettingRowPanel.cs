namespace SpaceSnoop.Wpf.Views.Settings;

public sealed class SettingRowPanel : Panel
{
    public const double LabelMinWidth = 200;
    public const double StackedGap = 8;

    public static SettingRowPlan Plan(double available, double labelMin, double trailing)
    {
        if (double.IsPositiveInfinity(available) || available - trailing >= labelMin)
        {
            return new(false, Math.Max(0, available - trailing));
        }

        return new(true, Math.Max(0, available));
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var children = InternalChildren;

        if (children.Count == 0)
        {
            return new();
        }

        var (trailing, trailingHeight) = MeasureTrailing(new(double.PositiveInfinity, double.PositiveInfinity));
        var label = children[0];

        if (double.IsPositiveInfinity(availableSize.Width))
        {
            label.Measure(new(double.PositiveInfinity, availableSize.Height));

            return new(label.DesiredSize.Width + trailing, Math.Max(label.DesiredSize.Height, trailingHeight));
        }

        var plan = Plan(availableSize.Width, LabelMinWidth, trailing);
        label.Measure(new(plan.LabelWidth, double.PositiveInfinity));

        if (!plan.Stacked)
        {
            return new(availableSize.Width, Math.Max(label.DesiredSize.Height, trailingHeight));
        }

        var below = children.Count > 1 ? StackedGap + trailingHeight : 0;

        return new(availableSize.Width, label.DesiredSize.Height + below);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var children = InternalChildren;

        if (children.Count == 0)
        {
            return finalSize;
        }

        var trailing = 0d;
        var trailingHeight = 0d;

        for (var i = 1; i < children.Count; i++)
        {
            trailing += children[i].DesiredSize.Width;
            trailingHeight = Math.Max(trailingHeight, children[i].DesiredSize.Height);
        }

        var plan = Plan(finalSize.Width, LabelMinWidth, trailing);

        if (!plan.Stacked)
        {
            children[0].Arrange(new(0, 0, plan.LabelWidth, finalSize.Height));

            var x = plan.LabelWidth;

            for (var i = 1; i < children.Count; i++)
            {
                var width = children[i].DesiredSize.Width;
                children[i].Arrange(new(x, 0, width, finalSize.Height));
                x += width;
            }

            return finalSize;
        }

        var labelHeight = children[0].DesiredSize.Height;
        children[0].Arrange(new(0, 0, plan.LabelWidth, labelHeight));

        var left = children.Count > 1 && children[1] is FrameworkElement first ? -first.Margin.Left : 0;
        var top = labelHeight + StackedGap;

        for (var i = 1; i < children.Count; i++)
        {
            var width = Math.Max(0, Math.Min(children[i].DesiredSize.Width, finalSize.Width - left));
            children[i].Arrange(new(left, top, width, trailingHeight));
            left += width;
        }

        return finalSize;
    }

    private (double Width, double Height) MeasureTrailing(Size constraint)
    {
        var children = InternalChildren;
        var width = 0d;
        var height = 0d;

        for (var i = 1; i < children.Count; i++)
        {
            children[i].Measure(constraint);
            width += children[i].DesiredSize.Width;
            height = Math.Max(height, children[i].DesiredSize.Height);
        }

        return (width, height);
    }
}

public sealed record SettingRowPlan(bool Stacked, double LabelWidth);
