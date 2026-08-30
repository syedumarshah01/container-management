using ContainerManagement.Data;
using ContainerManagement.Models;
using Microsoft.EntityFrameworkCore;

namespace ContainerManagement.Services;

public class SalesService
{
    private readonly IDbContextFactory<AppDbContext> _factory;

    public SalesService(IDbContextFactory<AppDbContext> factory) => _factory = factory;

    public async Task<List<Sale>> ListSalesAsync(int take = 500)
    {
        await using var db = await _factory.CreateDbContextAsync();
        return await db.Sales
            .AsNoTracking()
            .Include(s => s.Customer)
            .Include(s => s.Lines).ThenInclude(l => l.Product)
            .Include(s => s.Lines).ThenInclude(l => l.Container)
            .OrderByDescending(s => s.Date)
            .ThenByDescending(s => s.Id)
            .Take(take)
            .ToListAsync();
    }

    public async Task<Sale?> GetSaleAsync(int id)
    {
        await using var db = await _factory.CreateDbContextAsync();
        return await db.Sales
            .AsNoTracking()
            .Include(s => s.Customer)
            .Include(s => s.Lines).ThenInclude(l => l.Product)
            .Include(s => s.Lines).ThenInclude(l => l.Container)
            .Include(s => s.Returns).ThenInclude(r => r.Lines)
            .FirstOrDefaultAsync(s => s.Id == id);
    }

