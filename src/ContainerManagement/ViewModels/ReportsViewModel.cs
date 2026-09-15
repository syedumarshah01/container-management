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
/// One report at a time, chosen from a list, so the shop asks for a figure instead of hunting for it.
///
/// Nothing on this page adds anything up. Every figure is handed over by the service the report's own page
/// calls, in the words that page would show, so this cannot become a second answer to a question the book has
/// already answered - which is the only way a page like this is safe to trust.
///
/// Choosing a report also decides what the page reads from the database: a page that fetched all nine to show
/// one would be a page that took nine times as long to say what it had to say. And the sheet carries the
/// report on the screen, no more, because "printed from the page you were on" means the same thing here as it
/// does everywhere else in this book.
///
/// The dates are offered only to the reports that can be read over them. A control that cannot change what is
/// under it is a control that teaches the shop to ignore it, so "from 3 Mar" appears beside the figures a range
/// moves and not beside the shelf as it stands today.
/// </summary>
public partial class ReportsViewModel : ViewModelBase
{
    private readonly ReportService _reports;
    private readonly LedgerService _ledger;
    private readonly CashBookService _cash;
    private readonly ShopExpenseService _shopExpenses;
    private readonly PrintService _print;
    private readonly ExportService _export;
    private readonly IAppShell _shell;
    private bool _ready;

    public ReportsViewModel(ReportService reports, LedgerService ledger, CashBookService cash,
        ShopExpenseService shopExpenses, PrintService print, ExportService export, IAppShell shell)
    {
        _reports = reports;
        _ledger = ledger;
        _cash = cash;
        _shopExpenses = shopExpenses;
        _print = print;
        _export = export;
        _shell = shell;
        SelectedReport = ReportChoices.First();
        // Last, because the line above moves the picker and the picker asks the page to load: a page that read
        // its figures before it was built would be reading them with half an object.
        _ready = true;
    }

    /// <summary>The reports this book can make, in the order a shop tends to want them: how it stands, what
    /// happened over a stretch of days, then the month-by-month papers, then the lists.</summary>
    public IReadOnlyList<ReportChoice> ReportChoices { get; } = new List<ReportChoice>
    {
        new("Whole book", "book"),
        new("Period figures", "period"),
        new("Main ledger", "till"),
        new("Sales", "sales"),
        new("Expenses", "bills"),
        new("Containers", "containers"),
        new("Who owes", "owes"),
        new("Stock", "stock"),
        new("Profit by item", "items"),
    };

    [ObservableProperty] private ReportChoice? selectedReport;

    // The dates, read the way every other page on this book reads them: either end may be left out, and the
    // figures move when Apply is pressed.
    [ObservableProperty] private DateTimeOffset? fromDate;
    [ObservableProperty] private DateTimeOffset? toDate;

    [ObservableProperty] private string reportTitle = "The book, as it stands today";
    [ObservableProperty] private string periodLabel = "This month";
    [ObservableProperty] private string yearLabel = "";

    [ObservableProperty] private string bookContainers = "0";
    [ObservableProperty] private string bookRevenue = Money.Pkr(0);
    [ObservableProperty] private string bookProfit = Money.Pkr(0);
    [ObservableProperty] private string bookInMarket = Money.Pkr(0);
    [ObservableProperty] private string bookStock = Money.Pkr(0);

    [ObservableProperty] private string periodContainers = "0";
    [ObservableProperty] private string periodRevenue = Money.Pkr(0);
    [ObservableProperty] private string periodProfit = Money.Pkr(0);
    [ObservableProperty] private string periodInMarket = Money.Pkr(0);

    [ObservableProperty] private string tillIn = Money.Pkr(0);
    [ObservableProperty] private string tillOut = Money.Pkr(0);
    [ObservableProperty] private string tillReturns = Money.Pkr(0);
    [ObservableProperty] private string tillClosing = Money.Pkr(0);

    [ObservableProperty] private string yearBills = "0";
    [ObservableProperty] private string yearSold = Money.Pkr(0);
    [ObservableProperty] private string yearReceived = Money.Pkr(0);
    [ObservableProperty] private string yearStillOwed = Money.Pkr(0);
    [ObservableProperty] private string yearProfit = Money.Pkr(0);

    [ObservableProperty] private string billCount = "0";
    [ObservableProperty] private string billAmount = Money.Pkr(0);

    [ObservableProperty] private string listNote = "";

    public ObservableCollection<ContainerProfitRow> Containers { get; } = new();
    public ObservableCollection<ReceivableRow> WhoOwes { get; } = new();
    public ObservableCollection<InventoryRow> Stock { get; } = new();
    public ObservableCollection<ItemProfitRow> Items { get; } = new();
    public ObservableCollection<TillYearRow> TillRows { get; } = new();
    public ObservableCollection<SalesYearRow> SalesRows { get; } = new();
    public ObservableCollection<ExpenseYearRow> BillRows { get; } = new();

