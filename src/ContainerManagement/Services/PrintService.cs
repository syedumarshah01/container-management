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
        // Which lot the goods came out of, when the whole bill came from one: the shop wants its paper to tie
        // the money to the container it paid for, and a customer holding the bill can see what was sold to
        // them. A bill drawn across two lots says nothing, because naming one of them would be a half-truth.
        var lots = sale.Lines.Select(l => l.Container).Where(c => c is not null).DistinctBy(c => c.Id).ToList();
        if (lots.Count == 1)
        {
            var lot = lots[0];
            var number = string.IsNullOrWhiteSpace(lot.ContainerNumber) ? "" : " · " + lot.ContainerNumber;
            sb.Append($"<p class='muted'>From container: {H(lot.Title)}{H(number)}</p>");
        }

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

    /// <summary>
    /// How long a message may be once it is dressed for a link. A text travels to WhatsApp inside a URL, and
    /// a browser stops reading a link that runs on too long - silently, from the end. The number is set well
    /// under the shortest limit any of them has, because a ledger line that goes missing from a message is a
    /// line the customer will pay for twice or dispute for a week.
    /// </summary>
    public const int ShareUrlBudget = 1600;

    /// <summary>
    /// The customer's number as it has to be dialled: digits only, and a local 03xx... carried up to 923xx...
    /// The complaint names what was found instead of saying "invalid", because a shop reads these off a
    /// notebook and needs to know whether the typing is wrong or the customer simply has no number saved.
    /// Anything that is not ASCII digits is refused here rather than sent along as a link that opens the wrong
    /// chat - a number typed in Urdu digits, for instance, looks fine on the page and is garbage to a browser.
    /// </summary>
    public static string ShareNumber(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
            throw new InvalidOperationException("This customer has no mobile number saved - add one and Save first.");
        var digits = new string(phone.Where(char.IsDigit).ToArray());
        if (digits.StartsWith("00", StringComparison.Ordinal))
            digits = digits[2..];
        if (digits.Length == 10 && digits.StartsWith("3", StringComparison.Ordinal))
            digits = "92" + digits;
        if (digits.Length == 11 && digits.StartsWith("0", StringComparison.Ordinal))
            digits = "92" + digits[1..];
        if (digits.Length < 10 || digits.Any(c => c < '0' || c > '9'))
            throw new InvalidOperationException($"\"{phone}\" does not read as a mobile number - a Pakistani one is 03xx and seven more digits.");
        return digits;
    }

    /// <summary>The ledger as a chat message, at the length a link can carry.</summary>
    public static string ShareText(string shop, string customerName, IReadOnlyList<LedgerRow> rows, decimal balance)
        => ShareText(shop, customerName, rows, balance, ShareUrlBudget);

    /// <summary>
    /// A customer's ledger written out as a chat message: the same lines, in the same order and the same words
    /// as the printed statement, oldest first, closing on the balance - because the paper and the message are
    /// read against each other, and a figure that agrees on both is the entire point of sending it. A book
    /// too long for a link loses its oldest lines and says how many, since the lines worth keeping are the
    /// recent ones and the total; what is dropped is pointed at the statement, not quietly removed.
    /// </summary>
    public static string ShareText(
        string shop, string customerName, IReadOnlyList<LedgerRow> rows, decimal balance, int maxUrlChars)
    {
        var head = $"Assalamualaikum {customerName},"
            + "\n\n"
            + $"{shop} - your ledger as at {DateTime.Today:dd MMM yyyy}:";
        var tail = ShareBalance(balance);
        var lines = rows.Select(ShareLine).ToList();

        // Filled from the newest line backwards, so the first thing the cut gives up is the oldest entry.
        var kept = new List<string>();
        for (var i = lines.Count - 1; i >= 0; i--)
        {
            var trial = new List<string> { lines[i] };
            trial.AddRange(kept);
            if (kept.Count > 0
                && Uri.EscapeDataString(Build(head, trial, lines.Count - trial.Count, tail)).Length > maxUrlChars)
                break;
            kept.Insert(0, lines[i]);
        }
        return Build(head, kept, lines.Count - kept.Count, tail);

        // The parts are passed in rather than read from the enclosing method, because that is what lets the
        // same builder be used to try a message on for length before it is kept.
        static string Build(string greeting, IReadOnlyList<string> body, int skipped, string signOff)
        {
            var sb = new StringBuilder(greeting).Append("\n\n");
            foreach (var line in body)
                sb.Append(line).Append('\n');
            if (skipped > 0)
                sb.Append($"{skipped} earlier line{(skipped == 1 ? "" : "s")} - they are on the printed statement")
                    .Append('\n');
            return sb.Append('\n').Append(signOff).ToString();
        }
    }

    /// <summary>
    /// The line the customer answers to: what is owed, in figures and in the words the till uses, on the same
    /// line, so a 0 typed either side of a lac shows up as a mismatch before it shows up in the cash.
    /// </summary>
    public static string ShareBalance(decimal balance)
    {
        var owed = Money.Round(balance);
        if (owed > 0)
        {
            var words = Money.Words(owed);
            return $"Balance due: {Money.Pkr(owed)}"
                + (words.Length == 0 ? "" : $" ({words})")
                + "\n\n"
                + "Please send when convenient. JazakAllah.";
        }
        if (owed == 0)
            return "Nothing is outstanding - the ledger is settled. JazakAllah.";
        return $"{Money.Pkr(Money.Round(-owed))} has been paid over and above what is owed. Tell us whether to "
            + "hand it back or keep it against the next bill.";
    }

    /// <summary>
    /// One ledger line, in the order the money moved. Each column's figure is taken from the text the page and
    /// the statement already print - including the dash that means nothing moved in it - because a share that
    /// re-derives amounts from Debit and Credit is a second chance to get one wrong.
    /// </summary>
    private static string ShareLine(LedgerRow r)
    {
        var moved = new List<string>();
        void Add(string label, string text)
        {
            if (text != "—")
                moved.Add(label + " " + text);
        }
        Add("sold", r.SoldText);
        Add("returned", r.ReturnedText);
        Add("received", r.ReceivedText);
        Add("paid out", r.PaidOutText);

        var parts = new List<string> { r.DateText };
        if (!string.IsNullOrWhiteSpace(r.Description))
            parts.Add(r.Description.Trim());
        if (moved.Count > 0)
            parts.Add(string.Join(", ", moved));
        parts.Add("balance " + r.RunningText);
        return string.Join(" - ", parts);
    }

    /// <summary>
    /// Hands the message to WhatsApp and says which number it was opened for. wa.me goes through whatever
    /// browser is on the machine, which is the case that works whether or not the desktop app is installed;
    /// the whatsapp:// link is tried after it, for the reverse. Neither opening at all is worth an error
    /// message, since nothing was sent, and the shop gets the text on the clipboard in that case rather than
    /// a status line nobody saw.
    /// </summary>
    public static string WhatsApp(string? phone, string text)
    {
        var digits = ShareNumber(phone);
        var query = Uri.EscapeDataString(text);
        var problem = "nothing on this computer is set to open a web link";
        foreach (var url in new[]
        {
            $"https://wa.me/{digits}?text={query}",
            $"whatsapp://send?phone={digits}&text={query}",
        })
        {
            try
            {
                // A handle that comes back empty is not a refusal: the shell still took the link, and
                // complaining about that would tell the shop nothing had happened when a chat is opening.
                using var opened = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true,
                });
                return digits;
            }
            catch (Exception ex)
            {
                problem = ex.Message;
            }
        }
        throw new InvalidOperationException($"WhatsApp could not be opened on this computer ({problem}).");
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
    /// An order sheet on paper: the eleven columns the sheet has, its bills, and the summary the page shows
    /// across its top. Nothing is worked out here - every figure is read off the row, the bill or the total it
    /// belongs to, because a paper that recomputes is a paper that can print a number the screen never showed.
    /// No prose either: the rate the yen was taken at is in the line under the title, each bill's own rate is
    /// in its own line, and the goods column says "before bills" where the summary's profit is after them.
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
            + "<th class='num'>Total sells</th><th class='num'>Profit, before bills</th></tr>");
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

        // The bills, always as a table - an empty one with Rs 0 under it states what the sheet costs, and
        // needs no sentence beside it. A yen bill carries its own figure and its own rate in the middle
        // column, and that is the whole explanation: the rupees on the right were multiplied once, at the
        // rate in the line, and a reprint next month prints the same number whatever the rate is then.
        sb.Append("<h2>Bills</h2>");
        sb.Append("<table><tr><th>What it was for</th><th>As it was written</th>"
            + "<th class='num'>Rs on the sheet</th></tr>");
        foreach (var e in plan.Expenses)
        {
            sb.Append("<tr>");
            sb.Append($"<td>{H(e.DescriptionText)}</td>");
            sb.Append($"<td>{H(e.Note.Length > 0 ? e.Note : e.AmountText)}</td>");
            sb.Append($"<td class='num'>{H(e.AmountText)}</td>");
            sb.Append("</tr>");
        }
        var bills = plan.Expenses.Count == 0
            ? "no bills"
            : plan.Expenses.Count == 1 ? "one bill" : plan.Expenses.Count + " bills";
        sb.Append($"<tr class='total'><th>Total bills</th><th>{H(bills)}</th>"
            + $"<th class='num'>{H(t.ExpenseText)}</th></tr>");
        sb.Append("</table>");

        // The sheet's summary, in the words and the order the figures come in across the top of the page. A
        // line of figures is a line that gets checked; a sentence about the money is prose that gets skipped.
        sb.Append("<h2>Summary</h2>");
        sb.Append("<table><tr><th class='num'>&yen; cost</th><th class='num'>Rs cost</th>"
            + "<th class='num'>+ expense</th><th class='num'>= all in</th>"
            + "<th class='num'>All sold for</th><th class='num'>Profit</th><th class='num'>Margin</th>"
            + "<th class='num'>Weight</th></tr>");
        sb.Append($"<tr class='total'><th class='num'>{H(t.CostYenText)}</th>"
            + $"<th class='num'>{H(t.CostPkrText)}</th><th class='num'>{H(t.ExpenseText)}</th>"
            + $"<th class='num'>{H(t.SpendText)}</th><th class='num'>{H(t.SaleText)}</th>"
            + $"<th class='num'>{H(t.ProfitText)}</th><th class='num'>{H(t.MarginText)}</th>"
            + $"<th class='num'>{H(t.WeightText)}</th></tr>");
        sb.Append("</table>");
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
