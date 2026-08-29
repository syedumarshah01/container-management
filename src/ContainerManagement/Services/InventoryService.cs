using ContainerManagement.Data;
using ContainerManagement.Models;
using Microsoft.EntityFrameworkCore;

namespace ContainerManagement.Services;

public class InventoryService
{
    private readonly IDbContextFactory<AppDbContext> _factory;

    public InventoryService(IDbContextFactory<AppDbContext> factory) => _factory = factory;

    public async Task<List<CargoContainer>> ListContainersAsync()
    {
        await using var db = await _factory.CreateDbContextAsync();
        return await db.Containers
            .AsNoTracking()
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync();
    }

    public async Task<CargoContainer?> GetContainerAsync(int id)
    {
        await using var db = await _factory.CreateDbContextAsync();
        return await db.Containers
            .AsNoTracking()
            .Include(c => c.Items).ThenInclude(i => i.Product)
            .Include(c => c.Expenses)
            .Include(c => c.Supplier)
            .Include(c => c.SupplierPayments)
            .FirstOrDefaultAsync(c => c.Id == id);
    }

    public async Task<CargoContainer> CreateContainerAsync(
        string title, string? number, string origin, DateTime? arrival, string? notes,
        string? currency = null, decimal? rate = null, string? bl = null,
        decimal? cartons = null, decimal? cbm = null, decimal? weight = null,
        string? supplierName = null, decimal supplierAmount = 0)
    {
        if (string.IsNullOrWhiteSpace(title))
            throw new InvalidOperationException("Container title is required.");

        await using var db = await _factory.CreateDbContextAsync();
        var c = new CargoContainer
        {
            Title = title.Trim(),
            ContainerNumber = TrimOrNull(number),
            Origin = string.IsNullOrWhiteSpace(origin) ? "China" : origin.Trim(),
            ArrivalDate = arrival,
            Notes = notes?.Trim(),
            Currency = string.IsNullOrWhiteSpace(currency) ? "PKR" : currency.Trim().ToUpperInvariant(),
            ExchangeRate = rate is > 0 ? rate.Value : 1,
            BlNumber = TrimOrNull(bl),
            Cartons = cartons,
            Cbm = cbm,
            WeightKg = weight,
            SupplierAmount = supplierAmount
        };
        if (!string.IsNullOrWhiteSpace(supplierName))
            c.SupplierId = await FindOrCreateSupplierId(db, supplierName, null);
        db.Containers.Add(c);
        await db.SaveChangesAsync();
        return c;
    }

