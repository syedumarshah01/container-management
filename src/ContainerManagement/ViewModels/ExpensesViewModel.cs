using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ContainerManagement.Models;
using ContainerManagement.Services;

namespace ContainerManagement.ViewModels;

public partial class ExpensesViewModel : ViewModelBase
{
    private readonly ShopExpenseService _expenses;
    private readonly IAppShell _shell;
    private List<ShopExpenseRow> _all = new();
    private bool _ready;

    public ExpensesViewModel(ShopExpenseService expenses, IAppShell shell)
    {
        _expenses = expenses;
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

    public ObservableCollection<ShopExpenseRow> Rows { get; } = new();

    [ObservableProperty] private ShopExpenseRow? selected;
    [ObservableProperty] private DateTimeOffset? spendDate = DateTimeOffset.Now;
    [ObservableProperty] private string what = "";
    [ObservableProperty] private decimal? amount;
    [ObservableProperty] private string notes = "";
    [ObservableProperty] private string monthTotal = Money.Pkr(0);
    [ObservableProperty] private string allTotal = Money.Pkr(0);
    [ObservableProperty] private string monthLabel = "This month";
    [ObservableProperty] private MonthChoice? selectedMonth;
    [ObservableProperty] private YearChoice? selectedYear;

    public override async Task LoadAsync()
    {
        var keepId = Selected?.Id;
        var list = await _expenses.ListAsync();
        decimal running = 0;
        _all = new List<ShopExpenseRow>(list.Count);
        foreach (var e in list)
        {
            running += e.Amount;
            _all.Add(new ShopExpenseRow
            {
                Id = e.Id,
                Date = e.Date,
                Description = e.Description,
                Amount = e.Amount,
                Notes = e.Notes ?? "",
                Running = running
            });
        }

        foreach (var year in _all.Select(r => r.Date.Year).Distinct())
        {
            if (Years.All(y => y.Year != year))
                Years.Insert(0, new YearChoice(year));
        }

        AllTotal = Money.Pkr(running);
        ShowMonth();
        Selected = keepId is int id ? Rows.FirstOrDefault(r => r.Id == id) : null;
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
            .OrderBy(r => r.Date)
            .ThenBy(r => r.Id)
            .ToList();
        decimal run = 0;
        var shown = new List<ShopExpenseRow>(monthRows.Count);
        foreach (var r in monthRows)
        {
            run += r.Amount;
            shown.Add(new ShopExpenseRow
            {
                Id = r.Id,
                Date = r.Date,
                Description = r.Description,
                Amount = r.Amount,
                Notes = r.Notes,
                Running = run
            });
        }
        shown.Reverse();
        Rows.Clear();
        foreach (var r in shown)
            Rows.Add(r);
        MonthTotal = Money.Pkr(run);
    }

    partial void OnSelectedChanged(ShopExpenseRow? value)
    {
        if (value is null)
            return;
        SpendDate = new DateTimeOffset(value.Date);
        What = value.Description;
        Amount = value.Amount;
        Notes = value.Notes;
    }

    [RelayCommand]
    private async Task AddAsync()
    {
        try
        {
            await _expenses.AddAsync(SpendDate?.DateTime ?? DateTime.Today, What, Amount ?? 0, Notes);
            _shell.Notify("Expense added.");
            What = "";
            Amount = null;
            Notes = "";
            SpendDate = DateTimeOffset.Now;
            await LoadAsync();
        }
        catch (Exception ex)
        {
            _shell.Notify(ex.Message, true);
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (Selected is null)
        {
            _shell.Notify("Select a line in the ledger to edit.", true);
            return;
        }
        try
        {
            await _expenses.UpdateAsync(Selected.Id, SpendDate?.DateTime ?? DateTime.Today, What, Amount ?? 0, Notes);
            _shell.Notify("Expense updated.");
            await LoadAsync();
        }
        catch (Exception ex)
        {
            _shell.Notify(ex.Message, true);
        }
    }

    [RelayCommand]
    private async Task RemoveAsync()
    {
        if (Selected is null)
        {
            _shell.Notify("Select a line in the ledger to remove.", true);
            return;
        }
        try
        {
            await _expenses.DeleteAsync(Selected.Id);
            _shell.Notify("Expense removed.");
            What = "";
            Amount = null;
            Notes = "";
            await LoadAsync();
        }
        catch (Exception ex)
        {
            _shell.Notify(ex.Message, true);
        }
    }
}

public sealed class MonthChoice
{
    public MonthChoice(int number, string name)
    {
        Number = number;
        Name = name;
    }

    public int Number { get; }
    public string Name { get; }
    public override string ToString() => Name;
}

public sealed class YearChoice
{
    public YearChoice(int year) => Year = year;
    public int Year { get; }
    public override string ToString() => Year.ToString();
}

public class ShopExpenseRow
{
    public int Id { get; set; }
    public DateTime Date { get; set; }
    public string Description { get; set; } = "";
    public decimal Amount { get; set; }
    public string Notes { get; set; } = "";
    public decimal Running { get; set; }
    public string DateText => Date.ToString("dd MMM yyyy");
    public string AmountText => Money.Pkr(Amount);
    public string RunningText => Money.Pkr(Running);
}
