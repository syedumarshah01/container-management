using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ContainerManagement.Models;
using ContainerManagement.Services;

namespace ContainerManagement.ViewModels;

public partial class ItemSalesViewModel : ViewModelBase
{
    private readonly ReportService _reports;
    private readonly IAppShell _shell;
    private List<SoldProductOption> _products = new();

    public ItemSalesViewModel(ReportService reports, IAppShell shell)
    {
        _reports = reports;
        _shell = shell;
        SearchItems = PopulateSearchAsync;
    }

    public Func<string?, CancellationToken, Task<IEnumerable<object>>> SearchItems { get; }
    public ObservableCollection<ItemCustomerSaleRow> Customers { get; } = new();

    [ObservableProperty] private string query = "";
    [ObservableProperty] private SoldProductOption? selectedProduct;
    [ObservableProperty] private ItemCustomerSaleRow? selectedCustomer;
    [ObservableProperty] private string heading = "Item sales";
    [ObservableProperty] private string totalQty = "—";
    [ObservableProperty] private string totalAmount = "—";
    [ObservableProperty] private string avgCost = "—";
    [ObservableProperty] private string avgPrice = "—";
    [ObservableProperty] private bool hasResult;

    public override async Task LoadAsync()
    {
        _products = await _reports.ListSoldProductsAsync();
        if (SelectedProduct is not null)
            await ShowProductAsync(SelectedProduct);
    }

    partial void OnSelectedProductChanged(SoldProductOption? value)
    {
        if (value is null)
        {
            ClearResult();
            return;
        }
        _ = ShowProductAsync(value);
    }

    [RelayCommand]
    private void OpenCustomer()
    {
        if (SelectedCustomer is not null)
            _shell.OpenCustomer(SelectedCustomer.CustomerId);
    }

    private async Task ShowProductAsync(SoldProductOption product)
    {
        try
        {
            var (qty, amount, cost, price, rows) = await _reports.GetItemSalesByCustomerAsync(product.ProductId);
            Heading = product.Name;
            TotalQty = Money.Qty(qty) + " " + product.Unit;
            TotalAmount = Money.Pkr(amount);
            AvgCost = Money.Pkr(cost);
            AvgPrice = Money.Pkr(price);
            Customers.Clear();
            foreach (var r in rows)
                Customers.Add(r);
            HasResult = true;
        }
        catch (Exception ex)
        {
            _shell.Notify(ex.Message, true);
        }
    }

    private void ClearResult()
    {
        Heading = "Item sales";
        TotalQty = "—";
        TotalAmount = "—";
        AvgCost = "—";
        AvgPrice = "—";
        Customers.Clear();
        HasResult = false;
    }

    private async Task<IEnumerable<object>> PopulateSearchAsync(string? search, CancellationToken ct)
    {
        await Task.Delay(300, ct);
        if (string.IsNullOrWhiteSpace(search))
            return Array.Empty<object>();

        return _products
            .Where(p =>
                p.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
                || (p.Sku ?? "").Contains(search, StringComparison.OrdinalIgnoreCase))
            .Take(40)
            .Cast<object>()
            .ToList();
    }
}
