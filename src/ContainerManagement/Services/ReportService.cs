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
        var sales = await db.Sales
            .AsNoTracking()
            .Include(s => s.Customer)
            .Where(s => s.Status == SaleStatus.Active)
            .ToListAsync();
        var recent = sales.OrderByDescending(s => s.Date).ThenByDescending(s => s.Id).Take(8).ToList();
        var pays = await db.Payments.AsNoTracking().Where(p => p.SaleId != null).ToListAsync();
        var saleReturns = await db.SaleReturns.AsNoTracking().ToListAsync();
        var unpaid = sales
            .Select(s => new AttentionInvoiceRow
            {
                SaleId = s.Id,
                CustomerId = s.CustomerId,
                CustomerName = s.Customer.Name,
                Date = s.Date,
                Remaining = s.TotalAmount
                    - pays.Where(p => p.SaleId == s.Id).Sum(p => p.Amount)
                    - saleReturns.Where(r => r.SaleId == s.Id).Sum(r => r.Amount)
            })
            .Where(u => u.Remaining > 0.009m)
            .OrderByDescending(u => u.Remaining)
            .ToList();

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
                : inv.Count(r => r.IsLow) + " items at or below " + Money.Qty(shop.LowStockQty),
            TopReceivables = receivables.Where(r => r.Balance > 0).Take(5).ToList(),
            ContainerProfits = profits,
            RecentSales = recent,
            LowStockItems = inv.Where(r => r.IsLow).OrderBy(r => r.TotalRemaining).ThenBy(r => r.ProductName).Take(10).ToList(),
            UnpaidInvoices = unpaid.Take(10).ToList(),
            UnpaidCount = unpaid.Count,
            UnpaidTotal = unpaid.Sum(u => u.Remaining)
        };
    }

    public async Task<(decimal Sales, decimal Profit, List<HomeDayRow> Days)> GetHomeMonthAsync()
    {
        var start = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        var end = start.AddMonths(1);
        await using var db = await _factory.CreateDbContextAsync();

        var lines = await db.SaleLines.AsNoTracking()
            .Include(l => l.Sale)
            .Where(l => l.Sale.Status == SaleStatus.Active)
            .ToListAsync();
        lines = lines.Where(l => l.Sale.Date >= start && l.Sale.Date < end).ToList();

        var returned = await db.SaleReturnLines.AsNoTracking()
            .Include(l => l.Return)
            .ToListAsync();
        returned = returned.Where(l => l.Return.Date >= start && l.Return.Date < end).ToList();

        var expenses = await db.ShopExpenses.AsNoTracking().ToListAsync();
        expenses = expenses.Where(e => e.Date >= start && e.Date < end).ToList();

        var days = new Dictionary<DateTime, (decimal Sales, decimal Cogs, decimal Expenses)>();

        void Touch(DateTime day)
        {
            day = day.Date;
            if (!days.ContainsKey(day))
                days[day] = (0, 0, 0);
        }

        foreach (var l in lines)
        {
            var day = l.Sale.Date.Date;
            Touch(day);
            var cur = days[day];
            days[day] = (cur.Sales + l.Quantity * l.UnitPrice, cur.Cogs + l.Quantity * l.UnitCost, cur.Expenses);
        }

        foreach (var r in returned)
        {
            var day = r.Return.Date.Date;
            Touch(day);
            var cur = days[day];
            days[day] = (cur.Sales - r.Amount, cur.Cogs - r.Quantity * r.UnitCost, cur.Expenses);
        }

        foreach (var e in expenses)
        {
            var day = e.Date.Date;
            Touch(day);
            var cur = days[day];
            days[day] = (cur.Sales, cur.Cogs, cur.Expenses + e.Amount);
        }

        var rows = days
            .OrderByDescending(kv => kv.Key)
            .Select(kv => new HomeDayRow
            {
                Date = kv.Key,
                Sales = kv.Value.Sales,
                Profit = kv.Value.Sales - kv.Value.Cogs - kv.Value.Expenses
            })
            .ToList();

        return (rows.Sum(r => r.Sales), rows.Sum(r => r.Profit), rows);
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
                    IsLow = remaining <= threshold,
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
            .Where(r => r.Lots.Count > 0)
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
        var retQ = db.SaleReturnLines.AsNoTracking()
            .Include(l => l.Product)
            .Include(l => l.Return)
            .AsQueryable();
        if (from is DateTime rf) retQ = retQ.Where(l => l.Return.Date >= rf.Date);
        if (to is DateTime rt) retQ = retQ.Where(l => l.Return.Date < rt.Date.AddDays(1));
        if (containerId is > 0) retQ = retQ.Where(l => l.ContainerId == containerId);
        var returned = await retQ.ToListAsync();

        return list
            .GroupBy(l => new { l.ProductId, l.Product.Name, l.Product.Sku })
            .Select(g =>
            {
                var rets = returned.Where(x => x.ProductId == g.Key.ProductId).ToList();
                var qty = g.Sum(x => x.Quantity) - rets.Sum(x => x.Quantity);
                var revenue = g.Sum(x => x.Quantity * x.UnitPrice) - rets.Sum(x => x.Amount);
                var cogs = g.Sum(x => x.Quantity * x.UnitCost) - rets.Sum(x => x.Quantity * x.UnitCost);
                return new ItemProfitRow
                {
                    ProductName = g.Key.Name,
                    Sku = g.Key.Sku,
                    QtySold = qty,
                    Revenue = revenue,
                    Cogs = cogs,
                    Profit = revenue - cogs
                };
            })
            .Where(r => r.QtySold > 0.0005m || r.Revenue > 0.009m)
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
        var returned = await db.SaleReturnLines.AsNoTracking().ToListAsync();

        return lines
            .GroupBy(l => new { l.ProductId, l.Product.Name, l.Product.Sku, l.Product.Unit })
            .Select(g => new SoldProductOption
            {
                ProductId = g.Key.ProductId,
                Name = g.Key.Name,
                Sku = g.Key.Sku,
                Unit = g.Key.Unit,
                QtySold = g.Sum(x => x.Quantity) - returned.Where(x => x.ProductId == g.Key.ProductId).Sum(x => x.Quantity)
            })
            .Where(p => p.QtySold > 0.0005m)
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
        var returned = await db.SaleReturnLines.AsNoTracking()
            .Include(l => l.Return)
            .Where(l => l.ProductId == productId)
            .ToListAsync();

        var totalQty = lines.Sum(x => x.Quantity) - returned.Sum(x => x.Quantity);
        var totalAmount = lines.Sum(x => x.Quantity * x.UnitPrice) - returned.Sum(x => x.Amount);
        var totalCost = lines.Sum(x => x.Quantity * x.UnitCost) - returned.Sum(x => x.Quantity * x.UnitCost);

        var customers = lines
            .GroupBy(l => new { l.Sale.CustomerId, l.Sale.Customer.Name })
            .Select(g =>
            {
                var rets = returned.Where(x => x.Return.CustomerId == g.Key.CustomerId).ToList();
                var qty = g.Sum(x => x.Quantity) - rets.Sum(x => x.Quantity);
                var cost = g.Sum(x => x.Quantity * x.UnitCost) - rets.Sum(x => x.Quantity * x.UnitCost);
                var amount = g.Sum(x => x.Quantity * x.UnitPrice) - rets.Sum(x => x.Amount);
                return new ItemCustomerSaleRow
                {
                    CustomerId = g.Key.CustomerId,
                    CustomerName = g.Key.Name,
                    Qty = qty,
                    AvgCost = qty == 0 ? 0 : Math.Round(cost / qty, 2),
                    AvgPrice = qty == 0 ? 0 : Math.Round(amount / qty, 2),
                    Amount = amount
                };
            })
            .Where(r => r.Qty > 0.0005m)
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

        var returnLines = await db.SaleReturnLines.AsNoTracking().Include(l => l.Return).ToListAsync();
        if (from is DateTime rf) returnLines = returnLines.Where(l => l.Return.Date >= rf.Date).ToList();
        if (to is DateTime rt) returnLines = returnLines.Where(l => l.Return.Date < rt.Date.AddDays(1)).ToList();

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
            var rets = returnLines.Where(x => x.ContainerId == c.Id).ToList();
            var revenue = (s?.Revenue ?? 0) - rets.Sum(x => x.Amount);
            var cogs = (s?.Cogs ?? 0) - rets.Sum(x => x.Quantity * x.UnitCost);
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
                QtySold = (s?.QtySold ?? 0) - rets.Sum(x => x.Quantity),
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
