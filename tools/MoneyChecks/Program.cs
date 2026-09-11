using System.Globalization;
using ContainerManagement.Data;
using ContainerManagement.Models;
using ContainerManagement.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MoneyChecks;

/// <summary>
/// Checks the money in ProBooks by running the app's real services against a throwaway database in
/// the temp folder - nothing here reads or writes Documents\ProBooks. Every figure it expects is
/// worked out by hand in rupees and paisa, so a change in what the app rounds, stores or reports
/// shows up as a failure rather than as a number on screen that looks almost right.
///
/// Exit code is the number of failures, so check.bat / check.sh can gate a build.
/// Sums are done in C# rather than SumAsync, because the app stores money in TEXT columns and a SQL
/// aggregate would quietly run in floating point - which is the thing these checks exist to catch.
/// </summary>
public static class Program
{
    private static int _pass;
    private static int _fail;
    private static int _note;

    public static async Task<int> Main()
    {
        var culture = CultureInfo.CreateSpecificCulture("en-PK");
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;

        FormatRules();

        var dir = Path.Combine(Path.GetTempPath(), "probooks-moneychecks-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        try
        {
            await Flows(dir);
            await YearStatement(dir);
            await MonthReceipts(dir);
            Storage(dir);
        }
        catch (Exception ex)
        {
            _fail++;
            Console.WriteLine("  FAIL  the checks themselves stopped: " + ex.Message);
            Console.WriteLine(ex);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* temp folder */ }
        }

        Console.WriteLine();
        Console.WriteLine($"  {_pass} passed, {_fail} failed, {_note} noted");
        Console.WriteLine(_fail == 0
            ? "  Every money identity held. Read the notes above, and know this does not test the UI."
            : "  A money identity did NOT hold. Do not run real figures through this build.");
        return _fail;
    }

    // ------------------------------------------------------------------ the rules

    private static void FormatRules()
    {
        Head("rounding: half a paisa goes away from zero, never to the nearest even figure");
        Eq("Rs 267.525 becomes 267.53", 267.53m, Money.Round(267.525m));
        Eq("Rs 5000.005 becomes 5000.01", 5000.01m, Money.Round(5000.005m));
        Eq("Rs 1850.445 becomes 1850.45", 1850.45m, Money.Round(1850.445m));
        Eq("-Rs 1.005 becomes -1.01", -1.01m, Money.Round(-1.005m));
        Eq("0.3755 kg becomes 0.376", 0.376m, Money.Round(0.3755m, 3));
        Warn("Math.Round on its own would have said 267.52 - that is the difference this makes",
            Math.Round(267.525m, 2) == 267.52m);

        Head("printing: what a label shows is the figure that is stored");
        Check("Money.Pkr(543.9375) prints Rs 543.94", Plain(Money.Pkr(543.9375m)) == "Rs 543.94", Money.Pkr(543.9375m));
        Check("Money.Pkr(0) prints Rs 0", Plain(Money.Pkr(0m)) == "Rs 0", Money.Pkr(0m));
        Check("and it never rounds the paisa off a figure, which is what lets a list print the amount it holds",
            Plain(Money.Pkr(135_019.27m)) == "Rs 135019.27", Money.Pkr(135_019.27m));
        Check("Money.Yen(250) prints ¥250", Plain(Money.Yen(250m)) == "¥250", Money.Yen(250m));
        Check("Money.Kg(0.055) keeps three decimals", Plain(Money.Kg(0.055m)) == "0.055", Money.Kg(0.055m));
        Check("Money.Kg(375) has no trailing zeros", Plain(Money.Kg(375m)) == "375", Money.Kg(375m));

        Head("the amount readback: a shifted zero always changes what it says");
        Check("1573250 reads 15 lac 73 thousand 250", Money.Words(1573250m) == "15 lac 73 thousand 250", Money.Words(1573250m));
        Check("10000000 reads 1 crore", Money.Words(10000000m) == "1 crore", Money.Words(10000000m));
        Check("1000000000 reads 1 arab", Money.Words(1000000000m) == "1 arab", Money.Words(1000000000m));
        Check("999 reads nothing at all", Money.Words(999m) == "", "'" + Money.Words(999m) + "'");
        Check("-150000 keeps its sign", Money.Words(-150000m) == "-1 lac 50 thousand", Money.Words(-150000m));
        var rng = new Random(20260909);
        var same = 0;
        for (var i = 0; i < 200000; i++)
        {
            var v = rng.Next(1000, 2_000_000_000);
            if (Money.Words(v) == Money.Words(v * 10L)) same++;
            if (v >= 10_000 && Money.Words(v) == Money.Words(v / 10)) same++;
        }
        Check("200,000 amounts: multiplying or dividing by ten never reads the same", same == 0, same + " collisions");

        Head("order sheet formulas - the paper sheet, in code");
        var rows = new List<BuyPlanLineRow>
        {
            new() { ItemName = "LED bulb", Quantity = 250, UnitCostYen = 1m, UnitWeightKg = 0.375m, SalePricePkr = 450m, YenRate = 1.0701m },
            new() { ItemName = "Charger", Quantity = 40, UnitCostYen = 640.5m, UnitWeightKg = 0.12m, SalePricePkr = 1999.99m, YenRate = 1.0701m }
        };
        var plan = new BuyPlanRow { YenRate = 1.0701m, ExpensePkr = 100_000.005m, Lines = rows };
        plan.RefreshTotals();
        Eq("¥250 at 1.0701 = Rs 267.53, rounded up not to even", 267.53m, rows[0].CostPkr);
        Eq("250 pieces at Rs 450 sell for 112,500", 112_500m, rows[0].SalePkr);
        Eq("250 pieces at 0.375 kg = 93.75 kg", 93.75m, rows[0].TotalWeightKg);
        Eq("the sheet's cost Rs is the rows' cost added, so it cannot drift",
            rows[0].CostPkr + rows[1].CostPkr, plan.Total.CostPkr);
        Eq("the sheet's sell total is the rows' sell added",
            rows[0].SalePkr + rows[1].SalePkr, plan.Total.SalePkr);
        Eq("the one expense figure is kept once", 100_000.01m, plan.Total.ExpensePkr);
        Eq("all in = goods + that one expense", plan.Total.CostPkr + 100_000.01m, plan.Total.SpendPkr);
        Eq("profit = sold − all in", plan.Total.SalePkr - plan.Total.SpendPkr, plan.Total.ProfitPkr);
        Eq("weight = the rows' weight added", 93.75m + 4.8m, plan.Total.TotalWeightKg);
        Eq("a row's profit is its own sell less its own cost, expense excluded",
            rows[0].SalePkr - rows[0].CostPkr, rows[0].ProfitPkr);
        // A rate typed with nine decimals, as a live rate copied from a bank SMS often is.
        var fussy = new BuyPlanRow
        {
            YenRate = 1.0701234567m,
            Lines = new List<BuyPlanLineRow> { new() { Quantity = 250m, UnitCostYen = 640.5m, SalePricePkr = 1999.99m } }
        };
        fussy.RefreshTotals();
        Eq("a nine-decimal rate prices the row by the six decimals the save keeps", 171353.51m,
            fussy.Lines[0].CostPkr);
        Eq("and the row holds that rate, not the one typed", 1.070123m, fussy.Lines[0].YenRate);
        // The same expense, pinned to the rupee figure a re-opened sheet will hold (line above holds
        // the field; this one holds the total, which is what the tape and the printout read).
        Eq("so the sheet's all-in is a paisa figure before saving as well", 127_683.50m, plan.Total.SpendPkr);

        var noRate = new BuyPlanRow { YenRate = 0m, Lines = rows };
        noRate.RefreshTotals();
        Eq("a rate of 0 counts as 1 rather than dividing by nothing", 250m + 40m * 640.5m, noRate.Total.CostPkr);
    }

    // ------------------------------------------------------------------ the flows

