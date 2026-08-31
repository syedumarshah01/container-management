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

    [ObservableProperty] private string monthSales = "—";
    [ObservableProperty] private string monthProfit = "—";
    [ObservableProperty] private string hint = "";
    [ObservableProperty] private string lastBackup = "None yet";

    public ObservableCollection<HomeDayRow> Days { get; } = new();

    public override async Task LoadAsync()
    {
        Hint = DateTime.Today.ToString("MMMM yyyy");
        LastBackup = _backups.ListBackups().FirstOrDefault()?.WhenText ?? "None yet";

        var (sales, profit, days) = await _reports.GetHomeMonthAsync();
        MonthSales = Money.Pkr(sales);
        MonthProfit = Money.Pkr(profit);
        Days.Clear();
        foreach (var d in days)
            Days.Add(d);
    }

    [RelayCommand] private void GoNewSale() => _shell.GoNewSale();
    [RelayCommand] private void GoBackup() => _shell.GoBackup();
}
