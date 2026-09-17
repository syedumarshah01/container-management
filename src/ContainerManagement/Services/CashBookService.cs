using ContainerManagement.Data;
using ContainerManagement.Models;
using Microsoft.EntityFrameworkCore;

namespace ContainerManagement.Services;

public class CashBookService
{
    private readonly IDbContextFactory<AppDbContext> _factory;

    public CashBookService(IDbContextFactory<AppDbContext> factory) => _factory = factory;

    /// <summary>What has been paid, newest first, so a payment can be checked against its note.</summary>
    public async Task<List<SupplierPaymentRow>> SupplierPaymentsAsync()
    {
        await using var db = await _factory.CreateDbContextAsync();
        var pays = await db.SupplierPayments.AsNoTracking().ToListAsync();
        return pays
            .OrderByDescending(x => x.Date.Date).ThenByDescending(x => x.Id)
            .Select(x => new SupplierPaymentRow
            {
                ContainerId = x.ContainerId ?? 0,
                Date = x.Date,
                Method = x.Method,
                Amount = x.Amount,
                Notes = x.Notes
            })
            .ToList();
    }

    /// <summary>
    /// What goods came back, day by day. Returns live here because this is the page that answers "how
    /// does the book look today", and a return on a credit bill moves no cash at all - so it never
    /// appears as a row, and without this figure the page would be silent about money the shop has
    /// agreed to give back. The amounts are the value of the goods, not cash that left: the caller keeps
    /// them out of the running total.
    /// </summary>
    public async Task<List<(DateTime Date, decimal Amount)>> ListReturnsAsync()
    {
        await using var db = await _factory.CreateDbContextAsync();
        var rows = await db.SaleReturns.AsNoTracking().ToListAsync();
        return rows.Select(r => (r.Date, r.Amount)).ToList();
    }

    public async Task<List<CashBookEntry>> ListAsync()
    {
        await using var db = await _factory.CreateDbContextAsync();
        await ImportMissingAsync(db);
        return await db.CashBook.AsNoTracking()
            .OrderBy(e => e.Date.Date)
            .ThenBy(e => e.Kind == CashBookKind.Opening ? 0 : 1)
            .ThenBy(e => e.Id)
            .ToListAsync();
    }

    public async Task SetOpeningAsync(decimal cashOnHand)
    {
        cashOnHand = Money.Round(cashOnHand);
        await using var db = await _factory.CreateDbContextAsync();
        var old = await db.CashBook.Where(e => e.Kind == CashBookKind.Opening).ToListAsync();
        var others = await db.CashBook.Where(e => e.Kind != CashBookKind.Opening).Select(e => e.Date).ToListAsync();
        var date = others.Count == 0 ? DateTime.Today : others.Min().Date;
        db.CashBook.RemoveRange(old);
        if (cashOnHand != 0)
        {
            db.CashBook.Add(new CashBookEntry
            {
                Date = date,
                Kind = CashBookKind.Opening,
                Description = cashOnHand > 0
                    ? "Opening cash in hand"
                    : "Opening — cash already short",
                AmountIn = cashOnHand > 0 ? cashOnHand : 0,
                AmountOut = cashOnHand < 0 ? -cashOnHand : 0
            });
        }
        await db.SaveChangesAsync();
    }

    public async Task<List<SupplierPayTarget>> SupplierContainersAsync()
    {
        await using var db = await _factory.CreateDbContextAsync();
        var list = await db.Containers.AsNoTracking()
            .Include(c => c.Supplier)
            .Include(c => c.SupplierPayments)
            .Where(c => c.SupplierId != null)
            .OrderBy(c => c.Supplier!.Name)
            .ThenBy(c => c.Title)
            .ToListAsync();
        return list.Select(c =>
        {
            // The same subtraction the container's own page makes, from the same helper, so the amount this
            // list invites the shop to pay cannot disagree with the amount the lot says is owed on it.
            var owed = InventoryService.OwedOnContainer(c.SupplierAmount, c.SupplierPayments);
            var supplier = string.IsNullOrWhiteSpace(c.Supplier?.Name) ? "Supplier" : c.Supplier!.Name;
            return new SupplierPayTarget
            {
                Id = c.Id,
                SupplierName = supplier,
                ContainerTitle = c.Title,
                Owed = owed,
                Label = supplier + " · " + c.Title + " · "
                    + (owed > 0.009m ? "owe " + Money.Pkr(owed)
                       : owed < -0.009m ? PaidExtraText(-owed)
                       : "settled")
            };
        }).ToList();
    }

