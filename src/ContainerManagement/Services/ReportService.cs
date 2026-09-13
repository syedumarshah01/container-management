using ContainerManagement.Data;
using ContainerManagement.Models;
using Microsoft.EntityFrameworkCore;

namespace ContainerManagement.Services;

public class ReportService
{
    private readonly IDbContextFactory<AppDbContext> _factory;

    public ReportService(IDbContextFactory<AppDbContext> factory) => _factory = factory;

    /// <summary>
    /// Home's book: the containers, their selling, what their bills still have out there, the shelf and the
    /// profit. Two dates are optional - with neither, this is the book entire, which is what the page opens
    /// on; with them, the sums cover those days and nothing else, by the same rules the container pages use,
    /// because this method reads those rows rather than adding the money a second way.
    ///
    /// What a range does to each figure is not the same thing, and the card says so rather than pretending:
    /// sales, profit and what is still out there are the bills inside the dates; the container count is the
    /// containers that did business in them; and the shelf is what is on it today, because stock between two
    /// dates would have to be rebuilt from every movement since, which this book does not keep.
    /// The lists under the card - the bills needing attention, low stock - stay the shop's whole situation,
    /// because a short list is not a safe thing to act on.
    /// </summary>
    public async Task<DashboardVm> GetDashboardAsync(DateTime? from = null, DateTime? to = null)
    {
        var (start, end) = BookRange(from, to);
        await using var db = await _factory.CreateDbContextAsync();
        var profits = await GetContainerProfitsAsync(db, start, end);
        var receivables = await GetReceivableSnapshotAsync(db);
        var sales = await db.Sales
            .AsNoTracking()
            .Include(s => s.Customer)
            .Where(s => s.Status == SaleStatus.Active)
            .ToListAsync();
        var recent = sales.OrderByDescending(s => s.Date).ThenByDescending(s => s.Id).Take(8).ToList();
        var pays = await db.Payments.AsNoTracking().Where(p => p.SaleId != null).ToListAsync();
        var saleReturns = await db.SaleReturns.AsNoTracking().ToListAsync();
        // What is left on each bill, by the one formula a bill is measured with everywhere else - its own
        // page, the customer's ledger, the containers' money. The totals below and the list of bills needing
        // attention are both read off this dictionary, because a page that added the same money a second way
        // would be a page with two answers to one question.
        var left = sales.ToDictionary(
            s => s.Id,
            s => SalesService.RemainingOf(s,
                pays.Where(p => p.SaleId == s.Id).Sum(p => p.Amount),
                saleReturns.Where(r => r.SaleId == s.Id).Sum(r => r.Amount)));
        var unpaid = sales
            .Select(s => new AttentionInvoiceRow
            {
                SaleId = s.Id,
                CustomerId = s.CustomerId,
                CustomerName = s.Customer.Name,
                Date = s.Date,
                Remaining = left[s.Id]
            })
            .Where(u => u.Remaining > 0.009m)
            .OrderByDescending(u => u.Remaining)
            .ToList();

        var startOfMonth = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);
        var shop = ShopSettings.Load();
        var inv = await GetGrandInventoryAsync(shop.LowStockQty);

        var containers = await db.Containers.AsNoTracking().ToListAsync();
        // Ranged, "containers" can only mean the ones with money moving on them in the dates: a container with
        // nothing sold, returned or spent in them has no figure to contribute, and counting it would say the
        // shop did business on a lot it did not. Unranged it is the book's own count, as it always was.
        var counted = start is null && end is null
            ? containers.Count
            : profits.Count(p => p.Revenue != 0m || p.Cogs != 0m || p.Expenses != 0m);
        var onContainers = Money.Round(profits.Sum(p => p.Expenses));
        var atTheShop = Money.Round((await db.ShopExpenses.AsNoTracking().ToListAsync()).Sum(e => e.Amount));