    private static async Task Flows(string dir)
    {
        var file = Path.Combine(dir, "flow.db");
        var services = new ServiceCollection();
        services.AddDbContextFactory<AppDbContext>(o => o.UseSqlite($"Data Source={file};Cache=Shared;Mode=ReadWriteCreate"));
        var sp = services.BuildServiceProvider();
        var factory = sp.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using (var db = await factory.CreateDbContextAsync())
        {
            await db.Database.EnsureCreatedAsync();
        }

        var inventory = new InventoryService(factory);
        var sales = new SalesService(factory);
        var ledger = new LedgerService(factory);
        var reports = new ReportService(factory);
        var cash = new CashBookService(factory);
        var shop = new ShopExpenseService(factory);
        var plans = new BuyPlanService(factory);

        Head("a container: the bill, what was handed over, what is left");
        var container = await inventory.CreateContainerAsync(
            "AUDIT container", "CNT-0001", "China", DateTime.Today, null,
            // cartons and CBM are given here and never shown in the import editor again - the checks
            // below watch them to be sure that form does not erase what it does not display.
            "PKR", 1, null, 1_200m, 8.5m, null,
            "Yiwu Trading", 10_000_000.005m, 3_000_000.004m, "TT");
        // The shop typed "we owe 10,000,000.01" and "paid 3,000,000.004 now". The box is the balance, so
        // the stored bill is that balance plus the money handed over - to the paisa each.
        Eq("the bill is stored as the figure typed plus what was paid at creation", 13_000_000.01m,
            container.SupplierAmount);
        Eq("so what is owed on the page is the figure typed, not that figure netted down again",
            10_000_000.01m, await inventory.SupplierBalanceAsync(container.Id));
        var targets = await cash.SupplierContainersAsync();
        Eq("the We owe page reads the same figure", 10_000_000.01m, targets.Single(t => t.Id == container.Id).Owed);
        var payments = (await cash.SupplierPaymentsAsync()).Where(p => p.ContainerId == container.Id).ToList();
        Check("the payment made on the container form is recorded, once", payments.Count == 1, payments.Count + " rows");
        Eq("at the amount typed", 3_000_000m, payments.Count > 0 ? payments[0].Amount : -1m);
        Check("with the method that was chosen", payments.Count > 0 && payments[0].Method == "TT");
        var book = await cash.ListAsync();
        var outEntry = book.Single(e => e.Kind == CashBookKind.SupplierOut);
        Eq("cash in the ledger went out by exactly that", 3_000_000m, outEntry.AmountOut);
        Check("and the ledger line says who and which container",
            outEntry.Description.Contains("Yiwu Trading") && outEntry.Description.Contains("AUDIT container"),
            outEntry.Description);

        Head("goods: a cost with too many decimals, then a sale by weight");
        var bulbs = await inventory.AddGoodsAsync(container.Id, "LED bulb", "pcs", "LB-1", 1000m, 1850.445m, null, null, null, 0.375m, null);
        var chargers = await inventory.AddGoodsAsync(container.Id, "Charger", "pcs", "CH-9", 500m, 0m, null, null, null, 0.12m, null);
        Eq("Rs 1850.445 a piece is stored as 1850.45", 1850.45m, bulbs.UnitCost);
        var customer = await ledger.CreateCustomerAsync("AUDIT customer", null, null, null);
        var bill = await sales.CreateSaleAsync(customer.Id, DateTime.Today, new List<NewSaleLineInput>
        {
            new() { ContainerId = container.Id, ContainerItemId = bulbs.Id, ProductId = bulbs.ProductId, ProductName = "LED bulb", Unit = "pcs", Quantity = 0.375m, UnitPrice = 1450.50m },
            new() { ContainerId = container.Id, ContainerItemId = chargers.Id, ProductId = chargers.ProductId, ProductName = "Charger", Unit = "pcs", Quantity = 7m, UnitPrice = 19999.99m }
        }, 0m, "Cash", null, 5000.005m, DateTime.Today.AddDays(14));

        Eq("0.375 × Rs 1450.50 = 543.9375 billed as Rs 543.94", 543.94m, bill.Lines[0].LineTotal);
        Eq("7 × Rs 19,999.99 = Rs 139,999.93", 139_999.93m, bill.Lines[1].LineTotal);
        Eq("the discount is kept to the paisa", 5000.01m, bill.DiscountAmount);
        Eq("the bill is its own lines less the discount", 135_543.86m, bill.TotalAmount);
        Eq("and what was written to the database reads back identical", 135_543.86m,
            await PersistedBill(factory, bill.Id));

        await inventory.AddExpenseAsync(container.Id, DateTime.Today, "Sea Freight", 200_000m, "audit");
        var first = await reports.GetContainerProfitAsync(container.Id);
        Eq("the container's revenue is what the bill asked for, discount off", 135_543.86m, first.Revenue);
        Check("because the discount is shared over the bill's lines and the shares add back to the bill",
            first.Revenue == bill.TotalAmount, $"billed {bill.TotalAmount}, counted {first.Revenue} of sales");
        Eq("its cost is its sold lines' cost: 693.92 + 0", 693.92m, first.Cogs);
        Eq("its expenses are recorded", 200_000m, first.Expenses);
        Eq("and profit, as every page defines it, is revenue minus cost only", 134_849.94m, first.Profit);
        Warn("container profit ignores the container's own freight and customs - the margin is 200,000 lower than this row says",
            first.Profit == first.Revenue - first.Cogs - first.Expenses,
            $"row shows {first.Profit}; after its 200,000 of expenses the money actually left with is {first.Revenue - first.Cogs - first.Expenses}");
        Eq("and the discount is off profit too, not only off the bill", bill.TotalAmount - first.Cogs, first.Profit);
        Info("a container expense is also not put through the cash book: rent paid out of the till moves cash, sea freight does not.");

        Head("paying the printed bill works (a stored 543.9375 used to reject 543.94)");
        var payBill = await sales.CreateSaleAsync(customer.Id, DateTime.Today, new List<NewSaleLineInput>
        {
            new() { ContainerId = container.Id, ContainerItemId = bulbs.Id, ProductId = bulbs.ProductId, ProductName = "LED bulb", Unit = "pcs", Quantity = 0.375m, UnitPrice = 1450.50m }
        }, 543.94m, "Cash", null, 0m, null);
        Eq("paying exactly what the bill says settles it to zero", 0m, await sales.RemainingOnInvoiceAsync(payBill.Id));
        await Throws<InvalidOperationException>("one paisa more than the bill is refused, not silently credited",
            () => sales.CreateSaleAsync(customer.Id, DateTime.Today, new List<NewSaleLineInput>
            {
                new() { ContainerId = container.Id, ContainerItemId = bulbs.Id, ProductId = bulbs.ProductId, ProductName = "LED bulb", Unit = "pcs", Quantity = 0.375m, UnitPrice = 1450.50m }
            }, 543.95m, "Cash", null, 0m, null));
        await Throws<InvalidOperationException>("a discount larger than the bill is refused",
            () => sales.CreateSaleAsync(customer.Id, DateTime.Today, new List<NewSaleLineInput>
            {
                new() { ContainerId = container.Id, ContainerItemId = chargers.Id, ProductId = chargers.ProductId, ProductName = "Charger", Unit = "pcs", Quantity = 1m, UnitPrice = 100m }
            }, 0m, "Cash", null, 5000m, null));
        await Throws<InvalidOperationException>("you cannot sell more than the container holds",
            () => sales.CreateSaleAsync(customer.Id, DateTime.Today, new List<NewSaleLineInput>
            {
                new() { ContainerId = container.Id, ContainerItemId = chargers.Id, ProductId = chargers.ProductId, ProductName = "Charger", Unit = "pcs", Quantity = 100000m, UnitPrice = 100m }
            }, 0m, "Cash", null, 0m, null));

        await using (var db = await factory.CreateDbContextAsync())
        {
            var b = await db.ContainerItems.SingleAsync(i => i.Id == bulbs.Id);
            Eq("1000 pieces less 0.375 twice leaves 999.25", 999.25m, b.QuantityRemaining);
        }

        Head("every page reads the same billed money - Home, the container row, the Profit page");
        var beforeReprice = await reports.GetContainerProfitAsync(container.Id);
        var home = await reports.GetHomeMonthAsync();
        Eq("the container now holds both bills: 135,543.86 + 543.94", 136_087.80m, beforeReprice.Revenue);
        Eq("Home's month says the same figure, not a second version of it", beforeReprice.Revenue, home.Sales);
        Eq("and Home's profit agrees with the container row, no shop expenses yet", beforeReprice.Profit, home.Profit);
        var items = await reports.GetItemProfitsAsync(null, null, null);
        Eq("the Profit page item by item adds back to the same money", beforeReprice.Revenue, items.Sum(i => i.Revenue));
        Check("and the second bill, which had no discount, was not touched by the sharing",
            beforeReprice.Revenue - first.Revenue == payBill.Lines[0].LineTotal,
            $"{beforeReprice.Revenue - first.Revenue} added for an undiscounted line of {payBill.Lines[0].LineTotal}");

        Head("profit follows a corrected cost - the case that stayed wrong for one release");
        Eq("two sold lines at 693.92 each before the cost is fixed", 1387.84m, beforeReprice.Cogs);
        var repriced = await inventory.UpdateGoodsAsync(bulbs.Id, "LED bulb", "pcs", "LB-1", 1000m, 999.25m, 2000m, null, null, 0.375m, null);
        Check("both sold lines of that lot were re-costed", repriced == 2, repriced + " lines touched");
        var later = await reports.GetContainerProfitAsync(container.Id);
        Eq("the same two lines now cost 750 each", 1500m, later.Cogs);
        Eq("so profit fell by exactly the cost increase of 112.16", beforeReprice.Profit - 112.16m, later.Profit);
        Check("and no stock was invented or lost by the re-costing",
            later.QtyReceived == beforeReprice.QtyReceived && later.QtySold == beforeReprice.QtySold);

        Head("returns: credited at the price the customer paid, cost followed back out");
        var bulbLine = bill.Lines.Single(l => l.ProductId == bulbs.ProductId);
        await sales.ReturnItemsAsync(bill.Id, new List<SaleReturnInput> { new() { SaleLineId = bulbLine.Id, Quantity = 0.125m } });
        await using (var db = await factory.CreateDbContextAsync())
        {
            var ret = await db.SaleReturns.SingleAsync(r => r.SaleId == bill.Id);
            Check("a part return credits some of the bill", ret.Amount > 0 && ret.Amount < bill.TotalAmount, ret.Amount.ToString());
            var b = await db.ContainerItems.SingleAsync(i => i.Id == bulbs.Id);
            Eq("and the pieces are back on the shelf", 999.375m, b.QuantityRemaining);
        }
        await sales.ReturnItemsAsync(bill.Id, new List<SaleReturnInput> { new() { SaleLineId = bulbLine.Id, Quantity = 0.250m } });
        var chargerLine = bill.Lines.Single(l => l.ProductId == chargers.ProductId);
        await sales.ReturnItemsAsync(bill.Id, new List<SaleReturnInput> { new() { SaleLineId = chargerLine.Id, Quantity = 7m } });
        await using (var db = await factory.CreateDbContextAsync())
        {
            var credited = (await db.SaleReturns.Where(r => r.SaleId == bill.Id).ToListAsync()).Sum(r => r.Amount);
            Eq("returning the whole bill credits exactly what was billed, paisa for paisa",
                bill.TotalAmount, credited);
        }
        await Throws<InvalidOperationException>("one piece more than was sold cannot come back",
            () => sales.ReturnItemsAsync(bill.Id, new List<SaleReturnInput> { new() { SaleLineId = chargerLine.Id, Quantity = 1m } }));

        Head("the customer's balance is the same figure worked out two ways");
        var balance = await ledger.GetBalanceAsync(customer.Id);
        await using (var db = await factory.CreateDbContextAsync())
        {
            var billed = (await db.Sales.Where(s => s.CustomerId == customer.Id && s.Status == SaleStatus.Active).ToListAsync())
                .Sum(s => s.TotalAmount);
            var paid = (await db.Payments.Where(p => p.CustomerId == customer.Id).ToListAsync()).Sum(p => p.Amount);
            var returned = (await db.SaleReturns.Where(r => r.CustomerId == customer.Id).ToListAsync()).Sum(r => r.Amount);
            Eq("bills − payments − returns", billed - paid - returned, balance);
            var ledgerLines = await db.LedgerEntries.Where(e => e.CustomerId == customer.Id).ToListAsync();
            Eq("and the ledger's own entries agree to the paisa", ledgerLines.Sum(e => e.Debit - e.Credit), balance);
        }

        Head("a return on a bill that was paid hands the cash back, once, and the till says so");
        var balBefore = await ledger.GetBalanceAsync(customer.Id);
        var refundsBefore = await RefundedTotalAsync(factory);
        var paidLine = payBill.Lines.Single(l => l.ProductId == bulbs.ProductId);
        var back1 = await sales.ReturnItemsAsync(payBill.Id,
            new List<SaleReturnInput> { new() { SaleLineId = paidLine.Id, Quantity = 0.125m } });
        Eq("0.125 kg of a settled bill is credited at the price it sold for", 181.31m, back1);
        Eq("the till paid out exactly that, and nothing else moved", 181.31m, await RefundedTotalAsync(factory) - refundsBefore);
        Eq("their balance does not change, because the money is genuinely back in their hand", balBefore,
            await ledger.GetBalanceAsync(customer.Id));
        await using (var db = await factory.CreateDbContextAsync())
        {
            var lines = await db.CashBook.Where(e => e.Kind == CashBookKind.RefundOut).ToListAsync();
            Check("one refund line, not one per entry of the return", lines.Count == 1, lines.Count + " refund lines");
            Check("it names the customer and the bill it came from",
                lines[0].Description.Contains(customer.Name) && lines[0].Description.Contains("#" + payBill.Id),
                lines[0].Description);
            var adj = await db.LedgerEntries
                .Where(e => e.Type == LedgerType.Adjustment && e.SaleId == payBill.Id).ToListAsync();
            Eq("and their ledger carries the matching debit, so the two books still agree", 181.31m,
                adj.Sum(e => e.Debit - e.Credit));
        }

        // The rest of the same bill. There is no second switch any more: the bill is settled, so what came
        // back was money handed over, and the rule says it goes back.
        var back2 = await sales.ReturnItemsAsync(payBill.Id,
            new List<SaleReturnInput> { new() { SaleLineId = paidLine.Id, Quantity = 0.250m } });
        Eq("the rest of a settled bill is paid from the cashbook too", 362.63m, back2);
        Eq("and the till has now paid the whole bill back", 543.94m, await RefundedTotalAsync(factory));
        Eq("while their balance never moved, because the goods line and the cash line cancel",
            balBefore, await ledger.GetBalanceAsync(customer.Id));

        // And a bill that is still outstanding: the return is relief from a debt, never a cash movement.
        var third = await sales.CreateSaleAsync(customer.Id, DateTime.Today, new List<NewSaleLineInput>
        {
            new() { ContainerId = container.Id, ContainerItemId = chargers.Id, ProductId = chargers.ProductId, ProductName = "Charger", Unit = "pcs", Quantity = 2m, UnitPrice = 1999.99m }
        }, 1_000m, "Cash", null, 0m, null);
        var thirdLine = third.Lines.Single(l => l.ProductId == chargers.ProductId);
        var back3 = await sales.ReturnItemsAsync(third.Id,
            new List<SaleReturnInput> { new() { SaleLineId = thirdLine.Id, Quantity = 1m } });
        Eq("a return on a bill they still owe pays nothing out, however small the payment was", 0m, back3);
        Eq("the return comes off what they owe instead", 999.99m, await sales.RemainingOnInvoiceAsync(third.Id));
        Eq("and the till still holds only the two refunds from the settled bill", 543.94m, await RefundedTotalAsync(factory));

        // The case the first cut of this could not express: money was received for the bill, but not
        // all of it, so the return is part relief and part cash.
        var fourth = await sales.CreateSaleAsync(customer.Id, DateTime.Today, new List<NewSaleLineInput>
        {
            new() { ContainerId = container.Id, ContainerItemId = chargers.Id, ProductId = chargers.ProductId, ProductName = "Charger", Unit = "pcs", Quantity = 1m, UnitPrice = 1999.99m }
        }, 1_500m, "Cash", null, 0m, null);
        var fourthLine = fourth.Lines.Single(l => l.ProductId == chargers.ProductId);
        var back4 = await sales.ReturnItemsAsync(fourth.Id,
            new List<SaleReturnInput> { new() { SaleLineId = fourthLine.Id, Quantity = 1m } });
        Eq("a Rs 1,999.99 return on a Rs 1,500 payment hands back the payment, not the whole return",
            1_500m, back4);
        Eq("and nothing is left owing on that bill", 0m, await sales.RemainingOnInvoiceAsync(fourth.Id));
        Eq("the till has paid out every refund, and no more than those bills ever brought in", 2_043.94m,
            await RefundedTotalAsync(factory));

        // What the Main ledger's fourth card is built from: the goods, not the cash.
        var returns = await cash.ListReturnsAsync();
        Eq("the page's returns figure is every credit the book has taken", 140_087.78m, returns.Sum(r => r.Amount));
        await using (var db = await factory.CreateDbContextAsync())
        {
            var credited = (await db.LedgerEntries.Where(e => e.Type == LedgerType.Return).ToListAsync())
                .Sum(e => e.Credit - e.Debit);
            Eq("and it is the same money their ledgers were credited with", credited, returns.Sum(r => r.Amount));
        }

        Head("paying the supplier: two payments, two ledger lines, no double counting");
        await inventory.PaySupplierAsync(container.Id, DateTime.Today, 1_000_000.004m, "LC", "part payment, HBL ref 99");
        await inventory.PaySupplierAsync(container.Id, DateTime.Today, 500_000m, "Cash", null);
        Eq("owed is the figure on the box less what the page has paid since", 8_500_000.01m,
            await inventory.SupplierBalanceAsync(container.Id));
        book = await cash.ListAsync();
        await using (var db = await factory.CreateDbContextAsync())
        {
            var pays = await db.SupplierPayments.ToListAsync();
            var supOut = book.Where(e => e.Kind == CashBookKind.SupplierOut).ToList();
            Check("one cash-book line per payment", supOut.Count == pays.Count, $"{supOut.Count} lines for {pays.Count} payments");
            Eq("and the amounts agree to the paisa", pays.Sum(p => p.Amount), supOut.Sum(e => e.AmountOut));
            Check("the note typed when paying is on the ledger line",
                supOut.Any(e => e.Description.Contains("HBL ref 99")),
                string.Join("  |  ", supOut.Select(e => e.Description)));
        }

        Head("the form's paid box moves the payments, and the till moves with them");
        await inventory.UpdateImportDetailsAsync(container.Id, "Yiwu Trading", 10_000_000.01m, 4_700_000.005m, null);
        await using (var db = await factory.CreateDbContextAsync())
        {
            var newest = (await db.SupplierPayments.ToListAsync()).OrderByDescending(p => p.Id).First();
            Eq("typing more records one payment for the difference, to the paisa", 200_000.01m, newest.Amount);
            Check("dated the day the money is said to have left", newest.Date.Date == DateTime.Today,
                newest.Date.ToString("dd MMM yyyy"));
            Eq("paid so far is the figure typed, not the figure typed less rounding", 4_700_000.01m,
                await inventory.PaidSoFarAsync(container.Id));
            Eq("and what is owed stays the figure on the box - paying moves the bill, not the balance",
                10_000_000.01m, await inventory.SupplierBalanceAsync(container.Id));
        }
        await using (var db = await factory.CreateDbContextAsync())
        {
            var pays = await db.SupplierPayments.ToListAsync();
            var outLines = (await db.CashBook.ToListAsync()).Where(e => e.Kind == CashBookKind.SupplierOut).ToList();
            Eq("and the cash book went out by the same total", pays.Sum(p => p.Amount), outLines.Sum(e => e.AmountOut));
            Check("the payment it added says where it came from",
                pays.Any(p => p.Notes == "Recorded on the container form"),
                string.Join("  |  ", pays.Select(p => p.Notes ?? "(no note)")));
        }

        await inventory.UpdateImportDetailsAsync(container.Id, "Yiwu Trading", 10_000_000.01m, 4_150_000m, null);
        await using (var db = await factory.CreateDbContextAsync())
        {
            var pays = await db.SupplierPayments.OrderBy(p => p.Date).ThenBy(p => p.Id).ToListAsync();
            Check("typing less takes the newest payment off entirely, and trims the next", pays.Count == 3,
                pays.Count + " payments: " + string.Join(", ", pays.Select(p => Money.Pkr(p.Amount))));
            Eq("leaving exactly the figure typed", 4_150_000m, pays.Sum(p => p.Amount));
            Eq("the trimmed payment keeps its own cash line at the trimmed amount", 150_000m, pays[2].Amount);
            Eq("the owed figure is still what the box said after taking payments back too", 10_000_000.01m,
                await inventory.SupplierBalanceAsync(container.Id));
            var outLines = (await db.CashBook.ToListAsync()).Where(e => e.Kind == CashBookKind.SupplierOut).ToList();
            Check("no payment is left without a cash line, and no cash line without a payment",
                pays.Count == outLines.Count && pays.All(p => outLines.Any(e => e.SupplierPaymentId == p.Id)),
                pays.Count + " payments, " + outLines.Count + " cash lines");
        }
        // The balance box below what has been paid is not a contradiction to refuse - it is a settled
        // container, which is the ordinary shape of goods bought and paid for.
        await inventory.UpdateImportDetailsAsync(container.Id, "Yiwu Trading", 4_000_000m, null, null);
        await using (var db = await factory.CreateDbContextAsync())
        {
            var c = await db.Containers.SingleAsync(x => x.Id == container.Id);
            Eq("the bill becomes the balance typed plus every payment on the container", 8_150_000m, c.SupplierAmount);
            Eq("and the page owes exactly the figure that was written", 4_000_000m,
                await inventory.SupplierBalanceAsync(container.Id));
            Check("while a save that only moved the balance leaves the payment pile alone",
                (await db.SupplierPayments.ToListAsync()).Count == 3,
                "payments: " + (await db.SupplierPayments.CountAsync()));
        }
        await inventory.UpdateImportDetailsAsync(container.Id, "Yiwu Trading", 10_000_000.01m, null, 999m);
        await using (var db = await factory.CreateDbContextAsync())
        {
            var c = await db.Containers.SingleAsync(x => x.Id == container.Id);
            Eq("an empty paid box touches no payment at all", 4_150_000m, await inventory.PaidSoFarAsync(container.Id));
            Check("and the fields this form no longer shows keep the figures creation gave them",
                c.Cartons == 1_200m && c.Cbm == 8.5m,
                "cartons " + c.Cartons + ", cbm " + c.Cbm);
            Eq("while the weight still saves", 999m, c.WeightKg ?? 0m);
        }

        // A payment larger than the figure on the container is refused on the page, the same way a
        // customer is never allowed to pay a bill by one paisa more.
        await Throws<InvalidOperationException>("one paisa past what the container says is owed is refused",
            () => inventory.PaySupplierAsync(container.Id, DateTime.Today, 10_000_000.02m, "TT", null));
        Eq("and the refusal wrote no payment at all", 4_150_000m, await inventory.PaidSoFarAsync(container.Id));
        await inventory.PaySupplierAsync(container.Id, DateTime.Today, 4_000_000m, "TT", "what the box said");
        await inventory.UpdateImportDetailsAsync(container.Id, "Yiwu Trading", 10_000_000.01m, null, 888m);
        await using (var db = await factory.CreateDbContextAsync())
        {
            var c = await db.Containers.SingleAsync(x => x.Id == container.Id);
            Eq("a paying container still saves an untouched field, and the bill grows with it",
                18_150_000.01m, c.SupplierAmount);
            Eq("the payment the page recorded is left exactly as it was", 8_150_000m,
                await inventory.PaidSoFarAsync(container.Id));
            Eq("and the owed figure is the box, whatever the pile under it looks like", 10_000_000.01m,
                await inventory.SupplierBalanceAsync(container.Id));
        }

        Head("a shop expense keeps what was typed");
        await shop.AddAsync(DateTime.Today, "Rent", 25_000.009m, null);
        await using (var db = await factory.CreateDbContextAsync())
        {
            Eq("Rs 25,000.009 is stored as 25,000.01", 25_000.01m, (await db.ShopExpenses.SingleAsync()).Amount);
        }

        Head("an order sheet: what was saved is what the sheet showed");
        // The sheet as it looks while you type, worked out here so the saved one can be compared to it.
        var live = new BuyPlanRow
        {
            YenRate = 1.0701m,
            ExpensePkr = 100_000.005m,
            Lines = new List<BuyPlanLineRow>
            {
                new() { ItemName = "LED bulb", Quantity = 250m, UnitCostYen = 1m, UnitWeightKg = 0.375m, SalePricePkr = 450m },
                new() { ItemName = "Charger", Quantity = 40m, UnitCostYen = 640.5m, UnitWeightKg = 0.12m, SalePricePkr = 1999.99m }
            }
        };
        live.RefreshTotals();
        var planId = (await plans.CreateAsync("AUDIT sheet")).Id;
        await plans.SaveAsync(planId, "AUDIT sheet", 1.0701m, 100_000.005m, new List<BuyPlanLineInput>
        {
            new() { ItemName = "LED bulb", Quantity = 250m, UnitCostYen = 1m, UnitWeightKg = 0.375m, SalePricePkr = 450m },
            new() { ItemName = "Charger", Quantity = 40m, UnitCostYen = 640.5m, UnitWeightKg = 0.12m, SalePricePkr = 1999.99m }
        });
        var saved = await plans.GetAsync(planId);
        Eq("the yen rate is kept to six decimals", 1.0701m, saved.YenRate);
        Eq("the expense to two", 100_000.01m, saved.ExpensePkr);
        Eq("a per-piece weight to three", 0.375m, saved.Lines[0].UnitWeightKg);
        Eq("the re-opened sheet costs what the live sheet cost", live.Total.CostPkr, saved.Total.CostPkr);
        Eq("and sells for what it sold for", live.Total.SalePkr, saved.Total.SalePkr);
        Eq("and profits by the same", live.Total.ProfitPkr, saved.Total.ProfitPkr);
        Eq("a row's own cost survives the round trip", live.Lines[1].CostPkr, saved.Lines[1].CostPkr);
        Eq("and so does the one expense figure", live.Total.ExpensePkr, saved.Total.ExpensePkr);
        var copied = await plans.GetAsync((await plans.DuplicateAsync(planId)).Id);
        Eq("a duplicate carries the same figures", saved.Total.ProfitPkr, copied.Total.ProfitPkr);
        await plans.DeleteAsync(planId);
        await using (var db = await factory.CreateDbContextAsync())
        {
            Check("deleting a sheet takes its rows with it - no orphans left to sum",
                !await db.BuyPlanLines.AnyAsync(l => l.PlanId == planId));
        }

        Head("the guards refuse before anything is written");
        await Throws<InvalidOperationException>("a container needs a title",
            () => inventory.CreateContainerAsync("   ", null, "China", null, null, null, null, null, null, null, null, null, 0m, 0m, null));
        await Throws<InvalidOperationException>("a payment with no supplier name is refused, not half-saved",
            () => inventory.CreateContainerAsync("No supplier", null, "China", DateTime.Today, null, null, null, null, null, null, null, null, 100m, 50m, null));
        await Throws<InvalidOperationException>("a payment of Rs 0.004 is refused rather than recorded as zero",
            () => inventory.PaySupplierAsync(container.Id, DateTime.Today, 0.004m, "Cash", null));
        await Throws<InvalidOperationException>("a zero container expense is refused",
            () => inventory.AddExpenseAsync(container.Id, DateTime.Today, "Labour", 0m, null));
        await Throws<InvalidOperationException>("a negative cost is refused",
            () => inventory.AddGoodsAsync(container.Id, "Bad", "pcs", null, 5m, -1m, null, null, null, null, null));

        await EverythingIsExactMoney(factory);
    }

