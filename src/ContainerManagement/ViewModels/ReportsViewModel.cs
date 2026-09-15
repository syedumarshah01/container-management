using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ContainerManagement.Models;
using ContainerManagement.Services;

namespace ContainerManagement.ViewModels;

/// <summary>
/// Every report the book can make, on one page, so a figure can be found without walking the sidebar for it.
///
/// Nothing on this page adds anything up. Each figure is handed over by the service the report's own page calls,
/// and each line of a list carries the words that page would show, so the hub cannot become a second answer to
/// a question the book has already answered - which is the only way a page like this is safe to trust. The one
/// decision made here is presentation: a list is cut to its biggest <see cref="Shown" /> lines so the page can
/// be read, and says how many there were. The printed sheet carries every line, so nothing is lost by the cut.
///
/// The dates in the header pick one month and the year it falls in. Only the figures a service is willing to
/// move on those dates are shown as the month's; a figure the service computes over the whole book is labelled
/// as the book's and left alone, because a number inside a month's heading that was not filtered by the month
/// is the quietest way to tell a shop something that is not true.
/// </summary>
public partial class ReportsViewModel : ViewModelBase
{
    /// <summary>How many lines of a list the screen keeps. The paper keeps them all.</summary>
    private const int Shown = 8;

    private readonly ReportService _reports;
    private readonly LedgerService _ledger;
    private readonly CashBookService _cash;
    private readonly ShopExpenseService _shopExpenses;
    private readonly PrintService _print;
    private readonly IAppShell _shell;
    private bool _ready;

    // The whole lists, kept for the sheet. The collections the grids bind to are the cut-down copies.
    private List<ContainerProfitRow> _containers = new();
    private List<ReceivableRow> _receivables = new();
    private List<InventoryRow> _stock = new();
    private List<ItemProfitRow> _items = new();
    private List<TillYearRow> _till = new();
    private List<SalesYearRow> _salesYear = new();
    private List<ExpenseYearRow> _bills = new();

    public ReportsViewModel(ReportService reports, LedgerService ledger, CashBookService cash,
        ShopExpenseService shopExpenses, PrintService print, IAppShell shell)
    {
        _reports = reports;
        _ledger = ledger;
        _cash = cash;
        _shopExpenses = shopExpenses;
        _print = print;
        _shell = shell;
        SelectedMonth = MonthChoices.First(m => m.Number == DateTime.Today.Month);
        SelectedYear = Years.First(y => y.Year == DateTime.Today.Year);
        // Set last, because the two lines above move the pickers and every picker asks the page to load: a page
        // that read the database before it was built would be reading it with half an object.
        _ready = true;
    }

    public IReadOnlyList<MonthChoice> MonthChoices { get; } = Enumerable.Range(1, 12)
        .Select(m => new MonthChoice(m, new DateTime(2000, m, 1).ToString("MMMM")))
        .ToList();

    public ObservableCollection<YearChoice> Years { get; } = new(
        Enumerable.Range(DateTime.Today.Year - 5, 8).Reverse().Select(y => new YearChoice(y)));

    [ObservableProperty] private MonthChoice? selectedMonth;
    [ObservableProperty] private YearChoice? selectedYear;

    // The book, as it stands today.
    [ObservableProperty] private string bookContainers = "0";
    [ObservableProperty] private string bookRevenue = Money.Pkr(0);
    [ObservableProperty] private string bookProfit = Money.Pkr(0);
    [ObservableProperty] private string bookInMarket = Money.Pkr(0);
    [ObservableProperty] private string bookStock = Money.Pkr(0);
    [ObservableProperty] private string bookOwed = Money.Pkr(0);
    [ObservableProperty] private string bookOutstanding = Money.Pkr(0);
    [ObservableProperty] private string bookLowStock = "";

    // The month in the header. Only the figures the service filters by dates appear here.
    [ObservableProperty] private string monthLabel = "This month";
    [ObservableProperty] private string yearLabel = "";

    // Each table's title, once, so the card and the printed sheet never carry two spellings of one date.
    [ObservableProperty] private string tillHeading = "Till";
    [ObservableProperty] private string salesHeading = "Sales";
    [ObservableProperty] private string billsHeading = "Shop bills";
    [ObservableProperty] private string itemsHeading = "Profit by item";
    [ObservableProperty] private string monthContainers = "0";
    [ObservableProperty] private string monthRevenue = Money.Pkr(0);
    [ObservableProperty] private string monthProfit = Money.Pkr(0);
    [ObservableProperty] private string monthInMarket = Money.Pkr(0);

    // The till, for the year of the month picked.
    [ObservableProperty] private string tillIn = Money.Pkr(0);
    [ObservableProperty] private string tillOut = Money.Pkr(0);
    [ObservableProperty] private string tillReturns = Money.Pkr(0);
    [ObservableProperty] private string tillClosing = Money.Pkr(0);

