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
            "PKR", 1, null, null, null, null,
            "Yiwu Trading", 10_000_000.005m, 3_000_000.004m, "TT");
        Eq("the supplier bill is kept to the paisa", 10_000_000.01m, container.SupplierAmount);
        Eq("paid 3,000,000.004 was taken as 3,000,000.00, so 7,000,000.01 is owed",
            7_000_000.01m, await inventory.SupplierBalanceAsync(container.Id));
        var targets = await cash.SupplierContainersAsync();
        Eq("the We owe page reads the same figure", 7_000_000.01m, targets.Single(t => t.Id == container.Id).Owed);
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
        Eq("the container's revenue is the gross of its sold lines", 140_543.87m, first.Revenue);
        Eq("its cost is its sold lines' cost: 693.92 + 0", 693.92m, first.Cogs);
        Eq("its expenses are recorded", 200_000m, first.Expenses);
        Eq("and profit, as every page defines it, is revenue minus cost only", 139_849.95m, first.Profit);
        Warn("container profit ignores the container's own freight and customs - the margin is 200,000 lower than this row says",
            first.Profit == first.Revenue - first.Cogs - first.Expenses,
            $"row shows {first.Profit}; after its 200,000 of expenses the money actually left with is {first.Revenue - first.Cogs - first.Expenses}");
        Warn("and it counts the bill's GROSS, so a sale discount never reaches profit",
            first.Profit == bill.TotalAmount - first.Cogs - first.Expenses,
            $"the customer was billed {bill.TotalAmount} but profit counts {first.Revenue} of sales - the {bill.DiscountAmount} discount is nowhere in it");
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

        Head("profit follows a corrected cost - the case that stayed wrong for one release");
        var beforeReprice = await reports.GetContainerProfitAsync(container.Id);
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

        Head("paying the supplier: two payments, two ledger lines, no double counting");
        await inventory.PaySupplierAsync(container.Id, DateTime.Today, 1_000_000.004m, "LC", "part payment, HBL ref 99");
        await inventory.PaySupplierAsync(container.Id, DateTime.Today, 500_000m, "Cash", null);
        Eq("owed is the bill less all three payments", 5_500_000.01m, await inventory.SupplierBalanceAsync(container.Id));
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

        Head("a shop expense keeps what was typed");
        await shop.AddAsync(DateTime.Today, "Rent", 25_000.009m, null);
        await using (var db = await factory.CreateDbContextAsync())
        {
            Eq("Rs 25,000.009 is stored as 25,000.01", 25_000.01m, (await db.ShopExpenses.SingleAsync()).Amount);
        }

        Head("an order sheet: what was saved is what the sheet showed");
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
        Eq("the re-opened sheet costs what the live sheet cost", plan.Total.CostPkr, saved.Total.CostPkr);
        Eq("and sells for what it sold for", plan.Total.SalePkr, saved.Total.SalePkr);
        Eq("and profits by the same", plan.Total.ProfitPkr, saved.Total.ProfitPkr);
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
            () => inventory.CreateContainerAsync("No supplier", null, "China", null, null, null, null, null, null, null, null, null, 100m, 50m, null));
        await Throws<InvalidOperationException>("a payment of Rs 0.004 is refused rather than recorded as zero",
            () => inventory.PaySupplierAsync(container.Id, DateTime.Today, 0.004m, "Cash", null));
        await Throws<InvalidOperationException>("a zero container expense is refused",
            () => inventory.AddExpenseAsync(container.Id, DateTime.Today, "Labour", 0m, null));
        await Throws<InvalidOperationException>("a negative cost is refused",
            () => inventory.AddGoodsAsync(container.Id, "Bad", "pcs", null, 5m, -1m, null, null, null, null, null));

        await EverythingIsExactMoney(factory);
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

    // ------------------------------------------------------------------ tiny harness

    private static void Head(string text) => Console.WriteLine(Environment.NewLine + "  " + text);

    private static void Info(string text) => Console.WriteLine("  info  " + text);

    private static void Check(string name, bool ok, string? detail = null)
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

    private static void Warn(string name, bool ok, string? detail = null)
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
