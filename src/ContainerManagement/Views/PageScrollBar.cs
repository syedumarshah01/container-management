using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace ContainerManagement.Views;

/// <summary>
/// Short, centered page scrollbar with a gap from the content cards.
/// </summary>
public static class PageScrollBar
{
    private static readonly IBrush Thumb = new SolidColorBrush(Color.Parse("#1B2A4A"));
    private static readonly IBrush Track = new SolidColorBrush(Color.Parse("#D9D6CE"));

    public static ScrollViewer Wrap(Control inner, IBrush background)
    {
        var viewer = new ScrollViewer
        {
            Content = inner,
            Background = background,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Visible,
            AllowAutoHide = false
        };
        Attach(viewer);
        return viewer;
    }

    public static void Attach(ScrollViewer viewer)
    {
        viewer.Classes.Add("page");
        viewer.AllowAutoHide = false;
        viewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        viewer.VerticalScrollBarVisibility = ScrollBarVisibility.Visible;
        viewer.TemplateApplied += (_, _) => Style(viewer);
        viewer.SizeChanged += (_, _) => Style(viewer);
        if (viewer.IsLoaded)
            Style(viewer);
    }

    public static void Style(ScrollViewer viewer)
    {
        var height = viewer.Bounds.Height;
        if (height <= 0)
            return;

        foreach (var bar in viewer.GetVisualDescendants().OfType<ScrollBar>())
        {
            if (!ReferenceEquals(bar.TemplatedParent, viewer))
                continue;
            if (bar.Orientation != Orientation.Vertical)
                continue;

            bar.AllowAutoHide = false;
            bar.VerticalAlignment = VerticalAlignment.Center;
            bar.Height = Math.Clamp(height * 0.4, 160, 260);
            bar.Width = 12;
            bar.MinWidth = 12;
            bar.Margin = new Thickness(20, 0, 8, 0);
            bar.Opacity = 1;
            bar.Background = Track;
            bar.Resources["ScrollBarThumbFill"] = Thumb;
            bar.Resources["ScrollBarThumbFillPointerOver"] = Thumb;
            bar.Resources["ScrollBarPanningThumbBackground"] = Thumb;
            bar.Resources["ScrollBarTrackFill"] = Track;
        }
    }
}