    // Sales for the year, and the shop's own bills for it.
    [ObservableProperty] private string yearBills = "0";
    [ObservableProperty] private string yearSold = Money.Pkr(0);
    [ObservableProperty] private string yearReceived = Money.Pkr(0);
    [ObservableProperty] private string yearStillOwed = Money.Pkr(0);
    [ObservableProperty] private string yearProfit = Money.Pkr(0);
    [ObservableProperty] private string billCount = "0";
    [ObservableProperty] private string billAmount = Money.Pkr(0);

    // The lists. Each is the report's own rows, cut to the biggest few for reading.
    public ObservableCollection<ContainerProfitRow> Containers { get; } = new();
    public ObservableCollection<ReceivableRow> WhoOwes { get; } = new();
    public ObservableCollection<InventoryRow> Stock { get; } = new();
    public ObservableCollection<ItemProfitRow> Items { get; } = new();
    public ObservableCollection<TillYearRow> TillRows { get; } = new();
    public ObservableCollection<SalesYearRow> SalesRows { get; } = new();
    public ObservableCollection<ExpenseYearRow> BillRows { get; } = new();

    [ObservableProperty] private string containersNote = "";
    [ObservableProperty] private string whoOwesNote = "";
    [ObservableProperty] private string stockNote = "";
    [ObservableProperty] private string itemsNote = "";

    public override async Task LoadAsync()
    {
        var year2 = SelectedYear?.Year ?? DateTime.Today.Year;
        var month = SelectedMonth?.Number ?? DateTime.Today.Month;
        var start = new DateTime(year2, month, 1);
        var last = start.AddMonths(1).AddDays(-1);
        MonthLabel = start.ToString("MMMM yyyy");
        YearLabel = year2.ToString(CultureInfo.InvariantCulture);
        TillHeading = $"Till in {YearLabel}";
        SalesHeading = $"Sales in {YearLabel}";
        BillsHeading = $"Shop bills in {YearLabel}";
        ItemsHeading = $"Profit by item, {MonthLabel}";

        var book = await _reports.GetDashboardAsync();
        var period = await _reports.GetDashboardAsync(start, last);

        BookContainers = book.TotalContainers.ToString(CultureInfo.InvariantCulture);
        BookRevenue = Money.Pkr(book.TotalRevenue);
        BookProfit = Money.Pkr(book.TotalProfit);
        BookInMarket = Money.Pkr(book.MoneyInMarket);
        BookStock = Money.Pkr(book.InventoryValue);
        BookOwed = Money.Pkr(book.MoneyOwedByCustomers);
        BookOutstanding = Money.Pkr(book.Outstanding);
        BookLowStock = book.LowStockCount == 0
            ? "Nothing low"
            : book.LowStockCount + " items low";

        MonthContainers = period.TotalContainers.ToString(CultureInfo.InvariantCulture);
        MonthRevenue = Money.Pkr(period.TotalRevenue);
        MonthProfit = Money.Pkr(period.TotalProfit);
        MonthInMarket = Money.Pkr(period.MoneyInMarket);

        _containers = await _reports.GetContainerProfitsAsync();
        _receivables = await _ledger.GetReceivablesAsync();
        _stock = await _reports.GetGrandInventoryAsync();
        _items = await _reports.GetItemProfitsAsync(start, last, null);
        _till = await _cash.GetYearCashAsync(year2);
        _salesYear = await _reports.GetYearSalesAsync(year2);
        _bills = await _shopExpenses.GetYearAsync(year2);

        ContainersNote = Fill(Containers,
            _containers.OrderByDescending(r => r.InMarket).ThenBy(r => r.Title), "biggest still out first");
        WhoOwesNote = Fill(WhoOwes,
            _receivables.Where(r => r.Balance > 0.009m).OrderByDescending(r => r.Balance), "biggest first");
        StockNote = Fill(Stock,
            _stock.OrderByDescending(r => r.TotalValue).ThenBy(r => r.ProductName), "worth most first");
        ItemsNote = Fill(Items,
            _items.OrderByDescending(r => r.Revenue).ThenBy(r => r.ProductName), "sold most first");

        TillRows.Clear();
        foreach (var r in _till)
            TillRows.Add(r);
        SalesRows.Clear();
        foreach (var r in _salesYear)
            SalesRows.Add(r);
        BillRows.Clear();
        foreach (var r in _bills)
            BillRows.Add(r);

        // The year's own line, by the label the service gave it. A page that added its twelve months would be
        // a page that could come to a different total than the year report it is standing on.
        var till = _till.FirstOrDefault(r => r.IsTotal);
        TillIn = till?.InText ?? Money.Pkr(0);
        TillOut = till?.OutText ?? Money.Pkr(0);
        TillReturns = till?.ReturnsText ?? Money.Pkr(0);
        TillClosing = till?.ClosingText ?? Money.Pkr(0);

        var sales = _salesYear.FirstOrDefault(r => r.IsTotal);
        YearBills = sales?.BillsText ?? "0";
        YearSold = sales?.SoldText ?? Money.Pkr(0);
        YearReceived = sales?.ReceivedText ?? Money.Pkr(0);
        YearStillOwed = sales?.StillOwedText ?? Money.Pkr(0);
        YearProfit = sales?.ProfitText ?? Money.Pkr(0);

        var bills = _bills.FirstOrDefault(r => r.IsTotal);
        BillCount = bills?.CountText ?? "0";
        BillAmount = bills?.AmountText ?? Money.Pkr(0);
    }