    // What the page shows. Each report is one card, so the figures under the dropdown are the report named in
    // it, and nothing else on the page can be mistaken for them.
    public bool ShowBook => Is("book");
    public bool ShowPeriod => Is("period");
    public bool ShowTill => Is("till");
    public bool ShowSales => Is("sales");
    public bool ShowBills => Is("bills");
    public bool ShowContainers => Is("containers");
    public bool ShowWhoOwes => Is("owes");
    public bool ShowStock => Is("stock");
    public bool ShowItems => Is("items");

    /// <summary>Whether the dates in the header can change what is below them at all.</summary>
    public bool ShowRange => Is("period") || Is("till") || Is("sales") || Is("bills") || Is("items");

    /// <summary>The profit reports, and only they, get the CSV button: a sheet is written from the two lists
    /// this page can show, and offering it beside a till or a stock table would be a button that writes
    /// something other than what is in front of the person pressing it.</summary>
    public bool ShowExport => ShowContainers || ShowItems;

    /// <summary>What the reset button is worth here: a month's card goes back to this month, a year's table
    /// goes back to this year, and a button that promises the wrong one is a trap.</summary>
    public string ResetLabel => Is("till") || Is("sales") || Is("bills") ? "This year" : "This month";

    public bool HasRange => Period.HasRange(FromDate?.DateTime.Date, ToDate?.DateTime.Date);

    partial void OnFromDateChanged(DateTimeOffset? value) => OnPropertyChanged(nameof(HasRange));

    partial void OnToDateChanged(DateTimeOffset? value) => OnPropertyChanged(nameof(HasRange));

    private bool Is(string key) => SelectedReport?.Key == key;

    private void RaiseReportViews()
    {
        foreach (var name in new[]
                 {
                     nameof(ShowBook), nameof(ShowPeriod), nameof(ShowTill), nameof(ShowSales),
                     nameof(ShowBills), nameof(ShowContainers), nameof(ShowWhoOwes), nameof(ShowStock),
                     nameof(ShowItems), nameof(ShowRange), nameof(ShowExport), nameof(ResetLabel)
                 })
            OnPropertyChanged(name);
    }

    partial void OnSelectedReportChanged(ReportChoice? value)
    {
        if (_ready) _ = ReloadAsync();
    }

    /// <summary>The card changes only once its own figures have arrived, so a report never appears on the
    /// screen holding the numbers of the one just left. On a page of money that is the difference between a
    /// refresh and a wrong figure that is on screen for a moment.</summary>
    private async Task ReloadAsync()
    {
        try
        {
            await LoadAsync();
        }
        finally
        {
            RaiseReportViews();
        }
    }

    /// <summary>Re-reads the report on the screen with the dates as they stand, so the figures change when the
    /// shop says - and only then, because a page that moved on every turn of a date picker cannot be read
    /// while it moves.</summary>
    [RelayCommand]
    private async Task ApplyAsync() => await LoadAsync();

    [RelayCommand]
    private async Task ThisMonthAsync()
    {
        FromDate = null;
        ToDate = null;
        await LoadAsync();
    }

