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
    Return = 4,
    /// <summary>Money the shop handed over to the customer, because their book was in their favour.</summary>
    Payout = 5
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
    /// <summary>The cost price as it was written on the invoice, in CostCurrency. Kept next to the rupee
    /// figure rather than instead of it, so a supplier's bill can be checked against the entry forever.</summary>
    public decimal ForeignCost { get; set; }

    /// <summary>Which currency the cost price was typed in. A rate is not applied twice in this book - the
    /// rupee figure is fixed when the item is saved - so the currency has to be recorded, not guessed at
    /// from whether the two figures happen to differ.</summary>
    public string CostCurrency { get; set; } = "PKR";

    /// <summary>The rate a yen cost price was multiplied by, kept on the item so the rupee figure can be
    /// re-derived from the yen one. Null on an item priced in rupees.</summary>
    public decimal? CostRate { get; set; }

    /// <summary>The cost the shop typed, in the currency it was typed in - which is what the item form
    /// shows back, so editing an item's name never has a converted rupee figure dropped into its price box.</summary>
    public decimal CostEntered => CostCurrency == "JPY" ? ForeignCost : UnitCost;

    public decimal LandedUnitCost { get; set; }
    public decimal? Cartons { get; set; }
    public decimal? Cbm { get; set; }
    /// <summary>What one piece weighs, in kilograms - the same figure the order sheet asks for, and the
    /// one written on the carton. The container's freight is shared out over what the lot weighs in all,
    /// which is this times how many were received. Null means it has not been weighed, which stops the
    /// sharing for the whole container rather than guessing at it.</summary>
    public decimal? WeightKg { get; set; }
    public string? PhotoPath { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public List<SaleLine> SaleLines { get; set; } = new();

    /// <summary>
    /// What the piece actually cost: the price of the goods plus this item's share of the container's
    /// expenses, which are shared out by weight (see InventoryService.SplitExpense). Every cost figure in the app - a
    /// sold line's cost, what stock left in the store is worth, profit - reads this and not UnitCost, so
    /// freight and customs are in the cost of the goods rather than a number sitting beside them.
    /// LandedUnitCost is written by that one method, from UnitCost, every time an expense or a weight
    /// changes: it is never added to, so no amount can be shared out twice.
    /// </summary>
    public decimal EffectiveCost => LandedUnitCost > 0 ? LandedUnitCost : UnitCost;

    /// <summary>The freight and customs carried by one piece, on its own - what the cost column shows as
    /// "of which freight", and the difference a shop can check: it is the item's share of the container's
    /// expenses divided by how many pieces that share was bought for.</summary>
    public decimal CostEachFreight => LandedUnitCost > 0 ? LandedUnitCost - UnitCost : 0m;
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

    /// <summary>
    /// A line's money, rounded once here: the invoice, the customer's balance and the profit figures
    /// all read these, so the bill that is printed is the bill that is stored.
    /// </summary>
    public decimal LineTotal => Money.Round(Quantity * UnitPrice);
    public decimal LineCost => Money.Round(Quantity * UnitCost);
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

/// <summary>
/// Money the shop paid out to a customer - an advance they no longer want back as goods, or a refund their
/// ledger left owing them. Deliberately not a Payment with a negative amount: Payment means money the till
/// received, and every page that adds payments up would then have to subtract a sign it cannot see. The
/// payout keeps its own row, and its own line in the customer's book and in the till.
/// </summary>
public class CustomerPayout
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;
    public DateTime Date { get; set; } = DateTime.Now;
    public decimal Amount { get; set; }
    public string Method { get; set; } = "Cash";
    public string? Notes { get; set; }
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
    public int? PayoutId { get; set; }
}

public class ContainerExpense
{
    public int Id { get; set; }
    public int ContainerId { get; set; }
    public CargoContainer Container { get; set; } = null!;
    public DateTime Date { get; set; } = DateTime.Now;
    public string Category { get; set; } = "Other";

    /// <summary>Always Pakistani rupees - what the books, the container's total and each item's cost use -
    /// whether the line was written in rupees or in yen.</summary>
    public decimal Amount { get; set; }

    /// <summary>The currency the figure was written in, so the shop can keep its own paperwork's number on
    /// the line instead of only the conversion of it.</summary>
    public string Currency { get; set; } = "PKR";

    /// <summary>The amount as typed, in Currency. Zero on a line written in rupees.</summary>
    public decimal AmountForeign { get; set; }

    /// <summary>The yen rate this line was converted at. Kept on the line rather than read from the
    /// container, because a rate changed next month must not re-value an expense already paid; the figure
    /// on paper and the figure in the cost are then the same figure forever.</summary>
    public decimal? RateUsed { get; set; }

    public string? Notes { get; set; }

    /// <summary>How a rupee total was arrived at, for the line itself: a yen expense shows the yen figure
    /// and the rate beside it, so nobody has to trust the conversion after the day it was written.</summary>
    public string SourceText => Currency != "PKR" && AmountForeign > 0m && RateUsed is decimal rate
        ? Money.Yen(AmountForeign) + " at " + Currencies.RateText(rate) + " = " + Money.Pkr(Amount)
        : "";
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

/// <summary>
/// A paper-order sheet kept before the goods are bought: what will be bought from China,
/// in yen, with the sale price per piece. It never touches stock — it is only a plan.
/// </summary>
public class BuyPlan
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    /// <summary>Rupees for 1 yen. Used to turn the yen cost into a rupee cost.</summary>
    public decimal YenRate { get; set; } = 1;

    /// <summary>One total for freight, customs, clearing, labour — in rupees.</summary>
    public decimal ExpensePkr { get; set; }

    public List<BuyPlanLine> Lines { get; set; } = new();

    public override string ToString() => Title;
}

public class BuyPlanLine
{
    public int Id { get; set; }
    public int PlanId { get; set; }
    public BuyPlan Plan { get; set; } = null!;
    public string ItemName { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal UnitCostYen { get; set; }
    public decimal UnitWeightKg { get; set; }
    public decimal SalePricePkr { get; set; }
}

public enum CashBookKind
{
    Opening = 0,
    CustomerIn = 1,
    SupplierOut = 2,
    ExpenseOut = 3,
    RefundOut = 4,
    /// <summary>Cash given to a customer to settle what their own ledger says we are holding.</summary>
    CustomerOut = 5
}

public class CashBookEntry
{
    public int Id { get; set; }
    public DateTime Date { get; set; } = DateTime.Now;
    public CashBookKind Kind { get; set; }
    public string Description { get; set; } = "";
    public decimal AmountIn { get; set; }
    public decimal AmountOut { get; set; }
    public int? PaymentId { get; set; }
    public int? SupplierPaymentId { get; set; }
    public int? ShopExpenseId { get; set; }
    public int? SaleId { get; set; }
    public int? PayoutId { get; set; }
}
