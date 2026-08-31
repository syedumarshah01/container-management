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

    public async Task<ShopExpense> AddAsync(DateTime date, string description, decimal amount, string? notes)
    {
        if (string.IsNullOrWhiteSpace(description))
            throw new InvalidOperationException("Say what the expense is for.");
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
        return row;
    }

    public async Task UpdateAsync(int id, DateTime date, string description, decimal amount, string? notes)
    {
        if (string.IsNullOrWhiteSpace(description))
            throw new InvalidOperationException("Say what the expense is for.");
        if (amount <= 0)
            throw new InvalidOperationException("Amount must be greater than zero.");

        await using var db = await _factory.CreateDbContextAsync();
        var row = await db.ShopExpenses.FindAsync(id)
            ?? throw new InvalidOperationException("Expense not found.");
        row.Date = date.Date;
        row.Description = description.Trim();
        row.Amount = amount;
        row.Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        await db.SaveChangesAsync();
    }

    public async Task DeleteAsync(int id)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var row = await db.ShopExpenses.FindAsync(id)
            ?? throw new InvalidOperationException("Expense not found.");
        db.ShopExpenses.Remove(row);
        await db.SaveChangesAsync();
    }
}