    public override async Task LoadAsync()
    {
        var key = SelectedReport?.Key ?? "book";
        var choice = SelectedReport ?? ReportChoices.First();
        ReportTitle = choice.Name;

        var from = FromDate?.DateTime.Date;
        var to = ToDate?.DateTime.Date;
        var ranged = Period.HasRange(from, to);
        // With no dates the page reads the month it is standing in, which is what a shop wants from a report
        // page on a Tuesday morning; with dates it reads exactly those dates, one-ended ones included.
        var monthStart = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        var start = from ?? monthStart;
        var last = to ?? monthStart.AddMonths(1).AddDays(-1);
        var (cardStart, cardEnd) = ranged ? (from, to) : (start, last);
        var year = (from ?? to ?? DateTime.Today).Year;
        PeriodLabel = ranged ? Period.Words(from, to) : start.ToString("MMMM yyyy");
        YearLabel = year.ToString(CultureInfo.InvariantCulture);

        switch (key)
        {
            case "book":
            {
                var book = await _reports.GetDashboardAsync();
                BookContainers = book.TotalContainers.ToString(CultureInfo.InvariantCulture);
                BookRevenue = Money.Pkr(book.TotalRevenue);
                BookProfit = Money.Pkr(book.TotalProfit);
                BookInMarket = Money.Pkr(book.MoneyInMarket);
                BookStock = Money.Pkr(book.InventoryValue);
                break;
            }
            case "period":
            {
                // Only the figures GetDashboardAsync filters by the dates. Stock and the shop's own bills are
                // not among them, so they are not offered here under a period's heading.
                var period = await _reports.GetDashboardAsync(cardStart, cardEnd);
                PeriodContainers = period.TotalContainers.ToString(CultureInfo.InvariantCulture);
                PeriodRevenue = Money.Pkr(period.TotalRevenue);
                PeriodProfit = Money.Pkr(period.TotalProfit);
                PeriodInMarket = Money.Pkr(period.MoneyInMarket);
                break;
            }
            case "till":
            {
                var rows = await _cash.GetYearCashAsync(year);
                Fill(TillRows, rows);
                var total = rows.FirstOrDefault(r => r.IsTotal);
                TillIn = total?.InText ?? Money.Pkr(0);
                TillOut = total?.OutText ?? Money.Pkr(0);
                TillReturns = total?.ReturnsText ?? Money.Pkr(0);
                TillClosing = total?.ClosingText ?? Money.Pkr(0);
                break;
            }
            case "sales":
            {
                var rows = await _reports.GetYearSalesAsync(year);
                Fill(SalesRows, rows);
                var total = rows.FirstOrDefault(r => r.IsTotal);
                YearBills = total?.BillsText ?? "0";
                YearSold = total?.SoldText ?? Money.Pkr(0);
                YearReceived = total?.ReceivedText ?? Money.Pkr(0);
                YearStillOwed = total?.StillOwedText ?? Money.Pkr(0);
                YearProfit = total?.ProfitText ?? Money.Pkr(0);
                break;
            }
            case "bills":
            {
                var rows = await _shopExpenses.GetYearAsync(year);
                Fill(BillRows, rows);
                var total = rows.FirstOrDefault(r => r.IsTotal);
                BillCount = total?.CountText ?? "0";
                BillAmount = total?.AmountText ?? Money.Pkr(0);
                break;
            }
            case "containers":
            {
                var rows = await _reports.GetContainerProfitsAsync();
                ListNote = Fill(Containers, rows.OrderByDescending(r => r.InMarket).ThenBy(r => r.Title),
                    "biggest still out first");
                break;
            }
            case "owes":
            {
                var rows = await _ledger.GetReceivablesAsync();
                ListNote = Fill(WhoOwes,
                    rows.Where(r => r.Balance > 0.009m).OrderByDescending(r => r.Balance), "biggest first");
                break;
            }
            case "stock":
            {
                var rows = await _reports.GetGrandInventoryAsync();
                ListNote = Fill(Stock,
                    rows.OrderByDescending(r => r.TotalValue).ThenBy(r => r.ProductName), "worth most first");
                break;
            }
            case "items":
            {
                var rows = await _reports.GetItemProfitsAsync(from, to, null);
                ListNote = Fill(Items,
                    rows.OrderByDescending(r => r.Revenue).ThenBy(r => r.ProductName), "sold most first");
                break;
            }
        }
    }

    /// <summary>Puts a list in front of the shop and returns the words saying what it is holding: a report
    /// shown whole, so the screen and the sheet never differ, and an empty one says so rather than showing a
    /// blank table, which reads the same as one that failed to load.</summary>
    private static string Fill<T>(ObservableCollection<T> target, IEnumerable<T> rows, string order)
    {
        var list = rows.ToList();
        target.Clear();
        foreach (var r in list)
            target.Add(r);
        return list.Count switch
        {
            0 => "Nothing here.",
            1 => "1 line",
            var n => $"{n} lines, {order}"
        };
    }

    private void Fill<T>(ObservableCollection<T> target, IEnumerable<T> rows) => Fill(target, rows, "");

    /// <summary>The report on the screen, on paper, in the words on the screen - so the sheet can be handed
    /// over or filed without anyone having to remember which boxes the page was showing.</summary>
    /// <summary>
    /// The two profit sheets, each one the report of the same name on this page and read exactly the way that
    /// report reads it: the containers sheet is the book as it stands, the items sheet takes the dates the page
    /// is holding. A file that quietly covered a different stretch from the card above it is the quickest way
    /// for a CSV and a screen to start telling two stories, and on a page like this one of them ends up in
    /// somebody's accounts.
    /// </summary>
    [RelayCommand]
    private async Task ExportAsync()
    {
        var from = FromDate?.DateTime.Date;
        var to = ToDate?.DateTime.Date;
        try
        {
            var rows = await _reports.GetContainerProfitsAsync();
            var items = await _reports.GetItemProfitsAsync(from, to, null);
            _export.ProfitWorkbook(rows.ToList(), items.ToList());
            _shell.Notify("CSV files opened. Excel can open them.");
        }
        catch (Exception ex) { _shell.Notify(ex.Message, true); }
    }

