using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ContainerManagement.Models;
using ContainerManagement.Services;

namespace ContainerManagement.ViewModels;

public partial class MainLedgerViewModel : ViewModelBase
{
    private readonly CashBookService _cash;
    private readonly IAppShell _shell;
    private List<CashBookRowVm> _all = new();
    private bool _ready;

    public MainLedgerViewModel(CashBookService cash, IAppShell shell)
    {
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
    [ObservableProperty] private string monthLabel = "This month";
    [ObservableProperty] private decimal? openingAmount;
    [ObservableProperty] private MonthChoice? selectedMonth;
    [ObservableProperty] private YearChoice? selectedYear;

    public override async Task LoadAsync()
    {
        var list = await _cash.ListAsync();
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

        CashInHand = Money.Pkr(_all.Sum(r => r.AmountIn - r.AmountOut));
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

        var prior = _all.Where(r => r.Date < start).ToList();
        var monthRows = _all.Where(r => r.Date >= start && r.Date < end).ToList();
        decimal running = prior.Sum(r => r.AmountIn - r.AmountOut);

        Rows.Clear();
        if (prior.Count > 0)
        {
            Rows.Add(new CashBookRowVm
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
            Rows.Add(new CashBookRowVm
            {
                Date = r.Date,
                Description = r.Description,
                AmountIn = r.AmountIn,
                AmountOut = r.AmountOut,
                Running = running
            });
        }

        MonthIn = Money.Pkr(monthRows.Sum(r => r.AmountIn));
        MonthOut = Money.Pkr(monthRows.Sum(r => r.AmountOut));
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
