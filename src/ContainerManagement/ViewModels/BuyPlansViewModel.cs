using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ContainerManagement.Models;
using ContainerManagement.Services;

namespace ContainerManagement.ViewModels;

/// <summary>
/// The list of China order plans. Numbers here come from each plan's rows; the plan is a
/// calculation sheet, not stock.
/// </summary>
public partial class BuyPlansViewModel : ViewModelBase
{
    private readonly BuyPlanService _plans;
    private readonly AccessService _access;
    private readonly IAppShell _shell;

    public BuyPlansViewModel(BuyPlanService plans, AccessService access, IAppShell shell)
    {
        _plans = plans;
        _access = access;
        _shell = shell;
    }

    public ObservableCollection<BuyPlanRow> Rows { get; } = new();

    [ObservableProperty] private BuyPlanRow? selected;
    [ObservableProperty] private string newTitle = "";
    [ObservableProperty] private string newSupplier = "";
    [ObservableProperty] private decimal? newYenRate = BuyPlanService.SuggestedRate;
    [ObservableProperty] private decimal? newExpense;
    [ObservableProperty] private bool showAddForm;
    [ObservableProperty] private bool confirmDelete;
    [ObservableProperty] private string summary = "";
    [ObservableProperty] private string allSpend = "—";
    [ObservableProperty] private string allSale = "—";
    [ObservableProperty] private string allProfit = "—";
    [ObservableProperty] private bool allProfitGood = true;
    [ObservableProperty] private string bestPlan = "—";
    [ObservableProperty] private bool isOwner;

    public override async Task LoadAsync()
    {
        IsOwner = _access.IsOwner;
        var keepId = Selected?.Id;
        var list = await _plans.ListAsync();

        Rows.Clear();
        foreach (var r in list)
            Rows.Add(r);
        Selected = keepId is int id ? Rows.FirstOrDefault(r => r.Id == id) : null;

        var all = Rows.ToList();
        var spend = all.Sum(r => r.Total.SpendPkr);
        var sale = all.Sum(r => r.Total.SalePkr);
        var profit = all.Sum(r => r.Total.ProfitPkr);

        AllSpend = Money.PkrCompact(spend);
        AllSale = Money.PkrCompact(sale);
        AllProfit = Money.PkrCompact(profit);
        AllProfitGood = profit >= 0;
        var best = all.OrderByDescending(r => r.Total.ProfitPkr).FirstOrDefault();
        BestPlan = best is null || all.Count == 0 ? "—" : $"{best.TitleText} · {best.Total.ProfitText}";
        Summary = all.Count == 0
            ? "No plans yet. Start one when you sit down to write the China list."
            : all.Count + (all.Count == 1 ? " plan" : " plans")
              + " · if all of it sells at these prices, " + Money.PkrCompact(profit) + " is made on top of "
              + Money.PkrCompact(spend);
    }

    [RelayCommand]
    private void BeginAdd()
    {
        if (!_access.IsOwner)
        {
            _shell.Notify("Owner PIN needed to add a buy plan.", true);
            return;
        }
        ShowAddForm = true;
    }

    [RelayCommand] private void CancelAdd() => ShowAddForm = false;

    [RelayCommand]
    private void Open()
    {
        if (Selected is not null)
            _shell.OpenBuyPlan(Selected.Id);
    }

    [RelayCommand]
    private async Task CreateAsync()
    {
        if (!_access.IsOwner)
        {
            _shell.Notify("Owner PIN needed to add a buy plan.", true);
            return;
        }
        try
        {
            var plan = await _plans.CreateAsync(NewTitle, NewSupplier, NewYenRate ?? 0, NewExpense ?? 0);
            NewTitle = "";
            NewSupplier = "";
            NewExpense = null;
            ShowAddForm = false;
            await LoadAsync();
            _shell.Notify("Plan made. Fill the rows, then Save plan.");
            _shell.OpenBuyPlan(plan.Id);
        }
        catch (Exception ex)
        {
            _shell.Notify(ex.Message, true);
        }
    }

    [RelayCommand]
    private async Task DuplicateAsync()
    {
        if (!_access.IsOwner)
        {
            _shell.Notify("Owner PIN needed to copy a buy plan.", true);
            return;
        }
        if (Selected is null)
        {
            _shell.Notify("Pick a plan in the table first.", true);
            return;
        }
        try
        {
            var copy = await _plans.DuplicateAsync(Selected.Id);
            await LoadAsync();
            Selected = Rows.FirstOrDefault(r => r.Id == copy.Id);
            _shell.Notify("Copied. Change the numbers on the copy.");
        }
        catch (Exception ex)
        {
            _shell.Notify(ex.Message, true);
        }
    }

    [RelayCommand]
    private async Task DeleteAsync()
    {
        if (!_access.IsOwner)
        {
            _shell.Notify("Owner PIN needed to delete a buy plan.", true);
            return;
        }
        if (Selected is null)
        {
            _shell.Notify("Pick a plan in the table first.", true);
            return;
        }
        if (!ConfirmDelete)
        {
            _shell.Notify("Tick “Delete for good” first.", true);
            return;
        }
        try
        {
            var gone = Selected.TitleText;
            await _plans.DeleteAsync(Selected.Id);
            ConfirmDelete = false;
            await LoadAsync();
            _shell.Notify(gone + " deleted.");
        }
        catch (Exception ex)
        {
            _shell.Notify(ex.Message, true);
        }
    }
}
