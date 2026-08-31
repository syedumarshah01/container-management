using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ContainerManagement.Models;
using ContainerManagement.Services;

namespace ContainerManagement.ViewModels;

public partial class DashboardViewModel : ViewModelBase
{
    private readonly ReportService _reports;
    private readonly BackupService _backups;
    private readonly IAppShell _shell;

    public DashboardViewModel(ReportService reports, BackupService backups, IAppShell shell)
    {
        _reports = reports;
        _backups = backups;
        _shell = shell;
    }

    [ObservableProperty] private string inventoryValue = "—";
    [ObservableProperty] private string totalSale = "—";
    [ObservableProperty] private string totalProfit = "—";
    [ObservableProperty] private string profitLabel = "Profit this month";
    [ObservableProperty] private string hint = "";
    [ObservableProperty] private DateTimeOffset? fromDate;
    [ObservableProperty] private DateTimeOffset? toDate;
    [ObservableProperty] private string lowStockCount = "0";
    [ObservableProperty] private string lastBackup = "None yet";
    [ObservableProperty] private InventoryRow? selectedLowStock;

    public ObservableCollection<InventoryRow> LowStock { get; } = new();

    public override async Task LoadAsync()
    {
        var start = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        FromDate ??= new DateTimeOffset(start);
        ToDate ??= new DateTimeOffset(DateTime.Today);

        var dash = await _reports.GetDashboardAsync();
        InventoryValue = Money.Pkr(dash.InventoryValue);
        Hint = $"{dash.SalesThisMonth} sales this month · {dash.CustomerCount} customers";

        LowStock.Clear();
        foreach (var row in dash.LowStockItems)
            LowStock.Add(row);
        LowStockCount = dash.LowStockCount.ToString();

        LastBackup = _backups.ListBackups().FirstOrDefault()?.WhenText ?? "None yet";

        await ApplyAsync();
    }

    [RelayCommand]
    private async Task ApplyAsync()
    {
        var from = FromDate?.DateTime;
        var to = ToDate?.DateTime;
        var list = await _reports.GetContainerProfitsAsync(from, to);
        TotalSale = Money.Pkr(list.Sum(r => r.Revenue));
        TotalProfit = Money.Pkr(list.Sum(r => r.Profit));
        ProfitLabel = IsThisMonth(from, to) ? "Profit this month" : "Profit";
    }

    [RelayCommand] private void GoNewSale() => _shell.GoNewSale();
    [RelayCommand] private void GoStock() => _shell.GoInventory();
    [RelayCommand] private void GoBackup() => _shell.GoBackup();

    private static bool IsThisMonth(DateTime? from, DateTime? to)
    {
        var start = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        return from?.Date == start && (to is null || to.Value.Date >= DateTime.Today);
    }
}
