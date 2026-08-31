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

    public ExpensesViewModel(ShopExpenseService expenses, IAppShell shell)
    {
        _expenses = expenses;
        _shell = shell;
    }

    public override bool FillsPage => true;

    public ObservableCollection<ShopExpenseRow> Rows { get; } = new();

    [ObservableProperty] private ShopExpenseRow? selected;
    [ObservableProperty] private DateTimeOffset? spendDate = DateTimeOffset.Now;
    [ObservableProperty] private string what = "";
    [ObservableProperty] private decimal? amount;
    [ObservableProperty] private string notes = "";
    [ObservableProperty] private string monthTotal = Money.Pkr(0);
    [ObservableProperty] private string allTotal = Money.Pkr(0);

    public override async Task LoadAsync()
    {
        var keepId = Selected?.Id;
        var list = await _expenses.ListAsync();
        decimal running = 0;
        var rows = new List<ShopExpenseRow>(list.Count);
        foreach (var e in list)
        {
            running += e.Amount;
            rows.Add(new ShopExpenseRow
            {
                Id = e.Id,
                Date = e.Date,
                Description = e.Description,
                Amount = e.Amount,
                Notes = e.Notes ?? "",
                Running = running
            });
        }
        rows.Reverse();
        Rows.Clear();
        foreach (var r in rows)
            Rows.Add(r);

        var start = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        MonthTotal = Money.Pkr(list.Where(e => e.Date >= start).Sum(e => e.Amount));
        AllTotal = Money.Pkr(running);
        Selected = keepId is int id ? Rows.FirstOrDefault(r => r.Id == id) : null;
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
