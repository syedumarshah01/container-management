using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ContainerManagement.Data;
using ContainerManagement.Models;
using ContainerManagement.Services;

namespace ContainerManagement.ViewModels;

/// <summary>
/// One year, in the three books a year is asked about: the till, the selling, and the costs. Nothing here
/// is worked out a second time - the months are grouped from the same rows the Main ledger, Sales and
/// Expenses pages stand on, so the year a statement prints is the year those pages show. A month with
/// nothing in it still gets its row: twelve rows is a year, and a statement that skips the quiet months is
/// how a quiet month gets forgotten.
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
    [ObservableProperty] private string yearLabel = "";
    [ObservableProperty] private string afterCosts = Money.Pkr(0);
    [ObservableProperty] private string afterCostsHow = "";
    [ObservableProperty] private string tillTape = "";
    [ObservableProperty] private string salesTape = "";
    [ObservableProperty] private string costTape = "";

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
        YearLabel = year.ToString();

        Till.Clear();
        foreach (var r in await _cash.GetYearCashAsync(year))
            Till.Add(r);
        Sales.Clear();
        foreach (var r in await _reports.GetYearSalesAsync(year))
            Sales.Add(r);
        Costs.Clear();
        foreach (var r in await _expenses.GetYearAsync(year))
            Costs.Add(r);

        var inSum = Money.Round(Till.Sum(r => r.CashIn));
        var outSum = Money.Round(Till.Sum(r => r.CashOut));
        var back = Money.Round(Till.Sum(r => r.Returns));
        var closing = Till.Count > 0 ? Till[^1].Closing : 0m;
        TillTape = "In " + Money.Pkr(inSum) + " · Out " + Money.Pkr(outSum)
                   + " · goods back " + Money.Pkr(back) + " · cash at the year's end " + Money.Pkr(closing);

        var bills = Sales.Sum(r => r.Bills);
        var sold = Money.Round(Sales.Sum(r => r.Sold));
        var gotIn = Money.Round(Sales.Sum(r => r.Received));
        var owed = Money.Round(Sales.Sum(r => r.StillOwed));
        var profit = Money.Round(Sales.Sum(r => r.Profit));
        SalesTape = bills + (bills == 1 ? " bill · " : " bills · ") + "Sold " + Money.Pkr(sold)
                    + " · money in " + Money.Pkr(gotIn) + " · still owed " + Money.Pkr(owed)
                    + " · profit " + Money.Pkr(profit);

        var lines = Costs.Sum(r => r.Count);
        var costSum = Money.Round(Costs.Sum(r => r.Amount));
        CostTape = lines + (lines == 1 ? " line · " : " lines · ") + Money.Pkr(costSum);

        AfterCosts = Money.Pkr(Money.Round(profit - costSum));
        AfterCostsHow = Money.Pkr(profit) + " of selling profit, less " + Money.Pkr(costSum) + " of costs";
    }

    /// <summary>
    /// The same rows, on paper, with totals under each table. Printed rather than exported, because a year
    /// statement is a document someone asks for - the bank, the tax file, a partner - and it has to say
    /// which figures are cash and which are goods.
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
