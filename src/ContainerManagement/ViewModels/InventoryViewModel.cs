using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ContainerManagement.Models;
using ContainerManagement.Services;

namespace ContainerManagement.ViewModels;

public partial class InventoryViewModel : ViewModelBase
{
    private readonly ReportService _reports;
    private readonly IAppShell _shell;
    private List<InventoryRow> _all = new();

    public InventoryViewModel(ReportService reports, IAppShell shell)
    {
        _reports = reports;
        _shell = shell;
    }

    public override bool FillsPage => true;

    public ObservableCollection<InventoryRow> Rows { get; } = new();
    public ObservableCollection<InventoryLot> SelectedLots { get; } = new();

    [ObservableProperty] private string query = "";
    [ObservableProperty] private string totalValue = "—";
    [ObservableProperty] private string productCount = "—";
    [ObservableProperty] private string lotCount = "—";
    [ObservableProperty] private string unitsRemaining = "—";
    [ObservableProperty] private string lowHint = "";
    [ObservableProperty] private InventoryRow? selected;
    [ObservableProperty] private InventoryLot? selectedLot;
    [ObservableProperty] private string lotsHeading = "Select an item to see which containers hold it.";
    [ObservableProperty] private bool hasSelectedLots;

    public override async Task LoadAsync()
    {
        var keepId = Selected?.ProductId;
        _all = await _reports.GetGrandInventoryAsync();
        ApplyFilter();
        if (keepId is int id)
            Selected = Rows.FirstOrDefault(r => r.ProductId == id);
    }

    partial void OnQueryChanged(string value) => ApplyFilter();

    partial void OnSelectedChanged(InventoryRow? value)
    {
        SelectedLots.Clear();
        if (value is null)
        {
            HasSelectedLots = false;
            LotsHeading = "Select an item to see which containers hold it.";
            return;
        }

        foreach (var lot in value.Lots.OrderBy(l => l.ContainerTitle))
            SelectedLots.Add(lot);
        HasSelectedLots = SelectedLots.Count > 0;
        LotsHeading = SelectedLots.Count == 1
            ? value.ProductName + " is in 1 container."
            : value.ProductName + " is in " + SelectedLots.Count + " containers.";
    }

    [RelayCommand]
    private void OpenLot()
    {
        if (SelectedLot is not null)
            _shell.OpenContainer(SelectedLot.ContainerId);
    }

    private void ApplyFilter()
    {
        IEnumerable<InventoryRow> src = _all;
        if (!string.IsNullOrWhiteSpace(Query))
        {
            src = _all.Where(r =>
                r.ProductName.Contains(Query, StringComparison.OrdinalIgnoreCase)
                || (r.Sku ?? "").Contains(Query, StringComparison.OrdinalIgnoreCase)
                || r.Lots.Any(l => l.ContainerTitle.Contains(Query, StringComparison.OrdinalIgnoreCase)));
        }

        var list = src.ToList();
        var keepId = Selected?.ProductId;
        Rows.Clear();
        foreach (var r in list.Where(r => r.TotalRemaining > 0))
            Rows.Add(r);

        TotalValue = Money.Pkr(list.Sum(r => r.TotalValue));
        ProductCount = list.Count.ToString();
        LotCount = list.Sum(r => r.Lots.Count).ToString();
        UnitsRemaining = Money.Qty(list.Sum(r => r.TotalRemaining));
        var low = list.Count(r => r.IsLow);
        LowHint = low == 0 ? "No low-stock items." : low + " items are at or below the low-stock level (Settings).";

        Selected = keepId is int id ? Rows.FirstOrDefault(r => r.ProductId == id) : null;
    }
}
