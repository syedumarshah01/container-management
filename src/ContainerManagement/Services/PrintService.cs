using System.Net;
using System.Text;
using ContainerManagement.Data;
using ContainerManagement.Models;

namespace ContainerManagement.Services;

public class PrintService
{
    public string InvoiceHtml(Sale sale, ShopSettings shop, decimal previousBalance, decimal invoiceBalance,
        decimal totalDue, decimal dueToday)
    {
        var sb = new StringBuilder();
        Start(sb, shop, $"Invoice #{sale.Id}");
        sb.Append($"<p class='muted'>{H(sale.Date.ToString("dd MMM yyyy"))}");
        if (sale.DueDate is DateTime due)
            sb.Append($" · Due {H(due.ToString("dd MMM yyyy"))}");
        if (sale.Status == SaleStatus.Cancelled)
            sb.Append(" · <b>CANCELLED</b>");
        sb.Append("</p>");
        sb.Append($"<p><b>Bill to:</b> {H(sale.Customer.Name)}");
        if (!string.IsNullOrWhiteSpace(sale.Customer.Phone))
            sb.Append($" · {H(sale.Customer.Phone)}");
        if (!string.IsNullOrWhiteSpace(sale.Customer.Address))
            sb.Append($"<br/>{H(sale.Customer.Address)}");
        sb.Append("</p>");

        sb.Append("<table><tr><th>Item</th><th>Qty</th><th>Price</th><th>Amount</th></tr>");
        foreach (var l in sale.Lines)
        {
            sb.Append("<tr>");
            sb.Append($"<td>{H(l.Product.Name)}</td>");
            sb.Append($"<td>{H(Money.Qty(l.Quantity))}</td>");
            sb.Append($"<td>{H(Money.Pkr(l.UnitPrice))}</td>");
            sb.Append($"<td>{H(Money.Pkr(l.LineTotal))}</td>");
            sb.Append("</tr>");
        }
        sb.Append("</table>");

        var gross = sale.Lines.Sum(l => l.LineTotal);
        sb.Append($"<p>Items: {H(Money.Pkr(gross))}<br/>");
        if (sale.DiscountAmount > 0)
            sb.Append($"Discount: {H(Money.Pkr(sale.DiscountAmount))}<br/>");
        sb.Append($"<b>Total: {H(Money.Pkr(sale.TotalAmount))}</b><br/>");
        sb.Append($"Received: {H(Money.Pkr(sale.PaidNow))}</p>");
        sb.Append("<p>");
        // The date is named on the paper because these are the figures the book held then, not now: a bill
        // reprinted next month says so in the line itself rather than leaving the reader to guess which
        // side of the month they are reading.
        sb.Append($"Previous ledger balance, as at {H(sale.Date.ToString("dd MMM yyyy"))}: {H(Money.Pkr(previousBalance))}<br/>");
        sb.Append($"This invoice balance: {H(Money.Pkr(invoiceBalance))}<br/>");
        sb.Append($"<b>Total balance due: {H(Money.Pkr(totalDue))}</b>");
        if (dueToday != totalDue)
            sb.Append($"<br/><span class='muted'>Outstanding on their book today, {H(DateTime.Now.ToString("dd MMM yyyy"))}: {H(Money.Pkr(dueToday))}</span>");
        sb.Append("</p>");
        if (!string.IsNullOrWhiteSpace(sale.Notes))
            sb.Append($"<p class='muted'>{H(sale.Notes)}</p>");
        End(sb);
        return sb.ToString();
    }

