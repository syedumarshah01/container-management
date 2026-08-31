using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace ContainerManagement.Views;

/// <summary>
/// Hosts a DataGrid and moves its real scrollbar beside the table so rows still scroll.
/// </summary>
public sealed class SideScroll : DockPanel
{
    private static readonly IBrush Thumb = new SolidColorBrush(Color.Parse("#661B2A4A"));
    private static readonly IBrush Track = new SolidColorBrush(Colors.Transparent);

    public SideScroll()
    {
        LastChildFill = true;
        AttachedToVisualTree += (_, _) => MoveBar();
        LayoutUpdated += (_, _) => MoveBar();
    }

    private void MoveBar()
    {
        var grid = Children.OfType<DataGrid>().FirstOrDefault();
        if (grid is null)
            return;

        grid.VerticalScrollBarVisibility = ScrollBarVisibility.Visible;

        var bar = grid.GetVisualDescendants()
            .OfType<ScrollBar>()
            .FirstOrDefault(b => b.Orientation == Orientation.Vertical && !ReferenceEquals(b.Parent, this));
        if (bar is null || bar.Parent is not Panel panel)
            return;

        panel.Children.Remove(bar);
        bar.AllowAutoHide = false;
        bar.Width = 8;
        bar.MinWidth = 8;
        bar.Margin = new Thickness(16, 8, 4, 8);
        bar.VerticalAlignment = VerticalAlignment.Stretch;
        bar.Opacity = 0.4;
        bar.Background = Track;
        bar.Resources["ScrollBarThumbFill"] = Thumb;
        bar.Resources["ScrollBarThumbFillPointerOver"] = Thumb;
        bar.Resources["ScrollBarPanningThumbBackground"] = Thumb;
        bar.Resources["ScrollBarTrackFill"] = Track;
        SetDock(bar, Dock.Right);
        Children.Insert(0, bar);
    }
}
