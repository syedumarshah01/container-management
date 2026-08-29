using ContainerManagement.Data;
using ContainerManagement.Models;
using Microsoft.EntityFrameworkCore;

namespace ContainerManagement.Services;

public class ReportService
{
    private readonly IDbContextFactory<AppDbContext> _factory;

    public ReportService(IDbContextFactory<AppDbContext> factory) => _factory = factory;

    public async Task<DashboardVm> GetDashboardAsync()
    {
        await using var db = await _factory.CreateDbContextAsync();
        var profits = await GetContainerProfitsAsync(db, null, null);
        var receivables = await GetReceivableSnapshotAsync(db);
        var recent = await db.Sales
            .AsNoTracking()
            .Include(s => s.Customer)
            .Where(s => s.Status == SaleStatus.Active)
            .OrderByDescending(s => s.Date)
            .ThenByDescending(s => s.Id)
            .Take(8)
            .ToListAsync();

        var startOfMonth = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);
        var shop = ShopSettings.Load();
        var inv = await GetGrandInventoryAsync(shop.LowStockQty);

        return new DashboardVm
        {
            OpenContainers = profits.Count(p => p.Status == ContainerStatus.Open),
            TotalContainers = profits.Count,
            InventoryValue = profits.Sum(p => p.RemainingValue),
            MoneyInMarket = receivables.Where(r => r.Balance > 0).Sum(r => r.Balance),
            TotalProfit = profits.Sum(p => p.Profit),
            TotalRevenue = profits.Sum(p => p.Revenue),
            TotalExpenses = profits.Sum(p => p.Expenses),
            CustomerCount = await db.Customers.CountAsync(c => !c.IsWalkIn),
            SalesThisMonth = await db.Sales.CountAsync(s => s.Date >= startOfMonth && s.Status == SaleStatus.Active),
            LowStockCount = inv.Count(r => r.IsLow),
            LowStockHint = inv.Count(r => r.IsLow) == 0
                ? ""
                : inv.Count(r => r.IsLow) + " items below " + Money.Qty(shop.LowStockQty),
            TopReceivables = receivables.Where(r => r.Balance > 0).Take(5).ToList(),
            ContainerProfits = profits,
            RecentSales = recent
        };
    }

    public async Task<List<ContainerProfitRow>> GetContainerProfitsAsync(DateTime? from = null, DateTime? to = null)
    {
        await using var db = await _factory.CreateDbContextAsync();
        return await GetContainerProfitsAsync(db, from, to);
    }

    public async Task<ContainerProfitRow?> GetContainerProfitAsync(int containerId) =>
        (await GetContainerProfitsAsync()).FirstOrDefault(p => p.ContainerId == containerId);

    public async Task<List<InventoryRow>> GetGrandInventoryAsync(decimal? lowAt = null)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var items = await db.ContainerItems
            .AsNoTracking()
            .Include(i => i.Product)
            .Include(i => i.Container)
            .Include(i => i.SaleLines)
            .ToListAsync();

        var threshold = lowAt ?? ShopSettings.Load().LowStockQty;

        return items
            .GroupBy(i => new { i.ProductId, i.Product.Name, i.Product.Unit, i.Product.Sku })
            .Select(g =>
            {
                var remaining = g.Sum(x => x.QuantityRemaining);
                return new InventoryRow
                {
                    ProductId = g.Key.ProductId,
                    ProductName = g.Key.Name,
                    Sku = g.Key.Sku,
                    Unit = g.Key.Unit,
                    TotalRemaining = remaining,
                    TotalValue = g.Sum(x => x.QuantityRemaining * x.UnitCost),
                    IsLow = remaining > 0 && remaining <= threshold,
                    Lots = g.Select(x => new InventoryLot
                    {
                        ContainerId = x.ContainerId,
                        ContainerTitle = x.Container.Title,
                        ContainerItemId = x.Id,
                        Remaining = x.QuantityRemaining,
                        Received = x.QuantityReceived,
                        UnitCost = x.UnitCost,
                        LandedCost = x.UnitCost,
                        NeverSold = x.QuantityRemaining == x.QuantityReceived && x.SaleLines.Count == 0
                    }).OrderBy(l => l.ContainerTitle).ToList()
                };
            })
            .Where(r => r.TotalRemaining > 0 || r.Lots.Any(l => l.NeverSold))
            .OrderBy(r => r.ProductName)
            .ToList();
    }

    public async Task<List<ItemProfitRow>> GetItemProfitsAsync(DateTime? from, DateTime? to, int? containerId)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var q = db.SaleLines.AsNoTracking()
            .Include(l => l.Product)
            .Include(l => l.Sale)
            .Where(l => l.Sale.Status == SaleStatus.Active);
        if (from is DateTime f) q = q.Where(l => l.Sale.Date >= f.Date);
        if (to is DateTime t) q = q.Where(l => l.Sale.Date < t.Date.AddDays(1));
        if (containerId is > 0) q = q.Where(l => l.ContainerId == containerId);
        var list = await q.ToListAsync();
        return list
            .GroupBy(l => new { l.ProductId, l.Product.Name, l.Product.Sku })
            .Select(g => new ItemProfitRow
            {
                ProductName = g.Key.Name,
                Sku = g.Key.Sku,
                QtySold = g.Sum(x => x.Quantity),
                Revenue = g.Sum(x => x.Quantity * x.UnitPrice),
                Cogs = g.Sum(x => x.Quantity * x.UnitCost),
                Profit = g.Sum(x => x.Quantity * x.UnitPrice - x.Quantity * x.UnitCost)
            })
            .OrderByDescending(r => r.Profit)
            .ToList();
    }

    public async Task<List<SoldProductOption>> ListSoldProductsAsync()
    {
        await using var db = await _factory.CreateDbContextAsync();
        var lines = await db.SaleLines.AsNoTracking()
            .Include(l => l.Product)
            .Include(l => l.Sale)
            .Where(l => l.Sale.Status == SaleStatus.Active)
            .ToListAsync();

        return lines
            .GroupBy(l => new { l.ProductId, l.Product.Name, l.Product.Sku, l.Product.Unit })
            .Select(g => new SoldProductOption
            {
                ProductId = g.Key.ProductId,
                Name = g.Key.Name,
                Sku = g.Key.Sku,
                Unit = g.Key.Unit,
                QtySold = g.Sum(x => x.Quantity)
            })
            .OrderBy(p => p.Name)
            .ToList();
    }

    public async Task<(decimal TotalQty, decimal TotalAmount, decimal AvgCost, decimal AvgPrice, List<ItemCustomerSaleRow> Customers)>
        GetItemSalesByCustomerAsync(int productId)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var lines = await db.SaleLines.AsNoTracking()
            .Include(l => l.Sale).ThenInclude(s => s.Customer)
            .Where(l => l.ProductId == productId && l.Sale.Status == SaleStatus.Active)
            .ToListAsync();

        var totalQty = lines.Sum(x => x.Quantity);
        var totalAmount = lines.Sum(x => x.Quantity * x.UnitPrice);
        var totalCost = lines.Sum(x => x.Quantity * x.UnitCost);

        var customers = lines
            .GroupBy(l => new { l.Sale.CustomerId, l.Sale.Customer.Name })
            .Select(g =>
            {
                var qty = g.Sum(x => x.Quantity);
                return new ItemCustomerSaleRow
                {
                    CustomerId = g.Key.CustomerId,
                    CustomerName = g.Key.Name,
                    Qty = qty,
                    AvgCost = qty == 0 ? 0 : Math.Round(g.Sum(x => x.Quantity * x.UnitCost) / qty, 2),
                    AvgPrice = qty == 0 ? 0 : Math.Round(g.Sum(x => x.Quantity * x.UnitPrice) / qty, 2),
                    Amount = g.Sum(x => x.Quantity * x.UnitPrice)
                };
            })
            .OrderByDescending(r => r.Qty)
            .ThenBy(r => r.CustomerName)
            .ToList();

        return (
            totalQty,
            totalAmount,
            totalQty == 0 ? 0 : Math.Round(totalCost / totalQty, 2),
            totalQty == 0 ? 0 : Math.Round(totalAmount / totalQty, 2),
            customers);
    }

    public async Task<List<BestSellerRow>> GetBestSellersAsync(DateTime? from, DateTime? to)
    {
        var items = await GetItemProfitsAsync(from, to, null);
        return items
            .OrderByDescending(i => i.QtySold)
            .Take(20)
            .Select(i => new BestSellerRow
            {
                ProductName = i.ProductName,
                Qty = i.QtySold,
                Revenue = i.Revenue
            })
            .ToList();
    }

    public async Task<List<DailySummaryRow>> GetDailyAsync(DateTime? from, DateTime? to)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var sales = await db.Sales.AsNoTracking()
            .Where(s => s.Status == SaleStatus.Active)
            .ToListAsync();
        if (from is DateTime f) sales = sales.Where(s => s.Date >= f.Date).ToList();
        if (to is DateTime t) sales = sales.Where(s => s.Date < t.Date.AddDays(1)).ToList();

        var pays = await db.Payments.AsNoTracking().ToListAsync();
        if (from is DateTime f2) pays = pays.Where(p => p.Date >= f2.Date).ToList();
        if (to is DateTime t2) pays = pays.Where(p => p.Date < t2.Date.AddDays(1)).ToList();

        var days = sales.Select(s => s.Date.Date).Concat(pays.Select(p => p.Date.Date)).Distinct().OrderByDescending(d => d);
        return days.Select(d =>
        {
            var daySales = sales.Where(s => s.Date.Date == d).ToList();
            var dayPay = pays.Where(p => p.Date.Date == d).ToList();
            var billed = daySales.Sum(s => s.TotalAmount);
            var cash = dayPay.Sum(p => p.Amount);
            return new DailySummaryRow
            {
                Date = d,
                Bills = daySales.Count,
                Sales = billed,
                CashIn = cash,
                Credit = Math.Max(0, billed - daySales.Sum(s => s.PaidNow))
            };
        }).ToList();
    }

    private static async Task<List<ContainerProfitRow>> GetContainerProfitsAsync(AppDbContext db, DateTime? from, DateTime? to)
    {
        var containers = await db.Containers
            .AsNoTracking()
            .Include(c => c.Items)
            .Include(c => c.Expenses)
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync();

        var saleLines = await db.SaleLines.AsNoTracking().Include(l => l.Sale).ToListAsync();
        saleLines = saleLines.Where(l => l.Sale.Status == SaleStatus.Active).ToList();
        if (from is DateTime f) saleLines = saleLines.Where(l => l.Sale.Date >= f.Date).ToList();
        if (to is DateTime t) saleLines = saleLines.Where(l => l.Sale.Date < t.Date.AddDays(1)).ToList();

        var lines = saleLines
            .GroupBy(l => l.ContainerId)
            .Select(g => new
            {
                ContainerId = g.Key,
                Revenue = g.Sum(x => x.Quantity * x.UnitPrice),
                Cogs = g.Sum(x => x.Quantity * x.UnitCost),
                QtySold = g.Sum(x => x.Quantity)
            })
            .ToList();

        return containers.Select(c =>
        {
            var s = lines.FirstOrDefault(x => x.ContainerId == c.Id);
            var revenue = s?.Revenue ?? 0;
            var cogs = s?.Cogs ?? 0;
            var expenses = from is null && to is null
                ? c.Expenses.Sum(e => e.Amount)
                : c.Expenses.Where(e =>
                    (from is null || e.Date >= from.Value.Date) &&
                    (to is null || e.Date < to.Value.Date.AddDays(1))).Sum(e => e.Amount);
            return new ContainerProfitRow
            {
                ContainerId = c.Id,
                Title = c.Title,
                ContainerNumber = c.ContainerNumber,
                Origin = c.Origin,
                ArrivalDate = c.ArrivalDate,
                Status = c.Status,
                Revenue = revenue,
                Cogs = cogs,
                Expenses = expenses,
                Profit = revenue - cogs,
                RemainingValue = c.Items.Sum(i => i.QuantityRemaining * i.UnitCost),
                RemainingQty = c.Items.Sum(i => i.QuantityRemaining),
                QtySold = s?.QtySold ?? 0,
                QtyReceived = c.Items.Sum(i => i.QuantityReceived)
            };
        }).ToList();
    }

    private static async Task<List<ReceivableRow>> GetReceivableSnapshotAsync(AppDbContext db)
    {
        var customers = await db.Customers.AsNoTracking().ToListAsync();
        var entries = await db.LedgerEntries.AsNoTracking().ToListAsync();
        var balances = entries
            .GroupBy(e => e.CustomerId)
            .Select(g => new { CustomerId = g.Key, Balance = g.Sum(x => x.Debit - x.Credit) })
            .ToList();

        return customers
            .Select(c => new ReceivableRow
            {
                CustomerId = c.Id,
                Name = c.Name,
                Phone = c.Phone,
                Balance = balances.FirstOrDefault(b => b.CustomerId == c.Id)?.Balance ?? 0
            })
            .OrderByDescending(r => r.Balance)
            .ToList();
    }
}
