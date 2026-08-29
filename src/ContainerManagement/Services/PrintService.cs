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

        var gross = sale.Lines.Sum(l => l.Quantity * l.UnitPrice);
        sb.Append($"<p>Goods: {H(Money.Pkr(gross))}<br/>");
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
        Start(sb, shop, "Khata — " + customer.Name);
        sb.Append($"<p>{H(customer.Phone)} {H(customer.Address)}</p>");
        sb.Append($"<p><b>Balance: {H(Money.Pkr(balance))}</b></p>");
        sb.Append("<table><tr><th>Date</th><th>Particulars</th><th>Sold</th><th>Received</th><th>Balance</th></tr>");
        foreach (var r in rows.Reverse())
        {
            sb.Append("<tr>");
            sb.Append($"<td>{H(r.DateText)}</td>");
            sb.Append($"<td>{H(r.Description)}</td>");
            sb.Append($"<td>{H(r.DebitText)}</td>");
            sb.Append($"<td>{H(r.CreditText)}</td>");
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
            @media print{button{display:none}}
            </style></head><body>
            """);
        sb.Append($"<h1>{H(string.IsNullOrWhiteSpace(shop.CompanyName) ? "CargoKhata" : shop.CompanyName)}</h1>");
        sb.Append($"<p class='muted'>{H(shop.Phone)} {H(shop.Address)}</p>");
        sb.Append($"<h2>{H(title)}</h2>");
        sb.Append("<button onclick='window.print()'>Print</button>");
    }

    private static void End(StringBuilder sb) => sb.Append("</body></html>");

    private static string H(string? s) => WebUtility.HtmlEncode(s ?? "");
}
