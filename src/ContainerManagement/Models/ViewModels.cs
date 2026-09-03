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

    public static string Yen(decimal value) => "\u00a5" + Num(value);

    public static string Pct(decimal value)
    {
        var n = decimal.Round(value, 1, MidpointRounding.AwayFromZero);
        if (n == decimal.Truncate(n))
            return n.ToString("N0") + "%";
        return n.ToString("N1") + "%";
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
    public static readonly string[] All = ["PKR", "JPY", "CNY", "USD"];
}

public static class SupplierPayMethods
{
    public static readonly string[] All = ["TT", "LC", "Cash", "Bank Transfer", "Other"];
}

/// <summary>
/// One item row on a buy plan. YenRate is set by the page so the rupee columns follow
/// the plan's rate — change the rate and every row is rebuilt with it.
/// </summary>
public class BuyPlanLineRow
{
    public int Id { get; set; }
    public string ItemName { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal UnitCostYen { get; set; }
    public decimal UnitWeightKg { get; set; }
    public decimal SalePricePkr { get; set; }
    public string? Notes { get; set; }
    public decimal YenRate { get; set; } = 1;

    /// <summary>Quantity x cost in yen.</summary>
    public decimal CostYen => Quantity * UnitCostYen;

    /// <summary>The same cost in rupees at this plan's rate.</summary>
    public decimal CostPkr => Math.Round(CostYen * YenRate, 2);

    /// <summary>What the whole line would bring if it all sold at that price.</summary>
    public decimal SalePkr => Quantity * SalePricePkr;

    public decimal TotalWeightKg => Quantity * UnitWeightKg;

    /// <summary>Sale total minus this line's goods cost. Plan expenses are taken off at the plan level.</summary>
    public decimal ProfitPkr => SalePkr - CostPkr;

    public decimal CostPerPiecePkr => UnitCostYen * YenRate;

    public decimal MarginPct => SalePkr > 0.009m ? ProfitPkr / SalePkr * 100m : 0m;

    public string ItemNameText => string.IsNullOrWhiteSpace(ItemName) ? "(no name)" : ItemName.Trim();
    public string QuantityText => Money.Qty(Quantity);
    public string UnitCostYenText => Money.Yen(UnitCostYen);
    public string CostYenText => Money.Yen(CostYen);
    public string CostPkrText => Money.Pkr(CostPkr);
    public string CostPerPiecePkrText => Money.Pkr(CostPerPiecePkr);
    public string UnitWeightText => Money.Qty(UnitWeightKg);
    public string TotalWeightText => Money.Qty(TotalWeightKg);
    public string SalePriceText => Money.Pkr(SalePricePkr);
    public string SaleTotalText => Money.Pkr(SalePkr);
    public string ProfitText => Money.Pkr(ProfitPkr);
    public string MarginText => Money.Pct(MarginPct);
    public string NotesText => Notes ?? "";
    public bool ProfitIsGood => ProfitPkr >= 0;

    public BuyPlanLineInput ToInput() => new()
    {
        ItemName = ItemName,
        Quantity = Quantity,
        UnitCostYen = UnitCostYen,
        UnitWeightKg = UnitWeightKg,
        SalePricePkr = SalePricePkr,
        Notes = Notes
    };
}

/// <summary>What a plan adds up to. Built the same way for the list, the editor and the save.</summary>
public class BuyPlanTotal
{
    public int ItemCount { get; set; }
    public decimal CostYen { get; set; }
    public decimal CostPkr { get; set; }
    public decimal ExpensePkr { get; set; }
    public decimal SalePkr { get; set; }
    public decimal TotalWeightKg { get; set; }
    public decimal YenRate { get; set; } = 1;

    /// <summary>Goods cost plus the one expense figure — what goes in.</summary>
    public decimal SpendPkr => CostPkr + ExpensePkr;

    /// <summary>Sold everything, minus what went in.</summary>
    public decimal ProfitPkr => SalePkr - SpendPkr;

    public decimal ProfitYen => YenRate > 0.000001m ? Math.Round(ProfitPkr / YenRate, 0) : 0;

    public decimal MarginPct => SalePkr > 0.009m ? ProfitPkr / SalePkr * 100m : 0m;

    public bool ProfitIsGood => ProfitPkr >= 0;

    public string ItemCountText => ItemCount == 1 ? "1 item" : ItemCount + " items";
    public string CostYenText => Money.Yen(CostYen);
    public string CostPkrText => Money.Pkr(CostPkr);
    public string ExpenseText => Money.Pkr(ExpensePkr);
    public string SpendText => Money.Pkr(SpendPkr);
    public string SaleText => Money.Pkr(SalePkr);
    public string ProfitText => Money.Pkr(ProfitPkr);
    public string ProfitYenText => Money.Yen(ProfitYen);
    public string MarginText => Money.Pct(MarginPct);
    public string WeightText => Money.Qty(TotalWeightKg) + " kg";
    public string RateText => Money.Pkr(YenRate);

    public static BuyPlanTotal Build(IEnumerable<BuyPlanLineRow> lines, decimal yenRate, decimal expensePkr)
    {
        var rate = yenRate > 0 ? yenRate : 1;
        var list = lines.ToList();
        return new BuyPlanTotal
        {
            ItemCount = list.Count,
            CostYen = list.Sum(l => l.CostYen),
            CostPkr = list.Sum(l => l.CostPkr),
            ExpensePkr = expensePkr,
            SalePkr = list.Sum(l => l.SalePkr),
            TotalWeightKg = list.Sum(l => l.TotalWeightKg),
            YenRate = rate
        };
    }
}

/// <summary>A plan as the pages see it: header, rows, and the totals box.</summary>
public class BuyPlanRow
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Supplier { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public decimal YenRate { get; set; } = 1;
    public decimal ExpensePkr { get; set; }
    public List<BuyPlanLineRow> Lines { get; set; } = new();
    public BuyPlanTotal Total { get; set; } = new();

    public string TitleText => string.IsNullOrWhiteSpace(Title) ? "Untitled plan" : Title.Trim();
    public string SupplierText => string.IsNullOrWhiteSpace(Supplier) ? "—" : Supplier.Trim();
    public string CreatedText => CreatedAt.ToString("dd MMM yyyy");
    public string ItemCountText => Total.ItemCountText;
    public string CostYenText => Total.CostYenText;
    public string CostPkrText => Total.CostPkrText;
    public string ExpenseText => Total.ExpenseText;
    public string SpendText => Total.SpendText;
    public string SaleText => Total.SaleText;
    public string ProfitText => Total.ProfitText;
    public string MarginText => Total.MarginText;
    public string WeightText => Total.WeightText;
    public bool ProfitIsGood => Total.ProfitIsGood;

    public void RefreshTotals()
    {
        foreach (var l in Lines)
            l.YenRate = YenRate > 0 ? YenRate : 1;
        Total = BuyPlanTotal.Build(Lines, YenRate, ExpensePkr);
    }
}

/// <summary>A row as the page hands it to the save.</summary>
public class BuyPlanLineInput
{
    public string ItemName { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal UnitCostYen { get; set; }
    public decimal UnitWeightKg { get; set; }
    public decimal SalePricePkr { get; set; }
    public string? Notes { get; set; }
}
