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

    /// <summary>
    /// Quantities in a message, up to three decimals. Money.Qty's two would tell someone that 0.375 kg
    /// of stock is "0.38 left" - they then type 0.38 and the app refuses them, which reads like a bug.
    /// </summary>
    public static string Qty3(decimal value) => Kg(value);

    /// <summary>
    /// Weights get three decimals: a 55 g piece must read 0.055, not 0.06, or the row no longer
    /// multiplies out to the total next to it.
    /// </summary>
    public static string Kg(decimal value)
    {
        var n = decimal.Round(value, 3, MidpointRounding.AwayFromZero);
        if (n == decimal.Truncate(n))
            return n.ToString("N0");
        return n.ToString("N3").TrimEnd('0').TrimEnd('.');
    }

    /// <summary>
    /// A figure in the units a shop says out loud, for reading a typed amount back to yourself:
    /// 1573250 becomes "15 lac 73 thousand 250". Breakdown rather than one scale, because "15.7 lac"
    /// still hides whether that was fifteen point seven or a stray zero. Under a thousand returns
    /// nothing, and so does an unset box.
    /// </summary>
    public static string Words(decimal value)
    {
        var whole = decimal.Truncate(decimal.Abs(value));
        if (whole < 1000)
            return "";
        var sign = value < 0 ? "-" : "";
        if (whole > long.MaxValue)
            return sign + "over 9 kharb";

        var rest = (long)whole;
        var parts = new List<string>();
        foreach (var (size, name) in Scale)
        {
            if (rest < size)
                continue;
            parts.Add(rest / size + " " + name);
            rest %= size;
        }
        if (rest > 0)
            parts.Add(rest.ToString());
        return parts.Count == 0 ? "" : sign + string.Join(" ", parts);
    }

    /// <summary>South Asian numbering: two digits per step, thousand, lac, crore, arab, kharb.</summary>
    private static readonly (long Size, string Name)[] Scale =
    [
        (100_000_000_000, "kharb"),
        (1_000_000_000, "arab"),
        (10_000_000, "crore"),
        (100_000, "lac"),
        (1_000, "thousand")
    ];

    /// <summary>
    /// The app's one rounding rule: half a paisa goes away from zero, the way a bill is written.
    /// Math.Round's default is banker's rounding - it sends 267.525 to 267.52 - so two screens can
    /// disagree about the same figure by a paisa and nothing in the books explains which is right.
    /// Every money value entering or leaving the app goes through here, so what is printed is what
    /// is stored and what the totals add up to.
    /// </summary>
    public static decimal Round(decimal value, int decimals = 2) => decimal.Round(value, decimals, MidpointRounding.AwayFromZero);

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
    /// <summary>The lot's stock at the landed cost, so the lines under an item's Value row add back up to
    /// that row: goods price plus what the shipment's freight and customs added per piece, which is the
    /// same figure the container page shows and the same one a sale would be costed at.</summary>
    public decimal Value => Remaining * LandedCost;
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

/// <summary>
/// A customer the shop is holding money for, worked out from their own ledger: negative balance, so the
/// figure the We Owe page lists. Owed is never below zero, and a customer with a payout on record but
/// nothing left owing still appears, with Owed at zero, so the money already handed over can be seen
/// where it was paid.
/// </summary>
public class CustomerOwedRow
{
    public int CustomerId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public decimal Owed { get; set; }
    public decimal PaidOut { get; set; }
    public string OwedText => Owed > 0.009m ? Money.Pkr(Owed) : "Settled";
    public string PaidOutText => PaidOut > 0.009m ? Money.Pkr(PaidOut) : "—";
    public string Label => Name + " · " + (Owed > 0.009m ? "owe " + Money.Pkr(Owed) : "settled");
    public override string ToString() => Label;
}

/// <summary>
/// One month of the till, as the year statement lists it. In and Out are cash; "goods back" is what
/// customers took back in value that month and is added to neither - the Main ledger page carries it
/// beside the money for the same reason, because a return on a bill nobody paid moved no cash at all.
/// Closing counts the years before this one as well, which is the only way December's figure is the money
/// in hand rather than a year of movement.
/// </summary>
public class TillYearRow
{
    public int Month { get; set; }
    public bool IsTotal { get; set; }
    public string? Label { get; set; }

