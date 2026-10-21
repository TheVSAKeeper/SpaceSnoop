using System.Windows.Input;
using System.Windows.Media;

namespace SpaceSnoop.Wpf.Views.Scan;

public partial class ScanStructurePanel : UserControl
{
    private const double RowPadding = 4;

    public ScanStructurePanel()
    {
        InitializeComponent();
    }

    public double TreeRowCapacity()
    {
        var chrome = Tree.IsVisible && FindDescendant<ScrollContentPresenter>(Tree) is { ActualHeight: > 0 } viewport
            ? Tree.ActualHeight - viewport.ActualHeight
            : 0;
        var height = TreeRow.ActualHeight - Tree.Margin.Top - chrome;
        var row = TreeRowHeight();

        return row > 0 ? Math.Max(0, height) / row : double.PositiveInfinity;
    }

    public double TreeRowHeight()
    {
        if (Tree.ItemContainerGenerator.ContainerFromIndex(0) is TreeViewItem item
            && item.Template?.FindName("SelectionBd", item) is FrameworkElement { ActualHeight: > 0 } row)
        {
            return row.ActualHeight;
        }

        return Tree.FontSize * Tree.FontFamily.LineSpacing + RowPadding;
    }

    private void OnTreeSelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (DataContext is ScanViewModel { IsRefreshingVisibility: false } vm)
        {
            vm.SelectedNode = e.NewValue as ScanNodeViewModel;
        }
    }

    private void OnTreeRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (FindAncestor<TreeViewItem>(e.OriginalSource as DependencyObject) is { } item)
        {
            item.IsSelected = true;
        }
    }

    private static T? FindAncestor<T>(DependencyObject? current)
        where T : DependencyObject
    {
        while (current is not null and not T)
        {
            current = VisualTreeHelper.GetParent(current);
        }

        return current as T;
    }

    private static T? FindDescendant<T>(DependencyObject parent)
        where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);

            if ((child as T ?? FindDescendant<T>(child)) is { } found)
            {
                return found;
            }
        }

        return null;
    }
}
