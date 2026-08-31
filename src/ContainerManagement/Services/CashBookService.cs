using ContainerManagement.Data;
using ContainerManagement.Models;
using Microsoft.EntityFrameworkCore;

namespace ContainerManagement.Services;

public class CashBookService
{
    private readonly IDbContextFactory<AppDbContext> _factory;

    public CashBookService(IDbContextFactory<AppDbContext> factory) => _factory = factory;

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
            var owed = c.SupplierAmount - c.SupplierPayments.Sum(p => p.Amount);
            var supplier = string.IsNullOrWhiteSpace(c.Supplier?.Name) ? "Supplier" : c.Supplier!.Name;
            return new SupplierPayTarget
            {
                Id = c.Id,
                SupplierName = supplier,
                ContainerTitle = c.Title,
                Owed = owed,
                Label = supplier + " · " + c.Title + " · " + OwedLabel(owed)
            };
        }).ToList();
    }

    private static string OwedLabel(decimal owed)
    {
        if (owed > 0.009m) return "owe " + Money.Pkr(owed);
        if (owed < -0.009m) return "paid extra " + Money.Pkr(-owed);
        return "settled";
    }

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
        db.CashBook.Add(new CashBookEntry
        {
            Date = pay.Date,
            Kind = CashBookKind.SupplierOut,
            Description = "Paid " + supplierName + where,
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

public class SupplierPayTarget
{
    public int Id { get; set; }
    public string SupplierName { get; set; } = "";
    public string ContainerTitle { get; set; } = "";
    public decimal Owed { get; set; }
    public string Label { get; set; } = "";
}
