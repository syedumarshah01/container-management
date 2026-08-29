using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ContainerManagement.Models;
using ContainerManagement.Services;

namespace ContainerManagement.ViewModels;

public partial class DashboardViewModel : ViewModelBase
{
    private readonly ReportService _reports;
    private readonly IAppShell _shell;

    public DashboardViewModel(ReportService reports, IAppShell shell)
    {
        _reports = reports;
        _shell = shell;
    }

    [ObservableProperty] private string inventoryValue = "—";
    [ObservableProperty] private string moneyInMarket = "—";
    [ObservableProperty] private string totalProfit = "—";
    [ObservableProperty] private string containersSummary = "—";
    [ObservableProperty] private string hint = "";

    public ObservableCollection<ReceivableRow> TopReceivables { get; } = new();
    public ObservableCollection<Sale> RecentSales { get; } = new();
    public ObservableCollection<ContainerProfitRow> ContainerProfits { get; } = new();

    [ObservableProperty] private ReceivableRow? selectedReceivable;
    [ObservableProperty] private Sale? selectedSale;
    [ObservableProperty] private ContainerProfitRow? selectedContainer;

    public override async Task LoadAsync()
    {
        var vm = await _reports.GetDashboardAsync();
        InventoryValue = Money.Pkr(vm.InventoryValue);
        MoneyInMarket = Money.Pkr(vm.MoneyInMarket);
        TotalProfit = Money.Pkr(vm.TotalProfit);
        ContainersSummary = $"{vm.OpenContainers} / {vm.TotalContainers}";
        Hint = $"{vm.SalesThisMonth} sales this month · {vm.CustomerCount} customers"
               + (string.IsNullOrWhiteSpace(vm.LowStockHint) ? "" : " · " + vm.LowStockHint);

        Replace(TopReceivables, vm.TopReceivables);
        Replace(RecentSales, vm.RecentSales);
        Replace(ContainerProfits, vm.ContainerProfits);
    }

    [RelayCommand]
    private void OpenReceivable()
    {
        if (SelectedReceivable is not null)
            _shell.OpenCustomer(SelectedReceivable.CustomerId);
    }

    [RelayCommand]
    private void OpenSale()
    {
        if (SelectedSale is not null)
            _shell.OpenSale(SelectedSale.Id);
    }

    [RelayCommand]
    private void OpenContainer()
    {
        if (SelectedContainer is not null)
            _shell.OpenContainer(SelectedContainer.ContainerId);
    }

    [RelayCommand] private void GoNewContainer() => _shell.GoContainers();
    [RelayCommand] private void GoNewSale() => _shell.GoNewSale();

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> source)
    {
        target.Clear();
        foreach (var item in source)
            target.Add(item);
    }
}
