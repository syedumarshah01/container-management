using ContainerManagement.Data;
using ContainerManagement.Models;
using Microsoft.EntityFrameworkCore;

namespace ContainerManagement.Services;

/// <summary>
/// The order sheet written before buying from China: item, quantity, yen cost, weight and sale price for
/// every lot, and one row per bill the shipment will cost - sea freight, customs, clearing, labour - in yen
/// or rupees as the bill was written. The sheet's expense figure is those rows added up, exactly as a
/// container's is, so a total and the lines under it are never two different numbers. A sheet is only a plan
/// - stock and ledgers are not touched.
/// </summary>
public class BuyPlanService
{
    private const decimal DefaultYenRate = 17;

    private readonly IDbContextFactory<AppDbContext> _factory;

    public BuyPlanService(IDbContextFactory<AppDbContext> factory) => _factory = factory;

    public static decimal SuggestedRate => DefaultYenRate;

    public async Task<List<BuyPlanRow>> ListAsync()
    {
        await using var db = await _factory.CreateDbContextAsync();
        var plans = await db.BuyPlans
            .AsNoTracking()
            .Include(p => p.Lines)
            .Include(p => p.Expenses)
            .OrderByDescending(p => p.CreatedAt)
            .ThenByDescending(p => p.Id)
            .ToListAsync();
        return plans.Select(ToRow).ToList();
    }

