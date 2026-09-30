namespace SpaceSnoop.Wpf.Views;

public sealed class FirstFitPanel : Panel
{
    public static readonly DependencyProperty OrientationProperty = DependencyProperty.Register(
        nameof(Orientation),
        typeof(Orientation),
        typeof(FirstFitPanel),
        new FrameworkPropertyMetadata(Orientation.Horizontal, FrameworkPropertyMetadataOptions.AffectsMeasure));

    private int _shown;

    public Orientation Orientation
    {
        get => (Orientation)GetValue(OrientationProperty);
        set => SetValue(OrientationProperty, value);
    }

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

        var vertical = Orientation == Orientation.Vertical;
        var constraint = vertical
            ? new Size(availableSize.Width, double.PositiveInfinity)
            : new Size(double.PositiveInfinity, availableSize.Height);
        var extents = new double[children.Count];

        for (var i = 0; i < children.Count; i++)
        {
            children[i].Measure(constraint);
            extents[i] = vertical ? children[i].DesiredSize.Height : children[i].DesiredSize.Width;
        }

        _shown = Pick(vertical ? availableSize.Height : availableSize.Width, extents);

        for (var i = 0; i < children.Count; i++)
        {
            children[i].SetCurrentValue(VisibilityProperty, i == _shown ? Visibility.Visible : Visibility.Hidden);
        }

        var shown = children[_shown].DesiredSize;

        return new(Math.Min(shown.Width, availableSize.Width), Math.Min(shown.Height, availableSize.Height));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var children = InternalChildren;

        for (var i = 0; i < children.Count; i++)
        {
            var size = i == _shown ? finalSize : default;

            children[i].Arrange(new(size));
        }

        return finalSize;
    }
}
