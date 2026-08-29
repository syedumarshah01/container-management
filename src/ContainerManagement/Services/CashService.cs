using ContainerManagement.Data;
using ContainerManagement.Models;
using Microsoft.EntityFrameworkCore;

namespace ContainerManagement.Services;

public class CashService
{
    private readonly IDbContextFactory<AppDbContext> _factory;

    public CashService(IDbContextFactory<AppDbContext> factory) => _factory = factory;

    public async Task<(List<CashBookRow> Rows, Dictionary<string, decimal> InByMethod, decimal OutTotal)>
        GetDayAsync(DateTime day)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var start = day.Date;
        var end = start.AddDays(1);

        var pays = await db.Payments.AsNoTracking()
            .Include(p => p.Customer)
            .Where(p => p.Date >= start && p.Date < end)
            .OrderBy(p => p.Id)
            .ToListAsync();

        var moves = await db.CashMovements.AsNoTracking()
            .Where(m => m.Date >= start && m.Date < end)
            .OrderBy(m => m.Id)
            .ToListAsync();

        var rows = new List<CashBookRow>();
        var inn = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        decimal outing = 0;

        foreach (var p in pays)
        {
            Add(inn, p.Method, p.Amount);
            rows.Add(new CashBookRow
            {
                When = p.Date.ToString("HH:mm"),
                What = $"{p.Customer.Name}" + (p.SaleId is int s ? $" · sale #{s}" : ""),
                Method = p.Method,
                InText = Money.Pkr(p.Amount)
            });
        }

        foreach (var m in moves)
        {
            if (string.Equals(m.Direction, "In", StringComparison.OrdinalIgnoreCase))
            {
                Add(inn, m.Method, m.Amount);
                rows.Add(new CashBookRow
                {
                    When = m.Date.ToString("HH:mm"),
                    What = m.Notes ?? "Cash in",
                    Method = m.Method,
                    InText = Money.Pkr(m.Amount)
                });
            }
            else
            {
                outing += m.Amount;
                rows.Add(new CashBookRow
                {
                    When = m.Date.ToString("HH:mm"),
                    What = m.Notes ?? "Cash out",
                    Method = m.Method,
                    OutText = Money.Pkr(m.Amount)
                });
            }
        }

        return (rows, inn, outing);
    }

    public async Task AddMovementAsync(DateTime date, string direction, string method, decimal amount, string? notes)
    {
        if (amount <= 0)
            throw new InvalidOperationException("Amount must be greater than zero.");
        await using var db = await _factory.CreateDbContextAsync();
        db.CashMovements.Add(new CashMovement
        {
            Date = date,
            Direction = direction == "In" ? "In" : "Out",
            Method = string.IsNullOrWhiteSpace(method) ? "Cash" : method.Trim(),
            Amount = amount,
            Notes = notes?.Trim()
        });
        await db.SaveChangesAsync();
    }

    private static void Add(Dictionary<string, decimal> map, string method, decimal amount)
    {
        var key = string.IsNullOrWhiteSpace(method) ? "Cash" : method;
        map[key] = map.TryGetValue(key, out var v) ? v + amount : amount;
    }
}