    /// <summary>
    /// Puts the cut-down list in front of the shop and returns the words saying what was cut, in the same shape
    /// every list on this
    /// page uses: how many lines there are, and what they are ordered by. An empty report says it is empty
    /// rather than showing a blank table, which reads the same as one that failed to load.
    /// </summary>
    private static string Fill<T>(ObservableCollection<T> target, IEnumerable<T> rows, string order)
    {
        var list = rows.ToList();
        target.Clear();
        foreach (var r in list.Take(Shown))
            target.Add(r);
        return list.Count switch
        {
            0 => "Nothing here.",
            var n when n <= Shown => $"{n} {(n == 1 ? "line" : "lines")}, {order}",
            var n => $"{Shown} of {n} lines, {order}"
        };
    }

    partial void OnSelectedMonthChanged(MonthChoice? value)
    {
        if (_ready) _ = LoadAsync();
    }

    partial void OnSelectedYearChanged(YearChoice? value)
    {
        if (_ready) _ = LoadAsync();
    }

    [RelayCommand]
    private void Print()
    {
        var containers = _containers
            .Select(r => new[] { r.Title, r.RevenueText, r.ProfitText, r.InMarketText })
            .Cast<IReadOnlyList<string>>().ToList();
        var owes = _receivables.Where(r => r.Balance > 0.009m)
            .Select(r => new[] { r.Name, r.BalanceText, r.OldestDueText, r.Aging })
            .Cast<IReadOnlyList<string>>().ToList();
        var stock = _stock
            .Select(r => new[] { r.ProductName, r.InStockText, r.ValueText, r.LotsText })
            .Cast<IReadOnlyList<string>>().ToList();
        var items = _items
            .Select(r => new[] { r.ProductName, r.QtyText, r.RevenueText, r.ProfitText })
            .Cast<IReadOnlyList<string>>().ToList();
        var till = _till
            .Select(r => new[] { r.MonthText, r.InText, r.OutText, r.ReturnsText, r.ClosingText })
            .Cast<IReadOnlyList<string>>().ToList();
        var sales = _salesYear
            .Select(r => new[] { r.MonthText, r.BillsText, r.SoldText, r.ReceivedText, r.StillOwedText, r.ProfitText })
            .Cast<IReadOnlyList<string>>().ToList();
        var bills = _bills
            .Select(r => new[] { r.MonthText, r.CountText, r.AmountText })
            .Cast<IReadOnlyList<string>>().ToList();

        // The figures are the strings the page is already holding, so a sheet printed from here is the page.
        _print.PrintTables("reports.html", "Reports",
            $"The book as it stands today, and {MonthLabel} of {YearLabel}", new[]
            {
                new PrintTable("The book",
                    new[] { "What", "Money" },
                    new List<IReadOnlyList<string>>
                    {
                        new[] { "Sold", BookRevenue },
                        new[] { "Profit", BookProfit },
                        new[] { "Still in the market", BookInMarket },
                        new[] { "Stock on the shelf", BookStock },
                        new[] { "Customers owe", BookOwed },
                        new[] { "Left unpaid on bills", BookOutstanding },
                        new[] { "Containers", BookContainers },
                        new[] { "Low stock", BookLowStock },
                    }, null, 1),
                new PrintTable(MonthLabel,
                    new[] { "What", "Money" },
                    new List<IReadOnlyList<string>>
                    {
                        new[] { "Sold", MonthRevenue },
                        new[] { "Profit", MonthProfit },
                        new[] { "Still in the market", MonthInMarket },
                        new[] { "Containers that landed", MonthContainers },
                    }, null, 1),
                new PrintTable(TillHeading,
                    new[] { "Month", "In", "Out", "Returns", "Cash at the month's end" }, till, null, 1),
                new PrintTable(SalesHeading,
                    new[] { "Month", "Bills", "Sold", "Received", "Still owed", "Profit" }, sales, null, 1),
                new PrintTable(BillsHeading,
                    new[] { "Month", "Lines", "Money" }, bills, null, 1),
                new PrintTable("Containers",
                    new[] { "Container", "Sold", "Profit", "Still in the market" }, containers, null, 1),
                new PrintTable("Who owes",
                    new[] { "Customer", "Owes", "Oldest bill", "How long" }, owes, null, 1),
                new PrintTable("Stock",
                    new[] { "Item", "In stock", "Value", "From" }, stock, null, 1),
                new PrintTable(ItemsHeading,
                    new[] { "Item", "Sold", "Amount", "Profit" }, items, null, 1),
            });
        _shell.Notify($"Printed every report on one sheet: the book, {MonthLabel}, {YearLabel}.");
    }
}