    /// <summary>Carried on the row rather than worked out by the page, so the tint and the weight a total
    /// needs are one decision in one place, and the table of months and the printed statement agree.</summary>
    public bool Bold => IsTotal;
    public bool Tint => IsTotal;
    public decimal CashIn { get; set; }
    public decimal CashOut { get; set; }
    public decimal Returns { get; set; }
    public decimal Closing { get; set; }
    public string MonthText => Label ?? MonthName(Month);
    public string InText => CashIn == 0 ? "\u2014" : Money.Pkr(CashIn);
    public string OutText => CashOut == 0 ? "\u2014" : Money.Pkr(CashOut);
    public string NetText => Money.Pkr(CashIn - CashOut);
    public string ReturnsText => Returns == 0 ? "\u2014" : Money.Pkr(Returns);
    public string ClosingText => Money.Pkr(Closing);
    internal static string MonthName(int month)
        => new DateTime(2000, Math.Min(12, Math.Max(1, month)), 1).ToString("MMMM");

    /// <summary>The year's own line at the foot of the table, so the twelve months add up where the reader
    /// can see it and the page needs no sentence saying what they add up to. One builder for the screen and
    /// the paper both - a total worked out twice is a total that can disagree with itself.
    /// Month stays 12 as a fallback only: the line is named by Label, and a total that had to borrow a
    /// month's name would read as a thirteenth December rather than as what it is.</summary>
    public static TillYearRow Totals(int year, IReadOnlyList<TillYearRow> rows)
    {
        var months = rows.Where(r => !r.IsTotal).ToList();
        return new TillYearRow
        {
            Month = 12,
            IsTotal = true,
            Label = "Total " + year,
            CashIn = Money.Round(months.Sum(r => r.CashIn)),
            CashOut = Money.Round(months.Sum(r => r.CashOut)),
            Returns = Money.Round(months.Sum(r => r.Returns)),
            Closing = months.Count > 0 ? months[^1].Closing : 0m
        };
    }
}

/// <summary>
/// One month of selling, on the rule Home already uses: a bill is its total after the discount, shared
/// over its lines, and a return comes off the month the goods walked back rather than the month of the bill
/// it undoes. Profit is sold less what those goods cost. Shop costs are not folded in - they keep their own
/// months further down the same statement, which is how the app has always separated the two.
/// </summary>
public class SalesYearRow
{
    public int Month { get; set; }
    public bool IsTotal { get; set; }
    public string? Label { get; set; }
    public bool Bold => IsTotal;
    public bool Tint => IsTotal;
    public int Bills { get; set; }
    public decimal Sold { get; set; }
    public decimal Cogs { get; set; }
    public decimal Received { get; set; }
    public decimal Returned { get; set; }
    public decimal StillOwed { get; set; }
    public decimal Profit => Money.Round(Sold - Cogs);
    public string MonthText => Label ?? TillYearRow.MonthName(Month);
    public string BillsText => Bills == 0 ? "\u2014" : Bills.ToString();
    public string SoldText => Sold == 0 ? "\u2014" : Money.Pkr(Sold);
    public string ReceivedText => Received == 0 ? "\u2014" : Money.Pkr(Received);
    public string ReturnedText => Returned == 0 ? "\u2014" : Money.Pkr(Returned);
    public string StillOwedText => StillOwed == 0 ? "\u2014" : Money.Pkr(StillOwed);
    public string ProfitText => Sold == 0 && Cogs == 0 ? "\u2014" : Money.Pkr(Profit);

    /// <summary>The year's line under the months. Profit is the year's sold money less the year's cost,
    /// which is the same figure as the twelve months' profits added together - so the column at the foot
    /// can be checked against the column above it, paisa for paisa.</summary>
    public static SalesYearRow Totals(int year, IReadOnlyList<SalesYearRow> rows)
    {
        var months = rows.Where(r => !r.IsTotal).ToList();
        return new SalesYearRow
        {
            Month = 12,
            IsTotal = true,
            Label = "Total " + year,
            Bills = months.Sum(r => r.Bills),
            Sold = Money.Round(months.Sum(r => r.Sold)),
            Cogs = Money.Round(months.Sum(r => r.Cogs)),
            Received = Money.Round(months.Sum(r => r.Received)),
            Returned = Money.Round(months.Sum(r => r.Returned)),
            StillOwed = Money.Round(months.Sum(r => r.StillOwed))
        };
    }
}