    public string StatementHtml(Customer customer, IReadOnlyList<LedgerRow> rows, decimal balance, ShopSettings shop)
    {
        var sb = new StringBuilder();
        Start(sb, shop, "Ledger — " + customer.Name);
        sb.Append($"<p>{H(customer.Phone)} {H(customer.Address)}</p>");
        sb.Append($"<p><b>Balance: {H(Money.Pkr(balance))}</b></p>");
        // The columns are the ones the customer's page shows, in the same order, because a statement that
        // leaves a column out leaves the reader with rows whose balance moves for no reason they can see.
        // "Returned" is the one that was missing: a return is neither a sale nor money handed over, so it
        // prints in none of the other three, and a paper ledger used to carry it as a line of dashes that
        // still changed the balance. Money columns are right-aligned so the page adds up by its last digit.
        sb.Append("<table><tr><th>No.</th><th>Date</th><th>Particulars</th>"
            + "<th class='num'>Sold</th><th class='num'>Returned</th><th class='num'>Received</th>"
            + "<th class='num'>Paid out</th><th class='num'>Balance</th></tr>");
        // The rows arrive in the order the money moved, and a paper statement is read that way: opening
        // line at the top, the balance running down to the figure printed above it. The screen puts the
        // newest entry under the reader's eye; this page must not, or the statement contradicts the book
        // it came from. The number is printed so a line can be quoted from the paper to the till.
        foreach (var r in rows)
        {
            sb.Append("<tr>");
            sb.Append($"<td>{r.Step}</td>");
            sb.Append($"<td>{H(r.DateText)}</td>");
            sb.Append($"<td>{H(r.Description)}</td>");
            sb.Append($"<td class='num'>{H(r.SoldText)}</td>");
            sb.Append($"<td class='num'>{H(r.ReturnedText)}</td>");
            sb.Append($"<td class='num'>{H(r.ReceivedText)}</td>");
            sb.Append($"<td class='num'>{H(r.PaidOutText)}</td>");
            sb.Append($"<td class='num'>{H(r.RunningText)}</td>");
            sb.Append("</tr>");
        }
        sb.Append("</table>");
        End(sb);
        return sb.ToString();
    }