    /// <summary>Everything the till has handed back, across the whole database.</summary>
    /// <summary>Every rupee the shop has handed to a customer from the till, newest rule aside: this is
    /// the figure the We Owe page's history panel and the Main ledger's outflow have to agree with.</summary>
    private static async Task<decimal> PaidOutTotalAsync(IDbContextFactory<AppDbContext> factory)
    {
        await using var db = await factory.CreateDbContextAsync();
        var lines = await db.CashBook.AsNoTracking()
            .Where(e => e.Kind == CashBookKind.CustomerOut)
            .ToListAsync();
        return lines.Sum(e => e.AmountOut);
    }

    /// <summary>Cash in hand as the Main ledger works it out - every line in, less every line out.</summary>
    private static async Task<decimal> CashInHandAsync(IDbContextFactory<AppDbContext> factory)
    {
        await using var db = await factory.CreateDbContextAsync();
        var lines = await db.CashBook.AsNoTracking().ToListAsync();
        return lines.Sum(e => e.AmountIn - e.AmountOut);
    }

    private static async Task<decimal> RefundedTotalAsync(IDbContextFactory<AppDbContext> factory)
    {
        await using var db = await factory.CreateDbContextAsync();
        var lines = await db.CashBook.AsNoTracking()
            .Where(e => e.Kind == CashBookKind.RefundOut)
            .ToListAsync();
        return lines.Sum(e => e.AmountOut);
    }

