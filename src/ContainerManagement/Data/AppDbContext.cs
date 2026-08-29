using ContainerManagement.Models;
using Microsoft.EntityFrameworkCore;

namespace ContainerManagement.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<CargoContainer> Containers => Set<CargoContainer>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<ContainerItem> ContainerItems => Set<ContainerItem>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Sale> Sales => Set<Sale>();
    public DbSet<SaleLine> SaleLines => Set<SaleLine>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<LedgerEntry> LedgerEntries => Set<LedgerEntry>();
    public DbSet<ContainerExpense> Expenses => Set<ContainerExpense>();
    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<SupplierPayment> SupplierPayments => Set<SupplierPayment>();
    public DbSet<StockAdjustment> StockAdjustments => Set<StockAdjustment>();
    public DbSet<CashMovement> CashMovements => Set<CashMovement>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<CargoContainer>(e =>
        {
            e.ToTable("Containers");
            e.Property(x => x.Title).HasMaxLength(200).IsRequired();
            e.Property(x => x.ContainerNumber).HasMaxLength(80);
            e.Property(x => x.Origin).HasMaxLength(80);
            e.Property(x => x.Currency).HasMaxLength(10);
            e.Property(x => x.BlNumber).HasMaxLength(80);
            e.Property(x => x.ExchangeRate).HasPrecision(18, 6);
            e.Property(x => x.SupplierAmount).HasPrecision(18, 2);
            e.HasIndex(x => x.Title);
            e.HasOne(x => x.Supplier).WithMany(s => s.Containers).HasForeignKey(x => x.SupplierId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        model.Entity<Product>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.Sku).HasMaxLength(80);
            e.Property(x => x.Unit).HasMaxLength(30).IsRequired();
            e.Property(x => x.LastSalePrice).HasPrecision(18, 2);
            e.HasIndex(x => x.Name);
        });

        model.Entity<ContainerItem>(e =>
        {
            e.Ignore(x => x.EffectiveCost);
            e.Property(x => x.QuantityReceived).HasPrecision(18, 3);
            e.Property(x => x.QuantityRemaining).HasPrecision(18, 3);
            e.Property(x => x.UnitCost).HasPrecision(18, 2);
            e.Property(x => x.ForeignCost).HasPrecision(18, 4);
            e.Property(x => x.LandedUnitCost).HasPrecision(18, 4);
            e.HasOne(x => x.Container).WithMany(c => c.Items).HasForeignKey(x => x.ContainerId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Product).WithMany(p => p.Items).HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.ContainerId, x.ProductId });
        });

        model.Entity<Customer>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.Phone).HasMaxLength(40);
            e.HasIndex(x => x.Name);
        });

        model.Entity<Sale>(e =>
        {
            e.Property(x => x.TotalAmount).HasPrecision(18, 2);
            e.Property(x => x.PaidNow).HasPrecision(18, 2);
            e.Property(x => x.DiscountAmount).HasPrecision(18, 2);
            e.HasOne(x => x.Customer).WithMany(c => c.Sales).HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.Date);
        });

        model.Entity<SaleLine>(e =>
        {
            e.Ignore(x => x.LineTotal);
            e.Ignore(x => x.LineCost);
            e.Ignore(x => x.LineProfit);
            e.Property(x => x.Quantity).HasPrecision(18, 3);
            e.Property(x => x.UnitPrice).HasPrecision(18, 2);
            e.Property(x => x.UnitCost).HasPrecision(18, 2);
            e.HasOne(x => x.Sale).WithMany(s => s.Lines).HasForeignKey(x => x.SaleId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Container).WithMany(c => c.SaleLines).HasForeignKey(x => x.ContainerId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.ContainerItem).WithMany(i => i.SaleLines).HasForeignKey(x => x.ContainerItemId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        });

        model.Entity<Payment>(e =>
        {
            e.Property(x => x.Amount).HasPrecision(18, 2);
            e.Property(x => x.Method).HasMaxLength(40);
            e.HasOne(x => x.Customer).WithMany(c => c.Payments).HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.Date);
        });

        model.Entity<LedgerEntry>(e =>
        {
            e.Property(x => x.Debit).HasPrecision(18, 2);
            e.Property(x => x.Credit).HasPrecision(18, 2);
            e.Property(x => x.Description).HasMaxLength(400);
            e.HasOne(x => x.Customer).WithMany(c => c.Ledger).HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.CustomerId, x.Date });
        });

        model.Entity<ContainerExpense>(e =>
        {
            e.ToTable("Expenses");
            e.Property(x => x.Amount).HasPrecision(18, 2);
            e.Property(x => x.Category).HasMaxLength(80);
            e.HasOne(x => x.Container).WithMany(c => c.Expenses).HasForeignKey(x => x.ContainerId).OnDelete(DeleteBehavior.Restrict);
        });

        model.Entity<Supplier>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
        });

        model.Entity<SupplierPayment>(e =>
        {
            e.Property(x => x.Amount).HasPrecision(18, 2);
            e.Property(x => x.Method).HasMaxLength(40);
            e.HasOne(x => x.Supplier).WithMany(s => s.Payments).HasForeignKey(x => x.SupplierId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Container).WithMany(c => c.SupplierPayments).HasForeignKey(x => x.ContainerId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        model.Entity<StockAdjustment>(e =>
        {
            e.Property(x => x.QuantityBefore).HasPrecision(18, 3);
            e.Property(x => x.QuantityAfter).HasPrecision(18, 3);
            e.HasOne(x => x.ContainerItem).WithMany().HasForeignKey(x => x.ContainerItemId).OnDelete(DeleteBehavior.Restrict);
        });

        model.Entity<CashMovement>(e =>
        {
            e.Property(x => x.Amount).HasPrecision(18, 2);
            e.Property(x => x.Direction).HasMaxLength(10);
            e.Property(x => x.Method).HasMaxLength(40);
        });
    }
}