    public string OpenHtml(string html, string fileName)
    {
        var path = Path.Combine(DbPaths.PrintDirectory, fileName);
        File.WriteAllText(path, html, Encoding.UTF8);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true
        });
        return path;
    }

    public static void WhatsApp(string? phone, string text)
    {
        var digits = new string((phone ?? "").Where(char.IsDigit).ToArray());
        if (digits.StartsWith("00"))
            digits = digits[2..];
        if (digits.StartsWith("0") && digits.Length == 11)
            digits = "92" + digits[1..];
        if (digits.Length < 10)
            throw new InvalidOperationException("Add a mobile number on the customer first.");
        var url = "https://wa.me/" + digits + "?text=" + Uri.EscapeDataString(text);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = url,
            UseShellExecute = true
        });
    }

    /// <summary>
    /// A year of the shop on paper: the till, the selling and the costs, month by month, with the year's own
    /// line under each table. Nothing is worked out here at all - the rows arrive already carrying that line,
    /// because a total computed a second time for the printer is a total that can disagree with the screen it
    /// was printed from. Months with nothing in them are printed as dashes rather than skipped, because a
    /// year is twelve months and a reader has to be able to see which ones were quiet.
    /// </summary>
    public string YearStatementHtml(
        int year,
        IReadOnlyList<TillYearRow> till,
        IReadOnlyList<SalesYearRow> sales,
        IReadOnlyList<ExpenseYearRow> costs,
        ShopSettings shop)
    {
        var sb = new StringBuilder();
        Start(sb, shop, $"Year statement {year}");

        var cash = till.FirstOrDefault(r => r.IsTotal) ?? TillYearRow.Totals(year, till);
        var sold = sales.FirstOrDefault(r => r.IsTotal) ?? SalesYearRow.Totals(year, sales);
        var cost = costs.FirstOrDefault(r => r.IsTotal) ?? ExpenseYearRow.Totals(year, costs);
        var brought = Money.Round(cash.Closing - (cash.CashIn - cash.CashOut));

        sb.Append("<h2>Main ledger</h2>");
        sb.Append("<table><tr><th>Month</th><th class='num'>In</th><th class='num'>Out</th>"
            + "<th class='num'>Net</th><th class='num'>Goods back</th><th class='num'>Closing</th></tr>");
        foreach (var r in till.Where(r => !r.IsTotal))
        {
            sb.Append($"<tr><td>{H(r.MonthText)}</td><td class='num'>{H(r.InText)}</td>"
                + $"<td class='num'>{H(r.OutText)}</td><td class='num'>{H(r.NetText)}</td>"
                + $"<td class='num'>{H(r.ReturnsText)}</td><td class='num'>{H(r.ClosingText)}</td></tr>");
        }
        sb.Append($"<tr class='total'><th>{H(cash.MonthText)}</th><th class='num'>{H(Money.Pkr(cash.CashIn))}</th>"
            + $"<th class='num'>{H(Money.Pkr(cash.CashOut))}</th>"
            + $"<th class='num'>{H(Money.Pkr(cash.CashIn - cash.CashOut))}</th>"
            + $"<th class='num'>{H(Money.Pkr(cash.Returns))}</th>"
            + $"<th class='num'>{H(Money.Pkr(cash.Closing))}</th></tr>");
        sb.Append("</table>");
        sb.Append($"<p class='muted'>Brought into the year: {H(Money.Pkr(brought))}. "
            + "Goods back is what customers took back in value, not cash that moved, so it is not added "
            + "to the in and out columns.</p>");

        sb.Append("<h2>Sales</h2>");
        sb.Append("<table><tr><th>Month</th><th class='num'>Bills</th><th class='num'>Sold</th>"
            + "<th class='num'>Received</th><th class='num'>Returned</th><th class='num'>Still owed</th>"
            + "<th class='num'>Profit</th></tr>");
        foreach (var r in sales.Where(r => !r.IsTotal))
        {
            sb.Append($"<tr><td>{H(r.MonthText)}</td><td class='num'>{H(r.BillsText)}</td>"
                + $"<td class='num'>{H(r.SoldText)}</td><td class='num'>{H(r.ReceivedText)}</td>"
                + $"<td class='num'>{H(r.ReturnedText)}</td><td class='num'>{H(r.StillOwedText)}</td>"
                + $"<td class='num'>{H(r.ProfitText)}</td></tr>");
        }
        sb.Append($"<tr class='total'><th>{H(sold.MonthText)}</th><th class='num'>{sold.Bills}</th>"
            + $"<th class='num'>{H(Money.Pkr(sold.Sold))}</th><th class='num'>{H(Money.Pkr(sold.Received))}</th>"
            + $"<th class='num'>{H(Money.Pkr(sold.Returned))}</th><th class='num'>{H(Money.Pkr(sold.StillOwed))}</th>"
            + $"<th class='num'>{H(Money.Pkr(sold.Profit))}</th></tr>");
        sb.Append("</table>");
        sb.Append("<p class='muted'>A return is taken off the month it was made in. Still owed is what the "
            + "bills of that month have not been paid, counting every payment and return since.</p>");

        sb.Append("<h2>Shop costs</h2>");
        sb.Append("<table><tr><th>Month</th><th class='num'>Lines</th><th class='num'>Amount</th></tr>");
        foreach (var r in costs.Where(r => r.IsTotal == false))
        {
            sb.Append($"<tr><td>{H(r.MonthText)}</td><td class='num'>{H(r.CountText)}</td>"
                + $"<td class='num'>{H(r.AmountText)}</td></tr>");
        }
        sb.Append($"<tr class='total'><th>{H(cost.MonthText)}</th><th class='num'>{cost.Count}</th>"
            + $"<th class='num'>{H(Money.Pkr(cost.Amount))}</th></tr>");
        sb.Append("</table>");

        sb.Append($"<p><b>{year} sold {H(Money.Pkr(sold.Sold))}, and {H(Money.Pkr(sold.Profit))} was left "
            + $"after the cost of those goods; {H(Money.Pkr(cost.Amount))} went out as shop costs, so "
            + $"{H(Money.Pkr(Money.Round(sold.Profit - cost.Amount)))} stands at the end of it.</b></p>");
        sb.Append($"<p class='muted'>Cash in hand at the year's end: {H(Money.Pkr(cash.Closing))}.</p>");
        End(sb);
        return sb.ToString();
    }

    /// <summary>
    /// An order sheet on paper, in the eleven columns the sheet itself has, with the bills under it and the
    /// money the lot adds up to below those. Nothing is worked out here: every figure is read off the row, the
    /// bill or the total it belongs to, because a paper that recomputes is a paper that can print a number the
    /// screen never showed. What the screen leaves out and the paper must not is the reasoning - the rate the
    /// yen was taken at, why a bill's rupees do not move when the rate moves, and why the rows' profit and the
    /// sheet's profit are two different numbers.
    /// </summary>
    public string BuyPlanHtml(BuyPlanRow plan, ShopSettings shop)
    {
        var sb = new StringBuilder();
        var t = plan.Total;
        Start(sb, shop, "Order sheet - " + plan.TitleText);
        sb.Append($"<p class='muted'>Made {H(plan.CreatedText)} · {H(t.ItemCountText)}"
            + $" · yen figures at Rs {H(Currencies.RateText(plan.YenRate))} for 1 yen</p>");

        sb.Append("<table><tr><th>Item</th><th class='num'>Qty</th><th class='num'>&yen; each</th>"
            + "<th class='num'>Total &yen;</th><th class='num'>Cost each</th><th class='num'>Total cost</th>"
            + "<th class='num'>kg each</th><th class='num'>Total kg</th><th class='num'>Sells each</th>"
            + "<th class='num'>Total sells</th><th class='num'>Profit</th></tr>");
        foreach (var l in plan.Lines)
        {
            sb.Append("<tr>");
            sb.Append($"<td>{H(l.ItemNameText)}</td>");
            sb.Append($"<td class='num'>{H(l.QuantityText)}</td>");
            sb.Append($"<td class='num'>{H(l.UnitCostYenText)}</td>");
            sb.Append($"<td class='num'>{H(l.CostYenText)}</td>");
            sb.Append($"<td class='num'>{H(l.CostPerPiecePkrText)}</td>");
            sb.Append($"<td class='num'>{H(l.CostPkrText)}</td>");
            sb.Append($"<td class='num'>{H(l.UnitWeightText)}</td>");
            sb.Append($"<td class='num'>{H(l.TotalWeightText)}</td>");
            sb.Append($"<td class='num'>{H(l.SalePriceText)}</td>");
            sb.Append($"<td class='num'>{H(l.SaleTotalText)}</td>");
            sb.Append($"<td class='num'>{H(l.ProfitText)}</td>");
            sb.Append("</tr>");
        }
        // The total line is the last thing on the page and it is a line of its own: a figure at the bottom of
        // a column that looks like another row is a figure nobody checks.
        sb.Append($"<tr class='total'><th>{H(t.ItemCountText)}</th><th class='num'>&mdash;</th>"
            + $"<th class='num'>&mdash;</th><th class='num'>{H(t.CostYenText)}</th><th class='num'>&mdash;</th>"
            + $"<th class='num'>{H(t.CostPkrText)}</th><th class='num'>&mdash;</th>"
            + $"<th class='num'>{H(t.WeightText)}</th><th class='num'>&mdash;</th>"
            + $"<th class='num'>{H(t.SaleText)}</th><th class='num'>{H(t.RowsProfitText)}</th></tr>");
        sb.Append("</table>");
        sb.Append("<p class='muted'>The last column is each row's sells less its own goods cost. The bills "
            + "below belong to the whole lot, so they are not shared between the rows: a row's profit here is "
            + "not yet the lot's.</p>");

        if (plan.Expenses.Count > 0)
        {
            sb.Append("<h2>Bills</h2>");
            sb.Append("<table><tr><th>What it was for</th><th>As it was written</th>"
                + "<th class='num'>Rs on the sheet</th></tr>");
            foreach (var e in plan.Expenses)
            {
                sb.Append("<tr>");
                sb.Append($"<td>{H(e.DescriptionText)}</td>");
                // A yen bill states its own figure and the rate it was taken at, which is the row's own and
                // not the sheet's; a rupee bill has nothing to explain, so it states its amount.
                sb.Append($"<td>{H(e.Note.Length > 0 ? e.Note : e.AmountText)}</td>");
                sb.Append($"<td class='num'>{H(e.AmountText)}</td>");
                sb.Append("</tr>");
            }
            var bills = plan.Expenses.Count == 1 ? "one bill" : plan.Expenses.Count + " bills";
            sb.Append($"<tr class='total'><th>Total bills</th><th class='muted'>{H(bills)}</th>"
                + $"<th class='num'>{H(t.ExpenseText)}</th></tr>");
            sb.Append("</table>");
            sb.Append("<p class='muted'>A bill written in yen was converted once, at the rate in the line "
                + "above, and keeps that rate: re-printing this sheet next month prints the same rupees even "
                + "if the rate on the sheet has moved. A bill is refused, not guessed at, if a yen figure "
                + "arrives with no rate - a rate of 1 would book &yen;180,000 as Rs 180,000.</p>");
        }
        else
        {
            sb.Append("<p class='muted'>No bills on this sheet: it costs the goods alone. Each bill is typed "
                + "on the sheet, in yen or rupees as it was written, and this figure is those bills added up.</p>");
        }

        sb.Append("<p><b>Goods " + H(t.CostPkrText) + " and bills " + H(t.ExpenseText)
            + " make " + H(t.SpendText) + " all in. Sold for " + H(t.SaleText) + ", that leaves "
            + H(t.ProfitText) + " - " + H(t.MarginText) + " of the selling.</b></p>");
        sb.Append("<p class='muted'>A plan, not a purchase: nothing here has been entered in the stock book, "
            + "the supplier's account or the till. Weights are what the pieces weigh, per piece and in all, so "
            + "the kilos on this paper are the kilos a container's freight is later shared over.</p>");
        End(sb);
        return sb.ToString();
    }

    private static void Start(StringBuilder sb, ShopSettings shop, string title)
    {
        sb.Append("""
            <!DOCTYPE html><html><head><meta charset="utf-8"/>
            <title>
            """);
        sb.Append(H(title));
        sb.Append("""
            </title>
            <style>
            body{font-family:Segoe UI,sans-serif;margin:32px;color:#1B2A4A}
            h1{margin:0 0 4px;font-size:22px}
            .muted{color:#6B7280}
            table{border-collapse:collapse;width:100%;margin:16px 0}
            th,td{border-bottom:1px solid #E5E7EB;padding:8px;text-align:left}
            th{font-size:12px;color:#6B7280}
            tr.total th{font-size:14px;color:#1B2A4A;border-top:2px solid #1B2A4A;background:#F7F6F2}
            td.num,th.num{text-align:right;white-space:nowrap}
            @media print{button{display:none}}
            </style></head><body>
            """);
        sb.Append($"<h1>{H(string.IsNullOrWhiteSpace(shop.CompanyName) ? AppInfo.ProductName : shop.CompanyName)}</h1>");
        sb.Append($"<p class='muted'>{H(shop.Phone)} {H(shop.Address)}</p>");
        sb.Append($"<h2>{H(title)}</h2>");
        sb.Append("<button onclick='window.print()'>Print</button>");
    }

    private static void End(StringBuilder sb) => sb.Append("</body></html>");

    private static string H(string? s) => WebUtility.HtmlEncode(s ?? "");
}
