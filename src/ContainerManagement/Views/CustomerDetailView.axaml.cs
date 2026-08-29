using Avalonia.Controls;
using Avalonia.Input;
using ContainerManagement.ViewModels;

namespace ContainerManagement.Views;

public partial class CustomerDetailView : UserControl
{
    public CustomerDetailView() => InitializeComponent();

    private void OnInvoiceDoubleTap(object? sender, TappedEventArgs e)
        => (DataContext as CustomerDetailViewModel)?.OpenInvoiceCommand.Execute(null);
}
