using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using ContainerManagement.ViewModels;

namespace ContainerManagement.Views;

/// <summary>
/// Keeps each page's view (and its scroll position) alive while that view-model is still in use.
/// </summary>
public class PageHost : UserControl
{
    public static readonly StyledProperty<ViewModelBase?> PageProperty =
        AvaloniaProperty.Register<PageHost, ViewModelBase?>(nameof(Page));

    private readonly ConditionalWeakTable<ViewModelBase, Control> _views = new();
    private readonly ViewLocator _locator = new();

    public ViewModelBase? Page
    {
        get => GetValue(PageProperty);
        set => SetValue(PageProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == PageProperty)
            Show(change.NewValue as ViewModelBase);
    }

    private void Show(ViewModelBase? vm)
    {
        if (vm is null)
        {
            Content = null;
            return;
        }

        if (!_views.TryGetValue(vm, out var view))
        {
            var inner = _locator.Build(vm) ?? new TextBlock { Text = "Missing page" };
            inner.DataContext = vm;
            inner.Margin = new Thickness(32, 28, 32, 16);
            var pageBrush = new SolidColorBrush(Color.Parse("#F4F3EF"));
            if (vm.FillsPage)
            {
                inner.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
                inner.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch;
                view = new Grid { Background = pageBrush, Children = { inner } };
            }
            else
            {
                view = new ScrollViewer
                {
                    Content = inner,
                    Background = pageBrush,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
                };
            }
            _views.Add(vm, view);
        }

        Content = view;
    }
}
