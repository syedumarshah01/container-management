using Avalonia.Controls;
using ContainerManagement.ViewModels;

namespace ContainerManagement.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Activated += (_, _) =>
        {
            if (DataContext is MainViewModel vm)
                vm.RequestLicenseCheck();
        };
    }
}