    [RelayCommand]
    private void Print()
    {
        var key = SelectedReport?.Key ?? "book";
        var tables = new List<PrintTable>();
        var subtitle = key is "book" or "containers" or "owes" or "stock"
            ? "As it stands today"
            : key is "period" or "items" ? PeriodLabel : $"{YearLabel}";

        if (ShowBook)
        {
            tables.Add(new PrintTable("The book", new[] { "What", "Money" }, new List<IReadOnlyList<string>>
            {
                new[] { "Sold", BookRevenue },
                new[] { "Profit", BookProfit },
                new[] { "Still in the market", BookInMarket },
                new[] { "Stock on the shelf", BookStock },
                new[] { "Containers", BookContainers },
            }, null, 1));
        }
        if (ShowPeriod)
        {
            tables.Add(new PrintTable(PeriodLabel, new[] { "What", "Money" }, new List<IReadOnlyList<string>>
            {
                new[] { "Sold", PeriodRevenue },
                new[] { "Profit", PeriodProfit },
                new[] { "Still in the market", PeriodInMarket },
                new[] { "Containers that landed", PeriodContainers },
            }, null, 1));
        }
        if (ShowTill)
        {
            tables.Add(new PrintTable("Month by month",
                new[] { "Month", "In", "Out", "Returns", "Cash at the month's end" },
                TillPaper(), null, 1));
        }
        if (ShowSales)
        {
            tables.Add(new PrintTable("Month by month",
                new[] { "Month", "Bills", "Sold", "Received", "Still owed", "Profit" },
                SalesPaper(), null, 1));
        }
        if (ShowBills)
        {
            tables.Add(new PrintTable("Month by month",
                new[] { "Month", "Lines", "Money" }, BillsPaper(), null, 1));
        }
        if (ShowContainers)
        {
            tables.Add(new PrintTable("Containers",
                new[] { "Container", "Status", "Sold", "Profit", "Still in the market" },
                Containers.Select(r => new[] { r.Title, r.StatusText, r.RevenueText, r.ProfitText, r.InMarketText })
                    .Cast<IReadOnlyList<string>>().ToList(), null, 1));
        }
        if (ShowWhoOwes)
        {
            tables.Add(new PrintTable("Who owes",
                new[] { "Customer", "Owes", "Oldest bill", "How long" },
                WhoOwes.Select(r => new[] { r.Name, r.BalanceText, r.OldestDueText, r.Aging })
                    .Cast<IReadOnlyList<string>>().ToList(), null, 1));
        }
        if (ShowStock)
        {
            tables.Add(new PrintTable("Stock",
                new[] { "Item", "In stock", "Value", "From" },
                Stock.Select(r => new[] { r.ProductName, r.InStockText, r.ValueText, r.LotsText })
                    .Cast<IReadOnlyList<string>>().ToList(), null, 1));
        }
        if (ShowItems)
        {
            tables.Add(new PrintTable($"Profit by item, {PeriodLabel}",
                new[] { "Item", "Sold", "Amount", "Profit" },
                Items.Select(r => new[] { r.ProductName, r.QtyText, r.RevenueText, r.ProfitText })
                    .Cast<IReadOnlyList<string>>().ToList(), null, 1));
        }

        _print.PrintTables("reports.html", ReportTitle, subtitle, tables);
        _shell.Notify($"Printed {ReportTitle.ToLowerInvariant()}.");
    }

    // The year tables print straight from what the grid is holding, so the sheet cannot carry a month the
    // screen never listed.
    private List<IReadOnlyList<string>> TillPaper() =>
        TillRows.Select(r => new[] { r.MonthText, r.InText, r.OutText, r.ReturnsText, r.ClosingText })
            .Cast<IReadOnlyList<string>>().ToList();

    private List<IReadOnlyList<string>> SalesPaper() =>
        SalesRows
            .Select(r => new[]
                { r.MonthText, r.BillsText, r.SoldText, r.ReceivedText, r.StillOwedText, r.ProfitText })
            .Cast<IReadOnlyList<string>>().ToList();

    private List<IReadOnlyList<string>> BillsPaper() =>
        BillRows.Select(r => new[] { r.MonthText, r.CountText, r.AmountText })
            .Cast<IReadOnlyList<string>>().ToList();
}

/// <summary>One entry in the list of reports. A name to read and a key to switch on, so the words in the
/// dropdown can be changed without touching what a change of selection means.</summary>
public sealed class ReportChoice
{
    public ReportChoice(string name, string key)
    {
        Name = name;
        Key = key;
    }

    public string Name { get; }
    public string Key { get; }
    public override string ToString() => Name;
}
