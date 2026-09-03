using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace ContainerManagement.Views;

/// <summary>
/// Moves each DataGrid's scrollbar out of the table and into a gutter in the same card.
/// </summary>
public static class TableScroll
{
    public static void Attach(Control root)
    {
        root.AttachedToVisualTree += (_, _) => WrapAll(root);
        root.LayoutUpdated += (_, _) => WrapAll(root);
    }

    private static void WrapAll(Control root)
    {
        foreach (var grid in root.GetVisualDescendants().OfType<DataGrid>().ToList())
            Wrap(grid);
    }

    private static void Wrap(DataGrid grid)
    {
        if (grid.Parent is SideScroll or null)
            return;

        var parent = grid.Parent;
        var panel = parent as Panel;
        var index = panel?.Children.IndexOf(grid) ?? -1;
        var row = Grid.GetRow(grid);
        var column = Grid.GetColumn(grid);
        var rowSpan = Grid.GetRowSpan(grid);
        var colSpan = Grid.GetColumnSpan(grid);
        var dock = DockPanel.GetDock(grid);

        var wrap = new SideScroll
        {
            HorizontalAlignment = grid.HorizontalAlignment,
            VerticalAlignment = grid.VerticalAlignment,
            Margin = grid.Margin
        };
        grid.Margin = new Thickness(0);
        Grid.SetRow(wrap, row);
        Grid.SetColumn(wrap, column);
        Grid.SetRowSpan(wrap, rowSpan);
        Grid.SetColumnSpan(wrap, colSpan);
        DockPanel.SetDock(wrap, dock);

        switch (parent)
        {
            case Panel p:
                p.Children.Remove(grid);
                wrap.Children.Add(grid);
                p.Children.Insert(Math.Max(0, index), wrap);
                break;
            case Decorator d:
                d.Child = null;
                wrap.Children.Add(grid);
                d.Child = wrap;
                break;
            case ContentControl c when ReferenceEquals(c.Content, grid):
                c.Content = null;
                wrap.Children.Add(grid);
                c.Content = wrap;
                break;
        }
    }
}
