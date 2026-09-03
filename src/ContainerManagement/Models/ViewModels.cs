namespace ContainerManagement.Models;

public static class Money
{
    public static string Pkr(decimal value) => "Rs " + Num(value);

    public static string PkrCompact(decimal value)
    {
        var abs = Math.Abs(value);
        if (abs >= 10_000_000m)
            return "Rs " + Num(value / 10_000_000m) + " Cr";
        if (abs >= 100_000m)
            return "Rs " + Num(value / 100_000m) + " L";
        return Pkr(value);
    }

    public static string Qty(decimal value) => Num(value);

    private static string Num(decimal value)
    {
        var n = decimal.Round(value, 2, MidpointRounding.AwayFromZero);
        if (n == decimal.Truncate(n))
            return n.ToString("N0");
        return n.ToString("N2").TrimEnd('0').TrimEnd('.');
    }
}

public class DashboardVm
{
    public int OpenContainers { get; set; }
    public int TotalContainers { get; set; }
    public decimal InventoryValue { get; set; }
    public decimal MoneyInMarket { get; set; }
    public decimal TotalProfit { get; set; }
    public decimal TotalRevenue { get; set; }
    public decimal TotalExpenses { get; set; }
    public int CustomerCount { get; set; }
    public int SalesThisMonth { get; set; }
    public int LowStockCount { get; set; }
    public string LowStockHint { get; set; } = "";
    public List<ReceivableRow> TopReceivables { get; set; } = new();
    public List<ContainerProfitRow> ContainerProfits { get; set; } = new();
    public List<Sale> RecentSales { get; set; } = new();
    public List<InventoryRow> LowStockItems { get; set; } = new();
    public List<AttentionInvoiceRow> UnpaidInvoices { get; set; } = new();
    public int UnpaidCount { get; set; }
    public decimal UnpaidTotal { get; set; }
}

public class AttentionInvoiceRow
{
    public int SaleId { get; set; }
    public int CustomerId { get; set; }
    public string CustomerName { get; set; } = "";
    public DateTime Date { get; set; }
    public decimal Remaining { get; set; }
    public string DateText => Date.ToString("dd MMM yyyy");
    public string RemainingText => Money.Pkr(Remaining);
    public string BillText => "#" + SaleId;
}

public class ContainerProfitRow
{
    public int ContainerId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? ContainerNumber { get; set; }
    public string Origin { get; set; } = "";
    public DateTime? ArrivalDate { get; set; }
    public ContainerStatus Status { get; set; }
    public decimal Revenue { get; set; }
    public decimal Cogs { get; set; }
    public decimal Expenses { get; set; }
    public decimal Profit { get; set; }
    public decimal RemainingValue { get; set; }
    public decimal RemainingQty { get; set; }
    public decimal QtySold { get; set; }
    public decimal QtyReceived { get; set; }
    public string StatusText => Status == ContainerStatus.Open ? "Open" : "Closed";
    public string ArrivalText => ArrivalDate?.ToString("dd MMM yyyy") ?? "—";
    public string ProfitText => Money.Pkr(Profit);
    public string RemainingValueText => Money.Pkr(RemainingValue);
    public string RevenueText => Money.Pkr(Revenue);
    public string ExpensesText => Money.Pkr(Expenses);
    public string CogsText => Money.Pkr(Cogs);
    public string QtySoldText => Money.Qty(QtySold);
    public string SoldAmountText => Money.Pkr(Revenue);
    public override string ToString() => string.IsNullOrWhiteSpace(Title) ? "Container" : Title;
}

