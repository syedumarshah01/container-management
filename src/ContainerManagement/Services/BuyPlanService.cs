using ContainerManagement.Data;
using ContainerManagement.Models;
using Microsoft.EntityFrameworkCore;

namespace ContainerManagement.Services;

/// <summary>
/// The China order sheet you fill before buying: item, quantity, yen cost, weight, sale price,
/// then one expense figure for the whole lot. Saved plans are only a plan — stock and ledgers
/// are not touched.
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
            .FirstOrDefaultAsync(p => p.Id == id);
        return plan is null ? null : ToRow(plan);
    }

    public async Task<BuyPlan> CreateAsync(string title, string? supplier, decimal yenRate, decimal expense)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var plan = new BuyPlan
        {
            Title = PickTitle(title),
            Supplier = TrimOrNull(supplier),
            YenRate = CleanRate(yenRate),
            ExpensePkr = CleanMoney(expense, "Expense")
        };
        db.BuyPlans.Add(plan);
        await db.SaveChangesAsync();
        return plan;
    }

    /// <summary>
    /// Writes the header and replaces every row with what the page holds, in one go, so a half
    /// typed sheet can never leave the plan with rows missing.
    /// </summary>
    public async Task SaveAsync(
        int id, string title, string? supplier, string? notes, decimal yenRate, decimal expense,
        IReadOnlyList<BuyPlanLineInput> lines)
    {
        foreach (var line in lines)
            Validate(line);

        await using var db = await _factory.CreateDbContextAsync();
        var plan = await db.BuyPlans.Include(p => p.Lines).FirstOrDefaultAsync(p => p.Id == id)
            ?? throw new InvalidOperationException("This plan is gone. It may have been deleted.");

        await using var tx = await db.Database.BeginTransactionAsync();

        plan.Title = PickTitle(title);
        plan.Supplier = TrimOrNull(supplier);
        plan.Notes = TrimOrNull(notes);
        plan.YenRate = CleanRate(yenRate);
        plan.ExpensePkr = CleanMoney(expense, "Expense");

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
                SalePricePkr = line.SalePricePkr,
                Notes = TrimOrNull(line.Notes)
            });
        }

        await db.SaveChangesAsync();
        await tx.CommitAsync();
    }

    public async Task<BuyPlan> DuplicateAsync(int id)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var plan = await db.BuyPlans.Include(p => p.Lines).AsNoTracking().FirstOrDefaultAsync(p => p.Id == id)
            ?? throw new InvalidOperationException("This plan is gone. It may have been deleted.");

        var copyTitle = await NextCopyTitle(db, plan.Title);
        var copy = new BuyPlan
        {
            Title = copyTitle,
            Supplier = plan.Supplier,
            Notes = plan.Notes,
            YenRate = plan.YenRate,
            ExpensePkr = plan.ExpensePkr
        };
        foreach (var l in plan.Lines)
        {
            copy.Lines.Add(new BuyPlanLine
            {
                ItemName = l.ItemName,
                Quantity = l.Quantity,
                UnitCostYen = l.UnitCostYen,
                UnitWeightKg = l.UnitWeightKg,
                SalePricePkr = l.SalePricePkr,
                Notes = l.Notes
            });
        }

        db.BuyPlans.Add(copy);
        await db.SaveChangesAsync();
        return copy;
    }

    public async Task DeleteAsync(int id)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var plan = await db.BuyPlans.Include(p => p.Lines).FirstOrDefaultAsync(p => p.Id == id)
            ?? throw new InvalidOperationException("This plan is gone. It may have been deleted.");

        await using var tx = await db.Database.BeginTransactionAsync();
        db.BuyPlanLines.RemoveRange(plan.Lines);
        db.BuyPlans.Remove(plan);
        await db.SaveChangesAsync();
        await tx.CommitAsync();
    }

    private static void Validate(BuyPlanLineInput line)
    {
        if (string.IsNullOrWhiteSpace(line.ItemName))
            throw new InvalidOperationException("Every row needs an item name.");
        if (line.Quantity < 0)
            throw new InvalidOperationException($"Quantity for {line.ItemName.Trim()} cannot be negative.");
        if (line.Quantity == 0)
            throw new InvalidOperationException($"Quantity for {line.ItemName.Trim()} must be greater than zero.");
        if (line.UnitCostYen < 0)
            throw new InvalidOperationException($"Yen cost for {line.ItemName.Trim()} cannot be negative.");
        if (line.UnitWeightKg < 0)
            throw new InvalidOperationException($"Weight for {line.ItemName.Trim()} cannot be negative.");
        if (line.SalePricePkr < 0)
            throw new InvalidOperationException($"Sale price for {line.ItemName.Trim()} cannot be negative.");
    }

    private static decimal CleanRate(decimal rate)
    {
        if (rate <= 0)
            throw new InvalidOperationException("Rupees for 1 yen must be more than zero. Type your rate, for example 17.");
        return decimal.Round(rate, 6, MidpointRounding.AwayFromZero);
    }

    private static decimal CleanMoney(decimal value, string what)
    {
        if (value < 0)
            throw new InvalidOperationException($"{what} cannot be negative.");
        return decimal.Round(value, 2, MidpointRounding.AwayFromZero);
    }

    private static string PickTitle(string title) =>
        string.IsNullOrWhiteSpace(title) ? "Plan " + DateTime.Now.ToString("dd MMM yyyy") : title.Trim();

    private static async Task<string> NextCopyTitle(AppDbContext db, string title)
    {
        var baseName = PickTitle(title);
        var candidate = baseName + " (copy)";
        var n = 2;
        while (await db.BuyPlans.AnyAsync(p => p.Title.ToLower() == candidate.ToLower()))
            candidate = $"{baseName} (copy {n++})";
        return candidate;
    }

    private static string? TrimOrNull(string? s) =>
        string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static BuyPlanRow ToRow(BuyPlan p)
    {
        var row = new BuyPlanRow
        {
            Id = p.Id,
            Title = p.Title,
            Supplier = p.Supplier ?? "",
            Notes = p.Notes ?? "",
            CreatedAt = p.CreatedAt,
            YenRate = p.YenRate,
            ExpensePkr = p.ExpensePkr,
            Lines = p.Lines
                .OrderBy(l => l.Id)
                .Select(l => new BuyPlanLineRow
                {
                    Id = l.Id,
                    ItemName = l.ItemName,
                    Quantity = l.Quantity,
                    UnitCostYen = l.UnitCostYen,
                    UnitWeightKg = l.UnitWeightKg,
                    SalePricePkr = l.SalePricePkr,
                    Notes = l.Notes
                })
                .ToList()
        };
        row.RefreshTotals();
        return row;
    }
}
