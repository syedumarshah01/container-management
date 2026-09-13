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

    // The whole book, in the eight figures the shop opens the day with. Nothing here is a restatement of
    // anything else: each is the figure the page behind it already shows, added up once in ReportService.
    [ObservableProperty] private string bookContainers = "—";
    [ObservableProperty] private string bookSales = "—";
    [ObservableProperty] private string bookMarket = "—";
    [ObservableProperty] private string bookStock = "—";
    [ObservableProperty] private string bookProfit = "—";
    [ObservableProperty] private string hint = "";
    [ObservableProperty] private string lastBackup = "None yet";

    public ObservableCollection<HomeDayRow> Days { get; } = new();

    public override async Task LoadAsync()
    {
        Hint = DateTime.Today.ToString("MMMM yyyy");
        LastBackup = _backups.ListBackups().FirstOrDefault()?.WhenText ?? "None yet";

        var book = await _reports.GetDashboardAsync();
        BookContainers = book.TotalContainers.ToString();
        BookSales = Money.Pkr(book.TotalRevenue);
        BookMarket = Money.Pkr(book.MoneyInMarket);
        BookStock = Money.Pkr(book.InventoryValue);
        BookProfit = Money.Pkr(book.TotalProfit);
        // Purchases, expenses and what is owed on bills are left off this card: they belong to the pages that
        // hold them - We Owe for what the suppliers were billed, the Expenses page for the till's own bills,
        // and To collect for the bills still owing. The book-wide sums stay in the model, where the checks
        // read them against the figures that are shown here.

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