    public async Task<decimal> RemainingOnInvoiceAsync(int saleId)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var sale = await db.Sales.AsNoTracking().FirstOrDefaultAsync(s => s.Id == saleId);
        if (sale is null || sale.Status != SaleStatus.Active)
            return 0;
        var paid = await db.Payments.AsNoTracking().Where(p => p.SaleId == saleId).ToListAsync();
        var returned = await db.SaleReturns.AsNoTracking().Where(r => r.SaleId == saleId).ToListAsync();
        return Math.Max(0, sale.TotalAmount - paid.Sum(p => p.Amount) - returned.Sum(r => r.Amount));
    }

    public async Task<List<UnpaidInvoice>> UnpaidInvoicesAsync(int customerId)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var sales = await db.Sales.AsNoTracking()
            .Where(s => s.CustomerId == customerId && s.Status == SaleStatus.Active)
            .ToListAsync();
        var pays = await db.Payments.AsNoTracking()
            .Where(p => p.CustomerId == customerId && p.SaleId != null)
            .ToListAsync();
        var returns = await db.SaleReturns.AsNoTracking()
            .Where(r => r.CustomerId == customerId)
            .ToListAsync();
        var list = new List<UnpaidInvoice>();
        foreach (var s in sales.OrderBy(s => s.Date))
        {
            var left = s.TotalAmount
                       - pays.Where(p => p.SaleId == s.Id).Sum(p => p.Amount)
                       - returns.Where(r => r.SaleId == s.Id).Sum(r => r.Amount);
            if (left > 0.009m)
            {
                list.Add(new UnpaidInvoice
                {
                    SaleId = s.Id,
                    Remaining = left,
                    Label = $"#{s.Id} {s.Date:dd MMM} · left {Money.Pkr(left)}"
                });
            }
        }
        return list;
    }

    public async Task<Sale> CreateSaleAsync(
        int customerId,
        DateTime date,
        IReadOnlyList<NewSaleLineInput> lines,
        decimal paidNow,
        string paymentMethod,
        string? notes,
        decimal discount,
        DateTime? dueDate)
    {
        return await SaveSaleAsync(null, customerId, date, lines, paidNow, paymentMethod, notes, discount, dueDate);
    }

    public async Task<Sale> UpdateSaleAsync(
        int saleId,
        int customerId,
        DateTime date,
        IReadOnlyList<NewSaleLineInput> lines,
        decimal paidNow,
        string paymentMethod,
        string? notes,
        decimal discount,
        DateTime? dueDate)
    {
        return await SaveSaleAsync(saleId, customerId, date, lines, paidNow, paymentMethod, notes, discount, dueDate);
    }

    public async Task CancelSaleAsync(int saleId)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var sale = await db.Sales.Include(s => s.Lines).Include(s => s.Customer)
            .FirstOrDefaultAsync(s => s.Id == saleId)
            ?? throw new InvalidOperationException("Sale not found.");
        if (sale.Status == SaleStatus.Cancelled)
            throw new InvalidOperationException("This sale is already cancelled.");
        if (await db.SaleReturns.AnyAsync(r => r.SaleId == saleId))
            throw new InvalidOperationException("This sale has returns. Return any leftover items instead of cancelling.");

        await using var tx = await db.Database.BeginTransactionAsync();
        foreach (var line in sale.Lines)
        {
            var item = await db.ContainerItems.FindAsync(line.ContainerItemId)
                ?? throw new InvalidOperationException("Stock lot missing.");
            item.QuantityRemaining += line.Quantity;
        }

        db.LedgerEntries.Add(new LedgerEntry
        {
            CustomerId = sale.CustomerId,
            Date = DateTime.Today,
            Type = LedgerType.Return,
            Debit = 0,
            Credit = sale.TotalAmount,
            Description = $"Cancelled sale #{sale.Id}",
            SaleId = sale.Id
        });

        var pays = await db.Payments.Where(p => p.SaleId == sale.Id).ToListAsync();
        foreach (var pay in pays)
        {
            db.LedgerEntries.Add(new LedgerEntry
            {
                CustomerId = sale.CustomerId,
                Date = DateTime.Today,
                Type = LedgerType.Adjustment,
                Debit = pay.Amount,
                Credit = 0,
                Description = $"Cash returned — cancelled sale #{sale.Id}",
                SaleId = sale.Id,
                PaymentId = pay.Id
            });
        }

        sale.Status = SaleStatus.Cancelled;
        sale.CancelledAt = DateTime.Now;
        await db.SaveChangesAsync();
        await tx.CommitAsync();
    }

    public async Task ReturnItemsAsync(int saleId, IReadOnlyList<SaleReturnInput> inputs)
    {
        var wanted = inputs.Where(x => x.Quantity > 0).ToList();
        if (wanted.Count == 0)
            throw new InvalidOperationException("Type how many of each item came back.");

        await using var db = await _factory.CreateDbContextAsync();
        var sale = await db.Sales
            .Include(s => s.Lines).ThenInclude(l => l.Product)
            .Include(s => s.Customer)
            .FirstOrDefaultAsync(s => s.Id == saleId)
            ?? throw new InvalidOperationException("Sale not found.");
        if (sale.Status == SaleStatus.Cancelled)
            throw new InvalidOperationException("This sale is cancelled.");

        var prior = await db.SaleReturnLines
            .Where(l => l.Return.SaleId == saleId)
            .ToListAsync();
        var alreadyQty = prior
            .GroupBy(l => l.SaleLineId)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Quantity));
        var alreadyAmount = await db.SaleReturns
            .Where(r => r.SaleId == saleId)
            .ToListAsync();
        var returnedSoFar = alreadyAmount.Sum(r => r.Amount);

        var gross = sale.Lines.Sum(l => l.Quantity * l.UnitPrice);
        var factor = gross == 0 ? 1m : sale.TotalAmount / gross;

        await using var tx = await db.Database.BeginTransactionAsync();

        var ret = new SaleReturn
        {
            SaleId = sale.Id,
            CustomerId = sale.CustomerId,
            Date = DateTime.Today
        };

        var names = new List<string>();
        foreach (var input in wanted)
        {
            var line = sale.Lines.FirstOrDefault(l => l.Id == input.SaleLineId)
                ?? throw new InvalidOperationException("That item is not on this bill.");
            var done = alreadyQty.GetValueOrDefault(line.Id);
            var left = line.Quantity - done;
            if (input.Quantity - left > 0.0005m)
                throw new InvalidOperationException(
                    $"Only {Money.Qty(left)} {line.Product.Name} can still come back from this bill.");

            var item = await db.ContainerItems.FindAsync(line.ContainerItemId)
                ?? throw new InvalidOperationException("Stock lot missing.");
            item.QuantityRemaining += input.Quantity;

            var amount = Math.Round(input.Quantity * line.UnitPrice * factor, 2);
            ret.Lines.Add(new SaleReturnLine
            {
                SaleLineId = line.Id,
                ContainerId = line.ContainerId,
                ContainerItemId = line.ContainerItemId,
                ProductId = line.ProductId,
                Quantity = input.Quantity,
                UnitPrice = line.UnitPrice,
                UnitCost = line.UnitCost,
                Amount = amount
            });
            names.Add(Money.Qty(input.Quantity) + " " + line.Product.Name);
            alreadyQty[line.Id] = done + input.Quantity;
        }

        ret.Amount = Math.Round(ret.Lines.Sum(l => l.Amount), 2);
        var qtyLeft = sale.Lines.Sum(l => l.Quantity - alreadyQty.GetValueOrDefault(l.Id));
        if (qtyLeft <= 0.0005m)
            ret.Amount = Math.Max(0, sale.TotalAmount - returnedSoFar);
        if (ret.Amount + returnedSoFar - sale.TotalAmount > 0.009m)
            ret.Amount = Math.Max(0, sale.TotalAmount - returnedSoFar);

        db.SaleReturns.Add(ret);
        await db.SaveChangesAsync();

        db.LedgerEntries.Add(new LedgerEntry
        {
            CustomerId = sale.CustomerId,
            Date = DateTime.Today,
            Type = LedgerType.Return,
            Debit = 0,
            Credit = ret.Amount,
            Description = $"Return from sale #{sale.Id}: {string.Join(", ", names)}",
            SaleId = sale.Id
        });
        await db.SaveChangesAsync();
        await tx.CommitAsync();
    }

    private async Task<Sale> SaveSaleAsync(
        int? existingId,
        int customerId,
        DateTime date,
        IReadOnlyList<NewSaleLineInput> lines,
        decimal paidNow,
        string paymentMethod,
        string? notes,
        decimal discount,
        DateTime? dueDate)
    {
        if (lines.Count == 0)
            throw new InvalidOperationException("Add at least one item to the sale.");
        if (paidNow < 0)
            throw new InvalidOperationException("Amount received cannot be negative.");
        if (discount < 0)
            throw new InvalidOperationException("Discount cannot be negative.");

        await using var db = await _factory.CreateDbContextAsync();
        var customer = await db.Customers.FindAsync(customerId)
            ?? throw new InvalidOperationException("Select a customer.");

        await using var tx = await db.Database.BeginTransactionAsync();

        Sale sale;
        if (existingId is int sid)
        {
            sale = await db.Sales.Include(s => s.Lines).FirstOrDefaultAsync(s => s.Id == sid)
                ?? throw new InvalidOperationException("Sale not found.");
            if (sale.Status == SaleStatus.Cancelled)
                throw new InvalidOperationException("Cannot edit a cancelled sale.");
            if (await db.SaleReturns.AnyAsync(r => r.SaleId == sale.Id))
                throw new InvalidOperationException("Cannot edit a sale that has returns.");
            if (sale.Date.Date != DateTime.Today)
                throw new InvalidOperationException("Only today's sales can be edited. Cancel and make a new bill instead.");

            foreach (var old in sale.Lines)
            {
                var item = await db.ContainerItems.FindAsync(old.ContainerItemId)
                    ?? throw new InvalidOperationException("Stock lot missing.");
                item.QuantityRemaining += old.Quantity;
            }

            var oldPays = await db.Payments.Where(p => p.SaleId == sale.Id).ToListAsync();
            var oldLed = await db.LedgerEntries.Where(e => e.SaleId == sale.Id).ToListAsync();
            db.Payments.RemoveRange(oldPays);
            db.LedgerEntries.RemoveRange(oldLed);
            db.SaleLines.RemoveRange(sale.Lines);
            sale.Lines.Clear();
            await db.SaveChangesAsync();
        }
        else
        {
            sale = new Sale { CustomerId = customerId };
            db.Sales.Add(sale);
        }

        sale.CustomerId = customerId;
        sale.Date = date;
        sale.Notes = notes?.Trim();
        sale.PaidNow = paidNow;
        sale.DiscountAmount = discount;
        sale.DueDate = dueDate;
        sale.Status = SaleStatus.Active;

        foreach (var line in lines)
        {
            if (line.ContainerId <= 0)
                throw new InvalidOperationException("Every item must be sold from a container.");
            if (line.Quantity <= 0)
                throw new InvalidOperationException($"Quantity for {line.ProductName} must be greater than zero.");
            if (line.UnitPrice < 0)
                throw new InvalidOperationException($"Price for {line.ProductName} cannot be negative.");

            var item = await db.ContainerItems
                .Include(i => i.Product)
                .Include(i => i.Container)
                .FirstOrDefaultAsync(i => i.Id == line.ContainerItemId)
                ?? throw new InvalidOperationException($"Stock lot not found for {line.ProductName}.");

            if (item.ContainerId != line.ContainerId)
                throw new InvalidOperationException($"{line.ProductName} does not belong to the selected container.");
            if (item.QuantityRemaining < line.Quantity)
                throw new InvalidOperationException(
                    $"Not enough {item.Product.Name} in {item.Container.Title}. Remaining: {Money.Qty(item.QuantityRemaining)} {item.Product.Unit}.");

            item.QuantityRemaining -= line.Quantity;
            item.Product.LastSalePrice = line.UnitPrice;

            sale.Lines.Add(new SaleLine
            {
                ContainerId = item.ContainerId,
                ContainerItemId = item.Id,
                ProductId = item.ProductId,
                Quantity = line.Quantity,
                UnitPrice = line.UnitPrice,
                UnitCost = item.UnitCost
            });
        }

        var gross = sale.Lines.Sum(l => l.Quantity * l.UnitPrice);
        if (discount > gross)
            throw new InvalidOperationException("Discount cannot be more than the bill.");
        sale.TotalAmount = gross - discount;
        if (paidNow > sale.TotalAmount)
            throw new InvalidOperationException("Amount received cannot be more than the bill. Put extra as a separate payment on the customer ledger.");

        await db.SaveChangesAsync();

        db.LedgerEntries.Add(new LedgerEntry
        {
            CustomerId = customerId,
            Date = date,
            Type = LedgerType.Sale,
            Debit = sale.TotalAmount,
            Credit = 0,
            Description = $"Sale #{sale.Id} to {customer.Name}",
            SaleId = sale.Id
        });

        if (paidNow > 0)
        {
            var pay = new Payment
            {
                CustomerId = customerId,
                Date = date,
                Amount = paidNow,
                Method = string.IsNullOrWhiteSpace(paymentMethod) ? "Cash" : paymentMethod.Trim(),
                Notes = $"Against sale #{sale.Id}",
                SaleId = sale.Id
            };
            db.Payments.Add(pay);
            await db.SaveChangesAsync();

            db.LedgerEntries.Add(new LedgerEntry
            {
                CustomerId = customerId,
                Date = date,
                Type = LedgerType.Payment,
                Debit = 0,
                Credit = paidNow,
                Description = $"{pay.Method} against sale #{sale.Id}",
                SaleId = sale.Id,
                PaymentId = pay.Id
            });
        }

        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return sale;
    }
}
