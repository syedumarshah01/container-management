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
            .ToListAsync();
        // Day by day in the order the money moved, and inside a day in the order the lines were written.
        // Grouping every line of one bill together reads tidily for a moment and then lies about the
        // sequence - a payment made last month against an older bill appearing above this month's return
        // - and the sequence is the one thing a ledger exists to show. The opening line leads its day.
        entries = entries
            .OrderBy(e => e.Date.Date)
            .ThenBy(e => e.Type == LedgerType.Opening ? 0 : 1)
            .ThenBy(e => e.Id)
            .ToList();

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
                Step = rows.Count + 1,
                SaleId = e.SaleId,
                PaymentId = e.PaymentId
            });
        }
        // Not reversed here: the printed statement reads top down from the opening balance, which is this
        // order, while the page wants the newest line under the reader's eye. The page reverses; the book
        // stays in the order it happened.
        return rows;
    }

    public async Task<Payment> ReceivePaymentAsync(
        int customerId, DateTime date, decimal amount, string method, string? notes, int? saleId = null)
    {
        amount = Money.Round(amount);
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
        CashBookService.PostCustomerPayment(db, pay, customer.Name);
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
        CashBookService.RemoveCustomerPayments(db, new[] { paymentId });
        db.Payments.Remove(pay);
        await db.SaveChangesAsync();
    }

    public async Task SetOpeningBalanceAsync(int customerId, decimal theyOwe)
    {
        theyOwe = Money.Round(theyOwe);
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

    /// <summary>
    /// What a customer handed over, in one month - or in the whole book when no month is given - together
    /// with the lines that make it up. The adding is done here rather than by the page so that the figure
    /// beside a month's rows *is* those rows: a receipts list whose headline was worked out separately is a
    /// headline that can quietly stop matching the paper under it.
    ///
    /// Only receipts count. A payout is money leaving the till to that same customer and a return credit
    /// moves their ledger without any cash, so neither is collected money, and neither is netted off the
    /// figure - which is why this reads the receipts table and not the ledger. The day is taken off each row
    /// after the rows are in memory: these date columns are TEXT, and asking SQLite for a range of days
    /// compares the strings instead of the dates.
    /// </summary>
    public async Task<(decimal Amount, int Count, List<Payment> Rows)> GetReceiptsAsync(
        int customerId, int? year, int? month)
    {
        if ((year is null) != (month is null))
            throw new ArgumentException("A month needs a year: pass both, or neither for every month.");

        await using var db = await _factory.CreateDbContextAsync();
        var all = await db.Payments.AsNoTracking()
            .Where(p => p.CustomerId == customerId)
            .ToListAsync();

        var rows = year is int y && month is int m
            ? all.Where(p => p.Date.Year == y && p.Date.Month == m).ToList()
            : all;
        // Newest first, as the customer's other lists are, and by writing order within a day - a receipt
        // taken at night keeps its place beside one taken that morning.
        rows = rows.OrderByDescending(p => p.Date).ThenByDescending(p => p.Id).ToList();
        return (Money.Round(rows.Sum(p => p.Amount)), rows.Count, rows);
    }

    /// <summary>
    /// Money handed to a customer to settle what their own book says we are holding of theirs. The mirror of
    /// ReceivePaymentAsync in direction only: it is never a negative payment, because a payment is money the
    /// till received and every other page adds those up as money in. So this writes three things in one
    /// transaction - the payout itself, a debit on their ledger so their balance moves towards nothing, and
    /// an outflow in the till so cash in hand falls by exactly the same figure.
    ///
    /// The ceiling is their ledger, not a guess: paying more than they are owed would leave the shop owed by
    /// its own customer, which is a different thing and not something a pay form should be able to create
    /// with a mistyped zero.
    /// </summary>
    public async Task<CustomerPayout> PayCustomerAsync(
        int customerId, DateTime date, decimal amount, string method, string? notes)
    {
        amount = Money.Round(amount);
        if (amount <= 0)
            throw new InvalidOperationException("Payment amount must be greater than zero.");

        await using var db = await _factory.CreateDbContextAsync();
        var customer = await db.Customers.FindAsync(customerId)
            ?? throw new InvalidOperationException("Customer not found.");
        var lines = await db.LedgerEntries.AsNoTracking()
            .Where(e => e.CustomerId == customerId)
            .ToListAsync();
        var balance = lines.Sum(e => e.Debit - e.Credit);
        var owed = Money.Round(-balance);
        if (owed <= 0.009m)
            throw new InvalidOperationException(balance > 0.009m
                ? "Nothing is owed to them - their ledger shows " + Money.Pkr(balance) + " still due to us."
                : "Nothing is owed to them - their ledger is settled.");
        if (amount - owed > 0.009m)
            throw new InvalidOperationException(
                "Their ledger says we owe them " + Money.Pkr(owed) + ". Paying " + Money.Pkr(amount)
                + " would leave us owed " + Money.Pkr(amount - owed) + " by this customer - take that in as "
                + "a payment from them instead of paying it out.");

        await using var tx = await db.Database.BeginTransactionAsync();
        var pay = new CustomerPayout
        {
            CustomerId = customerId,
            Date = date,
            Amount = amount,
            Method = string.IsNullOrWhiteSpace(method) ? "Cash" : method.Trim(),
            Notes = notes?.Trim()
        };
        db.CustomerPayouts.Add(pay);
        await db.SaveChangesAsync();

        db.LedgerEntries.Add(new LedgerEntry
        {
            CustomerId = customerId,
            Date = date,
            Type = LedgerType.Payout,
            Debit = amount,
            Credit = 0,
            Description = string.IsNullOrWhiteSpace(notes)
                ? $"{pay.Method} paid to {customer.Name}"
                : notes.Trim(),
            PayoutId = pay.Id
        });
        db.CashBook.Add(new CashBookEntry
        {
            Date = date,
            Kind = CashBookKind.CustomerOut,
            Description = "Paid to " + customer.Name + " · " + pay.Method
                + (string.IsNullOrWhiteSpace(notes) ? "" : " — " + notes.Trim()),
            AmountIn = 0,
            AmountOut = amount,
            PayoutId = pay.Id
        });
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return pay;
    }

    /// <summary>What has been handed to a customer, newest first, so a figure on the We Owe page can be
    /// checked against its note. Read from the payout rows, not from the ledger: the ledger line is the
    /// book, this is the record of the money.</summary>
    public async Task<List<CustomerPayoutRow>> ListPayoutsAsync(int customerId)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var rows = await db.CustomerPayouts.AsNoTracking()
            .Where(p => p.CustomerId == customerId)
            .ToListAsync();
        return rows
            .OrderByDescending(p => p.Date.Date).ThenByDescending(p => p.Id)
            .Select(p => new CustomerPayoutRow
            {
                CustomerId = p.CustomerId,
                Date = p.Date,
                Method = p.Method,
                Amount = p.Amount,
                Notes = p.Notes
            })
            .ToList();
    }

    /// <summary>
    /// The customers whose own book is in their favour, so the shop is holding their money: an advance that
    /// was never taken as goods, or a refund their settled bill left behind. Their balance is negative and
    /// "we owe them" is that figure without the sign - the same sum their own page runs over the same
    /// entries, so the two pages cannot tell two stories. Someone already paid back stays listed while a
    /// payout of theirs exists to be seen, which is why the pay form keeps a settled name selected instead
    /// of dropping the row the shop has just cleared.
    /// </summary>
    public async Task<List<CustomerOwedRow>> GetCustomerOwedAsync()
    {
        await using var db = await _factory.CreateDbContextAsync();
        var customers = await db.Customers.AsNoTracking().ToListAsync();
        var entries = await db.LedgerEntries.AsNoTracking().ToListAsync();
        var payouts = await db.CustomerPayouts.AsNoTracking().ToListAsync();

        var rows = new List<CustomerOwedRow>();
        foreach (var c in customers)
        {
            var lines = entries.Where(e => e.CustomerId == c.Id).ToList();
            if (lines.Count == 0)
                continue;
            var owed = Money.Round(-lines.Sum(e => e.Debit - e.Credit));
            var paidOut = Money.Round(payouts.Where(p => p.CustomerId == c.Id).Sum(p => p.Amount));
            if (owed <= 0.009m && paidOut <= 0.009m)
                continue;
            rows.Add(new CustomerOwedRow
            {
                CustomerId = c.Id,
                Name = c.Name,
                Phone = c.Phone,
                Owed = owed > 0 ? owed : 0m,
                PaidOut = paidOut
            });
        }

        return rows.OrderByDescending(r => r.Owed).ThenBy(r => r.Name).ToList();
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
