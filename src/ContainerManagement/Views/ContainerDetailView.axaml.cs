using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace ContainerManagement.Views;

public partial class ContainerDetailView : UserControl
{
    private static readonly IBrush PageThumb = new SolidColorBrush(Color.Parse("#1B2A4A"));
    private static readonly IBrush PageTrack = new SolidColorBrush(Color.Parse("#D9D6CE"));

    public ContainerDetailView()
    {
        InitializeComponent();
        SizeChanged += (_, e) => LayoutPage(e.NewSize);
        PageScroll.TemplateApplied += (_, _) => LayoutPage(Bounds.Size);
    }

    private void LayoutPage(Size size)
    {
        if (size.Height <= 0)
            return;

        MainPane.Height = size.Height;

        foreach (var bar in PageScroll.GetVisualDescendants().OfType<ScrollBar>())
        {
            if (!ReferenceEquals(bar.TemplatedParent, PageScroll))
                continue;
            if (bar.Orientation != Orientation.Vertical)
                continue;

            bar.AllowAutoHide = false;
            bar.VerticalAlignment = VerticalAlignment.Center;
            bar.Height = Math.Clamp(size.Height * 0.4, 160, 260);
            bar.Width = 12;
            bar.MinWidth = 12;
            bar.Margin = new Thickness(0, 0, 4, 0);
            bar.Opacity = 1;
            bar.Background = PageTrack;
            bar.Resources["ScrollBarThumbFill"] = PageThumb;
            bar.Resources["ScrollBarThumbFillPointerOver"] = PageThumb;
            bar.Resources["ScrollBarPanningThumbBackground"] = PageThumb;
            bar.Resources["ScrollBarTrackFill"] = PageTrack;
        }
    }
}