    public async Task UpdateContainerAsync(
        int id, string title, string? number, string origin, DateTime? arrival, string? notes,
        ContainerStatus status, string currency, decimal rate, string? bl,
        decimal? cartons, decimal? cbm, decimal? weight,
        string? supplierName, decimal supplierAmount)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var c = await db.Containers.FindAsync(id)
            ?? throw new InvalidOperationException("Container not found.");
        if (string.IsNullOrWhiteSpace(title))
            throw new InvalidOperationException("Container title is required.");
        c.Title = title.Trim();
        c.ContainerNumber = TrimOrNull(number);
        c.Origin = string.IsNullOrWhiteSpace(origin) ? "China" : origin.Trim();
        c.ArrivalDate = arrival;
        c.Notes = notes?.Trim();
        c.Status = status;
        c.Currency = string.IsNullOrWhiteSpace(currency) ? "PKR" : currency.Trim().ToUpperInvariant();
        c.ExchangeRate = rate > 0 ? rate : 1;
        c.BlNumber = TrimOrNull(bl);
        c.Cartons = cartons;
        c.Cbm = cbm;
        c.WeightKg = weight;
        c.SupplierAmount = supplierAmount;
        c.SupplierId = string.IsNullOrWhiteSpace(supplierName)
            ? null
            : await FindOrCreateSupplierId(db, supplierName, null);
        await db.SaveChangesAsync();
    }

    public async Task UpdateImportDetailsAsync(
        int id, string? supplierName, decimal supplierAmount,
        decimal? cartons, decimal? cbm, decimal? weight)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var c = await db.Containers.FindAsync(id)
            ?? throw new InvalidOperationException("Container not found.");
        c.Cartons = cartons;
        c.Cbm = cbm;
        c.WeightKg = weight;
        c.SupplierAmount = supplierAmount;
        c.SupplierId = string.IsNullOrWhiteSpace(supplierName)
            ? null
            : await FindOrCreateSupplierId(db, supplierName, null);
        await db.SaveChangesAsync();
    }

    public async Task SetStatusAsync(int id, ContainerStatus status)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var c = await db.Containers.FindAsync(id)
            ?? throw new InvalidOperationException("Container not found.");
        c.Status = status;
        await db.SaveChangesAsync();
    }

    public async Task<ContainerItem> AddGoodsAsync(
        int containerId, string productName, string unit, string? sku, decimal qty, decimal costEntered,
        string? notes, decimal? cartons, decimal? cbm, decimal? weight, string? photoPath)
    {
        if (string.IsNullOrWhiteSpace(productName))
            throw new InvalidOperationException("Goods name is required.");
        if (qty <= 0)
            throw new InvalidOperationException("Quantity must be greater than zero.");
        if (costEntered < 0)
            throw new InvalidOperationException("Unit cost cannot be negative.");

        await using var db = await _factory.CreateDbContextAsync();
        var container = await db.Containers.FindAsync(containerId)
            ?? throw new InvalidOperationException("Container not found.");
        if (container.Status == ContainerStatus.Closed)
            throw new InvalidOperationException("This container is closed. Re-open it to add goods.");

        var product = await FindOrCreateProductAsync(db, productName, unit, sku);
        if (!string.IsNullOrWhiteSpace(photoPath))
            product.PhotoPath = photoPath;

        var item = new ContainerItem
        {
            ContainerId = containerId,
            ProductId = product.Id,
            QuantityReceived = qty,
            QuantityRemaining = qty,
            ForeignCost = costEntered,
            UnitCost = costEntered,
            LandedUnitCost = costEntered,
            Notes = notes?.Trim(),
            Cartons = cartons,
            Cbm = cbm,
            WeightKg = weight,
            PhotoPath = photoPath
        };
        db.ContainerItems.Add(item);
        await db.SaveChangesAsync();
        return item;
    }

    public async Task UpdateGoodsAsync(
        int itemId, string productName, string unit, string? sku, decimal received, decimal remaining,
        decimal costEntered, decimal? cartons, decimal? cbm, decimal? weight, string? photoPath)
    {
        if (string.IsNullOrWhiteSpace(productName))
            throw new InvalidOperationException("Item name is required.");
        if (received <= 0)
            throw new InvalidOperationException("Purchased quantity must be greater than zero.");
        if (remaining < 0)
            throw new InvalidOperationException("In stock cannot be negative.");
        if (remaining > received)
            throw new InvalidOperationException("In stock cannot be more than purchased.");
        if (costEntered < 0)
            throw new InvalidOperationException("Price cannot be negative.");

        await using var db = await _factory.CreateDbContextAsync();
        var item = await db.ContainerItems.Include(i => i.Container).FirstOrDefaultAsync(i => i.Id == itemId)
            ?? throw new InvalidOperationException("Item not found.");

        var product = await FindOrCreateProductAsync(db, productName, unit, sku);

        item.ProductId = product.Id;
        item.QuantityReceived = received;
        item.QuantityRemaining = remaining;
        item.ForeignCost = costEntered;
        item.UnitCost = costEntered;
        item.LandedUnitCost = costEntered;
        item.Cartons = cartons;
        item.Cbm = cbm;
        item.WeightKg = weight;
        if (!string.IsNullOrWhiteSpace(photoPath))
        {
            item.PhotoPath = photoPath;
            product.PhotoPath = photoPath;
        }
        await db.SaveChangesAsync();
    }

    public async Task DeleteGoodsAsync(int itemId)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var item = await db.ContainerItems.FindAsync(itemId)
            ?? throw new InvalidOperationException("Item not found.");

        var onASale = await db.SaleLines.AnyAsync(l => l.ContainerItemId == itemId);
        if (onASale)
            throw new InvalidOperationException("Cannot delete this item — it is already on a sale.");
        if (item.QuantityRemaining != item.QuantityReceived)
            throw new InvalidOperationException("Cannot delete this item — stock has already moved.");

        var adjustments = await db.StockAdjustments.Where(a => a.ContainerItemId == itemId).ToListAsync();
        db.StockAdjustments.RemoveRange(adjustments);
        db.ContainerItems.Remove(item);
        await db.SaveChangesAsync();
    }

    public async Task AdjustStockAsync(int itemId, decimal counted, string reason)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var item = await db.ContainerItems.FindAsync(itemId)
            ?? throw new InvalidOperationException("Item not found.");
        if (counted < 0)
            throw new InvalidOperationException("Count cannot be negative.");
        if (counted > item.QuantityReceived)
            throw new InvalidOperationException("Count cannot be more than purchased.");

        db.StockAdjustments.Add(new StockAdjustment
        {
            ContainerItemId = itemId,
            Date = DateTime.Now,
            QuantityBefore = item.QuantityRemaining,
            QuantityAfter = counted,
            Reason = string.IsNullOrWhiteSpace(reason) ? "Physical count" : reason.Trim()
        });
        item.QuantityRemaining = counted;
        await db.SaveChangesAsync();
    }

    public async Task<ContainerExpense> AddExpenseAsync(int containerId, DateTime date, string category, decimal amount, string? notes)
    {
        if (amount <= 0)
            throw new InvalidOperationException("Expense amount must be greater than zero.");

        await using var db = await _factory.CreateDbContextAsync();
        if (!await db.Containers.AnyAsync(c => c.Id == containerId))
            throw new InvalidOperationException("Container not found.");

        var exp = new ContainerExpense
        {
            ContainerId = containerId,
            Date = date,
            Category = string.IsNullOrWhiteSpace(category) ? "Other" : category.Trim(),
            Amount = amount,
            Notes = notes?.Trim()
        };
        db.Expenses.Add(exp);
        await db.SaveChangesAsync();
        return exp;
    }

    public async Task UpdateExpenseAsync(int expenseId, DateTime date, string category, decimal amount, string? notes)
    {
        if (amount <= 0)
            throw new InvalidOperationException("Expense amount must be greater than zero.");

        await using var db = await _factory.CreateDbContextAsync();
        var exp = await db.Expenses.FindAsync(expenseId)
            ?? throw new InvalidOperationException("Expense not found.");
        exp.Date = date;
        exp.Category = string.IsNullOrWhiteSpace(category) ? "Other" : category.Trim();
        exp.Amount = amount;
        exp.Notes = notes?.Trim();
        await db.SaveChangesAsync();
    }

    public async Task DeleteExpenseAsync(int expenseId)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var exp = await db.Expenses.FindAsync(expenseId)
            ?? throw new InvalidOperationException("Expense not found.");
        db.Expenses.Remove(exp);
        await db.SaveChangesAsync();
    }

    public async Task PaySupplierAsync(int containerId, DateTime date, decimal amount, string method, string? notes)
    {
        if (amount <= 0)
            throw new InvalidOperationException("Amount must be greater than zero.");
        await using var db = await _factory.CreateDbContextAsync();
        var c = await db.Containers.FindAsync(containerId)
            ?? throw new InvalidOperationException("Container not found.");
        if (c.SupplierId is null)
            throw new InvalidOperationException("Set the supplier name on this container first.");
        db.SupplierPayments.Add(new SupplierPayment
        {
            SupplierId = c.SupplierId.Value,
            ContainerId = containerId,
            Date = date,
            Amount = amount,
            Method = string.IsNullOrWhiteSpace(method) ? "TT" : method.Trim(),
            Notes = notes?.Trim()
        });
        await db.SaveChangesAsync();
    }

    public async Task<decimal> SupplierBalanceAsync(int containerId)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var c = await db.Containers.AsNoTracking().FirstOrDefaultAsync(x => x.Id == containerId);
        if (c is null) return 0;
        var paid = await db.SupplierPayments.AsNoTracking()
            .Where(p => p.ContainerId == containerId)
            .ToListAsync();
        return c.SupplierAmount - paid.Sum(p => p.Amount);
    }

    public async Task<List<StockOption>> GetSellableStockAsync(int? containerId = null)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var q = db.ContainerItems
            .AsNoTracking()
            .Include(i => i.Product)
            .Include(i => i.Container)
            .Where(i => i.QuantityRemaining > 0 && i.Container.Status == ContainerStatus.Open);

        if (containerId is > 0)
            q = q.Where(i => i.ContainerId == containerId);

        return await q
            .OrderBy(i => i.Container.Title)
            .ThenBy(i => i.Product.Name)
            .Select(i => new StockOption
            {
                ContainerItemId = i.Id,
                ContainerId = i.ContainerId,
                ContainerTitle = i.Container.Title,
                ProductId = i.ProductId,
                ProductName = i.Product.Name,
                Sku = i.Product.Sku,
                Unit = i.Product.Unit,
                Remaining = i.QuantityRemaining,
                UnitCost = i.UnitCost,
                LandedCost = i.UnitCost,
                LastSalePrice = i.Product.LastSalePrice
            })
            .ToListAsync();
    }

    public async Task<List<CargoContainer>> ContainersWithStockAsync()
    {
        await using var db = await _factory.CreateDbContextAsync();
        return await db.Containers
            .AsNoTracking()
            .Where(c => c.Status == ContainerStatus.Open && c.Items.Any(i => i.QuantityRemaining > 0))
            .OrderBy(c => c.Title)
            .ToListAsync();
    }

    private static async Task<Product> FindOrCreateProductAsync(AppDbContext db, string name, string unit, string? sku)
    {
        name = name.Trim();
        unit = string.IsNullOrWhiteSpace(unit) ? "pcs" : unit.Trim();
        sku = TrimOrNull(sku);
        var existing = await db.Products.FirstOrDefaultAsync(p =>
            p.Name.ToLower() == name.ToLower() && (p.Sku ?? "") == (sku ?? ""));
        if (existing is not null)
        {
            existing.Unit = unit;
            return existing;
        }

        var p = new Product { Name = name, Unit = unit, Sku = sku };
        db.Products.Add(p);
        await db.SaveChangesAsync();
        return p;
    }

    private static async Task<int> FindOrCreateSupplierId(AppDbContext db, string name, string? phone)
    {
        name = name.Trim();
        var s = await db.Suppliers.FirstOrDefaultAsync(x => x.Name.ToLower() == name.ToLower());
        if (s is null)
        {
            s = new Supplier { Name = name, Phone = TrimOrNull(phone) };
            db.Suppliers.Add(s);
            await db.SaveChangesAsync();
        }
        return s.Id;
    }

    private static string? TrimOrNull(string? s) =>
        string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
