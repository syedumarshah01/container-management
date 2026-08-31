using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace ContainerManagement.Views;

/// <summary>
/// Hosts a DataGrid with its scrollbar beside the table, not over the rows.
/// </summary>
public sealed class SideScroll : DockPanel
{
    private static readonly IBrush Thumb = new SolidColorBrush(Color.Parse("#1B2A4A"));
    private static readonly IBrush Track = new SolidColorBrush(Color.Parse("#D9D6CE"));

    private readonly ScrollBar _bar;
    private ScrollViewer? _viewer;
    private bool _syncing;

    public SideScroll()
    {
        LastChildFill = true;
        _bar = new ScrollBar
        {
            Orientation = Orientation.Vertical,
            AllowAutoHide = false,
            Width = 12,
            MinWidth = 12,
            Margin = new Thickness(16, 8, 4, 8),
            Opacity = 1,
            Background = Track
        };
        _bar.Resources["ScrollBarThumbFill"] = Thumb;
        _bar.Resources["ScrollBarThumbFillPointerOver"] = Thumb;
        _bar.Resources["ScrollBarPanningThumbBackground"] = Thumb;
        _bar.Resources["ScrollBarTrackFill"] = Track;
        SetDock(_bar, Dock.Right);
        Children.Add(_bar);
        _bar.PropertyChanged += OnBarChanged;
        AttachedToVisualTree += (_, _) => Hook();
        LayoutUpdated += (_, _) =>
        {
            Hook();
            SyncBar();
        };
    }

    private DataGrid? Table => Children.OfType<DataGrid>().FirstOrDefault();

    private void Hook()
    {
        var grid = Table;
        if (grid is null)
            return;

        grid.VerticalScrollBarVisibility = ScrollBarVisibility.Hidden;
        grid.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;

        var viewer = grid.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        if (viewer is null || ReferenceEquals(viewer, _viewer))
            return;

        if (_viewer is not null)
            _viewer.ScrollChanged -= OnViewerScroll;

        _viewer = viewer;
        _viewer.AllowAutoHide = true;
        _viewer.VerticalScrollBarVisibility = ScrollBarVisibility.Hidden;
        _viewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        _viewer.Padding = new Thickness(0);
        _viewer.ScrollChanged += OnViewerScroll;
        SyncBar();
    }

    private void OnViewerScroll(object? sender, ScrollChangedEventArgs e)
    {
        if (!_syncing)
            SyncBar();
    }

    private void OnBarChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property != RangeBase.ValueProperty || _syncing || _viewer is null)
            return;
        _syncing = true;
        _viewer.Offset = new Vector(_viewer.Offset.X, _bar.Value);
        _syncing = false;
    }

    private void SyncBar()
    {
        if (_viewer is null)
            return;

        var overflow = Math.Max(0, _viewer.Extent.Height - _viewer.Viewport.Height);
        _syncing = true;
        _bar.Maximum = overflow;
        _bar.ViewportSize = Math.Max(1, _viewer.Viewport.Height);
        _bar.Value = Math.Clamp(_viewer.Offset.Y, 0, overflow);
        _bar.IsVisible = overflow > 1;
        _syncing = false;
    }
}