    private static async Task<decimal> PersistedBill(IDbContextFactory<AppDbContext> factory, int saleId)
    {
        await using var db = await factory.CreateDbContextAsync();
        return (await db.Sales.SingleAsync(s => s.Id == saleId)).TotalAmount;
    }

    // ------------------------------------------------------------------ storage

    private static void Storage(string dir)
    {
        Head("how far a huge amount can be trusted in each kind of column");
        Console.WriteLine("  note  a fresh install stores money as exact text; a database upgraded from an");
        Console.WriteLine("        older release stores some of it as a float (REAL). Both are checked here.");
        var probe = Path.Combine(dir, "probe.db");
        using var con = new SqliteConnection($"Data Source={probe};Mode=ReadWriteCreate");
        con.Open();
        using (var cmd = con.CreateCommand())
        {
            cmd.CommandText = "CREATE TABLE t (TextAmount TEXT, RealAmount REAL);";
            cmd.ExecuteNonQuery();
        }

        foreach (var v in new[] { 900_000_000.07m, 900_000_000_000.07m, 9_000_000_000_000.05m, 900_000_000_000_000.01m })
        {
            using (var ins = con.CreateCommand())
            {
                ins.CommandText = "INSERT INTO t (TextAmount, RealAmount) VALUES ($t, $r);";
                ins.Parameters.AddWithValue("$t", v.ToString(CultureInfo.InvariantCulture));
                ins.Parameters.AddWithValue("$r", (double)v);
                ins.ExecuteNonQuery();
            }
            using var q = con.CreateCommand();
            q.CommandText = "SELECT TextAmount, RealAmount FROM t ORDER BY rowid DESC LIMIT 1;";
            using var r = q.ExecuteReader();
            r.Read();
            var text = decimal.Parse(r.GetString(0), CultureInfo.InvariantCulture);
            var real = Convert.ToDecimal(r.GetValue(1), CultureInfo.InvariantCulture);
            var label = v.ToString(CultureInfo.InvariantCulture);
            Check($"text column keeps {label} exactly", text == v, text.ToString(CultureInfo.InvariantCulture));
            Warn($"float column keeps {label}", real == v,
                real == v ? "exact"
                          : "came back as " + real.ToString(CultureInfo.InvariantCulture) +
                            ", off by " + (v - real).ToString("0.###", CultureInfo.InvariantCulture));
        }
    }

