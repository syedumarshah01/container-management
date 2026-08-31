using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ContainerManagement.Models;
using ContainerManagement.Services;

namespace ContainerManagement.ViewModels;

public partial class MainLedgerViewModel : ViewModelBase
{
    private readonly CashBookService _cash;
    private readonly InventoryService _inventory;
    private readonly IAppShell _shell;
    private List<CashBookRowVm> _all = new();
    private bool _ready;

    public MainLedgerViewModel(CashBookService cash, InventoryService inventory, IAppShell shell)
    {
        _cash = cash;
        _inventory = inventory;
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
    public ObservableCollection<PayContainerOption> Containers { get; } = new();

    [ObservableProperty] private string cashInHand = Money.Pkr(0);
    [ObservableProperty] private string monthIn = Money.Pkr(0);
    [ObservableProperty] private string monthOut = Money.Pkr(0);
    [ObservableProperty] private string monthLabel = "This month";
    [ObservableProperty] private decimal? openingAmount;
    [ObservableProperty] private MonthChoice? selectedMonth;
    [ObservableProperty] private YearChoice? selectedYear;
    [ObservableProperty] private PayContainerOption? payContainer;
    [ObservableProperty] private DateTimeOffset? payDate = DateTimeOffset.Now;
    [ObservableProperty] private decimal? payAmount;

    public override async Task LoadAsync()
    {
        var list = await _cash.ListAsync();
        decimal running = 0;
        _all = new List<CashBookRowVm>(list.Count);
        foreach (var e in list)
        {
            running += e.AmountIn - e.AmountOut;
            _all.Add(new CashBookRowVm
            {
                Date = e.Date,
                Description = e.Description,
                AmountIn = e.AmountIn,
                AmountOut = e.AmountOut,
                Running = running
            });
        }

        foreach (var year in _all.Select(r => r.Date.Year).Distinct())
        {
            if (Years.All(y => y.Year != year))
                Years.Insert(0, new YearChoice(year));
        }

        CashInHand = Money.Pkr(running);
        OpeningAmount = list.Where(e => e.Kind == CashBookKind.Opening).Sum(e => e.AmountIn - e.AmountOut);

        var keepPay = PayContainer?.Id;
        Containers.Clear();
        foreach (var (id, label) in await _cash.SupplierContainersAsync())
            Containers.Add(new PayContainerOption { Id = id, Label = label });
        PayContainer = Containers.FirstOrDefault(c => c.Id == keepPay) ?? Containers.FirstOrDefault();

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

        var monthRows = _all.Where(r => r.Date >= start && r.Date < end)
            .OrderByDescending(r => r.Date)
            .ThenByDescending(r => r.Running)
            .ToList();
        Rows.Clear();
        foreach (var r in monthRows)
            Rows.Add(r);
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

    [RelayCommand]
    private async Task PaySupplierAsync()
    {
        if (PayContainer is null)
        {
            _shell.Notify("Pick a container that has a supplier.", true);
            return;
        }
        try
        {
            await _inventory.PaySupplierAsync(
                PayContainer.Id,
                PayDate?.DateTime ?? DateTime.Today,
                PayAmount ?? 0,
                "Bank Transfer",
                null);
            _shell.Notify("Supplier payment taken off cash.");
            PayAmount = null;
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

public class PayContainerOption
{
    public int Id { get; set; }
    public string Label { get; set; } = "";
    public override string ToString() => Label;
}
