using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;

namespace ContainerManagement.Views;

/// <summary>
/// Keeps a DataGrid's scrollbar in a gutter beside the rows, not over them.
/// </summary>
public static class TableScroll
{
    private const double Gutter = 28;

    public static void Attach(Control root)
    {
        root.AttachedToVisualTree += (_, _) =>
        {
            Apply(root);
            foreach (var grid in root.GetVisualDescendants().OfType<DataGrid>())
                grid.TemplateApplied += (_, _) => Inset(grid);
        };
    }

    public static void Apply(Control root)
    {
        foreach (var grid in root.GetVisualDescendants().OfType<DataGrid>())
            Inset(grid);
    }

    public static void Inset(DataGrid grid)
    {
        foreach (var viewer in grid.GetVisualDescendants().OfType<ScrollViewer>())
        {
            if (viewer.GetVisualAncestors().OfType<DataGrid>().FirstOrDefault() != grid)
                continue;

            viewer.AllowAutoHide = false;
            if (viewer.Padding.Right < Gutter)
                viewer.Padding = new Thickness(0, 0, Gutter, 0);
        }

        foreach (var bar in grid.GetVisualDescendants().OfType<ScrollBar>())
        {
            if (bar.GetVisualAncestors().OfType<DataGrid>().FirstOrDefault() != grid)
                continue;
            if (bar.Orientation != Avalonia.Layout.Orientation.Vertical)
                continue;

            bar.AllowAutoHide = false;
            if (bar.Margin.Left < 12)
                bar.Margin = new Thickness(12, 8, 8, 8);
        }
    }
}