    /// <summary>The invariant the whole app leans on: no money figure in the database has a third decimal.</summary>
    private static async Task EverythingIsExactMoney(IDbContextFactory<AppDbContext> factory)
    {
        Head("the day the goods actually landed");
        Throws<InvalidOperationException>("a container is not created on a guessed arrival date",
            () => inventory.CreateContainerAsync("No date", null, "China", null, null, null, null, null, null, null, null, null, 0m, 0m, null));
        var landed = await inventory.CreateContainerAsync("Back-dated", "CNT-0002", "China",
            new DateTime(2026, 3, 14), null, null, null, null, null, null, null, null, 0m, 0m, null);
        Check("a date in the past is kept exactly as written, not pushed to today",
            landed.ArrivalDate == new DateTime(2026, 3, 14), "stored " + landed.ArrivalDate);
        await inventory.UpdateImportDetailsAsync(landed.Id, null, landed.SupplierAmount, null, null,
            new DateTime(2026, 3, 20));
        await using (var dbDate = await factory.CreateDbContextAsync())
        {
            var corrected = await dbDate.Containers.AsNoTracking().SingleAsync(x => x.Id == landed.Id);
            Check("and the container page can put the day right afterwards",
                corrected.ArrivalDate == new DateTime(2026, 3, 20), "stored " + corrected.ArrivalDate);
            // a save that does not show the date must not lose it
            await inventory.UpdateImportDetailsAsync(landed.Id, null, corrected.SupplierAmount, null, null);
            var again = await dbDate.Containers.AsNoTracking().SingleAsync(x => x.Id == landed.Id);
            Check("a save that never mentions the date keeps the date it found",
                again.ArrivalDate == new DateTime(2026, 3, 20), "stored " + again.ArrivalDate);
        }

        Head("the figure typed on the container form is what the shop owes");
        // "Goods worth 20 lac, 20 lac handed over now" is typed as: we owe 20 lac, paid 20 lac. The paid
        // figure is a payment, not a reduction of the shopkeeper's own number, so the page must still say
        // 20 lac owed - which is the whole rule, tested at the number.
        var typed = await inventory.CreateContainerAsync("Typed balance", "CNT-0003", "China",
            new DateTime(2026, 4, 2), null, null, null, null, null, null, null, "Yiwu Trading",
            500_000m, 500_000m, "Cash");
        Eq("the bill is stored as that figure plus the money handed over", 1_000_000m, typed.SupplierAmount);
        var typedRow = (await cash.SupplierContainersAsync()).Single(t => t.Id == typed.Id);
        Eq("and We owe shows the figure that was typed", 500_000m, typedRow.Owed);
        Check("in the shop's words, not in a netted-down one",
            typedRow.Label.Contains("owe " + Money.Pkr(500_000m)), typedRow.Label);
        await inventory.PaySupplierAsync(typed.Id, new DateTime(2026, 4, 3), 500_000m, "Cash", null);
        Check("paying that figure settles the container",
            (await cash.SupplierContainersAsync()).Single(t => t.Id == typed.Id).Label.EndsWith("settled"));
        await Throws<InvalidOperationException>("and a paisa more is refused, because nothing is owed",
            () => inventory.PaySupplierAsync(typed.Id, new DateTime(2026, 4, 4), 0.01m, "Cash", null));

        // The other half: the money handed over at creation must reach the supplier's list and the till
        // exactly once. A payment filed twice is the other way a container starts owing nothing and
        // reading as overpaid.
        await using (var dbAll = await factory.CreateDbContextAsync())
        {
            var pays = (await dbAll.SupplierPayments.AsNoTracking().ToListAsync()).Select(p => p.Id).ToList();
            var links = (await dbAll.CashBook.AsNoTracking()
                .Where(e => e.Kind == CashBookKind.SupplierOut && e.SupplierPaymentId != null).ToListAsync())
                .Select(e => e.SupplierPaymentId!.Value).ToList();
            Check("one till line per supplier payment, no line without a payment, no payment without a line",
                links.Count == pays.Count && links.Distinct().Count() == links.Count && pays.All(links.Contains),
                pays.Count + " payments, " + links.Count + " till lines");
        }

        Head("the return rule: their debt first, the cash for what is left over");
        var askBill = await sales.CreateSaleAsync(customer.Id, DateTime.Today, new List<NewSaleLineInput>
        {
            new() { ContainerId = container.Id, ContainerItemId = chargers.Id, ProductId = chargers.ProductId, ProductName = "Charger", Unit = "pcs", Quantity = 2m, UnitPrice = 1999.99m }
        }, 3_499.98m, "Cash", null, 0m, null);
        var askLine = askBill.Lines.Single(l => l.ProductId == chargers.ProductId);
        var asked = new List<SaleReturnInput> { new() { SaleLineId = askLine.Id, Quantity = 1m } };
        var preview = await sales.PreviewReturnAsync(askBill.Id, asked);
        Eq("the goods are credited back on their ledger", 1_999.99m, preview.Credit);
        Eq("and only what the debt cannot absorb is paid from the cashbook", 1_499.99m, preview.Cash);
        Check("so the page can say both halves, in rupees, before anything is pressed",
            SalesService.DescribeReturn(preview.Credit, preview.Cash).Contains("Rs 500.00 comes off")
            && SalesService.DescribeReturn(preview.Credit, preview.Cash).Contains("paid out of the cashbook"),
            SalesService.DescribeReturn(preview.Credit, preview.Cash));
        var posted = await sales.ReturnItemsAsync(askBill.Id, asked);
        Eq("and posting pays out exactly what that line promised", preview.Cash, posted);
        await using (var dbAsk = await factory.CreateDbContextAsync())
        {
            var led = await dbAsk.LedgerEntries.Where(e => e.SaleId == askBill.Id).ToListAsync();
            Eq("their ledger shows the goods coming back", 1_999.99m,
                led.Where(e => e.Type == LedgerType.Return).Sum(e => e.Credit - e.Debit));
            Eq("and the cash going out, as its own line", 1_499.99m,
                led.Where(e => e.Type == LedgerType.Adjustment).Sum(e => e.Debit - e.Credit));
            Eq("while the bill itself is closed", 0m, await sales.RemainingOnInvoiceAsync(askBill.Id));
        }

        var second = await sales.CreateSaleAsync(customer.Id, DateTime.Today, new List<NewSaleLineInput>
        {
            new() { ContainerId = container.Id, ContainerItemId = chargers.Id, ProductId = chargers.ProductId, ProductName = "Charger", Unit = "pcs", Quantity = 2m, UnitPrice = 1999.99m }
        }, 3_499.98m, "Cash", null, 0m, null);
        var secondLine = second.Lines.Single(l => l.ProductId == chargers.ProductId);
        var secondAsk = new List<SaleReturnInput> { new() { SaleLineId = secondLine.Id, Quantity = 1m } };
        var preview2 = await sales.PreviewReturnAsync(second.Id, secondAsk);
        var paidOut = await sales.ReturnItemsAsync(second.Id, secondAsk);
        Eq("the same shape of bill settles the same way, to the paisa", preview2.Cash, paidOut);
        Check("the same figures again on a second bill, so the preview is not a promise the posting breaks",
            preview2.Credit == preview.Credit && preview2.Cash == paidOut,
            "preview " + preview2.Cash + ", posted " + paidOut);
        await using (var dbAsk2 = await factory.CreateDbContextAsync())
            Check("looking at a return wrote nothing on its own: one return per bill",
                (await dbAsk2.SaleReturns.Where(r => r.SaleId == askBill.Id).ToListAsync()).Count == 1);

        Head("paying a customer back: their book first, and the till by the same figure");
        var adv = await ledger.CreateCustomerAsync("Advance Cartage", "0344-1112233", null, null);
        var owesUs = await ledger.CreateCustomerAsync("Still Owes Traders", null, null, null);
        await ledger.SetOpeningBalanceAsync(adv.Id, -5_000m);      // money they left sitting with the shop
        await ledger.SetOpeningBalanceAsync(owesUs.Id, 2_000m);     // money they still owe us
        var inHandBefore = await CashInHandAsync(factory);
        var refundsSoFar = await RefundedTotalAsync(factory);
        var homeBefore = await reports.GetHomeMonthAsync();
        Eq("an advance reads on We owe as what we owe them, without the minus sign",
            5_000m, (await ledger.GetCustomerOwedAsync()).Single(r => r.CustomerId == adv.Id).Owed);
        Check("and a customer who still owes us is not on that page at all",
            (await ledger.GetCustomerOwedAsync()).All(r => r.CustomerId != owesUs.Id));
        await Throws<InvalidOperationException>(
            "paying out to someone who owes us is refused outright, not netted against their debt",
            () => ledger.PayCustomerAsync(owesUs.Id, DateTime.Today, 500m, "Cash", null));
        Check("and the refusal wrote nothing", (await ledger.ListPayoutsAsync(owesUs.Id)).Count == 0);

        await ledger.PayCustomerAsync(adv.Id, DateTime.Today, 1_000.004m, "Cash", "part, handed at the shop");
        var payoutSum = (await ledger.ListPayoutsAsync(adv.Id)).Sum(p => p.Amount);
        Eq("a payout keeps the figure to the paisa, like every other money box", 1_000m, payoutSum);
        Eq("their balance moves towards nothing by exactly that", -4_000m, await ledger.GetBalanceAsync(adv.Id));
        Eq("the till is lighter by the same figure, and by nothing else",
            inHandBefore - 1_000m, await CashInHandAsync(factory));
        Eq("so what We owe shows is the four thousand left, not the five thousand it started at",
            4_000m, (await ledger.GetCustomerOwedAsync()).Single(r => r.CustomerId == adv.Id).Owed);
        Eq("a payout is never counted as a refund of a bill, so the two figures stay separate",
            refundsSoFar, await RefundedTotalAsync(factory));
        var advRow = (await ledger.GetLedgerAsync(adv.Id)).Single(r => r.Type == LedgerType.Payout);
        Check("their page keeps it out of \"Sold\" and shows it under \"Paid out\"",
            advRow.SoldText == "\u2014" && advRow.PaidOutText == Money.Pkr(1_000m),
            advRow.SoldText + " / " + advRow.PaidOutText);

        await using (var dbOut = await factory.CreateDbContextAsync())
        {
            var till = await dbOut.CashBook.Where(e => e.Kind == CashBookKind.CustomerOut).ToListAsync();
            Check("one till line, as money out, naming who was paid and how",
                till.Count == 1 && till.Sum(e => e.AmountOut) == 1_000m && till.Sum(e => e.AmountIn) == 0m
                && till.All(e => e.Description.Contains("Advance Cartage") && e.Description.Contains("Cash")),
                till.Count + " till lines");
            Check("it is not tied to a payment, so deleting one of their payments can never take it away",
                till.All(e => e.PaymentId == null));
            var led = await dbOut.LedgerEntries.Where(e => e.Type == LedgerType.Payout).ToListAsync();
            Check("and their ledger carries it as a debit, which is what pulls their balance up",
                led.Count == 1 && led.Sum(e => e.Debit - e.Credit) == 1_000m, led.Count + " lines");
            Check("the We Owe page's note is on that line too",
                led.Count == 1 && led.All(e => e.Description.Contains("part, handed at the shop")),
                string.Join(" | ", led.Select(e => e.Description)));
            Check("no payment row was invented, so nothing on their bills moved",
                (await dbOut.Payments.Where(p => p.CustomerId == adv.Id).ToListAsync()).Count == 0);
        }

        await Throws<InvalidOperationException>("one paisa past what their book holds is refused",
            () => ledger.PayCustomerAsync(adv.Id, DateTime.Today, 4_000.01m, "Cash", null));
        await ledger.PayCustomerAsync(adv.Id, DateTime.Today, 4_000m, "Bank Transfer", "cleared the advance");
        Eq("paying the whole of it leaves nothing on either side", 0m, await ledger.GetBalanceAsync(adv.Id));
        Check("so they are no longer owed, while the money handed over stays visible where it was paid",
            (await ledger.GetCustomerOwedAsync()).Any(r => r.CustomerId == adv.Id
                && r.Owed == 0m && r.PaidOut == 5_000m));
        var payoutLines = await ledger.ListPayoutsAsync(adv.Id);
        Check("both payouts are on the record, newest first, and add back to the advance",
            payoutLines.Count == 2 && payoutLines[0].Amount == 4_000m && payoutLines[1].Amount == 1_000m
            && payoutLines.Sum(p => p.Amount) == 5_000m,
            payoutLines.Count + " rows, " + string.Join(" + ", payoutLines.Select(p => p.Amount.ToString("0.00"))));
        Eq("the till has paid out the whole advance and no more", 5_000m, await PaidOutTotalAsync(factory));
        await using (var dbAdv = await factory.CreateDbContextAsync())
        {
            var linesAdv = await dbAdv.LedgerEntries.Where(e => e.CustomerId == adv.Id).ToListAsync();
            Eq("their account end to end: an advance in, two payouts out, the book at nothing",
                0m, linesAdv.Sum(e => e.Debit - e.Credit));
        }
        var homeAfter = await reports.GetHomeMonthAsync();
        Eq("money paid to a customer settles a debt, it is not an expense, so profit did not move",
            homeBefore.Profit, homeAfter.Profit);
        Eq("and the billed money Home shows is untouched either", homeBefore.Sales, homeAfter.Sales);

        Head("the order the book is read in");
        var customerLedger = await ledger.GetLedgerAsync(customer.Id);
        Check("the book hands a customer's lines over in the order they were made - by day, and within a day in writing order",
            customerLedger.Zip(customerLedger.Skip(1), (a, b) => a.Date.Date < b.Date.Date
                || (a.Date.Date == b.Date.Date && a.Id < b.Id)).All(x => x),
            customerLedger.Count + " lines");
        Check("and numbers them step by step from the first line of the account",
            customerLedger.First().Step == 1 && customerLedger[^1].Step == customerLedger.Count,
            "steps " + customerLedger.First().Step + " to " + customerLedger[^1].Step);
        Eq("so the last line's running figure is the balance at the head of the page",
            await ledger.GetBalanceAsync(customer.Id), customerLedger[^1].RunningBalance);
        Check("and every line in between adds up to it, one step at a time",
            customerLedger.Skip(1).Zip(customerLedger, (now, before) =>
                now.RunningBalance - before.RunningBalance == now.Debit - now.Credit).All(x => x));
        var tillRows = await cash.ListAsync();
        Check("the till hands its rows over in the order the money moved, so reversing it for the page is safe",
            tillRows.SequenceEqual(tillRows.OrderBy(e => e.Date.Date).ThenBy(e => e.Id)),
            tillRows.Count + " lines");

        Head("scanning every money figure that ended up in the database");
        var bad = new List<string>();
        await using var db = await factory.CreateDbContextAsync();

        void Scan<T>(string what, List<T> rows, Func<T, (string, decimal)[]> pick)
        {
            foreach (var row in rows)
                foreach (var (name, value) in pick(row))
                    if (Money.Round(value) != value)
                        bad.Add($"{what}.{name} = {value}");
        }

        Scan("SaleLine", await db.SaleLines.ToListAsync(), x => new[] { ("UnitPrice", x.UnitPrice), ("UnitCost", x.UnitCost), ("LineTotal", x.LineTotal), ("LineCost", x.LineCost) });
        Scan("Sale", await db.Sales.ToListAsync(), x => new[] { ("TotalAmount", x.TotalAmount), ("PaidNow", x.PaidNow), ("DiscountAmount", x.DiscountAmount) });
        Scan("Payment", await db.Payments.ToListAsync(), x => new[] { ("Amount", x.Amount) });
        Scan("LedgerEntry", await db.LedgerEntries.ToListAsync(), x => new[] { ("Debit", x.Debit), ("Credit", x.Credit) });
        Scan("SaleReturn", await db.SaleReturns.ToListAsync(), x => new[] { ("Amount", x.Amount) });
        Scan("SaleReturnLine", await db.SaleReturnLines.ToListAsync(), x => new[] { ("Amount", x.Amount), ("UnitPrice", x.UnitPrice), ("UnitCost", x.UnitCost) });
        Scan("SupplierPayment", await db.SupplierPayments.ToListAsync(), x => new[] { ("Amount", x.Amount) });
        Scan("CustomerPayout", await db.CustomerPayouts.ToListAsync(), x => new[] { ("Amount", x.Amount) });
        Scan("ContainerExpense", await db.Expenses.ToListAsync(), x => new[] { ("Amount", x.Amount) });
        Scan("ShopExpense", await db.ShopExpenses.ToListAsync(), x => new[] { ("Amount", x.Amount) });
        Scan("CashBookEntry", await db.CashBook.ToListAsync(), x => new[] { ("AmountIn", x.AmountIn), ("AmountOut", x.AmountOut) });
        Scan("ContainerItem", await db.ContainerItems.ToListAsync(), x => new[] { ("UnitCost", x.UnitCost), ("ForeignCost", x.ForeignCost), ("LandedUnitCost", x.LandedUnitCost) });
        Scan("Container", await db.Containers.ToListAsync(), x => new[] { ("SupplierAmount", x.SupplierAmount) });
        Scan("Product", await db.Products.ToListAsync(), x => new[] { ("LastSalePrice", x.LastSalePrice ?? 0m) });
        Scan("BuyPlanLine", await db.BuyPlanLines.ToListAsync(), x => new[] { ("UnitCostYen", x.UnitCostYen), ("SalePricePkr", x.SalePricePkr) });
        Scan("BuyPlan", await db.BuyPlans.ToListAsync(), x => new[] { ("ExpensePkr", x.ExpensePkr) });
        Check("nothing stored has a third decimal, so printed = stored = summed", bad.Count == 0, string.Join("; ", bad));
    }

