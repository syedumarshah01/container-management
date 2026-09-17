using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ContainerManagement.Models;
using ContainerManagement.Services;

namespace ContainerManagement.ViewModels;

public partial class DashboardViewModel : ViewModelBase
{
    private readonly PrintService _print;
    private readonly ReportService _reports;
    private readonly BackupService _backups;
    private readonly IAppShell _shell;

    public DashboardViewModel(ReportService reports, BackupService backups, IAppShell shell, PrintService print)
    {
        _print = print;
        _reports = reports;
        _backups = backups;
        _shell = shell;
    }

    [ObservableProperty] private string monthSales = "—";
    [ObservableProperty] private string monthProfit = "—";

    /// <summary>The dates the book is read over. Home opens with neither, which is the book entire; one box
    /// left empty leaves that side open, because "everything since the 1st" is asked for far more often than
    /// the 1st-to-the-1st. They move the card of book figures and not the month under it - the month is the
    /// month, so there is always one figure on the page that needs no reading of the dates first. Apply is a
    /// button rather than a reload on every click, as on the Reports page: a number that moves while the dates
    /// are still being set is a number nobody reads.</summary>
    [ObservableProperty] private DateTimeOffset? fromDate;
    [ObservableProperty] private DateTimeOffset? toDate;

    // The whole book, in the eight figures the shop opens the day with. Nothing here is a restatement of
    // anything else: each is the figure the page behind it already shows, added up once in ReportService.
    [ObservableProperty] private string bookContainers = "—";
    [ObservableProperty] private string bookSales = "—";
    [ObservableProperty] private string bookMarket = "—";
    [ObservableProperty] private string bookStock = "—";
    [ObservableProperty] private string bookProfit = "—";
    [ObservableProperty] private string bookLabel = "Whole book";
    [ObservableProperty] private string bookHint = "";
    [ObservableProperty] private string hint = "";
    [ObservableProperty] private string lastBackup = "None yet";

    public ObservableCollection<HomeDayRow> Days { get; } = new();

    public override async Task LoadAsync()
    {
        // The line under the title used to name the current month, which is what the page showed. With dates
        // on the page it must name what is being shown instead, or the heading and the figures disagree.
        var from = FromDate?.DateTime.Date;
        var to = ToDate?.DateTime.Date;
        Hint = from is null && to is null
            ? DateTime.Today.ToString("MMMM yyyy")
            : Period.Words(from, to);
        LastBackup = _backups.ListBackups().FirstOrDefault()?.WhenText ?? "None yet";

        var book = await _reports.GetDashboardAsync(from, to);
        BookContainers = book.TotalContainers.ToString();
        BookSales = Money.Pkr(book.TotalRevenue);
        BookMarket = Money.Pkr(book.MoneyInMarket);
        BookStock = Money.Pkr(book.InventoryValue);
        BookProfit = Money.Pkr(book.TotalProfit);
        // Purchases, expenses and what is owed on bills are left off this card: they belong to the pages that
        // hold them - We Owe for what the suppliers were billed, the Expenses page for the till's own bills,
        // and To collect for the bills still owing. The book-wide sums stay in the model, where the checks
        // read them against the figures that are shown here.

        BookLabel = from is null && to is null ? "Whole book" : "In the period";
        BookHint = from is null && to is null
            ? ""
            : Period.Words(from, to) + ": what was sold, what those bills still have out there, and profit. "
              + "Containers are the ones that landed in them, sold or not, and stock is the shelf as at today.";

        var (sales, profit, days) = await _reports.GetHomeMonthAsync();
        MonthSales = Money.Pkr(sales);
        MonthProfit = Money.Pkr(profit);
        Days.Clear();
        foreach (var d in days)
            Days.Add(d);
    }

    [RelayCommand]
    private void Print()
    {
        var days = Days.Select(d => new[] { d.DateText, d.SalesText, d.ProfitText })
            .Cast<IReadOnlyList<string>>().ToList();
        _print.PrintTables("home.html", "Home", Hint, new[]
        {
            new PrintTable(BookLabel,
                new[] { "Containers", "Sold for", "In the market", "Stock value", "Profit" },
                new[] { (IReadOnlyList<string>)new[] { BookContainers, BookSales, BookMarket, BookStock, BookProfit } },
                null, 0),
            new PrintTable("This month", new[] { "Sales", "Profit" },
                new[] { (IReadOnlyList<string>)new[] { MonthSales, MonthProfit } }, null, 0),
            new PrintTable("Day by day", new[] { "Date", "Sales", "Profit" }, days, null),
        });
        _shell.Notify("Printed from the page you were on.");
    }

    /// <summary>Re-reads the page with the dates as they stand, so the figures change when the shop says so -
    /// and only then, because a page that moved on every keystroke of a date picker cannot be read at all.</summary>
    [RelayCommand]
    private async Task ApplyAsync() => await LoadAsync();

    /// <summary>Whether Home is being read over dates at all. The reset is offered only while it is, because
    /// a page already on the whole book does not need a button telling you to go back to it.</summary>
    public bool HasRange => FromDate is not null || ToDate is not null;

    partial void OnFromDateChanged(DateTimeOffset? value) => OnPropertyChanged(nameof(HasRange));
    partial void OnToDateChanged(DateTimeOffset? value) => OnPropertyChanged(nameof(HasRange));

    [RelayCommand]
    private async Task WholeBookAsync()
    {
        FromDate = null;
        ToDate = null;
        await LoadAsync();
    }

    [RelayCommand] private void GoNewSale() => _shell.GoNewSale();
    [RelayCommand] private void GoBackup() => _shell.GoBackup();
}