    /// <summary>
    /// The year in the till, month by month: what came in, what went out, the value of what came back, and
    /// the cash each month closed with. January's closing figure carries every line before the year as
    /// well, so the year can be read as a statement and not only as a movement, and December's is the
    /// number the Main ledger page holds when the year is the one we are standing in.
    /// </summary>
    public async Task<List<TillYearRow>> GetYearCashAsync(int year)
    {
        await using var db = await _factory.CreateDbContextAsync();
        await ImportMissingAsync(db);
        var lines = await db.CashBook.AsNoTracking().ToListAsync();
        var returns = await db.SaleReturns.AsNoTracking().ToListAsync();

        var start = new DateTime(year, 1, 1);
        var running = Money.Round(lines.Where(e => e.Date < start).Sum(e => e.AmountIn - e.AmountOut));
        var rows = new List<TillYearRow>(12);
        for (var m = 1; m <= 12; m++)
        {
            var from = start.AddMonths(m - 1);
            var to = from.AddMonths(1);
            var month = lines.Where(e => e.Date >= from && e.Date < to).ToList();
            var row = new TillYearRow
            {
                Month = m,
                CashIn = Money.Round(month.Sum(e => e.AmountIn)),
                CashOut = Money.Round(month.Sum(e => e.AmountOut)),
                Returns = Money.Round(returns.Where(r => r.Date >= from && r.Date < to).Sum(r => r.Amount))
            };
            running += row.CashIn - row.CashOut;
            row.Closing = Money.Round(running);
            rows.Add(row);
        }
        // The year's own line goes on the end of the rows, so every page that lists them ends the same way
        // and none of them has to add the twelve up for itself.
        var totals = TillYearRow.Totals(year, rows);
        rows.Add(totals);
        return rows;
    }

    /// <summary>
    /// The words for money paid past what a container says is owed. It should only ever be found on a
    /// container entered under the previous rule, since the pay page refuses it now - which is why the
    /// figure is stated plainly instead of dressed up as an arrangement with the supplier. One method,
    /// three places: the list, the dropdown and the pay panel must not drift into three stories.
    /// </summary>
    /// <summary>
    /// Cash as a month ends it: everything before the month carried in, then the month's own money in and out.
    /// Both figures come back, because the card has to say what it is - a month, not the whole book - and a
    /// carried figure is only worth having if the shop can see it and check it against last month's end.
    /// Money dated after the month does not reach back into it, and money on the first belongs to the month.
    /// </summary>
    public static (decimal Carried, decimal Closing) MonthCash(
        IEnumerable<(DateTime Date, decimal In, decimal Out)> rows, DateTime start)
    {
        var end = start.AddMonths(1);
        decimal carried = 0m;
        decimal net = 0m;
        foreach (var r in rows)
        {
            if (r.Date < start)
                carried += r.In - r.Out;
            else if (r.Date < end)
                net += r.In - r.Out;
        }
        return (Money.Round(carried), Money.Round(carried + net));
    }

    public static string PaidExtraText(decimal extra, bool terse = false)
        => (terse ? "paid extra " : "Paid extra ") + Money.Pkr(Money.Round(extra));

    public static void PostCustomerPayment(AppDbContext db, Payment pay, string customerName, int? invoiceNo = null)
    {
        db.CashBook.Add(new CashBookEntry
        {
            Date = pay.Date,
            Kind = CashBookKind.CustomerIn,
            // Named by the number on the paper rather than the row behind it, because a till line is read next
            // to a customer's bill and the two have to be saying the same thing.
            Description = invoiceNo is int no
                ? $"From {customerName} · sale #{no}"
                : pay.SaleId is int sid
                    ? $"From {customerName} · sale #{sid}"
                    : $"From {customerName}",
            AmountIn = pay.Amount,
            AmountOut = 0,
            PaymentId = pay.Id,
            SaleId = pay.SaleId
        });
    }

