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
    [ObservableProperty] private string totalSale = "—";
    [ObservableProperty] private string totalProfit = "—";
    [ObservableProperty] private string profitLabel = "Profit this month";
    [ObservableProperty] private string hint = "";
    [ObservableProperty] private DateTimeOffset? fromDate;
    [ObservableProperty] private DateTimeOffset? toDate;

    public ObservableCollection<ContainerProfitRow> ContainerProfits { get; } = new();

    [ObservableProperty] private ContainerProfitRow? selectedContainer;

    public override async Task LoadAsync()
    {
        var start = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        FromDate ??= new DateTimeOffset(start);
        ToDate ??= new DateTimeOffset(DateTime.Today);

        var dash = await _reports.GetDashboardAsync();
        InventoryValue = Money.Pkr(dash.InventoryValue);
        Hint = $"{dash.SalesThisMonth} sales this month · {dash.CustomerCount} customers"
               + (string.IsNullOrWhiteSpace(dash.LowStockHint) ? "" : " · " + dash.LowStockHint);

        await ApplyAsync();
    }

    [RelayCommand]
    private async Task ApplyAsync()
    {
        var from = FromDate?.DateTime;
        var to = ToDate?.DateTime;
        var list = await _reports.GetContainerProfitsAsync(from, to);
        ContainerProfits.Clear();
        foreach (var r in list)
            ContainerProfits.Add(r);

        TotalSale = Money.Pkr(list.Sum(r => r.Revenue));
        TotalProfit = Money.Pkr(list.Sum(r => r.Profit));
        ProfitLabel = IsThisMonth(from, to) ? "Profit this month" : "Profit";
    }

    [RelayCommand]
    private void OpenContainer()
    {
        if (SelectedContainer is not null)
            _shell.OpenContainer(SelectedContainer.ContainerId);
    }

    [RelayCommand] private void GoNewSale() => _shell.GoNewSale();

    private static bool IsThisMonth(DateTime? from, DateTime? to)
    {
        var start = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        return from?.Date == start && (to is null || to.Value.Date >= DateTime.Today);
    }
}