        return new DashboardVm
        {
            OpenContainers = containers.Count(c => c.Status == ContainerStatus.Open),
            TotalContainers = counted,
            TotalPurchases = Money.Round(containers.Sum(c => c.SupplierAmount)),
            InventoryValue = Money.Round(profits.Sum(p => p.RemainingValue)),
            MoneyInMarket = Money.Round(profits.Sum(p => p.InMarket)),
            Outstanding = Money.Round(left.Values.Sum()),
            TotalProfit = Money.Round(profits.Sum(p => p.Profit)),
            TotalRevenue = Money.Round(profits.Sum(p => p.Revenue)),
            ContainerExpenses = onContainers,
            ShopExpenses = atTheShop,
            TotalExpenses = Money.Round(onContainers + atTheShop),
            MoneyOwedByCustomers = Money.Round(receivables.Where(r => r.Balance > 0).Sum(r => r.Balance)),
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
            UnpaidTotal = Money.Round(unpaid.Sum(u => u.Remaining))
        };
    }

    /// <summary>
    /// This month's sales, cost and the till's own bills, day by day, by the rules this line has always used:
    /// a bill is its total after the discount shared over its lines, a return comes off the day it was made,
    /// and an expense belongs to the day it was written. The month is the month - Home's date boxes move the
    /// book above it, not this figure, so the page always has one number that says what the shop has done
    /// since the 1st without being told what "told" means.
    /// </summary>
    public async Task<(decimal Sales, decimal Profit, List<HomeDayRow> Days)> GetHomeMonthAsync()
    {
        var firstOfMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        var start = firstOfMonth;
        var end = firstOfMonth.AddMonths(1);
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

        var netted = await NetRevenueByLineAsync(db, lines.Select(l => l.SaleId));
        foreach (var l in lines)
        {
            var day = l.Sale.Date.Date;
            Touch(day);
            var cur = days[day];
            days[day] = (cur.Sales + netted.GetValueOrDefault(l.Id, l.LineTotal), cur.Cogs + l.LineCost, cur.Expenses);
        }

        foreach (var r in returned)
        {
            var day = r.Return.Date.Date;
            Touch(day);
            var cur = days[day];
            days[day] = (cur.Sales - r.Amount, cur.Cogs - Money.Round(r.Quantity * r.UnitCost), cur.Expenses);
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

    /// <summary>
    /// The selling year, month by month, on the rule Home's tape is already built by: a bill is its total
    /// after the discount, shared over its lines, and a return comes off the month it was made in, because
    /// that is the month the goods walked back through the door. Two more columns answer what a year is
    /// actually asked: the money that arrived that month, whatever bill it was pointed at, and what the
    /// bills of that month still have owing - measured over every payment and return ever made, so a March
    /// bill settled in July does not go on being owed. Everything is filtered in memory rather than in
    /// SQL, because the dates sit in text columns and a year boundary is not somewhere to let SQLite
    /// decide anything.
    /// </summary>
    public async Task<List<SalesYearRow>> GetYearSalesAsync(int year)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var start = new DateTime(year, 1, 1);
        var end = start.AddYears(1);

        var sales = await db.Sales.AsNoTracking().ToListAsync();
        var bills = sales
            .Where(s => s.Status == SaleStatus.Active && s.Date >= start && s.Date < end)
            .ToList();
        var ids = bills.Select(x => x.Id).ToList();
        var netted = await NetRevenueByLineAsync(db, ids);

        var bySale = (await db.SaleLines.AsNoTracking().ToListAsync())
            .Where(l => ids.Contains(l.SaleId))
            .GroupBy(l => l.SaleId)
            .ToDictionary(g => g.Key, g => g.ToList());
        var backLines = (await db.SaleReturnLines.AsNoTracking().Include(l => l.Return).ToListAsync())
            .Where(l => l.Return.Date >= start && l.Return.Date < end)
            .ToList();
        var returns = await db.SaleReturns.AsNoTracking().ToListAsync();
        var pays = await db.Payments.AsNoTracking().ToListAsync();

        var rows = new List<SalesYearRow>(12);
        for (var m = 1; m <= 12; m++)
        {
            var from = start.AddMonths(m - 1);
            var to = from.AddMonths(1);
            var monthBills = bills.Where(s => s.Date >= from && s.Date < to).ToList();
            var monthLines = monthBills.SelectMany(s =>
                bySale.TryGetValue(s.Id, out var l) ? l : new List<SaleLine>()).ToList();
            var monthBack = backLines.Where(l => l.Return.Date >= from && l.Return.Date < to).ToList();
            rows.Add(new SalesYearRow
            {
                Month = m,
                Bills = monthBills.Count,
                Sold = Money.Round(monthLines.Sum(l => netted.GetValueOrDefault(l.Id, l.LineTotal))
                                   - monthBack.Sum(l => l.Amount)),
                Cogs = Money.Round(monthLines.Sum(l => l.LineCost)
                                   - monthBack.Sum(l => Money.Round(l.Quantity * l.UnitCost))),
                Received = Money.Round(pays.Where(p => p.Date >= from && p.Date < to).Sum(p => p.Amount)),
                Returned = Money.Round(returns.Where(r => r.Date >= from && r.Date < to).Sum(r => r.Amount)),
                StillOwed = Money.Round(monthBills.Sum(s => Math.Max(0, s.TotalAmount
                    - pays.Where(p => p.SaleId == s.Id).Sum(p => p.Amount)
                    - returns.Where(r => r.SaleId == s.Id).Sum(r => r.Amount))))
            });
        }
        rows.Add(SalesYearRow.Totals(year, rows));
        return rows;
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
                    // Stock is worth what it cost to put it on the shelf, freight and customs included.
                    TotalValue = g.Sum(x => x.QuantityRemaining * x.EffectiveCost),
                    IsLow = remaining <= threshold,
                    Lots = g.Select(x => new InventoryLot
                    {
                        ContainerId = x.ContainerId,
                        ContainerTitle = x.Container.Title,
                        ContainerItemId = x.Id,
                        Remaining = x.QuantityRemaining,
                        Received = x.QuantityReceived,
                        UnitCost = x.UnitCost,
                        LandedCost = x.EffectiveCost,
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
        var netted = await NetRevenueByLineAsync(db, list.Select(l => l.SaleId));
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
                var revenue = g.Sum(x => netted.GetValueOrDefault(x.Id, x.LineTotal)) - rets.Sum(x => x.Amount);
                var cogs = g.Sum(x => x.LineCost) - rets.Sum(x => Money.Round(x.Quantity * x.UnitCost));
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

        var netted = await NetRevenueByLineAsync(db, lines.Select(x => x.SaleId));
        var totalQty = lines.Sum(x => x.Quantity) - returned.Sum(x => x.Quantity);
        var totalAmount = lines.Sum(x => netted.GetValueOrDefault(x.Id, x.LineTotal)) - returned.Sum(x => x.Amount);
        var totalCost = lines.Sum(x => x.LineCost) - returned.Sum(x => Money.Round(x.Quantity * x.UnitCost));

        var customers = lines
            .GroupBy(l => new { l.Sale.CustomerId, l.Sale.Customer.Name })
            .Select(g =>
            {
                var rets = returned.Where(x => x.Return.CustomerId == g.Key.CustomerId).ToList();
                var qty = g.Sum(x => x.Quantity) - rets.Sum(x => x.Quantity);
                var cost = g.Sum(x => x.LineCost) - rets.Sum(x => Money.Round(x.Quantity * x.UnitCost));
                var amount = g.Sum(x => netted.GetValueOrDefault(x.Id, x.LineTotal)) - rets.Sum(x => x.Amount);
                return new ItemCustomerSaleRow
                {
                    CustomerId = g.Key.CustomerId,
                    CustomerName = g.Key.Name,
                    Qty = qty,
                    AvgCost = qty == 0 ? 0 : Money.Round(cost / qty),
                    AvgPrice = qty == 0 ? 0 : Money.Round(amount / qty),
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
            totalQty == 0 ? 0 : Money.Round(totalCost / totalQty),
            totalQty == 0 ? 0 : Money.Round(totalAmount / totalQty),
            customers);
    }

    /// <summary>
    /// What is still owing on the goods each container sold: every bill's outstanding figure - taken from the
    /// bill itself, by the same formula the bill's page and the customer's ledger read - shared across the
    /// containers its lines came from in proportion to what each was billed for, with the paisa that will not
    /// divide going on the biggest share. That is the sharing a discount already gets across a bill's lines,
    /// and for the same reason: the parts have to add back to the whole exactly. A bill drawn from one
    /// container, which is what the sell page's container box is for, needs no sharing at all - its money is
    /// that container's, paisa for paisa.
    /// </summary>
    private static async Task<Dictionary<int, decimal>> OutstandingByContainerAsync(
        AppDbContext db, List<SaleLine> saleLines)
    {
        var map = new Dictionary<int, decimal>();
        var saleIds = saleLines.Select(l => l.SaleId).Distinct().ToList();
        if (saleIds.Count == 0)
            return map;

        var bills = await db.Sales.AsNoTracking().Where(s => saleIds.Contains(s.Id)).ToListAsync();
        var pays = await db.Payments.AsNoTracking()
            .Where(p => p.SaleId != null && saleIds.Contains(p.SaleId.Value)).ToListAsync();
        var backs = await db.SaleReturns.AsNoTracking()
            .Where(r => saleIds.Contains(r.SaleId)).ToListAsync();

        foreach (var bill in bills)
        {
            var left = SalesService.RemainingOf(bill,
                pays.Where(p => p.SaleId == bill.Id).Sum(p => p.Amount),
                backs.Where(r => r.SaleId == bill.Id).Sum(r => r.Amount));
            if (left == 0m)
                continue;

            var parts = saleLines.Where(l => l.SaleId == bill.Id)
                .GroupBy(l => l.ContainerId)
                .Select(g => (Container: g.Key, Weight: g.Sum(x => x.LineTotal)))
                .ToList();
            foreach (var (container, share) in ShareByWeight(parts, left))
            {
                map.TryGetValue(container, out var had);
                map[container] = Money.Round(had + share);
            }
        }

        return map;
    }

    /// <summary>One amount, shared across containers by weight, ordered the same way every time so the same
    /// lot keeps the odd paisa on every re-run rather than trading it with another.</summary>
    private static List<(int Container, decimal Share)> ShareByWeight(
        List<(int Container, decimal Weight)> parts, decimal amount)
    {
        var ordered = parts.OrderByDescending(p => p.Weight).ThenBy(p => p.Container).ToList();
        if (ordered.Count == 0)
            return new List<(int, decimal)>();
        var total = ordered.Sum(p => p.Weight);
        if (total <= 0m)
        {
            // Nothing to weigh the parts by, and money still owing: it goes on the lot at the top of the bill
            // rather than vanishing, so the containers always add back to the bill.
            return new List<(int, decimal)> { (ordered[0].Container, Money.Round(amount)) };
        }

        var factor = amount / total;
        var shares = ordered.Select(p => Money.Round(p.Weight * factor)).ToList();
        shares[0] = Money.Round(shares[0] + (amount - shares.Sum()));
        var out2 = new List<(int, decimal)>(ordered.Count);
        for (var i = 0; i < ordered.Count; i++)
            out2.Add((ordered[i].Container, shares[i]));
        return out2;
    }

    /// <summary>
    /// What each line of a bill is worth once that bill's discount is taken off, keyed by line id.
    ///
    /// A discount is not one item's loss, so it is shared across the bill's lines in proportion to what
    /// each was billed for, and the paisa that the sharing leaves over goes on the biggest line. The
    /// shares then add up to the bill's TotalAmount exactly - the figure the customer was asked to pay -
    /// so every page that reads them agrees with the bill and with the ledger. A bill with no discount
    /// returns its own lines unchanged, which is why undiscounted trading does not move at all.
    /// </summary>
    private static async Task<Dictionary<int, decimal>> NetRevenueByLineAsync(AppDbContext db, IEnumerable<int> saleIds)
    {
        var map = new Dictionary<int, decimal>();
        var ids = saleIds.Where(id => id > 0).Distinct().ToList();
        if (ids.Count == 0)
            return map;

        // Every line of the bill, not the lines this report happens to be filtering down to: the share
        // has to be measured against what the whole bill was.
        var billed = await db.SaleLines.AsNoTracking()
            .Where(l => ids.Contains(l.SaleId))
            .Select(l => new { l.Id, l.SaleId, l.LineTotal })
            .ToListAsync();
        var billedTotals = await db.Sales.AsNoTracking()
            .Where(s => ids.Contains(s.Id))
            .Select(s => new { s.Id, s.TotalAmount })
            .ToListAsync();

        foreach (var group in billed.GroupBy(l => l.SaleId))
        {
            var net = billedTotals.FirstOrDefault(t => t.Id == group.Key)?.TotalAmount ?? 0m;
            var gross = group.Sum(l => l.LineTotal);
            if (gross == 0)
            {
                foreach (var l in group)
                    map[l.Id] = 0m;
                continue;
            }

            var factor = net / gross;
            var ordered = group.OrderByDescending(l => l.LineTotal).ThenBy(l => l.Id).ToList();
            var shares = ordered.Select(l => Money.Round(l.LineTotal * factor)).ToList();
            shares[0] = Money.Round(shares[0] + (net - shares.Sum()));
            for (var i = 0; i < ordered.Count; i++)
                map[ordered[i].Id] = shares[i];
        }

        return map;
    }

    /// <summary>
    /// The dates Home reads its book over. No dates at all is no filter: the book entire, which is what the
    /// page opens on. One date typed leaves the other side open, because "everything since the 1st" is asked
    /// for far more often than a closed range that happens to be one day, and a shop should not have to know
    /// when its records start to use them. A range turned around the wrong way reads its own first day, which
    /// is a day of the shop's figures, rather than answering with nothing.
    /// </summary>
    private static (DateTime? From, DateTime? To) BookRange(DateTime? from, DateTime? to)
    {
        if (from is null && to is null)
            return (null, null);
        var start = from?.Date;
        var end = to?.Date;
        if (start is DateTime f && end is DateTime t && t < f)
            end = f;
        return (start, end);
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

        var netted = await NetRevenueByLineAsync(db, saleLines.Select(l => l.SaleId));
        var stillOut = await OutstandingByContainerAsync(db, saleLines);
        var lines = saleLines
            .GroupBy(l => l.ContainerId)
            .Select(g => new
            {
                ContainerId = g.Key,
                Revenue = g.Sum(x => netted.GetValueOrDefault(x.Id, x.LineTotal)),
                Cogs = g.Sum(x => x.LineCost),
                QtySold = g.Sum(x => x.Quantity)
            })
            .ToList();

        return containers.Select(c =>
        {
            var s = lines.FirstOrDefault(x => x.ContainerId == c.Id);
            var rets = returnLines.Where(x => x.ContainerId == c.Id).ToList();
            var revenue = (s?.Revenue ?? 0) - rets.Sum(x => x.Amount);
            var cogs = (s?.Cogs ?? 0) - rets.Sum(x => Money.Round(x.Quantity * x.UnitCost));
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
                RemainingValue = c.Items.Sum(i => i.QuantityRemaining * i.EffectiveCost),
                RemainingQty = c.Items.Sum(i => i.QuantityRemaining),
                QtySold = (s?.QtySold ?? 0) - rets.Sum(x => x.Quantity),
                QtyReceived = c.Items.Sum(i => i.QuantityReceived),
                // One subtraction apart, so the three money figures on a container cannot disagree with each
                // other: what its goods brought, what is still out there, and what has arrived.
                InMarket = stillOut.GetValueOrDefault(c.Id, 0m),
                Collected = Money.Round(revenue - stillOut.GetValueOrDefault(c.Id, 0m))
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
