using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ContainerManagement.Data;
using ContainerManagement.Models;
using ContainerManagement.Services;

namespace ContainerManagement.ViewModels;

/// <summary>
/// One year, in the three books a year is asked about: the till, the selling, and the costs. The months come
/// from the same rows the Main ledger, Sales and Expenses pages stand on, with the year's own line under
/// each table, so nothing is added up twice and the page has no sentence to explain what it means - the
/// figures add up in front of the reader instead. A month with nothing in it still gets its row, because
/// twelve rows is a year, and a statement that skips the quiet months is how a quiet month gets forgotten.
/// </summary>
public partial class YearStatementViewModel : ViewModelBase
{
    private readonly ReportService _reports;
    private readonly CashBookService _cash;
    private readonly ShopExpenseService _expenses;
    private readonly PrintService _print;
    private bool _ready;

    public YearStatementViewModel(
        ReportService reports, CashBookService cash, ShopExpenseService expenses, PrintService print)
    {
        _reports = reports;
        _cash = cash;
        _expenses = expenses;
        _print = print;
        SelectedYear = Years.FirstOrDefault(y => y.Year == DateTime.Today.Year) ?? Years.First();
        _ready = true;
    }

    public ObservableCollection<YearChoice> Years { get; } = new(
        Enumerable.Range(DateTime.Today.Year - 5, 8).Reverse().Select(y => new YearChoice(y)));

    public ObservableCollection<TillYearRow> Till { get; } = new();
    public ObservableCollection<SalesYearRow> Sales { get; } = new();
    public ObservableCollection<ExpenseYearRow> Costs { get; } = new();

    [ObservableProperty] private YearChoice? selectedYear;
    [ObservableProperty] private string afterCosts = Money.Pkr(0);

    partial void OnSelectedYearChanged(YearChoice? value)
    {
        if (_ready) _ = LoadAsync();
    }

    public override async Task LoadAsync()
    {
        // The years the shop has money in, so a year that was traded is on the list even if it was five
        // years ago, and a year that never was is not offered as a guess.
        var seen = (await _cash.ListAsync()).Select(e => e.Date.Year)
            .Concat((await _expenses.ListAsync()).Select(e => e.Date.Year))
            .Distinct()
            .ToList();
        foreach (var y in seen.Where(y => Years.All(x => x.Year != y)).OrderByDescending(y => y))
            Years.Insert(0, new YearChoice(y));

        var year = SelectedYear?.Year ?? DateTime.Today.Year;

        Till.Clear();
        foreach (var r in await _cash.GetYearCashAsync(year))
            Till.Add(r);
        Sales.Clear();
        foreach (var r in await _reports.GetYearSalesAsync(year))
            Sales.Add(r);
        Costs.Clear();
        foreach (var r in await _expenses.GetYearAsync(year))
            Costs.Add(r);

        // The one figure the three tables cannot say between them: the profit on the year's goods, after
        // its costs. Its two halves are the last line of the table above it and the last line of the one
        // below, so it can be checked by hand without a word of explanation.
        var profit = Sales.FirstOrDefault(r => r.IsTotal)?.Profit ?? 0m;
        var cost = Costs.FirstOrDefault(r => r.IsTotal)?.Amount ?? 0m;
        AfterCosts = Money.Pkr(Money.Round(profit - cost));
    }

    /// <summary>
    /// The same rows, on paper. Printed rather than exported, because a year statement is a document
    /// someone asks for - the bank, the tax file, a partner - and it has to say which figures are cash and
    /// which are goods, which is the one thing a screen can leave to memory.
    /// </summary>
    [RelayCommand]
    private void Print()
    {
        var year = SelectedYear?.Year ?? DateTime.Today.Year;
        _print.OpenHtml(
            _print.YearStatementHtml(year, Till, Sales, Costs, ShopSettings.Load()),
            $"year-statement-{year}.html");
    }
}
