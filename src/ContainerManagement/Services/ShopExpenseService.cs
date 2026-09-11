using ContainerManagement.Data;
using ContainerManagement.Models;
using Microsoft.EntityFrameworkCore;

namespace ContainerManagement.Services;

public class ShopExpenseService
{
    private readonly IDbContextFactory<AppDbContext> _factory;

    public ShopExpenseService(IDbContextFactory<AppDbContext> factory) => _factory = factory;

    public async Task<List<ShopExpense>> ListAsync()
    {
        await using var db = await _factory.CreateDbContextAsync();
        return await db.ShopExpenses.AsNoTracking()
            .OrderBy(e => e.Date)
            .ThenBy(e => e.Id)
            .ToListAsync();
    }

    /// <summary>
    /// The year's costs, month by month, with the count of lines under each figure: a month that shows
    /// nothing and a month nobody typed anything into are the same number on paper and should not look the
    /// same on a statement.
    /// </summary>
    public async Task<List<ExpenseYearRow>> GetYearAsync(int year)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var list = await db.ShopExpenses.AsNoTracking().ToListAsync();
        var rows = new List<ExpenseYearRow>(12);
        for (var m = 1; m <= 12; m++)
        {
            var from = new DateTime(year, m, 1);
            var to = from.AddMonths(1);
            var month = list.Where(e => e.Date >= from && e.Date < to).ToList();
            rows.Add(new ExpenseYearRow
            {
                Month = m,
                Count = month.Count,
                Amount = Money.Round(month.Sum(e => e.Amount))
            });
        }
        return rows;
    }

    public async Task<ShopExpense> AddAsync(DateTime date, string description, decimal amount, string? notes)
    {
        if (string.IsNullOrWhiteSpace(description))
            throw new InvalidOperationException("Say what the expense is for.");
        amount = Money.Round(amount);
        if (amount <= 0)
            throw new InvalidOperationException("Amount must be greater than zero.");

        await using var db = await _factory.CreateDbContextAsync();
        var row = new ShopExpense
        {
            Date = date.Date,
            Description = description.Trim(),
            Amount = amount,
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim()
        };
        db.ShopExpenses.Add(row);
        await db.SaveChangesAsync();
        CashBookService.PostExpense(db, row);
        await db.SaveChangesAsync();
        return row;
    }

    public async Task UpdateAsync(int id, DateTime date, string description, decimal amount, string? notes)
    {
        if (string.IsNullOrWhiteSpace(description))
            throw new InvalidOperationException("Say what the expense is for.");
        amount = Money.Round(amount);
        if (amount <= 0)
            throw new InvalidOperationException("Amount must be greater than zero.");

        await using var db = await _factory.CreateDbContextAsync();
        var row = await db.ShopExpenses.FindAsync(id)
            ?? throw new InvalidOperationException("Expense not found.");
        row.Date = date.Date;
        row.Description = description.Trim();
        row.Amount = Money.Round(amount);
        row.Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        CashBookService.SyncExpense(db, row);
        await db.SaveChangesAsync();
    }

    public async Task DeleteAsync(int id)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var row = await db.ShopExpenses.FindAsync(id)
            ?? throw new InvalidOperationException("Expense not found.");
        CashBookService.RemoveExpense(db, id);
        db.ShopExpenses.Remove(row);
        await db.SaveChangesAsync();
    }
}
