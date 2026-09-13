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
    [ObservableProperty] private string monthLabel = "This month";
    [ObservableProperty] private string monthHint = "Sales profit minus this month’s expense ledger.";

    /// <summary>Home opens on the current month and narrows only when both boxes are asked to: the figures on
    /// this page are then the shop's over those dates, by the same rules the month has always been added up
    /// by. One box left empty leaves that side open, because "everything since the 1st" is a thing shops ask
    /// for more than the 1st-to-the-1st is. Apply is a button rather than a reload on every click, as on the
    /// Profit page: a number that moves while you are still setting the dates is a number nobody reads.</summary>
    [ObservableProperty] private DateTimeOffset? fromDate;
    [ObservableProperty] private DateTimeOffset? toDate;

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
        // The line under the title used to name the current month, which is what the page showed. With dates
        // on the page it must name what is being shown instead, or the heading and the figures disagree.
        Hint = FromDate is null && ToDate is null
            ? DateTime.Today.ToString("MMMM yyyy")
            : PeriodWords(FromDate?.DateTime.Date, ToDate?.DateTime.Date);
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

        var from = FromDate?.DateTime.Date;
        var to = ToDate?.DateTime.Date;
        var (sales, profit, days) = await _reports.GetHomeMonthAsync(from, to);
        MonthSales = Money.Pkr(sales);
        MonthProfit = Money.Pkr(profit);
        MonthLabel = from is null && to is null ? "This month" : "In the period";
        MonthHint = from is null && to is null
            ? "Sales profit minus this month’s expense ledger."
            : PeriodWords(from, to) + ": sales, the cost of what was sold, and the till's own bills.";
        Days.Clear();
        foreach (var d in days)
            Days.Add(d);
    }

    private static string PeriodWords(DateTime? from, DateTime? to)
    {
        string Day(DateTime d) => d.ToString("d MMM yyyy");
        if (from is DateTime f && to is DateTime t)
            return f == t ? Day(f) : Day(f) + " to " + Day(t);
        if (from is DateTime only)
            return "from " + Day(only);
        return to is DateTime until ? "up to " + Day(until) : "the whole book";
    }

    /// <summary>Re-reads the page with the dates as they stand, so the figures change when the shop says so.</summary>
    [RelayCommand]
    private async Task ApplyAsync() => await LoadAsync();

    /// <summary>Whether Home is being read over dates at all. The reset is offered only while it is, because a
    /// page already on this month does not need a button telling you to go back to it.</summary>
    public bool HasRange => FromDate is not null || ToDate is not null;

    partial void OnFromDateChanged(DateTimeOffset? value) => OnPropertyChanged(nameof(HasRange));
    partial void OnToDateChanged(DateTimeOffset? value) => OnPropertyChanged(nameof(HasRange));

    [RelayCommand]
    private async Task ThisMonthAsync()
    {
        FromDate = null;
        ToDate = null;
        await LoadAsync();
    }

    [RelayCommand] private void GoNewSale() => _shell.GoNewSale();
    [RelayCommand] private void GoBackup() => _shell.GoBackup();
}
