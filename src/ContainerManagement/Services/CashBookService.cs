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
            var paid = c.SupplierPayments.Sum(p => p.Amount);
            var owed = Money.Round(c.SupplierAmount - paid);
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
    /// The words for money paid past what a container says is owed. It should only ever be found on a
    /// container entered under the previous rule, since the pay page refuses it now - which is why the
    /// figure is stated plainly instead of dressed up as an arrangement with the supplier. One method,
    /// three places: the list, the dropdown and the pay panel must not drift into three stories.
    /// </summary>
    public static string PaidExtraText(decimal extra, bool terse = false)
        => (terse ? "paid extra " : "Paid extra ") + Money.Pkr(Money.Round(extra));

    public static void PostCustomerPayment(AppDbContext db, Payment pay, string customerName)
    {
        db.CashBook.Add(new CashBookEntry
        {
            Date = pay.Date,
            Kind = CashBookKind.CustomerIn,
            Description = pay.SaleId is int sid
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

    public static void PostRefunds(AppDbContext db, IEnumerable<Payment> pays, int saleId, string customerName)
    {
        foreach (var pay in pays)
        {
            db.CashBook.Add(new CashBookEntry
            {
                Date = DateTime.Today,
                Kind = CashBookKind.RefundOut,
                Description = $"Cash returned to {customerName} · cancelled sale #{saleId}",
                AmountIn = 0,
                AmountOut = pay.Amount,
                PaymentId = pay.Id,
                SaleId = saleId
            });
        }
    }

    private static async Task ImportMissingAsync(AppDbContext db)
    {
        var linkedPay = await db.CashBook.AsNoTracking()
            .Where(e => e.PaymentId != null && e.Kind == CashBookKind.CustomerIn)
            .Select(e => e.PaymentId!.Value)
            .ToListAsync();
        var pays = await db.Payments.AsNoTracking().Include(p => p.Customer).ToListAsync();
        foreach (var p in pays.Where(p => !linkedPay.Contains(p.Id)))
            PostCustomerPayment(db, p, p.Customer.Name);

        var linkedSup = await db.CashBook.AsNoTracking()
            .Where(e => e.SupplierPaymentId != null)
            .Select(e => e.SupplierPaymentId!.Value)
            .ToListAsync();
        var supPays = await db.SupplierPayments.AsNoTracking()
            .Include(p => p.Supplier)
            .Include(p => p.Container)
            .ToListAsync();
        foreach (var p in supPays.Where(p => !linkedSup.Contains(p.Id)))
            PostSupplierPayment(db, p, p.Supplier.Name, p.Container?.Title);

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
                PostRefunds(db, new[] { p }, p.SaleId!.Value, p.Customer.Name);
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