    // ------------------------------------------------------------------ the year statement

    /// <summary>
    /// A year read as a statement, on a database of its own so every figure below is one written by hand
    /// rather than inherited from the flow above. The money is spread over three years on purpose: a year
    /// statement gets missed in exactly three ways - a figure landing in the wrong January, a closing
    /// balance that forgot the years before it, and a quiet month vanishing from the table instead of being
    /// reported as nothing - and all three are only visible when there is a year on each side.
    /// </summary>
    private static async Task YearStatement(string dir)
    {
        var file = Path.Combine(dir, "year.db");
        var services = new ServiceCollection();
        services.AddDbContextFactory<AppDbContext>(o => o.UseSqlite($"Data Source={file};Cache=Shared;Mode=ReadWriteCreate"));
        var sp = services.BuildServiceProvider();
        var f = sp.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using (var db = await f.CreateDbContextAsync())
        {
            await db.Database.EnsureCreatedAsync();
        }

        var inventory = new InventoryService(f);
        var sales = new SalesService(f);
        var ledger = new LedgerService(f);
        var reports = new ReportService(f);
        var cash = new CashBookService(f);
        var shop = new ShopExpenseService(f);
        var print = new PrintService();

        Head("a year read as a statement: what moved, what it closed with, and what a quiet month looks like");
        var ycontainer = await inventory.CreateContainerAsync("YEAR container", "CNT-Y1", "China",
            new DateTime(2025, 12, 1), null, "PKR", 1, null, null, null, null, null, 0m, 0m, null);
        var pump = await inventory.AddGoodsAsync(ycontainer.Id, "Water pump", "pcs", "WP-1", 40m, 1_000m,
            null, null, null, null, null);
        var buyer = await ledger.CreateCustomerAsync("Year buyer", null, null, null);

        await sales.CreateSaleAsync(buyer.Id, new DateTime(2025, 12, 20), new List<NewSaleLineInput>
        {
            new() { ContainerId = ycontainer.Id, ContainerItemId = pump.Id, ProductId = pump.ProductId, ProductName = "Water pump", Unit = "pcs", Quantity = 1m, UnitPrice = 1_000m }
        }, 1_000m, "Cash", null, 0m, null);
        var janBill = await sales.CreateSaleAsync(buyer.Id, new DateTime(2026, 1, 15), new List<NewSaleLineInput>
        {
            new() { ContainerId = ycontainer.Id, ContainerItemId = pump.Id, ProductId = pump.ProductId, ProductName = "Water pump", Unit = "pcs", Quantity = 2m, UnitPrice = 2_500m }
        }, 2_000m, "Cash", null, 0m, null);
        await ledger.ReceivePaymentAsync(buyer.Id, new DateTime(2026, 2, 10), 3_500m, "Cash", "advance, no bill yet");
        await shop.AddAsync(new DateTime(2026, 3, 5), "Shop rent", 1_000m, null);

        var janLine = janBill.Lines.Single();
        var returned = await sales.ReturnItemsAsync(janBill.Id,
            new List<SaleReturnInput> { new() { SaleLineId = janLine.Id, Quantity = 1m } });
        Eq("a return on that bill moves no cash, because the bill itself was still 3,000 short",
            0m, returned);
        // A return can only be made today, so the fixture moves the row the app wrote into June. What is
        // under test is a statement grouping the dates it is given, not who typed them in.
        await using (var dbMove = await f.CreateDbContextAsync())
        {
            var ret = await dbMove.SaleReturns.SingleAsync(r => r.SaleId == janBill.Id);
            ret.Date = new DateTime(2026, 6, 18);
            await dbMove.SaveChangesAsync();
        }
        // The advance is on their account, not on the bill, so the return became credit and left three
        // thousand of their money sitting with the shop - which is the case the pay form exists for.
        Eq("their book says three thousand of theirs is now sitting with the shop",
            -3_000m, await ledger.GetBalanceAsync(buyer.Id));
        await ledger.PayCustomerAsync(buyer.Id, new DateTime(2026, 6, 20), 3_000m, "Cash", "advance settled");
        await sales.CreateSaleAsync(buyer.Id, new DateTime(2026, 12, 31), new List<NewSaleLineInput>
        {
            new() { ContainerId = ycontainer.Id, ContainerItemId = pump.Id, ProductId = pump.ProductId, ProductName = "Water pump", Unit = "pcs", Quantity = 2m, UnitPrice = 1_100m }
        }, 0m, "Cash", null, 0m, null);
        await sales.CreateSaleAsync(buyer.Id, new DateTime(2027, 1, 1), new List<NewSaleLineInput>
        {
            new() { ContainerId = ycontainer.Id, ContainerItemId = pump.Id, ProductId = pump.ProductId, ProductName = "Water pump", Unit = "pcs", Quantity = 1m, UnitPrice = 900m }
        }, 900m, "Cash", null, 0m, null);

        var y26 = await cash.GetYearCashAsync(2026);
        Check("a year is twelve months and one line for the year itself", y26.Count == 13, y26.Count + " rows");
        Check("the last line is headed as the year's total, not as a month that never happened",
            y26[^1].IsTotal && y26[^1].MonthText == "Total 2026" && y26[11].MonthText == "December",
            y26[^1].MonthText + " / " + y26[11].MonthText);
        Check("and it is the only line the page marks out, so a total cannot be mistaken for January's neighbour",
            y26.Count(r => r.Tint) == 1 && y26.Count(r => r.Bold) == 1 && !y26[11].Tint && !y26[0].Bold,
            y26.Count(r => r.Tint) + " tinted rows");
        Eq("January's money in is the January bill's payment, and nothing else", 2_000m, y26[0].CashIn);
        Eq("and January closed on what the year brought in plus that", 3_000m, y26[0].Closing);
        Eq("February counts the advance the day it arrived", 3_500m, y26[1].CashIn);
        Eq("March paid the rent out", 1_000m, y26[2].CashOut);
        Eq("April did nothing, and says so without pretending the till was empty", 5_500m, y26[3].Closing);
        Eq("June's money out is the payout, and the payout alone", 3_000m, y26[5].CashOut);
        Eq("while the goods that came back the same month are shown beside it, added to neither column",
            2_500m, y26[5].Returns);
        Eq("so June closed on the money that is actually left", 2_500m, y26[5].Closing);
        Eq("the year in: two thousand and the advance", 5_500m, y26[^1].CashIn);
        Eq("the year out: the rent and one payout", 4_000m, y26[^1].CashOut);
        Eq("and the year's own line is the twelve months added up, not a second figure worked out aside",
            y26.Where(r => !r.IsTotal).Sum(r => r.CashIn), y26[^1].CashIn);
        Eq("so the year's closing is the thousand it was handed, plus the difference it made",
            1_000m, y26[^1].Closing - (y26[^1].CashIn - y26[^1].CashOut));
        Eq("December did not reach into the next year's January", 0m, y26[11].CashIn);
        Eq("and the year's closing is not cash in hand, because 2027 has money in it",
            2_500m, y26[11].Closing);
        Eq("while cash in hand is every year together", 3_400m, await CashInHandAsync(f));
        var y27 = await cash.GetYearCashAsync(2027);
        Eq("the next year's January takes only next January's money", 900m, y27[0].CashIn);
        Eq("and by its December the whole book agrees with the till", 3_400m, y27[11].Closing);
        var y25 = await cash.GetYearCashAsync(2025);
        Eq("a year before the shop's first money opens at nothing", 0m, y25[0].Closing);
        Eq("and 2025's December is the thousand that 2026 then carried forward", 1_000m, y25[11].CashIn);

        var s26 = await reports.GetYearSalesAsync(2026);
        Eq("January sold two pumps at 2,500", 5_000m, s26[0].Sold);
        Eq("at a cost of 1,000 apiece", 2_000m, s26[0].Cogs);
        Eq("so the month's profit is the difference, which the table can be checked against", 3_000m, s26[0].Profit);
        Eq("one bill", 1m, s26[0].Bills);
        Eq("February brought money in without writing a bill", 3_500m, s26[1].Received);
        Eq("and the February money did not close January's bill, because it was never pointed at it",
            500m, s26[0].StillOwed);
        Eq("June has only a return in it, so June sells a negative figure - Home's rule, not a second one",
            -2_500m, s26[5].Sold);
        Eq("its cost comes back with the goods", -1_000m, s26[5].Cogs);
        Eq("and the returned value is on the return column of its own", 2_500m, s26[5].Returned);
        Eq("December sold two pumps at 1,100", 2_200m, s26[11].Sold);
        Eq("and its unpaid bill is still owed, as a figure of its own", 2_200m, s26[11].StillOwed);
        Eq("so December made 200 of them", 200m, s26[11].Profit);
        Eq("sold across the year, returns and all", 4_700m, s26[^1].Sold);
        Eq("profit across the year", 1_700m, s26[^1].Profit);
        Eq("money that arrived across the year", 5_500m, s26[^1].Received);
        Eq("and what their bills still have owing at the end of it", 2_700m, s26[^1].StillOwed);
        Eq("the year's profit being the twelve months' profits, added, and its sold money less their cost",
            Money.Round(s26.Where(r => !r.IsTotal).Sum(r => r.Profit)), s26[^1].Profit);
        Eq("so the foot of the table can be checked against the column above it",
            Money.Round(s26[^1].Sold - s26[^1].Cogs), s26[^1].Profit);
        Eq("while that same customer's own book stands at nothing, the payout having cleared it",
            0m, await ledger.GetBalanceAsync(buyer.Id));
        var c26 = await shop.GetYearAsync(2026);
        Eq("the rent is March's", 1_000m, c26[2].Amount);
        Check("and a quiet month says so with a count, not only a dash", c26[3].Count == 0 && c26[2].Count == 1);
        // The failure this guards against is the quiet one: a year line whose heading is not read, so it
        // falls back on a month's number and arrives at the foot of the table claiming to be December.
        var yMonths = y26.Where(r => !r.IsTotal).Select(r => r.MonthText).ToList();
        var sMonths = s26.Where(r => !r.IsTotal).Select(r => r.MonthText).ToList();
        var cMonths = c26.Where(r => !r.IsTotal).Select(r => r.MonthText).ToList();
        Check("no year line in any of the three books can be read as one of the months",
            !yMonths.Contains(y26[^1].MonthText) && !sMonths.Contains(s26[^1].MonthText)
            && !cMonths.Contains(c26[^1].MonthText),
            y26[^1].MonthText + " / " + s26[^1].MonthText + " / " + c26[^1].MonthText);
        Check("the selling table and the costs table each mark out one line only, their own",
            s26.Count(r => r.Tint) == 1 && c26.Count(r => r.Tint) == 1
            && s26[^1].MonthText == "Total 2026" && c26[^1].MonthText == "Total 2026",
            s26.Count(r => r.Tint) + " / " + c26.Count(r => r.Tint));
        Eq("the year's costs", 1_000m, c26[^1].Amount);
        Eq("and the count of lines with them, so an empty year cannot look like a year of zero-cost lines",
            1, c26[^1].Count);
        Eq("so what stands at the end of the year is the profit on those goods, less the costs",
            700m, s26[^1].Profit - c26[^1].Amount);

        // The month box on a customer's page, held to the fixture this file already derived by hand:
        // receipts dated in the month and nothing else. June's payout and June's return credit both moved
        // this customer's book, and neither of them is money collected.
        var (jan, janCount, janRows) = await ledger.GetReceiptsAsync(buyer.Id, 2026, 1);
        Eq("January's receipts are the payment against the bill, and nothing else", 2_000m, jan);
        Check("one line, so the figure beside it can be added up by hand", janCount == 1 && janRows.Count == 1,
            janCount + " lines");
        Eq("February's are the advance, which was not against any bill",
            3_500m, (await ledger.GetReceiptsAsync(buyer.Id, 2026, 2)).Amount);
        Eq("June paid money out and took goods back, and still collected nothing",
            0m, (await ledger.GetReceiptsAsync(buyer.Id, 2026, 6)).Amount);
        Eq("December collected nothing on a bill that was never paid",
            0m, (await ledger.GetReceiptsAsync(buyer.Id, 2026, 12)).Amount);
        Eq("the month before the year keeps its own receipt, and the January after the year is not this January",
            1_000m + 900m, (await ledger.GetReceiptsAsync(buyer.Id, 2025, 12)).Amount
            + (await ledger.GetReceiptsAsync(buyer.Id, 2027, 1)).Amount);
        var (allIn, allCount, allRows) = await ledger.GetReceiptsAsync(buyer.Id, null, null);
        Eq("every month together is the four receipts in the book", 7_400m, allIn);
        Check("four of them, the payout having stayed out of the list", allCount == 4 && allRows.Count == 4,
            allCount + " rows");
        Eq("and the receipts book agrees with their own ledger to the paisa, counted on its money-in lines",
            allIn, (await ledger.GetLedgerAsync(buyer.Id))
                .Where(l => l.Type == LedgerType.Payment).Sum(l => l.Credit));

        var paper = print.YearStatementHtml(2026, y26, s26, c26, new ShopSettings());
        Check("the printed year gives every month its own row under each of the three books, and no month twice",
            Count(paper, "<tr><td>") == 36
            && new[] { "January", "February", "March", "April", "May", "June", "July", "August", "September",
                "October", "November", "December" }.All(mn => Count(paper, mn) == 3),
            Count(paper, "<tr><td>") + " month rows, " + Count(paper, "June") + " Junes");
        Check("and its year lines carry the same totals the pages count up to",
            paper.Contains(Money.Pkr(4_700m)) && paper.Contains(Money.Pkr(1_700m))
            && paper.Contains(Money.Pkr(2_700m)) && paper.Contains(Money.Pkr(2_500m))
            && paper.Contains(Money.Pkr(5_500m)) && paper.Contains(Money.Pkr(1_000m)));
        Check("the paper marks the same line out under a rule, once per table, and heads it the same way",
            Count(paper, "tr class='total'") == 3 && Count(paper, "<th>Total 2026</th>") == 3
            && Count(paper, "<td>Total 2026</td>") == 0,
            Count(paper, "tr class='total'") + " total rows, " + Count(paper, "<th>Total 2026</th>") + " headings");
        Check("it says, on paper, what the year was carrying when it opened",
            paper.Contains("Brought into the year: " + Money.Pkr(1_000m)), paper);
    }

