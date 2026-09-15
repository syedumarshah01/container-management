using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ContainerManagement.Models;
using ContainerManagement.Services;

namespace ContainerManagement.ViewModels;

public partial class MainLedgerViewModel : ViewModelBase
{
    private readonly PrintService _print;
    private readonly CashBookService _cash;
    private readonly IAppShell _shell;
    private List<CashBookRowVm> _all = new();
    private List<(DateTime Date, decimal Amount)> _returns = new();
    private bool _ready;

    public MainLedgerViewModel(CashBookService cash, IAppShell shell, PrintService print)
    {
        _print = print;
        _cash = cash;
        _shell = shell;
        SelectedMonth = MonthChoices.First(m => m.Number == DateTime.Today.Month);
        SelectedYear = Years.First(y => y.Year == DateTime.Today.Year);
        _ready = true;
    }

    public IReadOnlyList<MonthChoice> MonthChoices { get; } = Enumerable.Range(1, 12)
        .Select(m => new MonthChoice(m, new DateTime(2000, m, 1).ToString("MMMM")))
        .ToList();

    public ObservableCollection<YearChoice> Years { get; } = new(
        Enumerable.Range(DateTime.Today.Year - 5, 8).Reverse().Select(y => new YearChoice(y)));

    public ObservableCollection<CashBookRowVm> Rows { get; } = new();

    [ObservableProperty] private string cashInHand = Money.Pkr(0);
    [ObservableProperty] private string monthIn = Money.Pkr(0);
    [ObservableProperty] private string monthOut = Money.Pkr(0);
    [ObservableProperty] private string monthReturns = Money.Pkr(0);
    [ObservableProperty] private string monthLabel = "This month";

    /// <summary>Under the cash figure: which month it closes, and what came in from the month before it.</summary>
    [ObservableProperty] private string cashHint = "";
    [ObservableProperty] private decimal? openingAmount;
    [ObservableProperty] private MonthChoice? selectedMonth;
    [ObservableProperty] private YearChoice? selectedYear;

    public override async Task LoadAsync()
    {
        var list = await _cash.ListAsync();
        _returns = await _cash.ListReturnsAsync();
        _all = new List<CashBookRowVm>(list.Count);
        foreach (var e in list)
        {
            _all.Add(new CashBookRowVm
            {
                Date = e.Date,
                Description = e.Description,
                AmountIn = e.AmountIn,
                AmountOut = e.AmountOut
            });
        }

        foreach (var year in _all.Select(r => r.Date.Year).Distinct())
        {
            if (Years.All(y => y.Year != year))
                Years.Insert(0, new YearChoice(year));
        }

        OpeningAmount = list.Where(e => e.Kind == CashBookKind.Opening).Sum(e => e.AmountIn - e.AmountOut);
        ShowMonth();
    }

    partial void OnSelectedMonthChanged(MonthChoice? value)
    {
        if (_ready) ShowMonth();
    }

    partial void OnSelectedYearChanged(YearChoice? value)
    {
        if (_ready) ShowMonth();
    }

    private void ShowMonth()
    {
        var month = SelectedMonth?.Number ?? DateTime.Today.Month;
        var year = SelectedYear?.Year ?? DateTime.Today.Year;
        var start = new DateTime(year, month, 1);
        var end = start.AddMonths(1);
        MonthLabel = start.ToString("MMMM yyyy");

        MonthReturns = Money.Pkr(_returns.Where(r => r.Date >= start && r.Date < end).Sum(r => r.Amount));

        var prior = _all.Where(r => r.Date < start).ToList();
        var monthRows = _all.Where(r => r.Date >= start && r.Date < end).ToList();
        // One rule for the card, the row underneath it and the running column in the table, so none of the
        // three can disagree with the others about where the month closed.
        var (carried, closing) = CashBookService.MonthCash(
            _all.Select(r => (r.Date, r.AmountIn, r.AmountOut)).ToList(), start);
        CashInHand = Money.Pkr(closing);
        CashHint = $"at the end of {MonthLabel}"
            + (carried == 0m ? "" : $" \u00b7 {Money.Pkr(carried)} carried from {start.AddMonths(-1):MMMM}");
        decimal running = carried;

        // The rows are put together oldest first, because that is the only order in which a running
        // balance means anything: each figure has to be the book as it stood once that entry was made.
        // The page then shows them the other way round, latest on top - the shop opens this page to see
        // what happened last, and reading the column upwards is what a day book is for.
        var built = new List<CashBookRowVm>();
        if (prior.Count > 0)
        {
            built.Add(new CashBookRowVm
            {
                Date = start,
                Description = "Balance brought forward",
                AmountIn = 0,
                AmountOut = 0,
                Running = running
            });
        }

        foreach (var r in monthRows)
        {
            running += r.AmountIn - r.AmountOut;
            built.Add(new CashBookRowVm
            {
                Date = r.Date,
                Description = r.Description,
                AmountIn = r.AmountIn,
                AmountOut = r.AmountOut,
                Running = running
            });
        }

        Rows.Clear();
        for (var i = built.Count - 1; i >= 0; i--)
            Rows.Add(built[i]);

        MonthIn = Money.Pkr(monthRows.Sum(r => r.AmountIn));
        MonthOut = Money.Pkr(monthRows.Sum(r => r.AmountOut));
    }

    [RelayCommand]
    private void Print()
    {
        var rows = Rows.Select(r => new[] { r.DateText, r.Description, r.InText, r.OutText, r.RunningText })
            .Cast<IReadOnlyList<string>>().ToList();
        _print.PrintTable("main-ledger.html", "Main ledger", $"Cash in hand at the end of {MonthLabel}",
            new[] { "Date", "What it was", "In", "Out", "Cash in hand" },
            rows, new[] { "The month", "", MonthIn, MonthOut, CashInHand }, 2);
        // The sheet names the month it closes on, and the carried figure is already the table's first row, so
        // the paper can be checked against last month's paper without knowing what the page was showing. The
        // number read out is the one the card is holding, so the two cannot be quietly different sheets.
        _shell.Notify($"Printed {MonthLabel}. Cash at the month's end: {CashInHand}.");
    }

    [RelayCommand]
    private async Task SaveOpeningAsync()
    {
        try
        {
            await _cash.SetOpeningAsync(OpeningAmount ?? 0);
            _shell.Notify("Opening cash saved.");
            await LoadAsync();
        }
        catch (Exception ex)
        {
            _shell.Notify(ex.Message, true);
        }
    }
}

public class CashBookRowVm
{
    public DateTime Date { get; set; }
    public string Description { get; set; } = "";
    public decimal AmountIn { get; set; }
    public decimal AmountOut { get; set; }
    public decimal Running { get; set; }
    public string DateText => Date.ToString("dd MMM yyyy");
    public string InText => AmountIn == 0 ? "—" : Money.Pkr(AmountIn);
    public string OutText => AmountOut == 0 ? "—" : Money.Pkr(AmountOut);
    public string RunningText => Money.Pkr(Running);
}