public class InventoryRow
{
    public int ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string? Sku { get; set; }
    public string Unit { get; set; } = "pcs";
    public decimal TotalRemaining { get; set; }
    public decimal TotalValue { get; set; }
    public bool IsLow { get; set; }
    public List<InventoryLot> Lots { get; set; } = new();
    public string InStockText => Money.Qty(TotalRemaining) + (IsLow ? "  low" : "");
    public string ValueText => Money.Pkr(TotalValue);
    public string SkuText => string.IsNullOrWhiteSpace(Sku) ? "—" : Sku;
    public string LotsText =>
        Lots.Count switch
        {
            0 => "—",
            1 => $"{Lots[0].ContainerTitle} ({Money.Qty(Lots[0].Remaining)})",
            _ => Lots.Count + " containers"
        };
}

public class InventoryLot
{
    public int ContainerId { get; set; }
    public string ContainerTitle { get; set; } = string.Empty;
    public int ContainerItemId { get; set; }
    public decimal Remaining { get; set; }
    public decimal Received { get; set; }
    public decimal UnitCost { get; set; }
    public decimal LandedCost { get; set; }
    public bool NeverSold { get; set; }
    public decimal Value => Remaining * UnitCost;
    public string RemainingText => Money.Qty(Remaining);
    public string ValueText => Money.Pkr(Value);
}

public class ReceivableRow
{
    public int CustomerId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public decimal Balance { get; set; }
    public DateTime? LastSale { get; set; }
    public DateTime? LastPayment { get; set; }
    public DateTime? OldestDue { get; set; }
    public string Aging { get; set; } = "—";
    public string BalanceText => Money.Pkr(Balance);
    public string LastSaleText => LastSale?.ToString("dd MMM yyyy") ?? "—";
    public string LastPaymentText => LastPayment?.ToString("dd MMM yyyy") ?? "—";
    public string OldestDueText => OldestDue?.ToString("dd MMM yyyy") ?? "—";
}

public class LedgerRow
{
    public int Id { get; set; }
    public DateTime Date { get; set; }
    public LedgerType Type { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal Debit { get; set; }
    public decimal Credit { get; set; }
    public decimal RunningBalance { get; set; }
    public int? SaleId { get; set; }
    public int? PaymentId { get; set; }
    public string DateText => Date.ToString("dd MMM yyyy");
    public string SoldText => Debit == 0 ? "—" : Money.Pkr(Debit);
    public string ReturnedText => Type == LedgerType.Return && Credit != 0 ? Money.Pkr(Credit) : "—";
    public string ReceivedText => Type == LedgerType.Return || Credit == 0 ? "—" : Money.Pkr(Credit);
    public string DebitText => SoldText;
    public string CreditText => ReceivedText;
    public string RunningText => Money.Pkr(RunningBalance);
}

public class StockOption
{
    public int ContainerItemId { get; set; }
    public int ContainerId { get; set; }
    public string ContainerTitle { get; set; } = string.Empty;
    public int ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string? Sku { get; set; }
    public string Unit { get; set; } = "pcs";
    public decimal Remaining { get; set; }
    public decimal UnitCost { get; set; }
    public decimal LandedCost { get; set; }
    public decimal? LastSalePrice { get; set; }

    public decimal SellCost => UnitCost;

    public string SearchLabel
    {
        get
        {
            var sku = string.IsNullOrWhiteSpace(Sku) ? "" : $" [{Sku}]";
            return $"{ProductName}{sku}  ·  {ContainerTitle}  ·  {Money.Qty(Remaining)} {Unit}";
        }
    }

    public override string ToString() => ProductName;
}

public class SaleReturnInput
{
    public int SaleLineId { get; set; }
    public decimal Quantity { get; set; }
}

public class NewSaleLineInput
{
    public int ContainerId { get; set; }
    public int ContainerItemId { get; set; }
    public int ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string ContainerTitle { get; set; } = string.Empty;
    public string Unit { get; set; } = "pcs";
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal UnitCost { get; set; }
    public decimal Remaining { get; set; }
    public decimal LineTotal => Quantity * UnitPrice;
}

public class ItemProfitRow
{
    public string ProductName { get; set; } = "";
    public string? Sku { get; set; }
    public decimal QtySold { get; set; }
    public decimal Revenue { get; set; }
    public decimal Cogs { get; set; }
    public decimal Profit { get; set; }
    public string QtyText => Money.Qty(QtySold);
    public string RevenueText => Money.Pkr(Revenue);
    public string ProfitText => Money.Pkr(Profit);
}

public class SoldProductOption
{
    public int ProductId { get; set; }
    public string Name { get; set; } = "";
    public string? Sku { get; set; }
    public string Unit { get; set; } = "pcs";
    public decimal QtySold { get; set; }