    private static async Task MonthReceipts(string dir)
    {
        var file = Path.Combine(dir, "receipts.db");
        var services = new ServiceCollection();
        services.AddDbContextFactory<AppDbContext>(o => o.UseSqlite($"Data Source={file};Cache=Shared;Mode=ReadWriteCreate"));
        var sp = services.BuildServiceProvider();
        var f = sp.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using (var db = await f.CreateDbContextAsync())
        {
            await db.Database.EnsureCreatedAsync();
        }

        var ledger = new LedgerService(f);
        var inventory = new InventoryService(f);
        var sales = new SalesService(f);
        var print = new PrintService();

        Head("one customer's month of receipts: the edges of a month, the time of day on it, and money that is not a receipt");
        var edge = await ledger.CreateCustomerAsync("Edge buyer", null, null, null);
        await ledger.ReceivePaymentAsync(edge.Id, new DateTime(2026, 1, 31, 22, 15, 0), 1_000.50m, "Cash", "evening, last day of the month");
        await ledger.ReceivePaymentAsync(edge.Id, new DateTime(2026, 2, 1, 0, 0, 0), 2_000m, "Cash", "the first minute of February");
        await ledger.ReceivePaymentAsync(edge.Id, new DateTime(2025, 12, 31, 23, 59, 59), 400m, "Cash", "the year's last minute");
        await ledger.ReceivePaymentAsync(edge.Id, new DateTime(2027, 1, 1, 0, 1, 0), 700m, "Cash", "next year's first minute");
        await ledger.ReceivePaymentAsync(edge.Id, new DateTime(2026, 3, 15), 0.05m, "Cash", "five paisa, and nothing else");
        // Money handed back to this customer, dated inside February. A month's receipts figure must not
        // answer for it: netting it off would show 1,700 for a month in which 2,000 was collected.
        await ledger.PayCustomerAsync(edge.Id, new DateTime(2026, 2, 20), 300m, "Cash", "part of the advance handed back");

        Eq("a receipt at ten fifteen at night on the last day is still in that month",
            1_000.50m, (await ledger.GetReceiptsAsync(edge.Id, 2026, 1)).Amount);
        Eq("one at midnight on the first day is in the month that opened, and the money out that month is not netted off it",
            2_000m, (await ledger.GetReceiptsAsync(edge.Id, 2026, 2)).Amount);
        Eq("a month whose only receipt is five paisa is not an empty month",
            0.05m, (await ledger.GetReceiptsAsync(edge.Id, 2026, 3)).Amount);
        Eq("the last minute of a year belongs to that year, and the first minute of the next to the next",
            400m + 700m, (await ledger.GetReceiptsAsync(edge.Id, 2025, 12)).Amount
            + (await ledger.GetReceiptsAsync(edge.Id, 2027, 1)).Amount);
        var quiet = await ledger.GetReceiptsAsync(edge.Id, 2026, 4);
        Eq("a month with nothing in it is nothing, which is an answer and not a missing line", 0m, quiet.Amount);
        Check("and it says so with no rows under it", quiet.Count == 0 && quiet.Rows.Count == 0,
            quiet.Count + " rows");

        var (whole, wholeCount, wholeRows) = await ledger.GetReceiptsAsync(edge.Id, null, null);
        Eq("every month together is the five receipts, the payout having never been one", 4_100.55m, whole);
        Check("five lines, newest first, with the time of day deciding the order within a day",
            wholeCount == 5 && wholeRows.Count == 5
            && wholeRows[0].Date.Year == 2027 && wholeRows[0].Date.Month == 1
            && wholeRows[^1].Date.Year == 2025 && wholeRows[^1].Date.Month == 12,
            wholeCount + " rows, newest dated " + (wholeRows.Count > 0
                ? wholeRows[0].Date.ToString("dd MMM yyyy HH:mm") : "nothing"));
        Eq("paisa is carried, not rounded away: five paisa of receipts reads as five paisa, not as nothing",
            5m / 100m, wholeRows.Single(r => r.Date.Month == 3).Amount);

        decimal walked = 0m;
        for (var d = new DateTime(2025, 12, 1); d <= new DateTime(2027, 1, 1); d = d.AddMonths(1))
            walked += (await ledger.GetReceiptsAsync(edge.Id, d.Year, d.Month)).Amount;
        Eq("and fourteen months walked one at a time add back to the whole book exactly, with no receipt "
           "left between two months and none counted twice", whole, Money.Round(walked));

        await Throws<ArgumentException>("a month without a year is refused, rather than quietly read as every month",
            () => ledger.GetReceiptsAsync(edge.Id, 2026, null));

        // A bill and a return on it, with nothing paid on the bill, so the receipts above are untouched:
        // this is here to give the customer's printed ledger a line of every kind it can carry.
        var mbox = await inventory.CreateContainerAsync("RECEIPTS container", "CNT-R1", "China",
            new DateTime(2026, 1, 5), null, "PKR", 1, null, null, null, null, null, 0m, 0m, null);
        var part = await inventory.AddGoodsAsync(mbox.Id, "Fan belt", "pcs", "FB-1", 10m, 600m,
            null, null, null, null, null);
        var edgeBill = await sales.CreateSaleAsync(edge.Id, new DateTime(2026, 2, 5), new List<NewSaleLineInput>
        {
            new() { ContainerId = mbox.Id, ContainerItemId = part.Id, ProductId = part.ProductId, ProductName = "Fan belt", Unit = "pcs", Quantity = 1m, UnitPrice = 1_000m }
        }, 0m, "Cash", null, 0m, null);
        await sales.ReturnItemsAsync(edgeBill.Id,
            new List<SaleReturnInput> { new() { SaleLineId = edgeBill.Lines.Single().Id, Quantity = 1m } });

        var lines = await ledger.GetLedgerAsync(edge.Id);
        Check("every line of their book puts its money in exactly one column, so nothing is counted twice or left out",
            lines.All(r => new[] { r.SoldText, r.ReturnedText, r.ReceivedText, r.PaidOutText }
                .Count(t => t != "\u2014") == 1),
            lines.Count + " lines");
        Check("a bill is Sold, money in is Received, goods back is Returned, money out is Paid out",
            lines.Where(r => r.Type is LedgerType.Sale or LedgerType.Opening)
                .All(r => Only(r.SoldText, r.ReturnedText, r.ReceivedText, r.PaidOutText, 0))
            && lines.Where(r => r.Type == LedgerType.Payment)
                .All(r => Only(r.SoldText, r.ReturnedText, r.ReceivedText, r.PaidOutText, 2))
            && lines.Where(r => r.Type == LedgerType.Return)
                .All(r => Only(r.SoldText, r.ReturnedText, r.ReceivedText, r.PaidOutText, 1))
            && lines.Where(r => r.IsPaidOut)
                .All(r => Only(r.SoldText, r.ReturnedText, r.ReceivedText, r.PaidOutText, 3)),
            lines.Count(r => r.Type == LedgerType.Return) + " return lines, "
            + lines.Count(r => r.IsPaidOut) + " paid-out lines");
        Eq("and the four columns run the balance the page prints: billed, less goods back, less money in, "
           "plus money handed over",
            Money.Round(lines.Where(r => r.SoldText != "\u2014").Sum(r => r.Debit)
                - lines.Where(r => r.ReturnedText != "\u2014").Sum(r => r.Credit)
                - lines.Where(r => r.ReceivedText != "\u2014").Sum(r => r.Credit)
                + lines.Where(r => r.PaidOutText != "\u2014").Sum(r => r.Debit)),
            lines[^1].RunningBalance);

        var stmt = print.StatementHtml(await ledger.GetCustomerAsync(edge.Id)!, lines,
            await ledger.GetBalanceAsync(edge.Id), new ShopSettings());
        Check("their printed ledger carries the column the page shows, in the page's order",
            stmt.Contains("<th class='num'>Sold</th><th class='num'>Returned</th><th class='num'>Received</th>"
                + "<th class='num'>Paid out</th>") && Count(stmt, "<th") == 8,
            Count(stmt, "<th") + " headings");
        // The column prints the credit on the ledger line, so the check is built from that line rather than
        // from a figure assumed: a whole-bill return credits the bill, and nothing else, whatever it did to
        // the cashbook.
        var backCredit = lines.Single(r => r.Type == LedgerType.Return).Credit;
        Eq("a return of the whole bill credits the bill's own figure on their ledger", 1_000m, backCredit);
        var backRow = "<td class='num'>\u2014</td><td class='num'>" + Money.Pkr(backCredit)
            + "</td><td class='num'>\u2014</td><td class='num'>\u2014</td>";
        Check("and the goods that came back stand in their own column on paper, with nothing beside them - "
              "they were never sold and never paid",
            stmt.Contains(backRow), backRow);
    }

