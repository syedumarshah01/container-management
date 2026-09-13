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
    [ObservableProperty] private string bookPurchases = "—";
    [ObservableProperty] private string bookExpenses = "—";
    [ObservableProperty] private string bookExpensesSplit = "";
    [ObservableProperty] private string bookMarket = "—";
    [ObservableProperty] private string bookOutstanding = "—";
    [ObservableProperty] private string bookStock = "—";
    [ObservableProperty] private string bookProfit = "—";
    [ObservableProperty] private string bookNote = "";
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
        BookPurchases = Money.Pkr(book.TotalPurchases);
        BookExpenses = Money.Pkr(book.TotalExpenses);
        BookExpensesSplit = Money.Pkr(book.ContainerExpenses) + " on containers · "
                            + Money.Pkr(book.ShopExpenses) + " at the shop";
        BookMarket = Money.Pkr(book.MoneyInMarket);
        BookOutstanding = Money.Pkr(book.Outstanding);
        BookStock = Money.Pkr(book.InventoryValue);
        BookProfit = Money.Pkr(book.TotalProfit);
        // The two are the same money counted twice, so a difference between them is not a rounding quibble to
        // be smoothed over on the page: it means a bill's money is not reaching a container. It is named.
        BookNote = book.Outstanding == book.MoneyInMarket
            ? "The market figure and Outstanding are one sum: the same money, cut by container and by bill."
            : Money.Pkr(book.Outstanding - book.MoneyInMarket)
              + " owed on bills is not reaching any container's figure.";

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
