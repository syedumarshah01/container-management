namespace ContainerManagement.Models;

public enum ContainerStatus
{
    Open = 0,
    Closed = 1
}

public enum LedgerType
{
    Sale = 0,
    Payment = 1,
    Adjustment = 2,
    Opening = 3,
    Return = 4
}

public enum SaleStatus
{
    Active = 0,
    Cancelled = 1
}

public class CargoContainer
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? ContainerNumber { get; set; }
    public string Origin { get; set; } = "China";
    public DateTime? ArrivalDate { get; set; }
    public string? Notes { get; set; }
    public ContainerStatus Status { get; set; } = ContainerStatus.Open;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public string Currency { get; set; } = "PKR";
    public decimal ExchangeRate { get; set; } = 1;
    public string? BlNumber { get; set; }
    public decimal? Cartons { get; set; }
    public decimal? Cbm { get; set; }
    public decimal? WeightKg { get; set; }
    public int? SupplierId { get; set; }
    public Supplier? Supplier { get; set; }
    public decimal SupplierAmount { get; set; }

    public List<ContainerItem> Items { get; set; } = new();
    public List<ContainerExpense> Expenses { get; set; } = new();
    public List<SaleLine> SaleLines { get; set; } = new();
    public List<SupplierPayment> SupplierPayments { get; set; } = new();

    public override string ToString() => Title;
}

public class Product
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Sku { get; set; }
    public string Unit { get; set; } = "pcs";
    public string? Notes { get; set; }
    public string? PhotoPath { get; set; }
    public decimal? LastSalePrice { get; set; }

    public List<ContainerItem> Items { get; set; } = new();
}

public class ContainerItem
{
    public int Id { get; set; }
    public int ContainerId { get; set; }
    public CargoContainer Container { get; set; } = null!;
    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;
    public decimal QuantityReceived { get; set; }
    public decimal QuantityRemaining { get; set; }
    public decimal UnitCost { get; set; }
    public decimal ForeignCost { get; set; }
    public decimal LandedUnitCost { get; set; }
    public decimal? Cartons { get; set; }
    public decimal? Cbm { get; set; }
    public decimal? WeightKg { get; set; }
    public string? PhotoPath { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public List<SaleLine> SaleLines { get; set; } = new();

    public decimal EffectiveCost => UnitCost;
}

public class Customer
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Address { get; set; }
    public string? Notes { get; set; }
    public bool IsWalkIn { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public List<Sale> Sales { get; set; } = new();
    public List<Payment> Payments { get; set; } = new();
    public List<LedgerEntry> Ledger { get; set; } = new();

    public override string ToString() => IsWalkIn ? $"{Name} (cash counter)" : Name;
}

public class Sale
{
    public int Id { get; set; }
    public DateTime Date { get; set; } = DateTime.Now;
    public int CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;
    public string? Notes { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal PaidNow { get; set; }
    public decimal DiscountAmount { get; set; }
    public DateTime? DueDate { get; set; }
    public SaleStatus Status { get; set; } = SaleStatus.Active;
    public DateTime? CancelledAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public List<SaleLine> Lines { get; set; } = new();
    public List<SaleReturn> Returns { get; set; } = new();
}

public class SaleReturn
{
    public int Id { get; set; }
    public int SaleId { get; set; }
    public Sale Sale { get; set; } = null!;
    public int CustomerId { get; set; }
    public DateTime Date { get; set; } = DateTime.Now;
    public decimal Amount { get; set; }
    public string? Notes { get; set; }

    public List<SaleReturnLine> Lines { get; set; } = new();
}

public class SaleReturnLine
{
    public int Id { get; set; }
    public int SaleReturnId { get; set; }
    public SaleReturn Return { get; set; } = null!;
    public int SaleLineId { get; set; }
    public SaleLine SaleLine { get; set; } = null!;
    public int ContainerId { get; set; }
    public int ContainerItemId { get; set; }
    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal UnitCost { get; set; }
    public decimal Amount { get; set; }
}

public class SaleLine
{
    public int Id { get; set; }
    public int SaleId { get; set; }
    public Sale Sale { get; set; } = null!;
    public int ContainerId { get; set; }
    public CargoContainer Container { get; set; } = null!;
    public int ContainerItemId { get; set; }
    public ContainerItem ContainerItem { get; set; } = null!;
    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal UnitCost { get; set; }

    public decimal LineTotal => Quantity * UnitPrice;
    public decimal LineCost => Quantity * UnitCost;
    public decimal LineProfit => LineTotal - LineCost;
}

public class Payment
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;
    public DateTime Date { get; set; } = DateTime.Now;
    public decimal Amount { get; set; }
    public string Method { get; set; } = "Cash";
    public string? Notes { get; set; }
    public int? SaleId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

public class LedgerEntry
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;
    public DateTime Date { get; set; } = DateTime.Now;
    public LedgerType Type { get; set; }
    public decimal Debit { get; set; }
    public decimal Credit { get; set; }
    public string Description { get; set; } = string.Empty;
    public int? SaleId { get; set; }
    public int? PaymentId { get; set; }
}

public class ContainerExpense
{
    public int Id { get; set; }
    public int ContainerId { get; set; }
    public CargoContainer Container { get; set; } = null!;
    public DateTime Date { get; set; } = DateTime.Now;
    public string Category { get; set; } = "Other";
    public decimal Amount { get; set; }
    public string? Notes { get; set; }
}

public class Supplier
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Notes { get; set; }

    public List<CargoContainer> Containers { get; set; } = new();
    public List<SupplierPayment> Payments { get; set; } = new();

    public override string ToString() => Name;
}

public class SupplierPayment
{
    public int Id { get; set; }
    public int SupplierId { get; set; }
    public Supplier Supplier { get; set; } = null!;
    public DateTime Date { get; set; } = DateTime.Now;
    public decimal Amount { get; set; }
    public string Method { get; set; } = "TT";
    public string? Notes { get; set; }
    public int? ContainerId { get; set; }
    public CargoContainer? Container { get; set; }
}

public class StockAdjustment
{
    public int Id { get; set; }
    public int ContainerItemId { get; set; }
    public ContainerItem ContainerItem { get; set; } = null!;
    public DateTime Date { get; set; } = DateTime.Now;
    public decimal QuantityBefore { get; set; }
    public decimal QuantityAfter { get; set; }
    public string Reason { get; set; } = "";
}

public class CashMovement
{
    public int Id { get; set; }
    public DateTime Date { get; set; } = DateTime.Now;
    public string Direction { get; set; } = "Out";
    public string Method { get; set; } = "Cash";
    public decimal Amount { get; set; }
    public string? Notes { get; set; }
}

public class ShopExpense
{
    public int Id { get; set; }
    public DateTime Date { get; set; } = DateTime.Now;
    public string Description { get; set; } = "";
    public decimal Amount { get; set; }
    public string? Notes { get; set; }
}
