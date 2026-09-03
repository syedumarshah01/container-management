using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ContainerManagement.Models;
using ContainerManagement.Services;

namespace ContainerManagement.ViewModels;

public partial class ProfitViewModel : ViewModelBase
{
    private readonly ReportService _reports;
    private readonly ExportService _export;
    private readonly IAppShell _shell;

    public ProfitViewModel(ReportService reports, ExportService export, IAppShell shell)
    {
        _reports = reports;
        _export = export;
        _shell = shell;
    }

    public ObservableCollection<ContainerProfitRow> Rows { get; } = new();
    public ObservableCollection<ItemProfitRow> Items { get; } = new();
    public ObservableCollection<ContainerProfitRow> ContainerFilter { get; } = new();

    [ObservableProperty] private ContainerProfitRow? selected;
    [ObservableProperty] private ContainerProfitRow? filterContainer;
    [ObservableProperty] private DateTimeOffset? fromDate;
    [ObservableProperty] private DateTimeOffset? toDate;
    [ObservableProperty] private string revenue = "—";
    [ObservableProperty] private string cogs = "—";
    [ObservableProperty] private string expenses = "—";
    [ObservableProperty] private string profit = "—";
    [ObservableProperty] private string profitHint = "";

    public override async Task LoadAsync()
    {
        var all = await _reports.GetContainerProfitsAsync();
        ContainerFilter.Clear();
        ContainerFilter.Add(new ContainerProfitRow { ContainerId = 0, Title = "All containers" });
        foreach (var c in all)
            ContainerFilter.Add(c);
        FilterContainer ??= ContainerFilter[0];
        await ApplyAsync();
    }

    [RelayCommand]
    private async Task ApplyAsync()
    {
        var from = FromDate?.DateTime;
        var to = ToDate?.DateTime;
        var cid = FilterContainer is { ContainerId: > 0 } ? FilterContainer.ContainerId : (int?)null;

        var list = await _reports.GetContainerProfitsAsync(from, to);
        if (cid is int id)
            list = list.Where(r => r.ContainerId == id).ToList();
        Rows.Clear();
        foreach (var r in list)
            Rows.Add(r);

        var items = await _reports.GetItemProfitsAsync(from, to, cid);
        Items.Clear();
        foreach (var i in items)
            Items.Add(i);

        var rev = list.Sum(r => r.Revenue);
        var cost = list.Sum(r => r.Cogs);
        var exp = list.Sum(r => r.Expenses);
        var p = list.Sum(r => r.Profit);
        var stock = list.Sum(r => r.RemainingValue);
        Revenue = Money.Pkr(rev);
        Cogs = Money.Pkr(cost);
        Expenses = Money.Pkr(exp);
        Profit = Money.Pkr(p);
        ProfitHint = $"Unsold stock still worth {Money.Pkr(stock)}.";
    }

    [RelayCommand]
    private void Export()
    {
        try
        {
            _export.ProfitWorkbook(Rows.ToList(), Items.ToList());
            _shell.Notify("CSV files opened. Excel can open them.");
        }
        catch (Exception ex) { _shell.Notify(ex.Message, true); }
    }

    [RelayCommand]
    private void Open()
    {
        if (Selected is not null)
            _shell.OpenContainer(Selected.ContainerId);
    }
}