    public static void RemoveCustomerPayments(AppDbContext db, IEnumerable<int> paymentIds)
    {
        var ids = paymentIds.ToList();
        if (ids.Count == 0)
            return;
        var rows = db.CashBook.Where(e => e.PaymentId != null && ids.Contains(e.PaymentId.Value)).ToList();
        db.CashBook.RemoveRange(rows);
    }

    public static void PostSupplierPayment(AppDbContext db, SupplierPayment pay, string supplierName, string? containerTitle)
    {
        var where = string.IsNullOrWhiteSpace(containerTitle) ? "" : " · " + containerTitle;
        var note = string.IsNullOrWhiteSpace(pay.Notes) ? "" : " — " + pay.Notes.Trim();
        db.CashBook.Add(new CashBookEntry
        {
            Date = pay.Date,
            Kind = CashBookKind.SupplierOut,
            Description = "Paid " + supplierName + where + note,
            AmountIn = 0,
            AmountOut = pay.Amount,
            SupplierPaymentId = pay.Id
        });
    }

    /// <summary>
    /// A supplier's refund for goods the shop sent back, as a till line: money in, and money that was never
    /// income. It is kept out of the sales figures on purpose - a rupee that comes back off a bill the shop has
    /// settled is not a piece of goods sold, and a month's takings that counts it as one is claiming a sale that
    /// did not happen, which is the sort of figure a tax notice asks about.
    /// </summary>
    public static void PostSupplierRefund(AppDbContext db, SupplierReturn r, string supplierName, string? containerTitle)
    {
        var where = string.IsNullOrWhiteSpace(containerTitle) ? "" : " · " + containerTitle;
        db.CashBook.Add(new CashBookEntry
        {
            Date = r.Date,
            Kind = CashBookKind.SupplierIn,
            Description = "Refund from " + supplierName + where + " · goods sent back",
            AmountIn = r.IntoTillPkr,
            AmountOut = 0,
            SupplierReturnId = r.Id
        });
    }

    public static void PostExpense(AppDbContext db, ShopExpense exp)
    {
        db.CashBook.Add(new CashBookEntry
        {
            Date = exp.Date,
            Kind = CashBookKind.ExpenseOut,
            Description = "Expense · " + exp.Description,
            AmountIn = 0,
            AmountOut = exp.Amount,
            ShopExpenseId = exp.Id
        });
    }

    public static void SyncExpense(AppDbContext db, ShopExpense exp)
    {
        var row = db.CashBook.FirstOrDefault(e => e.ShopExpenseId == exp.Id);
        if (row is null)
        {
            PostExpense(db, exp);
            return;
        }
        row.Date = exp.Date;
        row.Description = "Expense · " + exp.Description;
        row.AmountOut = exp.Amount;
    }

    public static void RemoveExpense(AppDbContext db, int expenseId)
    {
        var rows = db.CashBook.Where(e => e.ShopExpenseId == expenseId).ToList();
        db.CashBook.RemoveRange(rows);
    }

    public static void PostRefunds(AppDbContext db, IEnumerable<Payment> pays, int saleId, string customerName,
        int? invoiceNo = null)
    {
        foreach (var pay in pays)
        {
            db.CashBook.Add(new CashBookEntry
            {
                Date = DateTime.Today,
                Kind = CashBookKind.RefundOut,
                Description = $"Cash returned to {customerName} · cancelled sale #{invoiceNo ?? saleId}",
                AmountIn = 0,
                AmountOut = pay.Amount,
                PaymentId = pay.Id,
                SaleId = saleId
            });
        }
    }