    /// <summary>Which one of a line's four money columns is filled: the dash is this app's own "nothing
    /// here", so a check that exactly one is not a dash is a check that a figure cannot be counted twice
    /// or vanish from the paper altogether.</summary>
    private static bool Only(string sold, string returned, string received, string paidOut, int which)
    {
        var cells = new[] { sold, returned, received, paidOut };
        return cells.Count(t => t != "\u2014") == 1 && cells[which] != "\u2014";
    }

    private static int Count(string haystack, string needle)
    {
        var n = 0;
        for (var i = haystack.IndexOf(needle, StringComparison.Ordinal); i >= 0;
             i = haystack.IndexOf(needle, i + needle.Length, StringComparison.Ordinal))
            n++;
        return n;
    }

    // ------------------------------------------------------------------ tiny harness

    private static void Head(string text) => Console.WriteLine(Environment.NewLine + "  " + text);

    private static void Info(string text) => Console.WriteLine("  info  " + text);

    private static void Check(string name, bool ok, string detail = null)
    {
        if (ok)
        {
            _pass++;
            Console.WriteLine("  ok    " + name);
            return;
        }
        _fail++;
        Console.WriteLine("  FAIL  " + name + (string.IsNullOrEmpty(detail) ? "" : "   ->   " + detail));
    }

    private static void Eq(string name, decimal expected, decimal actual) => Check(name, expected == actual, $"expected {expected}, got {actual}");

    private static void Warn(string name, bool ok, string detail = null)
    {
        if (ok)
        {
            _pass++;
            Console.WriteLine("  ok    " + name);
            return;
        }
        _note++;
        Console.WriteLine("  note  " + name + (string.IsNullOrEmpty(detail) ? "" : "   ->   " + detail));
    }

    private static async Task Throws<T>(string name, Func<Task> action) where T : Exception
    {
        try
        {
            await action();
            Check(name, false, "no error was raised at all");
        }
        catch (T ex)
        {
            Check(name, !string.IsNullOrWhiteSpace(ex.Message), ex.Message);
        }
        catch (Exception ex)
        {
            Check(name, false, $"wrong kind of error: {ex.GetType().Name} - {ex.Message}");
        }
    }

    // Grouping separators differ between Windows and Linux for en-PK, so compare without them.
    private static string Plain(string s) => s.Replace(",", "");
}