    public string SearchLabel
    {
        get
        {
            var sku = string.IsNullOrWhiteSpace(Sku) ? "" : $" [{Sku}]";
            return $"{Name}{sku}  ·  sold {Money.Qty(QtySold)} {Unit}";
        }
    }

    public override string ToString() => Name;
}

public class ItemCustomerSaleRow
{
    public int CustomerId { get; set; }
    public string CustomerName { get; set; } = "";
    public decimal Qty { get; set; }
    public decimal AvgCost { get; set; }
    public decimal AvgPrice { get; set; }
    public decimal Amount { get; set; }
    public string QtyText => Money.Qty(Qty);
    public string CostText => Money.Pkr(AvgCost);
    public string PriceText => Money.Pkr(AvgPrice);
    public string AmountText => Money.Pkr(Amount);
}

public class BestSellerRow
{
    public string ProductName { get; set; } = "";
    public decimal Qty { get; set; }
    public decimal Revenue { get; set; }
    public string QtyText => Money.Qty(Qty);
    public string RevenueText => Money.Pkr(Revenue);
}

public class DailySummaryRow
{
    public DateTime Date { get; set; }
    public int Bills { get; set; }
    public decimal Sales { get; set; }
    public decimal CashIn { get; set; }
    public decimal Credit { get; set; }
    public string DateText => Date.ToString("dd MMM yyyy");
    public string SalesText => Money.Pkr(Sales);
    public string CashInText => Money.Pkr(CashIn);
    public string CreditText => Money.Pkr(Credit);
}

public class HomeDayRow
{
    public DateTime Date { get; set; }
    public decimal Sales { get; set; }
    public decimal Profit { get; set; }
    public string DateText => Date.ToString("dd MMM yyyy");
    public string SalesText => Money.Pkr(Sales);
    public string ProfitText => Money.Pkr(Profit);
}

public class CashBookRow
{
    public string When { get; set; } = "";
    public string What { get; set; } = "";
    public string Method { get; set; } = "";
    public string InText { get; set; } = "—";
    public string OutText { get; set; } = "—";
}

public class UnpaidInvoice
{
    public int SaleId { get; set; }
    public string Label { get; set; } = "";
    public decimal Remaining { get; set; }
    public override string ToString() => Label;
}

public class CloudBackupInfo
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Folder { get; set; } = "";
    public string WhenText { get; set; } = "";
    public string SizeText { get; set; } = "";
    public string LocalPath { get; set; } = "";
    public bool IsLocalFolder { get; set; }
}

public static class ExpenseCategories
{
    public static readonly string[] All =
    [
        "Sea Freight",
        "Customs Duty",
        "Clearing & Forwarding",
        "Local Transport",
        "Labour",
        "Warehouse",
        "Insurance",
        "Other"
    ];
}

public static class PaymentMethods
{
    public static readonly string[] All =
    [
        "Cash",
        "Bank Transfer",
        "JazzCash",
        "EasyPaisa",
        "Cheque",
        "Other"
    ];
}

public static class Units
{
    public static readonly string[] All =
    [
        "pcs",
        "carton",
        "set",
        "pair",
        "dozen",
        "kg",
        "roll",
        "box"
    ];
}

public static class Currencies
{
    public static readonly string[] All = ["PKR", "CNY", "USD"];
}

public static class SupplierPayMethods
{
    public static readonly string[] All = ["TT", "LC", "Cash", "Bank Transfer", "Other"];
}