    public async Task<BuyPlanRow?> GetAsync(int id)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var plan = await db.BuyPlans
            .AsNoTracking()
            .Include(p => p.Lines)
            .Include(p => p.Expenses)
            .FirstOrDefaultAsync(p => p.Id == id);
        return plan is null ? null : ToRow(plan);
    }

    public async Task<BuyPlan> CreateAsync(string title)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var plan = new BuyPlan
        {
            Title = PickTitle(title),
            YenRate = DefaultYenRate,
            ExpensePkr = 0
        };
        db.BuyPlans.Add(plan);
        await db.SaveChangesAsync();
        return plan;
    }

    /// <summary>
    /// Writes the header, the goods rows and the expense rows in one go, so a half typed sheet can never
    /// leave a plan with rows missing. The expense figure on the sheet is not passed in: it is the sum of the
    /// rows, computed here, which is the only way the total and the lines under it cannot disagree.
    /// </summary>
    public async Task SaveAsync(
        int id, string title, decimal yenRate, IReadOnlyList<BuyPlanLineInput> lines,
        IReadOnlyList<BuyPlanExpenseInput> expenses)
    {
        var rate = CleanRate(yenRate);
        foreach (var line in lines)
            Validate(line);
        var rows = expenses.Select(e => ValidateExpense(e, rate)).ToList();

        await using var db = await _factory.CreateDbContextAsync();
        var plan = await db.BuyPlans.Include(p => p.Lines).Include(p => p.Expenses)
            .FirstOrDefaultAsync(p => p.Id == id)
            ?? throw new InvalidOperationException("This sheet is gone. It may have been deleted.");

        await using var tx = await db.Database.BeginTransactionAsync();

        plan.Title = PickTitle(title);
        plan.YenRate = rate;

        db.BuyPlanLines.RemoveRange(plan.Lines);
        plan.Lines.Clear();
        foreach (var line in lines)
        {
            plan.Lines.Add(new BuyPlanLine
            {
                ItemName = line.ItemName.Trim(),
                Quantity = line.Quantity,
                UnitCostYen = line.UnitCostYen,
                UnitWeightKg = line.UnitWeightKg,
                SalePricePkr = line.SalePricePkr
            });
        }

        db.BuyPlanExpenses.RemoveRange(plan.Expenses);
        plan.Expenses.Clear();
        foreach (var (description, pkr, code, foreign, used) in rows)
        {
            plan.Expenses.Add(new BuyPlanExpense
            {
                Description = description,
                AmountPkr = pkr,
                Currency = code,
                AmountForeign = foreign,
                RateUsed = used
            });
        }

        plan.ExpensePkr = Money.Round(rows.Sum(r => r.Pkr));

        await db.SaveChangesAsync();
        await tx.CommitAsync();
    }

    public async Task<BuyPlan> DuplicateAsync(int id)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var plan = await db.BuyPlans.Include(p => p.Lines).Include(p => p.Expenses).AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id)
            ?? throw new InvalidOperationException("This sheet is gone. It may have been deleted.");

        var copy = new BuyPlan
        {
            Title = await NextCopyTitle(db, plan.Title),
            YenRate = plan.YenRate,
            ExpensePkr = plan.ExpensePkr
        };
        foreach (var e in plan.Expenses)
        {
            // Copied as written, rupees and rate and all: a duplicate is a starting point, and re-multiplying
            // yesterday's bill at today's rate would make the copy disagree with the sheet it came from.
            copy.Expenses.Add(new BuyPlanExpense
            {
                Description = e.Description,
                AmountPkr = e.AmountPkr,
                Currency = e.Currency,
                AmountForeign = e.AmountForeign,
                RateUsed = e.RateUsed
            });
        }
        foreach (var l in plan.Lines)
        {
            copy.Lines.Add(new BuyPlanLine
            {
                ItemName = l.ItemName,
                Quantity = l.Quantity,
                UnitCostYen = l.UnitCostYen,
                UnitWeightKg = l.UnitWeightKg,
                SalePricePkr = l.SalePricePkr
            });
        }

        db.BuyPlans.Add(copy);
        await db.SaveChangesAsync();
        return copy;
    }

    public async Task DeleteAsync(int id)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var plan = await db.BuyPlans.Include(p => p.Lines).Include(p => p.Expenses)
            .FirstOrDefaultAsync(p => p.Id == id)
            ?? throw new InvalidOperationException("This sheet is gone. It may have been deleted.");

        await using var tx = await db.Database.BeginTransactionAsync();
        db.BuyPlanExpenses.RemoveRange(plan.Expenses);
        db.BuyPlanLines.RemoveRange(plan.Lines);
        db.BuyPlans.Remove(plan);
        await db.SaveChangesAsync();
        await tx.CommitAsync();
    }

    private static void Validate(BuyPlanLineInput line)
    {
        var name = string.IsNullOrWhiteSpace(line.ItemName) ? "A row" : line.ItemName.Trim();
        if (string.IsNullOrWhiteSpace(line.ItemName))
            throw new InvalidOperationException("Every row needs an item name.");
        if (line.Quantity <= 0)
            throw new InvalidOperationException($"{name} needs a quantity above zero.");
        if (line.UnitCostYen < 0 || line.SalePricePkr < 0 || line.UnitWeightKg < 0)
            throw new InvalidOperationException($"{name} has a negative number in it.");
    }

    /// <summary>
    /// An expense row as the book keeps it: the figure as written, in the currency it was written in, and the
    /// rupees it adds to the sheet. A yen row is converted once, at the rate on the row or the sheet's own,
    /// and that rate is kept beside the figure - so re-saving a sheet with the row untouched puts back the
    /// same rupees it held, rather than today's value of yesterday's bill. Refused out loud if a yen figure
    /// arrives with nothing to multiply it by.
    /// </summary>
    private static (string Description, decimal Pkr, string Currency, decimal Foreign, decimal? Rate)
        ValidateExpense(BuyPlanExpenseInput input, decimal planRate)
    {
        var description = string.IsNullOrWhiteSpace(input.Description)
            ? "Other"
            : input.Description.Trim();
        var amount = Money.Round(input.Amount);
        if (amount <= 0m)
            throw new InvalidOperationException($"{description} needs an amount above zero.");
        if (Currencies.CodeOf(input.Currency) != "JPY")
            return (description, amount, "PKR", 0m, null);

        var converted = Currencies.InRupees(amount, Currencies.RateFor(planRate, input.Rate));
        if (converted is null)
            throw new InvalidOperationException(Currencies.NoRateMessage(amount));
        return (description, converted.Value.Pkr, "JPY", converted.Value.Foreign, converted.Value.Rate);
    }

    private static decimal CleanRate(decimal rate)
    {
        if (rate <= 0)
            throw new InvalidOperationException("Rupees for 1 yen must be above zero. Type your rate, for example 17.");
        return Money.Round(rate, 6);
    }

    private static string PickTitle(string title) =>
        string.IsNullOrWhiteSpace(title) ? "Order sheet " + DateTime.Now.ToString("dd MMM") : title.Trim();

    private static async Task<string> NextCopyTitle(AppDbContext db, string title)
    {
        var baseName = PickTitle(title);
        var candidate = baseName + " (copy)";
        var n = 2;
        while (await db.BuyPlans.AnyAsync(p => p.Title.ToLower() == candidate.ToLower()))
            candidate = $"{baseName} (copy {n++})";
        return candidate;
    }

    private static BuyPlanRow ToRow(BuyPlan p)
    {
        var row = new BuyPlanRow
        {
            Id = p.Id,
            Title = p.Title,
            CreatedAt = p.CreatedAt,
            YenRate = p.YenRate,
            ExpensePkr = p.ExpensePkr,
            Expenses = p.Expenses
                .OrderBy(e => e.Id)
                .Select(e => new BuyPlanExpenseRow
                {
                    Id = e.Id,
                    Description = e.Description,
                    AmountPkr = e.AmountPkr,
                    Currency = e.Currency,
                    AmountForeign = e.AmountForeign,
                    RateUsed = e.RateUsed
                })
                .ToList(),
            Lines = p.Lines
                .OrderBy(l => l.Id)
                .Select(l => new BuyPlanLineRow
                {
                    Id = l.Id,
                    ItemName = l.ItemName,
                    Quantity = l.Quantity,
                    UnitCostYen = l.UnitCostYen,
                    UnitWeightKg = Money.Round(l.UnitWeightKg, 3),
                    SalePricePkr = l.SalePricePkr
                })
                .ToList()
        };
        row.RefreshTotals();
        return row;
    }
}