/// <summary>One month of shop costs. The count travels with the figure so an empty month cannot be
/// mistaken for a month whose costs were never typed in.</summary>
public class ExpenseYearRow
{
    public int Month { get; set; }
    public bool IsTotal { get; set; }
    public string? Label { get; set; }
    public bool Bold => IsTotal;
    public bool Tint => IsTotal;
    public int Count { get; set; }
    public decimal Amount { get; set; }
    public string MonthText => Label ?? TillYearRow.MonthName(Month);
    public string CountText => Count == 0 ? "\u2014" : Count.ToString();
    public string AmountText => Amount == 0 ? "\u2014" : Money.Pkr(Amount);

    /// <summary>The year's line under its months, with the count of entries beside the money.</summary>
    public static ExpenseYearRow Totals(int year, IReadOnlyList<ExpenseYearRow> rows)
    {
        var months = rows.Where(r => !r.IsTotal).ToList();
        return new ExpenseYearRow
        {
            Month = 12,
            IsTotal = true,
            Label = "Total " + year,
            Count = months.Sum(r => r.Count),
            Amount = Money.Round(months.Sum(r => r.Amount))
        };
    }
}

/// <summary>One payout the shop made to a customer, as the We Owe page lists them: newest first.</summary>
public class CustomerPayoutRow
{
    public int CustomerId { get; set; }
    public DateTime Date { get; set; }
    public string Method { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string? Notes { get; set; }
    public string DateText => Date.ToString("dd MMM yyyy");
    public string AmountText => Money.Pkr(Amount);
    public string NoteText => string.IsNullOrWhiteSpace(Notes) ? "—" : Notes!.Trim();
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

    /// <summary>Which line this is in the order the money moved, so a day of entries reads as a sequence.</summary>
    public int Step { get; set; }

    public int? SaleId { get; set; }
    public int? PaymentId { get; set; }
    public string DateText => Date.ToString("dd MMM yyyy");
    /// <summary>
    /// A debit that handed money over rather than billing them: the cash half of a return, or a payout made
    /// on the We Owe page. Their book has to carry it or their balance would lie, but it is not a sale, and
    /// printing it under "Sold" would tell a customer they were charged for money they were given.
    /// </summary>
    public bool IsPaidOut => Debit > 0 && Type is LedgerType.Payout or LedgerType.Adjustment;
    public string SoldText => Debit == 0 || IsPaidOut ? "—" : Money.Pkr(Debit);
    public string PaidOutText => IsPaidOut ? Money.Pkr(Debit) : "—";
    public string ReturnedText => Type == LedgerType.Return && Credit != 0 ? Money.Pkr(Credit) : "—";
    public string ReceivedText => Type == LedgerType.Return || Credit == 0 ? "—" : Money.Pkr(Credit);
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

    /// <summary>The cost a piece is sold against: what it was bought for, plus the freight and customs
    /// shared onto it by weight. This is the figure the sale page shows and the figure the bill will be
    /// costed at, so a shop pricing a piece out loud is pricing it against the whole cost of landing it -
    /// and the suggested price is built on it for the same reason.</summary>
    public decimal SellCost => LandedCost > 0 ? LandedCost : UnitCost;

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
    /// <summary>Rounded where it is defined, so the bill and the ledger add up to the same paisa.</summary>
    public decimal LineTotal => Money.Round(Quantity * UnitPrice);
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

/// <summary>
/// How a container's expenses fall on its items: the rupee total to be shared, the weight it is shared over,
/// the rate per kilogram, and each item's share. It is calculated once, in InventoryService, and both the
/// words on the container page and the cost written into every item come out of it - a share worked out
/// twice is a share that eventually disagrees with itself.
/// </summary>
public class ExpenseSplit
{
    public decimal ExpenseTotal { get; set; }
    public decimal TotalWeightKg { get; set; }
    public decimal PerKg { get; set; }
    public int UnweighedItems { get; set; }
    public int Items { get; set; }

    /// <summary>Each item's share of the expenses, in rupees, and the sum of them is ExpenseTotal to the
    /// paisa - the last paisa that will not divide goes on the heaviest item, as a discount's does.</summary>
    public Dictionary<int, decimal> SharePerItem { get; } = new();

    /// <summary>What the per-piece costs actually took on, and the rupees of expense they could not carry
    /// because a cost price is kept to the paisa. Named on the page, never smoothed away.</summary>
    public decimal Absorbed { get; set; }
    public decimal LeftOver { get; set; }

    public bool CanDistribute => ExpenseTotal > 0 && UnweighedItems == 0 && Items > 0 && TotalWeightKg > 0;

    /// <summary>Why the money is not in the costs, in the shop's own words, when it is not - an expense that
    /// cannot be shared out still belongs on the container, and a figure silently left out of the item's
    /// cost is the kind of hole a shop finds a year later.</summary>
    public string Tape
    {
        get
        {
            if (ExpenseTotal <= 0)
                return "";
            if (Items == 0)
                return Money.Pkr(ExpenseTotal) + " of expenses has nothing to sit on: this container has no items yet.";
            if (UnweighedItems > 0)
                return Money.Pkr(ExpenseTotal) + " of expenses is not in the costs: "
                       + UnweighedItems + (UnweighedItems == 1 ? " item has" : " items have")
                       + " no weight. Weigh them and it is shared by weight.";
            if (TotalWeightKg <= 0)
                return Money.Pkr(ExpenseTotal) + " of expenses is not in the costs: no item weighs anything.";
            var text = Money.Pkr(ExpenseTotal) + " over " + Money.Kg(TotalWeightKg) + " kg = "
                       + Money.Pkr(PerKg) + " a kilo, added to each item's cost by what it weighs.";
            if (LeftOver >= 0.005m)
                text += " " + Money.Pkr(LeftOver) + " would not divide into the per-piece costs and stays out of them.";
            else if (LeftOver <= -0.005m)
                text += " The per-piece costs carry " + Money.Pkr(-LeftOver) + " more than the expenses were.";
            return text;
        }
    }
}

/// <summary>A month a customer's receipts can be read for. Year and month are null together for "every
/// month", so one month and the whole book are asked for in the same shape and the page never has to hold a
/// second, unfiltered copy of the list to show a total.</summary>
public class CustomerMonth
{
    public int? Year { get; set; }
    public int? Month { get; set; }
    public string Label { get; set; } = "";
    public override string ToString() => Label;

    public static CustomerMonth All { get; } = new() { Label = "All months" };

    public static CustomerMonth Of(int year, int month)
        => new() { Year = year, Month = month, Label = new DateTime(year, month, 1).ToString("MMM yyyy") };
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

/// <summary>Money on a shipment is written in one of two currencies - the cargo is bought in yen, the shop
/// sells in rupees - and a container can additionally be *labelled* with a currency of its own. The labels
/// and the codes, and the rule for whether a rate is fit to convert with, live here once: the two money
/// forms on the container page and the order sheet all read these numbers, so no screen can end up with a
/// currency list or a rate rule of its own.</summary>
public static class Currencies
{
    /// <summary>The currencies a container can be labelled with on its import details.</summary>
    public static readonly string[] Codes = ["PKR", "JPY", "CNY", "USD"];

    /// <summary>What the money boxes offer, as a shop writes it rather than as a code: the figure typed in
    /// one of these boxes is taken to be in it, and the book keeps the three-letter code.</summary>
    public static readonly string[] EntryLabels = { "Rs (PKR)", "\u00a5 (JPY)" };

    /// <summary>The box shows the currency as a shop writes it; the book keeps the code. One mapping, in
    /// one place, so a label reworded on a form cannot quietly change what a figure is taken to mean.</summary>
    public static string CodeOf(string? shown) => shown is string t && t.Contains("JPY") ? "JPY" : "PKR";

    public static string Shown(string? code) => code == "JPY" ? EntryLabels[1] : EntryLabels[0];

    /// <summary>The rate a yen figure is multiplied by is held to six decimals - the size the order sheet
    /// uses for the same figure - and the multiplication is done with that rounded number, so the rupees on
    /// the line can be re-derived from the yen figure and the rate kept beside it, to the paisa, for as
    /// long as anyone cares to check.</summary>
    public static decimal Rate(decimal value) => Money.Round(value, 6);

    /// <summary>Whether a rate written here is one to convert with. A container sits at 1 until someone
    /// says otherwise, and ¥180,000 read as Rs 180,000 is not a conversion but a mistake - so the unset
    /// sentinel is refused, while a real yen rate (a little over forty paisa to the yen) is what is asked
    /// for and is accepted as it stands.</summary>
    public static bool UsableRate(decimal? rate) => rate is decimal r && r > 0m && r != 1m;

    /// <summary>
    /// A figure in yen, and the rate it is to be taken at, turned into the rupees the book keeps - or null
    /// when there is no rate to convert with. The rupees are multiplied out of the rate as it is *stored*
    /// (six decimals, one rounding, in C# rather than in a floating-point column) so that the yen figure
    /// and the rate kept beside it re-derive the rupee total to the paisa, for as long as anyone cares to
    /// check. Both pages show this same answer before anything is written, so what is read on screen is what
    /// the book keeps, not an approximation of it.
    /// </summary>
    public static (decimal Pkr, decimal Foreign, decimal Rate)? InRupees(decimal yenAmount, decimal? rate)
    {
        if (yenAmount <= 0m || !UsableRate(rate))
            return null;
        var used = Rate(rate!.Value);
        return (Money.Round(yenAmount * used), Money.Round(yenAmount), used);
    }

    /// <summary>The rate a yen figure is to be multiplied by: the one written on the form if there was one,
    /// the container's or the sheet's otherwise. One rule for both pages, because a figure that previews at
    /// one rate and books at another is the mistake this whole corner exists to prevent.</summary>
    public static decimal RateFor(decimal bookRate, decimal? typed)
        => typed is decimal given && UsableRate(given) ? Rate(given) : bookRate;

    /// <summary>What to say when a yen figure arrives with nothing to convert it by.</summary>
    public static string NoRateMessage(decimal amount)
        => $"¥{amount:N0} needs a rate: write Rs for 1 yen in the rate box. A rate of 1 would book the yen "
           + "figure as rupees, so nothing is guessed at - and if the bill was in rupees after all, choose "
           + "Rs (PKR).";

    /// <summary>A rate the way it is written under a shopkeeper's own hand: as many decimals as it takes to
    /// repeat the multiplication, and no trailing zeros to read past. One shape for the figure, so the line
    /// under an expense and the note under an item's cost price do not learn to differ - and it is the same
    /// number the money was multiplied by, so a line's yen figure and this rate give its rupees back.</summary>
    public static string RateText(decimal? rate) => rate is decimal r ? r.ToString("0.######") : "";
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

public static class SupplierPayMethods
{
    public static readonly string[] All = ["TT", "LC", "Cash", "Bank Transfer", "Other"];
}

/// <summary>
/// One row of an order sheet. YenRate comes from the plan, so the rupee columns follow
/// the rate as it is typed.
/// </summary>
public class BuyPlanLineRow
{
    public int Id { get; set; }
    public string ItemName { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal UnitCostYen { get; set; }
    public decimal UnitWeightKg { get; set; }
    public decimal SalePricePkr { get; set; }

    private decimal _yenRate = 1;

    /// <summary>
    /// Rupees per yen for this row, cut to the six decimals the save keeps. Normalised on the way in,
    /// because the grid hands rows the rate it has typed and the database hands them back the rate it
    /// kept: priced with nine decimals and re-priced with six, a row of 250 moved a paisa.
    /// </summary>
    public decimal YenRate
    {
        get => _yenRate;
        set => _yenRate = Money.Round(value, 6);
    }

    public decimal CostYen => Quantity * UnitCostYen;
    public decimal CostPkr => Money.Round(CostYen * YenRate);
    public decimal SalePkr => Quantity * SalePricePkr;
    public decimal TotalWeightKg => Quantity * UnitWeightKg;

    /// <summary>Selling total minus this row's goods cost. The expense figure is per lot, not per row.</summary>
    public decimal ProfitPkr => SalePkr - CostPkr;

    public string ItemNameText => string.IsNullOrWhiteSpace(ItemName) ? "(no name)" : ItemName.Trim();
    public string QuantityText => Money.Qty(Quantity);
    public string UnitCostYenText => Money.Yen(UnitCostYen);
    public string CostYenText => Money.Yen(CostYen);
    public decimal CostPerPiecePkr => UnitCostYen * YenRate;

    public string CostPkrText => Money.Pkr(CostPkr);
    public string CostPerPiecePkrText => Money.Pkr(CostPerPiecePkr);
    public string UnitWeightText => Money.Kg(UnitWeightKg);
    public string TotalWeightText => Money.Kg(TotalWeightKg);
    public string SalePriceText => Money.Pkr(SalePricePkr);
    public string SaleTotalText => Money.Pkr(SalePkr);
    public string ProfitText => Money.Pkr(ProfitPkr);
    public bool ProfitIsGood => ProfitPkr >= 0;

    public BuyPlanLineInput ToInput() => new()
    {
        ItemName = ItemName,
        Quantity = Quantity,
        UnitCostYen = UnitCostYen,
        UnitWeightKg = UnitWeightKg,
        SalePricePkr = SalePricePkr
    };
}

/// <summary>What a sheet adds up to. Built the same way on the list, the sheet and the save.</summary>
public class BuyPlanTotal
{
    public int ItemCount { get; set; }
    public decimal CostYen { get; set; }
    public decimal CostPkr { get; set; }
    public decimal ExpensePkr { get; set; }
    public decimal SalePkr { get; set; }
    public decimal TotalWeightKg { get; set; }
    public decimal YenRate { get; set; } = 1;

    /// <summary>Goods cost plus the expense figure — all the money that goes in.</summary>
    public decimal SpendPkr => CostPkr + ExpensePkr;

    /// <summary>Sold everything, minus what went in.</summary>
    public decimal ProfitPkr => SalePkr - SpendPkr;

    /// <summary>
    /// What the goods rows alone come to, before the bills: the column of a printed sheet's per-row profit,
    /// which does not add up to the profit below it because the bills belong to the lot and not to a row.
    /// Stated here rather than worked out on the paper, so a printed figure is a figure the model holds.
    /// </summary>
    public decimal RowsProfitPkr => Money.Round(SalePkr - CostPkr);

    public decimal MarginPct => SalePkr > 0.009m ? ProfitPkr / SalePkr * 100m : 0m;

    public bool ProfitIsGood => ProfitPkr >= 0;

    public string ItemCountText => ItemCount == 1 ? "1 row" : ItemCount + " rows";
    public string CostYenText => Money.Yen(CostYen);
    public string CostPkrText => Money.Pkr(CostPkr);
    public string ExpenseText => Money.Pkr(ExpensePkr);
    public string SpendText => Money.Pkr(SpendPkr);
    public string SaleText => Money.Pkr(SalePkr);
    public string ProfitText => Money.Pkr(ProfitPkr);
    public string RowsProfitText => Money.Pkr(RowsProfitPkr);
    public string MarginText => Money.Pct(MarginPct);
    public string WeightText => Money.Kg(TotalWeightKg) + " kg";

    public static BuyPlanTotal Build(IEnumerable<BuyPlanLineRow> lines, decimal yenRate, decimal expensePkr)
    {
        var list = lines.ToList();
        return new BuyPlanTotal
        {
            ItemCount = list.Count,
            // Money at the definition, not at the label: each row is already a rounded paisa figure,
            // and so is the expense, so the tape, the saved sheet and the printed one are one number.
            CostYen = Money.Round(list.Sum(l => l.CostYen)),
            CostPkr = Money.Round(list.Sum(l => l.CostPkr)),
            ExpensePkr = Money.Round(expensePkr),
            SalePkr = Money.Round(list.Sum(l => l.SalePkr)),
            TotalWeightKg = list.Sum(l => l.TotalWeightKg),
            YenRate = yenRate > 0 ? yenRate : 1
        };
    }
}

/// <summary>An order sheet as the pages see it: header, rows, totals.</summary>
public class BuyPlanRow
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public decimal YenRate { get; set; } = 1;
    public decimal ExpensePkr { get; set; }
    public List<BuyPlanLineRow> Lines { get; set; } = new();
    public List<BuyPlanExpenseRow> Expenses { get; set; } = new();
    public BuyPlanTotal Total { get; set; } = new();

    public string TitleText => string.IsNullOrWhiteSpace(Title) ? "Untitled sheet" : Title.Trim();
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
        // The rate is cut to the six decimals the save keeps, HERE, so a row's cost is computed with
        // the same number the database will hold. A nine-decimal rate typed at the keyboard otherwise
        // priced the sheet one way and re-opened it another.
        var rate = Money.Round(YenRate > 0 ? YenRate : 1, 6);
        foreach (var l in Lines)
            l.YenRate = rate;
        // The expense figure is the rows and nothing else. Recomputed here, on the page as it is typed and
        // on the sheet as it was saved, so the total a sheet shows is always the sum of what is under it.
        ExpensePkr = Money.Round(Expenses.Sum(e => e.AmountPkr));
        Total = BuyPlanTotal.Build(Lines, rate, ExpensePkr);
    }
}

/// <summary>An expense row on an order sheet, as the page shows it and as the form above it edits it.</summary>
public class BuyPlanExpenseRow
{
    public int Id { get; set; }
    public string Description { get; set; } = string.Empty;

    private decimal _amountPkr;

    /// <summary>What the row adds to the sheet, in rupees. Rounded on the way in, because the rupees a row
    /// holds are the rupees the sheet totals and the tape prints: a page showing 192,618.0045 beside a book
    /// keeping 192,618.00 is two figures for one amount.</summary>
    public decimal AmountPkr
    {
        get => _amountPkr;
        set => _amountPkr = Money.Round(value);
    }

    public string Currency { get; set; } = "PKR";
    public decimal AmountForeign { get; set; }
    public decimal? RateUsed { get; set; }

    public string DescriptionText => string.IsNullOrWhiteSpace(Description) ? "Other" : Description.Trim();
    public string AmountText => Money.Pkr(AmountPkr);

    /// <summary>The line under the rupees: what was written, the rate it was taken at, and what came out.
    /// Empty on a rupee row, because there is nothing there to explain.</summary>
    public string Note => Currency != "PKR" && AmountForeign > 0m && RateUsed is decimal rate
        ? Money.Yen(AmountForeign) + " at " + Currencies.RateText(rate) + " = " + Money.Pkr(AmountPkr)
        : "";

    /// <summary>What the amount box holds when the row is picked up: the invoice's yen figure on a yen row,
    /// the rupees on a rupee one. The converted number is never put back into the box, because the box is
    /// where the shop writes what the bill said.</summary>
    public decimal AmountEntered => Currency == "JPY" && AmountForeign > 0m ? AmountForeign : AmountPkr;

    public BuyPlanExpenseInput ToInput() => new()
    {
        Description = DescriptionText,
        Amount = AmountEntered,
        Currency = Currency,
        Rate = RateUsed
    };
}

/// <summary>An expense row as the page hands it to the save. The amount is the figure as written, in the
/// currency named with it; <c>Rate</c> is the rate to multiply a yen figure by - null takes the sheet's own
/// rate, and a row re-saved untouched hands back the rate it was converted at, so saving a sheet never
/// re-values the money already on it.</summary>
public class BuyPlanExpenseInput
{
    public string Description { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "PKR";
    public decimal? Rate { get; set; }
}

/// <summary>A row as the page hands it to the save.</summary>
public class BuyPlanLineInput
{
    public string ItemName { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal UnitCostYen { get; set; }
    public decimal UnitWeightKg { get; set; }
    public decimal SalePricePkr { get; set; }
}
