using System.Net;
using System.Text;
using ContainerManagement.Data;
using ContainerManagement.Models;

namespace ContainerManagement.Services;

public class PrintService
{
    public string InvoiceHtml(Sale sale, ShopSettings shop, decimal previousBalance, decimal invoiceBalance, decimal totalDue)
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
        sb.Append($"Previous ledger balance: {H(Money.Pkr(previousBalance))}<br/>");
        sb.Append($"This invoice balance: {H(Money.Pkr(invoiceBalance))}<br/>");
        sb.Append($"<b>Total balance due: {H(Money.Pkr(totalDue))}</b>");
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
        sb.Append("<table><tr><th>No.</th><th>Date</th><th>Particulars</th><th>Sold</th><th>Received</th>"
            + "<th>Paid out</th><th>Balance</th></tr>");
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
            sb.Append($"<td>{H(r.DebitText)}</td>");
            sb.Append($"<td>{H(r.CreditText)}</td>");
            sb.Append($"<td>{H(r.PaidOutText)}</td>");
            sb.Append($"<td>{H(r.RunningText)}</td>");
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
    /// A year of the shop on paper: the till, the selling and the costs, month by month, each with a line of
    /// totals under it. Everything is laid out from the rows the pages are already showing - no figure is
    /// worked out a second time here, which is the only reason a printed statement cannot disagree with the
    /// screen it was printed from. Months with nothing in them are printed as dashes rather than skipped,
    /// because a year is twelve months and a reader has to be able to see which ones were quiet.
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

        var inSum = Money.Round(till.Sum(r => r.CashIn));
        var outSum = Money.Round(till.Sum(r => r.CashOut));
        var closing = till.Count > 0 ? till[^1].Closing : 0m;
        var brought = Money.Round(closing - (inSum - outSum));
        var backSum = Money.Round(till.Sum(r => r.Returns));

        sb.Append("<h2>Main ledger</h2>");
        sb.Append("<table><tr><th>Month</th><th class='num'>In</th><th class='num'>Out</th>"
            + "<th class='num'>Net</th><th class='num'>Goods back</th><th class='num'>Closing</th></tr>");
        foreach (var r in till)
        {
            sb.Append($"<tr><td>{H(r.MonthText)}</td><td class='num'>{H(r.InText)}</td>"
                + $"<td class='num'>{H(r.OutText)}</td><td class='num'>{H(r.NetText)}</td>"
                + $"<td class='num'>{H(r.ReturnsText)}</td><td class='num'>{H(r.ClosingText)}</td></tr>");
        }
        sb.Append($"<tr><th>{year}</th><th class='num'>{H(Money.Pkr(inSum))}</th>"
            + $"<th class='num'>{H(Money.Pkr(outSum))}</th>"
            + $"<th class='num'>{H(Money.Pkr(inSum - outSum))}</th>"
            + $"<th class='num'>{H(Money.Pkr(backSum))}</th>"
            + $"<th class='num'>{H(Money.Pkr(closing))}</th></tr>");
        sb.Append("</table>");
        sb.Append($"<p class='muted'>Brought into the year: {H(Money.Pkr(brought))}. "
            + "Goods back is what customers took back in value, not cash that moved, so it is not added "
            + "to the in and out columns.</p>");

        var bills = sales.Sum(r => r.Bills);
        var sold = Money.Round(sales.Sum(r => r.Sold));
        var received = Money.Round(sales.Sum(r => r.Received));
        var returned = Money.Round(sales.Sum(r => r.Returned));
        var owed = Money.Round(sales.Sum(r => r.StillOwed));
        var profit = Money.Round(sales.Sum(r => r.Profit));

        sb.Append("<h2>Sales</h2>");
        sb.Append("<table><tr><th>Month</th><th class='num'>Bills</th><th class='num'>Sold</th>"
            + "<th class='num'>Received</th><th class='num'>Returned</th><th class='num'>Still owed</th>"
            + "<th class='num'>Profit</th></tr>");
        foreach (var r in sales)
        {
            sb.Append($"<tr><td>{H(r.MonthText)}</td><td class='num'>{H(r.BillsText)}</td>"
                + $"<td class='num'>{H(r.SoldText)}</td><td class='num'>{H(r.ReceivedText)}</td>"
                + $"<td class='num'>{H(r.ReturnedText)}</td><td class='num'>{H(r.StillOwedText)}</td>"
                + $"<td class='num'>{H(r.ProfitText)}</td></tr>");
        }
        sb.Append($"<tr><th>{year}</th><th class='num'>{bills}</th>"
            + $"<th class='num'>{H(Money.Pkr(sold))}</th><th class='num'>{H(Money.Pkr(received))}</th>"
            + $"<th class='num'>{H(Money.Pkr(returned))}</th><th class='num'>{H(Money.Pkr(owed))}</th>"
            + $"<th class='num'>{H(Money.Pkr(profit))}</th></tr>");
        sb.Append("</table>");
        sb.Append("<p class='muted'>A return is taken off the month it was made in. Still owed is what the "
            + "bills of that month have not been paid, counting every payment and return since.</p>");

        var costCount = costs.Sum(r => r.Count);
        var costSum = Money.Round(costs.Sum(r => r.Amount));
        sb.Append("<h2>Shop costs</h2>");
        sb.Append("<table><tr><th>Month</th><th class='num'>Lines</th><th class='num'>Amount</th></tr>");
        foreach (var r in costs)
        {
            sb.Append($"<tr><td>{H(r.MonthText)}</td><td class='num'>{H(r.CountText)}</td>"
                + $"<td class='num'>{H(r.AmountText)}</td></tr>");
        }
        sb.Append($"<tr><th>{year}</th><th class='num'>{costCount}</th>"
            + $"<th class='num'>{H(Money.Pkr(costSum))}</th></tr>");
        sb.Append("</table>");

        sb.Append($"<p><b>{year} took {H(Money.Pkr(sold))} in sales, and {H(Money.Pkr(profit))} was left "
            + $"after the cost of those goods; {H(Money.Pkr(costSum))} went out as shop costs, so "
            + $"{H(Money.Pkr(Money.Round(profit - costSum)))} stands at the end of it.</b></p>");
        sb.Append($"<p class='muted'>Cash in hand at the year's end: {H(Money.Pkr(closing))}.</p>");
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