    private static async Task ImportMissingAsync(AppDbContext db)
    {
        // A till line names the bill it belongs to by the number on that bill's paper, so this repair reads
        // the numbers once and does not have to open a row per payment.
        var numbers = await db.Sales.AsNoTracking().ToDictionaryAsync(x => x.Id, x => x.InvoiceNo);
        int? NoOf(int? saleId) => saleId is int key && numbers.TryGetValue(key, out var no) ? no : null;

        var linkedPay = await db.CashBook.AsNoTracking()
            .Where(e => e.PaymentId != null && e.Kind == CashBookKind.CustomerIn)
            .Select(e => e.PaymentId!.Value)
            .ToListAsync();
        var pays = await db.Payments.AsNoTracking().Include(p => p.Customer).ToListAsync();
        foreach (var p in pays.Where(p => !linkedPay.Contains(p.Id)))
            PostCustomerPayment(db, p, p.Customer.Name, NoOf(p.SaleId));

        var linkedSup = await db.CashBook.AsNoTracking()
            .Where(e => e.SupplierPaymentId != null)
            .Select(e => e.SupplierPaymentId!.Value)
            .ToListAsync();
        var supPays = await db.SupplierPayments.AsNoTracking()
            .Include(p => p.Supplier)
            .Include(p => p.Container)
            .ToListAsync();
        // A credit note settles a bill without any cash moving, so it has no till line to be missing: filling
        // one in here would hand the repair a money movement the shop never made.
        foreach (var p in supPays.Where(p => !linkedSup.Contains(p.Id) && InventoryService.MovesCash(p.Method)))
            PostSupplierPayment(db, p, p.Supplier.Name, p.Container?.Title);

        var linkedRefunds = await db.CashBook.AsNoTracking()
            .Where(e => e.SupplierReturnId != null)
            .Select(e => e.SupplierReturnId!.Value)
            .ToListAsync();
        var refunds = await db.SupplierReturns.AsNoTracking()
            .Include(r => r.Container).ThenInclude(c => c!.Supplier)
            .ToListAsync();
        foreach (var r in refunds.Where(r =>
                     r.IntoTillPkr > 0m && !linkedRefunds.Contains(r.Id)))
            PostSupplierRefund(db, r, r.Container?.Supplier?.Name ?? "Supplier", r.Container?.Title);

        var linkedExp = await db.CashBook.AsNoTracking()
            .Where(e => e.ShopExpenseId != null)
            .Select(e => e.ShopExpenseId!.Value)
            .ToListAsync();
        var expenses = await db.ShopExpenses.AsNoTracking().ToListAsync();
        foreach (var e in expenses.Where(e => !linkedExp.Contains(e.Id)))
            PostExpense(db, e);

        var cancelled = await db.Sales.AsNoTracking()
            .Where(s => s.Status == SaleStatus.Cancelled)
            .Select(s => s.Id)
            .ToListAsync();
        if (cancelled.Count > 0)
        {
            var refunded = await db.CashBook.AsNoTracking()
                .Where(e => e.Kind == CashBookKind.RefundOut && e.PaymentId != null)
                .Select(e => e.PaymentId!.Value)
                .ToListAsync();
            foreach (var p in pays.Where(p =>
                         p.SaleId is int sid && cancelled.Contains(sid) && !refunded.Contains(p.Id)))
                PostRefunds(db, new[] { p }, p.SaleId!.Value, p.Customer.Name, NoOf(p.SaleId));
        }

        if (db.ChangeTracker.HasChanges())
            await db.SaveChangesAsync();
    }
}

public class SupplierPaymentRow
{
    public int ContainerId { get; set; }
    public DateTime Date { get; set; }
    public string Method { get; set; } = "";
    public decimal Amount { get; set; }
    public string? Notes { get; set; }

    public string DateText => Date.ToString("dd MMM yyyy");
    public string AmountText => Money.Pkr(Amount);
    public string NoteText => string.IsNullOrWhiteSpace(Notes) ? "—" : Notes!.Trim();
}

public class SupplierPayTarget
{
    public int Id { get; set; }
    public string SupplierName { get; set; } = "";
    public string ContainerTitle { get; set; } = "";
    public decimal Owed { get; set; }
    public string Label { get; set; } = "";
}
