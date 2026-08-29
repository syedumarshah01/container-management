using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using ContainerManagement.Models;
using ContainerManagement.Services;

namespace ContainerManagement.ViewModels;

public partial class InventoryViewModel : ViewModelBase
{
    private readonly ReportService _reports;
    private List<InventoryRow> _all = new();

    public InventoryViewModel(ReportService reports) => _reports = reports;

    public ObservableCollection<InventoryRow> Rows { get; } = new();

    [ObservableProperty] private string query = "";
    [ObservableProperty] private string totalValue = "—";
    [ObservableProperty] private string productCount = "—";
    [ObservableProperty] private string lotCount = "—";
    [ObservableProperty] private string unitsRemaining = "—";
    [ObservableProperty] private string lowHint = "";

    public override async Task LoadAsync()
    {
        _all = await _reports.GetGrandInventoryAsync();
        ApplyFilter();
    }

    partial void OnQueryChanged(string value) => ApplyFilter();

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
        Rows.Clear();
        foreach (var r in list.Where(r => r.TotalRemaining > 0))
            Rows.Add(r);

        TotalValue = Money.Pkr(list.Sum(r => r.TotalValue));
        ProductCount = list.Count.ToString();
        LotCount = list.Sum(r => r.Lots.Count).ToString();
        UnitsRemaining = Money.Qty(list.Sum(r => r.TotalRemaining));
        var low = list.Count(r => r.IsLow);
        LowHint = low == 0 ? "No low-stock items." : low + " items are at or below the low-stock level (Settings).";
    }
}
