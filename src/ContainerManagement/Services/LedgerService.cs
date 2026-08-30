using ContainerManagement.Data;
using ContainerManagement.Models;
using Microsoft.EntityFrameworkCore;

namespace ContainerManagement.Services;

public class LedgerService
{
    private readonly IDbContextFactory<AppDbContext> _factory;

    public LedgerService(IDbContextFactory<AppDbContext> factory) => _factory = factory;

    public async Task<List<Customer>> ListCustomersAsync()
    {
        await using var db = await _factory.CreateDbContextAsync();
        return await db.Customers
            .AsNoTracking()
            .OrderBy(c => c.IsWalkIn ? 0 : 1)
            .ThenBy(c => c.Name)
            .ToListAsync();
    }

    public async Task<Customer?> GetCustomerAsync(int id)
    {
        await using var db = await _factory.CreateDbContextAsync();
        return await db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id);
    }

    public async Task<Customer> CreateCustomerAsync(string name, string? phone, string? address, string? notes)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException("Customer name is required.");

        await using var db = await _factory.CreateDbContextAsync();
        var c = new Customer
        {
            Name = name.Trim(),
            Phone = phone?.Trim(),
            Address = address?.Trim(),
            Notes = notes?.Trim()
        };
        db.Customers.Add(c);
        await db.SaveChangesAsync();
        return c;
    }

    public async Task UpdateCustomerAsync(int id, string name, string? phone, string? address, string? notes)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var c = await db.Customers.FindAsync(id)
            ?? throw new InvalidOperationException("Customer not found.");
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException("Customer name is required.");
        c.Name = name.Trim();
        c.Phone = phone?.Trim();
        c.Address = address?.Trim();
        c.Notes = notes?.Trim();
        await db.SaveChangesAsync();
    }

    public async Task<decimal> GetBalanceAsync(int customerId)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var entries = await db.LedgerEntries.AsNoTracking()
            .Where(e => e.CustomerId == customerId)
            .ToListAsync();
        return entries.Sum(e => e.Debit - e.Credit);
    }

    public async Task<List<LedgerRow>> GetLedgerAsync(int customerId)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var entries = await db.LedgerEntries
            .AsNoTracking()
            .Where(e => e.CustomerId == customerId)
            .OrderBy(e => e.Date)
            .ThenBy(e => e.Id)
            .ToListAsync();

        decimal running = 0;
        var rows = new List<LedgerRow>(entries.Count);
        foreach (var e in entries)
        {
            running += e.Debit - e.Credit;
            rows.Add(new LedgerRow
            {
                Id = e.Id,
                Date = e.Date,
                Type = e.Type,
                Description = e.Description,
                Debit = e.Debit,
                Credit = e.Credit,
                RunningBalance = running,
                SaleId = e.SaleId,
                PaymentId = e.PaymentId
            });
        }
        rows.Reverse();
        return rows;
    }

    public async Task<Payment> ReceivePaymentAsync(
        int customerId, DateTime date, decimal amount, string method, string? notes, int? saleId = null)
    {
        if (amount <= 0)
            throw new InvalidOperationException("Payment amount must be greater than zero.");

        await using var db = await _factory.CreateDbContextAsync();
        var customer = await db.Customers.FindAsync(customerId)
            ?? throw new InvalidOperationException("Customer not found.");

        string? against = null;
        if (saleId is int sid)
        {
            var sale = await db.Sales.FindAsync(sid)
                ?? throw new InvalidOperationException("Invoice not found.");
            if (sale.CustomerId != customerId)
                throw new InvalidOperationException("That invoice is not for this customer.");
            if (sale.Status != SaleStatus.Active)
                throw new InvalidOperationException("That invoice is cancelled.");
            var already = await db.Payments.Where(p => p.SaleId == sid).ToListAsync();
            var returned = await db.SaleReturns.Where(r => r.SaleId == sid).ToListAsync();
            var left = sale.TotalAmount - already.Sum(p => p.Amount) - returned.Sum(r => r.Amount);
            if (amount - left > 0.009m)
                throw new InvalidOperationException($"Only {Money.Pkr(left)} is left on invoice #{sid}.");
            against = $" against sale #{sid}";
        }

        await using var tx = await db.Database.BeginTransactionAsync();

        var pay = new Payment
        {
            CustomerId = customerId,
            Date = date,
            Amount = amount,
            Method = string.IsNullOrWhiteSpace(method) ? "Cash" : method.Trim(),
            Notes = notes?.Trim(),
            SaleId = saleId
        };
        db.Payments.Add(pay);
        await db.SaveChangesAsync();

        db.LedgerEntries.Add(new LedgerEntry
        {
            CustomerId = customerId,
            Date = date,
            Type = LedgerType.Payment,
            Debit = 0,
            Credit = amount,
            Description = string.IsNullOrWhiteSpace(notes)
                ? $"{pay.Method} received from {customer.Name}{against}"
                : notes.Trim(),
            PaymentId = pay.Id,
            SaleId = saleId
        });
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return pay;
    }

    public async Task DeletePaymentAsync(int paymentId)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var pay = await db.Payments.FindAsync(paymentId)
            ?? throw new InvalidOperationException("Payment not found.");
        var led = await db.LedgerEntries.Where(e => e.PaymentId == paymentId).ToListAsync();
        db.LedgerEntries.RemoveRange(led);
        db.Payments.Remove(pay);
        await db.SaveChangesAsync();
    }

    public async Task SetOpeningBalanceAsync(int customerId, decimal theyOwe)
    {
        await using var db = await _factory.CreateDbContextAsync();
        if (!await db.Customers.AnyAsync(c => c.Id == customerId))
            throw new InvalidOperationException("Customer not found.");

        var old = await db.LedgerEntries
            .Where(e => e.CustomerId == customerId && e.Type == LedgerType.Opening)
            .ToListAsync();
        db.LedgerEntries.RemoveRange(old);

        if (theyOwe != 0)
        {
            db.LedgerEntries.Add(new LedgerEntry
            {
                CustomerId = customerId,
                Date = DateTime.Today,
                Type = LedgerType.Opening,
                Debit = theyOwe > 0 ? theyOwe : 0,
                Credit = theyOwe < 0 ? -theyOwe : 0,
                Description = theyOwe > 0
                    ? "Opening balance — they already owed you"
                    : "Opening balance — advance already with you"
            });
        }
        await db.SaveChangesAsync();
    }

    public async Task<List<Payment>> ListPaymentsAsync(int customerId)
    {
        await using var db = await _factory.CreateDbContextAsync();
        return await db.Payments.AsNoTracking()
            .Where(p => p.CustomerId == customerId)
            .OrderByDescending(p => p.Date)
            .ThenByDescending(p => p.Id)
            .ToListAsync();
    }

    public async Task<List<ReceivableRow>> GetReceivablesAsync()
    {
        await using var db = await _factory.CreateDbContextAsync();
        var customers = await db.Customers.AsNoTracking().ToListAsync();
        var entries = await db.LedgerEntries.AsNoTracking().ToListAsync();
        var balances = entries
            .GroupBy(e => e.CustomerId)
            .Select(g => new { CustomerId = g.Key, Balance = g.Sum(x => x.Debit - x.Credit) })
            .ToList();

        var lastSales = await db.Sales.AsNoTracking()
            .GroupBy(s => s.CustomerId)
            .Select(g => new { CustomerId = g.Key, Last = g.Max(x => x.Date) })
            .ToListAsync();

        var lastPays = await db.Payments.AsNoTracking()
            .GroupBy(p => p.CustomerId)
            .Select(g => new { CustomerId = g.Key, Last = g.Max(x => x.Date) })
            .ToListAsync();

        var activeSales = await db.Sales.AsNoTracking()
            .Where(s => s.Status == SaleStatus.Active)
            .ToListAsync();
        var invoicePays = await db.Payments.AsNoTracking()
            .Where(p => p.SaleId != null)
            .ToListAsync();
        var invoiceReturns = await db.SaleReturns.AsNoTracking().ToListAsync();

        var rows = new List<ReceivableRow>();
        foreach (var c in customers)
        {
            var bal = balances.FirstOrDefault(b => b.CustomerId == c.Id)?.Balance ?? 0;
            DateTime? oldest = null;
            foreach (var s in activeSales.Where(s => s.CustomerId == c.Id))
            {
                var left = s.TotalAmount
                           - invoicePays.Where(p => p.SaleId == s.Id).Sum(p => p.Amount)
                           - invoiceReturns.Where(r => r.SaleId == s.Id).Sum(r => r.Amount);
                if (left <= 0.009m) continue;
                var due = s.DueDate ?? s.Date;
                if (oldest is null || due < oldest) oldest = due;
            }

            rows.Add(new ReceivableRow
            {
                CustomerId = c.Id,
                Name = c.Name,
                Phone = c.Phone,
                Balance = bal,
                LastSale = lastSales.FirstOrDefault(s => s.CustomerId == c.Id)?.Last,
                LastPayment = lastPays.FirstOrDefault(p => p.CustomerId == c.Id)?.Last,
                OldestDue = oldest,
                Aging = AgingLabel(oldest, bal)
            });
        }

        return rows
            .OrderByDescending(r => r.Balance)
            .ThenBy(r => r.Name)
            .ToList();
    }

    private static string AgingLabel(DateTime? oldestDue, decimal balance)
    {
        if (balance <= 0 || oldestDue is null) return "—";
        var days = (DateTime.Today - oldestDue.Value.Date).Days;
        if (days <= 0) return "Current";
        if (days <= 30) return "1–30 days";
        if (days <= 60) return "31–60 days";
        if (days <= 90) return "61–90 days";
        return "90+ days";
    }
}
