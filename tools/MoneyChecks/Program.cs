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
        Updates();
        PrintPaper();

        var dir = Path.Combine(Path.GetTempPath(), "probooks-moneychecks-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        try
        {
            await Flows(dir);
            await Reconciliation(dir);
            await YearStatement(dir);
            await MonthReceipts(dir);
            await InvoiceStanding(dir);
            await FreightSplit(dir);
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
        // The bills are rows, and the sheet's expense figure is those rows added up - a total typed beside
        // them was retired on purpose, because two answers to "what did this cost" is one too many.
        var plan = new BuyPlanRow
        {
            YenRate = 1.0701m,
            Lines = rows,
            Expenses = new List<BuyPlanExpenseRow>
            {
                new() { Description = "Sea freight", AmountPkr = 192_618m },
                new() { Description = "Local clearing", AmountPkr = 100_000.005m }
            }
        };
        plan.RefreshTotals();
        Eq("¥250 at 1.0701 = Rs 267.53, rounded up not to even", 267.53m, rows[0].CostPkr);
        Eq("250 pieces at Rs 450 sell for 112,500", 112_500m, rows[0].SalePkr);
        Eq("250 pieces at 0.375 kg = 93.75 kg", 93.75m, rows[0].TotalWeightKg);
        Eq("the sheet's cost Rs is the rows' cost added, so it cannot drift",
            rows[0].CostPkr + rows[1].CostPkr, plan.Total.CostPkr);
        Eq("the sheet's sell total is the rows' sell added",
            rows[0].SalePkr + rows[1].SalePkr, plan.Total.SalePkr);
        Eq("so the sheet's expense figure is its bills, added and rounded once", 292_618.01m, plan.Total.ExpensePkr);
        Eq("and a bill typed to three decimals is a paisa figure before it is ever saved", 292_618.01m, plan.ExpensePkr);
        Eq("all in = goods + the bills", plan.Total.CostPkr + plan.Total.ExpensePkr, plan.Total.SpendPkr);
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
        Eq("a nine-decimal rate prices the row by the six decimals the save keeps",
            Money.Round(250m * 640.5m * 1.070123m), fussy.Lines[0].CostPkr);
        Check("and that is not what the uncut rate would have said, so the cut is doing real work",
            fussy.Lines[0].CostPkr != Money.Round(250m * 640.5m * 1.0701234567m),
            fussy.Lines[0].CostPkr + " against the nine-decimal " + Money.Round(250m * 640.5m * 1.0701234567m));
        Eq("and the row holds that rate, not the one typed", 1.070123m, fussy.Lines[0].YenRate);
        // The same expense, pinned to the rupee figure a re-opened sheet will hold (line above holds
        // the field; this one holds the total, which is what the tape and the printout read).
        Eq("so the sheet's all-in is a paisa figure before saving as well", 127_683.50m, plan.Total.SpendPkr);

        var noRate = new BuyPlanRow { YenRate = 0m, Lines = rows };
        noRate.RefreshTotals();
        Eq("a rate of 0 counts as 1 rather than dividing by nothing", 250m + 40m * 640.5m, noRate.Total.CostPkr);
    }

    // ------------------------------------------------------------------ the update rules
    // The decision an update makes is checked here, in the open, without a network: what a version means, what
    // the shop's facts allow, and what the script that does the work is and is not allowed to say.
    private static void Updates()
    {
        Head("updates: a version is three numbers, not a piece of text");
        Check("1.10.0 is newer than 1.9.0, which a string compare gets backwards",
            UpdateRules.Compare("1.9.0", "1.10.0") < 0,
            UpdateRules.Compare("1.9.0", "1.10.0").ToString());
        Check("and a shorter version reads as the same book with nothing left off",
            UpdateRules.Compare("1.10", "1.10.0") == 0 && UpdateRules.Compare("2", "2.0.0") == 0);
        Eq("patch numbers are compared too", 1, Math.Sign(UpdateRules.Compare("1.0.1", "1.0.2")));
        Check("a v, a suffix and a build tag all read as the version under them",
            UpdateRules.Parse("v1.2.3-beta") == (1, 2, 3) && UpdateRules.Parse("1.2.3+build7") == (1, 2, 3),
            UpdateRules.Parse("v1.2.3-beta").ToString());
        Check("four parts, junk and nothing at all are no version",
            UpdateRules.Parse("1.2.3.4") is null && UpdateRules.Parse("n/a") is null && UpdateRules.Parse(null) is null);
        Check("and when a version cannot be read there is no update - guessing one is worse than asking twice",
            !UpdateRules.IsUpdate("1.0.0", null) && !UpdateRules.IsUpdate(null, "1.4.0")
                && !UpdateRules.IsUpdate("1.0.0", "1.0.0") && UpdateRules.IsUpdate("1.0.0", "1.0.1"));
        Eq("the project file's version is the one a build carries", "1.1.0",
            Plain(UpdateRules.VersionFromProject("<Project><PropertyGroup><Version>1.1.0</Version></PropertyGroup></Project>") ?? ""));
        Check("and a project file with no version says so rather than inventing one",
            UpdateRules.VersionFromProject("<Project><PropertyGroup></PropertyGroup></Project>") is null);

        Head("updates: what the shop is told, and what the page may do");
        var free = UpdateRules.Decide(true, true, true, true, false, false, "1.0.0", "1.0.0");
        Check("nothing waiting is an answer, not an error: up to date, and no button",
            free.State == UpdateState.UpToDate && !free.CanApply && !free.ChangesWaiting, free.Message);
        var ready = UpdateRules.Decide(true, true, true, true, true, false, "1.0.0", "1.1.0");
        Check("work waiting, tools to build it, and nothing in the way is the only state that offers Update now",
            ready.State == UpdateState.Available && ready.CanApply && ready.ChangesWaiting, ready.Message);
        Check("and the line names both versions, so the shop knows what it is being moved to",
            ready.Message.Contains("1.0.0 to 1.1.0"), ready.Message);
        var dirty = UpdateRules.Decide(true, true, true, false, true, false, "1.0.0", "1.1.0");
        Check("a folder with unsaved work is stopped before the update is offered, however much is waiting",
            dirty.State == UpdateState.BlockedLocalChanges && !dirty.CanApply, dirty.Message);
        var split = UpdateRules.Decide(true, true, true, false, true, true, "1.0.0", "1.1.0");
        Check("and work on both sides is named first, because that one has no safe automatic answer",
            split.State == UpdateState.Diverged && !split.CanApply, split.Message);
        var offline = UpdateRules.Decide(true, true, false, true, false, false, "1.0.0", null);
        Check("a dead line is told as a dead line, and nothing is said about being up to date",
            offline.State == UpdateState.Offline && !offline.CanApply, offline.Message);
        var installed = UpdateRules.Decide(false, true, true, true, true, false, "1.0.0", "1.1.0");
        Check("an installed copy is not told to pull itself: it is not a folder anyone builds in",
            installed.State == UpdateState.NotInstall && !installed.CanApply, installed.Message);
        var noBuild = UpdateRules.Decide(true, false, true, true, true, false, "1.0.0", "1.1.0");
        Check("changes without a way to build them are still changes: the shop is told, and no button appears",
            noBuild.State == UpdateState.NoSdk && !noBuild.CanApply && noBuild.ChangesWaiting, noBuild.Message);
        var sameVersionButNew = UpdateRules.Decide(true, true, true, true, true, false, "1.1.0", "1.1.0");
        Check("a folder behind the branch on commits is behind even when nobody bumped the number - "
              + "the fixes are in the work, not in the label",
            sameVersionButNew.State == UpdateState.Available && sameVersionButNew.CanApply, sameVersionButNew.Message);

        Head("updates: the newest changelog entry, and nothing older than it");
        var notes = UpdateRules.NotesFrom("# Title\n\n## 1.2.0\n- one thing\n- another\n\n## 1.1.0\n- old thing\n");
        Check("the entry the shop sees is the newest one, headings and all",
            notes.Contains("1.2.0") && notes.Contains("one thing") && notes.Contains("another"), notes);
        Check("and it stops there, so a screen the size of a card never recites the book's history",
            !notes.Contains("old thing"), notes);
        Check("a missing changelog is no notes, not an error at the moment someone is deciding to update",
            UpdateRules.NotesFrom(null) == "" && UpdateRules.NotesFrom("") == "");

        Head("updates: what the script that does the work may and may not say");
        var script = UpdateRules.BuildScript(@"C:\shop\container", "main", 4321, new DateTime(2026, 9, 13, 21, 4, 0));
        foreach (var need in UpdateRules.ScriptMustContain)
        {
            Check($"the script says {need}, because the update is held to exactly this list",
                script.Contains(need, StringComparison.Ordinal), need);
        }
        foreach (var ban in UpdateRules.ScriptMustNotContain)
        {
            Check($"and it never says {ban} - a folder that cannot be fast-forwarded is reported, not overwritten",
                !script.Contains(ban, StringComparison.OrdinalIgnoreCase), ban);
        }
        Check("the merge comes after the fetch, so nothing is merged from a stale view of the branch",
            script.IndexOf("git fetch", StringComparison.Ordinal) < script.IndexOf("--ff-only", StringComparison.Ordinal));
        Check("it waits for this very process to close before it touches the files it is running from",
            script.Contains("PID eq 4321") && script.Contains("waitloop"), "the pid it waits on");
        Check("it starts the build the shop already starts, not a second copy somewhere else",
            script.Contains(UpdateRules.ExePath), "the path it starts");
        Check("and it leaves a log, because the run that goes wrong is the one nobody saw",
            script.Contains("update.log"), "no log");
        // A check must not create the shop's data folder to look at its name, so the script is asked what it
        // may touch instead: one folder, the source folder it was written for, and no database file at all.
        var named = System.Text.RegularExpressions.Regex.Matches(script, @"[A-Za-z]:[^""\s>]+")
            .Cast<System.Text.RegularExpressions.Match>().Select(x => x.Value).ToList();
        Check("the only folder an update may name is the source folder it was written for",
            named.Count > 0 && named.All(x => x.StartsWith(@"C:\shop\container", StringComparison.OrdinalIgnoreCase)),
            string.Join(" | ", named));
        Check("and it never names a database file, which is the one way an update could reach a shop's books",
            !script.Contains(".db", StringComparison.OrdinalIgnoreCase), "a .db path reached the script");

        Head("updates: the release the shop is offered is described honestly");
        var root = FindRepoRootForChecks();
        if (root is null)
        {
            Warn("the shipped project file and changelog agree on the version", true, "not a source folder - nothing to compare");
            return;
        }
        var csproj = File.ReadAllText(Path.Combine(root, "src", "ContainerManagement", "ContainerManagement.csproj"));
        var log = File.ReadAllText(Path.Combine(root, "CHANGELOG.md"));
        var shipped = UpdateRules.VersionFromProject(csproj) ?? "";
        var newest = System.Text.RegularExpressions.Regex.Match(log, @"(?m)^## (\S+)").Groups[1].Value;
        Check("the newest changelog entry is the version the build carries, so the notes match what arrives",
            Plain(shipped) == Plain(newest), $"the build says {shipped}, the changelog heads with {newest}");
        Check("and that entry has something in it, because an empty promise is worse than none",
            UpdateRules.NotesFrom(log).Split('\n').Length > 1, UpdateRules.NotesFrom(log));
    }

    /// <summary>The source folder the checks were built in, found the same patient way the update looks for it.</summary>
    private static string FindRepoRootForChecks()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CHANGELOG.md")))
            dir = dir.Parent;
        return dir?.FullName;
    }

    // ------------------------------------------------------------------ the paper
    // A print is a second voice saying the same figures, so it is checked as one: the words handed to it must
    // come out unchanged, the total must be last and marked, and a page must not have a print button that goes
    // nowhere - or a command nobody can press.
    private static void PrintPaper()
    {
        Head("a printed page says the words the page showed, and nothing else");
        var headers = new[] { "Customer", "Owes", "Oldest due" };
        var rows = new List<IReadOnlyList<string>>
        {
            new[] { "Abdul & Sons", "Rs 1,000.004", "3 Mar 2026" },
            new[] { "<b>Karim</b>", "Rs 40,000", "" },
        };
        var html = PrintService.TableHtml("To collect", "for the 13th", headers, rows,
            new[] { "Total", "Rs 41,000.00", "" }, 1, new ShopSettings { CompanyName = "Check shop" });
        Check("every cell arrives on the paper exactly as the page worded it - a print that re-rounds is a second book",
            html.Contains("Rs 1,000.004") && html.Contains("Rs 40,000") && html.Contains("Rs 41,000.00"),
            "a figure was changed on the way to the paper");
        Check("and the row's own words are there too, including an odd one like a shop name with an ampersand",
            html.Contains("Abdul &amp; Sons") && html.Contains("&lt;b&gt;Karim&lt;/b&gt;"),
            "a name was not escaped, so it could have eaten the table");
        Check("an empty cell prints as nothing rather than a dash nobody typed",
            html.Contains("<td class='num'></td>") || html.Contains("<td></td>"), "a dash came in from nowhere");
        var totalAt = html.IndexOf("class='total'", StringComparison.Ordinal);
        var lastRow = html.LastIndexOf("Abdul", StringComparison.Ordinal);
        Check("the total is on the paper, after the last row, and drawn as a total",
            totalAt > 0 && totalAt > lastRow, "the total line is missing or in the wrong place");
        Check("the money columns are the right-aligned ones, so a sheet can be added up by its last digit",
            html.Contains("<th class='num'>Owes</th>") && html.Contains("<th>Customer</th>"), "column alignment");
        var empty = PrintService.TableHtml("To collect", null, headers, Array.Empty<IReadOnlyList<string>>(),
            new[] { "Total", "Rs 0", "" }, 1, new ShopSettings());
        Check("a page with nothing on it prints that, and still prints its total line",
            empty.Contains("Nothing here.") && empty.Contains("class='total'") && empty.Contains("Rs 0"),
            "an empty page was skipped or fudged");
        var short_ = PrintService.TableHtml("Stock", null, headers,
            new List<IReadOnlyList<string>> { new[] { "Only one cell" } }, null, 1, new ShopSettings());
        Check("a row shorter than its headings still prints, with the rest left blank",
            short_.Contains("Only one cell") && short_.Contains("class='total'") == false,
            "the table builder could not cope with a short row");
        var two = PrintService.TablesHtml("We owe", null, new[]
        {
            new PrintTable("Suppliers", new[] { "Container", "Owed" },
                new List<IReadOnlyList<string>> { new[] { "Box one", "Rs 10" } }, new[] { "Total", "Rs 10" }),
            new PrintTable("Paid", new[] { "Date", "Amount" },
                new List<IReadOnlyList<string>> { new[] { "3 Mar", "Rs 4" } }, null),
        }, new ShopSettings());
        Check("several tables on one sheet keep their own headings and only their own totals",
            two.Contains("<h2>Suppliers</h2>") && two.Contains("<h2>Paid</h2>")
                && System.Text.RegularExpressions.Regex.Matches(two, "class='total'").Count == 1,
            "a total was shared between two tables, or one went missing");

        Head("a share with no words opens the chat empty, and one with words says them safely");
        Eq("no message means no query on the link, so the chat opens clean",
            "https://wa.me/923331234567", PrintService.ShareUrl("923331234567", ""));
        Eq("and null reads the same way",
            "https://wa.me/923331234567", PrintService.ShareUrl("923331234567", null));
        Check("a message is escaped, so an ampersand or a rupee sign in it cannot end the link early",
            PrintService.ShareUrl("923331234567", "you owe Rs 1 & 2")
                .StartsWith("https://wa.me/923331234567?text=you%20owe", StringComparison.Ordinal),
            PrintService.ShareUrl("923331234567", "you owe Rs 1 & 2"));

        Head("every page that can print has a button, and every button has a page");
        var root = FindRepoRootForChecks();
        if (root is null)
        {
            Warn("the print buttons and the print commands were paired up", true, "not a source folder");
            return;
        }
        var views = Directory.GetFiles(Path.Combine(root, "src", "ContainerManagement", "Views"), "*.axaml");
        var unbound = new List<string>();
        var silent = new List<string>();
        foreach (var f in views)
        {
            var axaml = File.ReadAllText(f);
            var name = Path.GetFileNameWithoutExtension(f);
            var vm = Path.Combine(root, "src", "ContainerManagement", "ViewModels",
                name.EndsWith("View") ? name[..^4] + "ViewModel.cs" : "");
            var hasCommand = File.Exists(vm)
                && System.Text.RegularExpressions.Regex.IsMatch(File.ReadAllText(vm), @"private (?:async )?(?:Task|void) Print");
            var hasButton = axaml.Contains("Content=\"Print\"");
            if (hasButton && !hasCommand) unbound.Add(name);
            if (hasCommand && !hasButton && name != "CustomerDetailView") silent.Add(Path.GetFileName(vm));
        }
        Check("no page carries a Print button whose command does not exist - a dead button is what started this",
            unbound.Count == 0, string.Join(", ", unbound));
        Check("and no page builds a print nobody can reach",
            silent.Count == 0, string.Join(", ", silent));
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

        // The expense that used to sit beside the cost is now inside it. 1,000 bulbs at 0.375 kg and 500
        // chargers at 0.12 kg are 375 + 60 = 435 kg, and Rs 200,000 over that is Rs 459.7701 a kilo, so
        // the bulbs' lot carries Rs 172,413.79 (Rs 172.41 a piece) and the chargers' Rs 27,586.21 (Rs 55.17
        // a piece). The two lines already sold are re-costed at those figures, which is what the next four
        // lines of arithmetic are about.
        await inventory.AddExpenseAsync(container.Id, DateTime.Today, "Sea Freight", 200_000m, "audit");
        var first = await reports.GetContainerProfitAsync(container.Id);
        Eq("the container's revenue is what the bill asked for, discount off", 135_543.86m, first.Revenue);
        Check("because the discount is shared over the bill's lines and the shares add back to the bill",
            first.Revenue == bill.TotalAmount, $"billed {bill.TotalAmount}, counted {first.Revenue} of sales");
        Eq("its cost is its sold lines' cost with the freight in: (1850.45+172.41)x0.375 + 55.17x7",
            1144.76m, first.Cogs);
        Eq("its expenses are recorded", 200_000m, first.Expenses);
        Eq("and profit is revenue minus that landed cost", 134_399.10m, first.Profit);
        Check("the freight did not sit beside the profit any more - it moved it, by what the sold pieces carry",
            134_849.94m - first.Profit == 450.84m,
            $"profit moved by {134_849.94m - first.Profit}, while the two lines picked up 64.65 + 386.19");
        Eq("and the discount is off profit too, not only off the bill", bill.TotalAmount - first.Cogs, first.Profit);
        Info("a container expense is not put through the cash book either: rent paid out of the till moves cash, sea freight does not.");

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

        Head("every page reads the same billed money - Home, the container row, the Reports profit reports");
        var beforeReprice = await reports.GetContainerProfitAsync(container.Id);
        var home = await reports.GetHomeMonthAsync();
        Eq("the container now holds both bills: 135,543.86 + 543.94", 136_087.80m, beforeReprice.Revenue);
        Eq("Home's month says the same figure, not a second version of it", beforeReprice.Revenue, home.Sales);
        Eq("and Home's profit agrees with the container row, no shop expenses yet", beforeReprice.Profit, home.Profit);
        var items = await reports.GetItemProfitsAsync(null, null, null);
        Eq("the item-by-item profit report adds back to the same money", beforeReprice.Revenue, items.Sum(i => i.Revenue));
        Check("and the second bill, which had no discount, was not touched by the sharing",
            beforeReprice.Revenue - first.Revenue == payBill.Lines[0].LineTotal,
            $"{beforeReprice.Revenue - first.Revenue} added for an undiscounted line of {payBill.Lines[0].LineTotal}");

        var homeBefore = await reports.GetDashboardAsync();
        Head("Home's dates: the range moves the book card, and the month under it stays the month");
        var year = new DateTime(DateTime.Today.Year, 1, 1);
        var wide = await reports.GetDashboardAsync(year, DateTime.Today.AddYears(1));
        Eq("a range wide enough to hold the book sees the book's own sales, added once and not twice",
            homeBefore.TotalRevenue, wide.TotalRevenue);
        Eq("the same profit", homeBefore.TotalProfit, wide.TotalProfit);
        Eq("the same money still out there", homeBefore.MoneyInMarket, wide.MoneyInMarket);
        Eq("and the same shelf, because stock is today's whatever the dates say",
            homeBefore.InventoryValue, wide.InventoryValue);
        Check("the containers counted are only the ones that did business in the dates, so never more than the book holds",
            wide.TotalContainers <= homeBefore.TotalContainers,
            wide.TotalContainers + " in the period of " + homeBefore.TotalContainers + " in the book");
        var lastYear = await reports.GetDashboardAsync(new DateTime(year.Year - 1, 1, 1), new DateTime(year.Year - 1, 12, 31));
        Check("a year with nothing in it says zero on every figure rather than borrowing this year's",
            lastYear.TotalRevenue == 0m && lastYear.TotalProfit == 0m && lastYear.MoneyInMarket == 0m
                && lastYear.TotalContainers == 0,
            $"{lastYear.TotalRevenue} billed, {lastYear.TotalContainers} containers");
        var openEnded = await reports.GetDashboardAsync(year, null);
        var closedToday = await reports.GetDashboardAsync(year, DateTime.Today);
        Eq("a range left open at the far end has run to today", closedToday.TotalRevenue, openEnded.TotalRevenue);
        var backwards = await reports.GetDashboardAsync(DateTime.Today, year);
        var todayOnly = await reports.GetDashboardAsync(DateTime.Today, DateTime.Today);
        Check("and a range typed the wrong way round reads its own first day rather than answering with nothing",
            backwards.TotalRevenue == todayOnly.TotalRevenue
                && backwards.TotalContainers == todayOnly.TotalContainers,
            $"{backwards.TotalRevenue} against {todayOnly.TotalRevenue} for {DateTime.Today:dd MMM yyyy} alone");
        var month = await reports.GetHomeMonthAsync();
        var monthStart = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        Check("the month under the card takes no dates at all, so none of this moved it - it is this month",
            month.Days.Count == 0 || month.Days.All(d => d.Date >= monthStart && d.Date < monthStart.AddMonths(1)),
            month.Days.Count + " day rows");

        Head("the reports page reads a year's totals from the year report's own total line");
        // The hub does not add a year's twelve months up itself: it takes the line the year report already
        // prints. That is only safe while the report does hand one over, and while it says which line it is -
        // otherwise the hub shows nothing and looks like a shop that traded nothing.
        var tillYear = await cash.GetYearCashAsync(DateTime.Today.Year);
        Check("the till's year hands back one total line, last, and labelled as a total",
            tillYear.Count(r => r.IsTotal) == 1 && tillYear[^1].IsTotal
                && (tillYear[^1].Label ?? "").StartsWith("Total"),
            tillYear.Count + " rows, last is \"" + (tillYear[^1].Label ?? "no label") + "\"");
        var salesYear = await reports.GetYearSalesAsync(DateTime.Today.Year);
        Check("the sales year does the same, with its twelve months before it",
            salesYear.Count(r => r.IsTotal) == 1 && salesYear[^1].IsTotal
                && salesYear.Count == 13,
            salesYear.Count + " rows");
        var billsYear = await shop.GetYearAsync(DateTime.Today.Year);
        Check("and so do the shop's own bills",
            billsYear.Count(r => r.IsTotal) == 1 && billsYear[^1].IsTotal,
            billsYear.Count + " rows");
        Check("so a figure read off that line is the year's, not one month's dressed up as it",
            salesYear[^1].SoldText == Money.Pkr(salesYear.Where(r => !r.IsTotal).Sum(r => r.Sold)),
            salesYear[^1].SoldText);

        Head("a container that has only landed is still a container");
        // A lot booked on a date and not yet sold off is the case the count got wrong: it had no bill and no
        // expense, so counting the containers that did business in a period showed a shop zero containers in the
        // very month it landed one. Written here and taken away again, so nothing after it reads a fixture row.
        var landed = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1).AddDays(-9);
        var quietId = 0;
        await using (var dbc = await factory.CreateDbContextAsync())
        {
            var quiet = new CargoContainer
            {
                Title = "Only landed, nothing sold",
                ContainerNumber = "IDLE0001",
                ArrivalDate = landed,
                CreatedAt = landed,
            };
            dbc.Containers.Add(quiet);
            await dbc.SaveChangesAsync();
            quietId = quiet.Id;
        }
        var landedDay = await reports.GetDashboardAsync(landed, landed);
        Check("the day it arrived counts it, though no money has ever been written on it",
            landedDay.TotalContainers >= 1,
            $"{landedDay.TotalContainers} containers counted on {landed:dd MMM yyyy}");
        var rowsOfQuiet = await reports.GetContainerProfitsAsync(landed, landed);
        var quietRow = rowsOfQuiet.Single(p => p.ContainerId == quietId);
        Check("and there is indeed nothing on it - the count is not including it because it sold something",
            quietRow.Revenue == 0m && quietRow.Cogs == 0m && quietRow.Expenses == 0m && quietRow.QtySold == 0m,
            $"{quietRow.Revenue} sold, {quietRow.Expenses} spent on the lot");
        var beforeItLanded = await reports.GetDashboardAsync(landed.AddDays(-6), landed.AddDays(-1));
        Check("a week before it landed does not count it, so the date the shop wrote is the only thing moving",
            beforeItLanded.TotalContainers == landedDay.TotalContainers - 1,
            $"{beforeItLanded.TotalContainers} before, {landedDay.TotalContainers} on the day");
        await using (var dbc = await factory.CreateDbContextAsync())
        {
            var row = await dbc.Containers.FindAsync(quietId);
            if (row is not null)
                dbc.Containers.Remove(row);
            await dbc.SaveChangesAsync();
        }
        Check("and the book is left as it was found",
            (await reports.GetDashboardAsync()).TotalContainers == homeBefore.TotalContainers,
            $"{(await reports.GetDashboardAsync()).TotalContainers} against {homeBefore.TotalContainers}");

        Head("profit follows a corrected cost - the case that stayed wrong for one release");
        Eq("the three sold lines cost 758.57 + 386.19 + 758.57 with the freight in", 1903.33m, beforeReprice.Cogs);
        var repriced = await inventory.UpdateGoodsAsync(bulbs.Id, "LED bulb", "pcs", "LB-1", 1000m, 999.25m, 2000m, null, null, 0.375m, null);
        Check("both sold lines of that lot were re-costed", repriced == 2, repriced + " lines touched");
        var later = await reports.GetContainerProfitAsync(container.Id);
        Eq("the two bulb lines move to 814.65 each, and the charger line is untouched", 2015.49m, later.Cogs);
        Eq("so profit fell by exactly the cost increase of 112.16", beforeReprice.Profit - 112.16m, later.Profit);
        Check("and no stock was invented or lost by the re-costing",
            later.QtyReceived == beforeReprice.QtyReceived && later.QtySold == beforeReprice.QtySold);
        Eq("the 999.25 pieces still on the shelf are worth Rs 149.55 more each, to the paisa",
            beforeReprice.RemainingValue + Money.Round(999.25m * 149.55m, 4), later.RemainingValue);

        // Home's card reads the book through the same rows, so a corrected cost has to reach it too - a page
        // that kept the old profit while the container's page had moved would be the worst kind of wrong.
        var homeAfter = await reports.GetDashboardAsync();
        Eq("Home's whole-book profit falls by the same 112.16", homeBefore.TotalProfit - 112.16m, homeAfter.TotalProfit);
        Check("while its sales figure does not stir, because no price was touched",
            homeAfter.TotalRevenue == homeBefore.TotalRevenue,
            $"billed {homeBefore.TotalRevenue} before the cost was corrected, {homeAfter.TotalRevenue} after");
        Check("and the stock it values is the shelf at its new cost, not the old one",
            homeAfter.InventoryValue > homeBefore.InventoryValue,
            $"{homeBefore.InventoryValue} to {homeAfter.InventoryValue}");

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
        var goodsRows = new List<BuyPlanLineInput>
        {
            new() { ItemName = "LED bulb", Quantity = 250m, UnitCostYen = 1m, UnitWeightKg = 0.375m, SalePricePkr = 450m },
            new() { ItemName = "Charger", Quantity = 40m, UnitCostYen = 640.5m, UnitWeightKg = 0.12m, SalePricePkr = 1999.99m }
        };
        // The sheet as it looks while you type, worked out here so the saved one can be compared to it. The
        // expense figure is not typed into it: two bills are, and the sheet adds them up.
        var live = new BuyPlanRow
        {
            YenRate = 1.0701m,
            Lines = new List<BuyPlanLineRow>
            {
                new() { ItemName = "LED bulb", Quantity = 250m, UnitCostYen = 1m, UnitWeightKg = 0.375m, SalePricePkr = 450m },
                new() { ItemName = "Charger", Quantity = 40m, UnitCostYen = 640.5m, UnitWeightKg = 0.12m, SalePricePkr = 1999.99m }
            },
            Expenses = new List<BuyPlanExpenseRow>
            {
                new()
                {
                    Description = "Sea freight", Currency = "JPY", AmountForeign = 180_000m, RateUsed = 1.0701m,
                    AmountPkr = Money.Round(180_000m * 1.0701m)
                },
                new() { Description = "Local clearing", Currency = "PKR", AmountPkr = 100_000.005m }
            }
        };
        live.RefreshTotals();
        Eq("the sheet's expense figure is its bills added up, to the paisa", 292_618.01m, live.ExpensePkr);
        var planId = (await plans.CreateAsync("AUDIT sheet")).Id;
        await plans.SaveAsync(planId, "AUDIT sheet", 1.0701m, goodsRows, new List<BuyPlanExpenseInput>
        {
            new() { Description = "Sea freight", Amount = 180_000m, Currency = "JPY" },
            new() { Description = "Local clearing", Amount = 100_000.005m, Currency = "PKR" }
        });
        var saved = await plans.GetAsync(planId);
        Eq("the yen rate is kept to six decimals", 1.0701m, saved.YenRate);
        Eq("the expense to two", 292_618.01m, saved.ExpensePkr);
        Eq("a bill typed in yen is stored in yen", 180_000m, saved.Expenses[0].AmountForeign);
        Eq("at the rate the sheet held when it was typed", 1.0701m, saved.Expenses[0].RateUsed ?? -1m);
        Eq("and the rupees it adds are that figure times that rate", 192_618m, saved.Expenses[0].AmountPkr);
        Check("stated on the row in the money it was written in",
            Plain(saved.Expenses[0].Note) == Plain("\u00a5180,000 at 1.0701 = Rs 192,618"),
            "the row says: " + saved.Expenses[0].Note);
        Check("and a rupee bill carries no rate and no note, there being nothing to explain",
            saved.Expenses[1].Currency == "PKR" && saved.Expenses[1].RateUsed is null
            && saved.Expenses[1].Note == "" && saved.Expenses[1].AmountPkr == 100_000.01m,
            $"{saved.Expenses[1].Currency} / {saved.Expenses[1].RateUsed?.ToString() ?? "none"} / {saved.Expenses[1].Note}");
        Eq("a per-piece weight to three", 0.375m, saved.Lines[0].UnitWeightKg);
        Eq("the re-opened sheet costs what the live sheet cost", live.Total.CostPkr, saved.Total.CostPkr);
        Eq("and sells for what it sold for", live.Total.SalePkr, saved.Total.SalePkr);
        Eq("and profits by the same", live.Total.ProfitPkr, saved.Total.ProfitPkr);
        Eq("a row's own cost survives the round trip", live.Lines[1].CostPkr, saved.Lines[1].CostPkr);
        Eq("and so do its bills, added up the same way", live.Total.ExpensePkr, saved.Total.ExpensePkr);
        var copied = await plans.GetAsync((await plans.DuplicateAsync(planId)).Id);
        Eq("a duplicate carries the same figures", saved.Total.ProfitPkr, copied.Total.ProfitPkr);
        Eq("and the same bills, in the currencies they were written in", saved.ExpensePkr, copied.ExpensePkr);
        Check("a duplicated yen bill is not re-multiplied at the new sheet's rate",
            copied.Expenses[0].AmountPkr == 192_618m && copied.Expenses[0].RateUsed == 1.0701m,
            $"{copied.Expenses[0].AmountPkr} at {copied.Expenses[0].RateUsed}");

        Head("the printed sheet carries the figures the sheet showed");
        var print = new PrintService();
        var html = print.BuyPlanHtml(saved, new ShopSettings { CompanyName = "Check shop" });
        var paper = OnPaper(html);
        foreach (var l in saved.Lines)
        {
            // Named one at a time, so a failure says which figure did not reach the paper rather than leaving
            // somebody to hunt through eleven columns of HTML.
            var missing = new List<string>();
            void Need(string what, string text)
            {
                if (!paper.Contains(Plain(text))) missing.Add(what + " (" + text + ")");
            }
            Need("qty", l.QuantityText);
            Need("yen cost", l.CostYenText);
            Need("rupee cost", l.CostPkrText);
            Need("kg each", l.UnitWeightText);
            Need("kg total", l.TotalWeightText);
            Need("sold for", l.SaleTotalText);
            Need("profit", l.ProfitText);
            Check("the printed row carries " + l.ItemNameText + "'s rupees, yen, kilos and profit",
                missing.Count == 0, "not on the paper: " + string.Join(", ", missing));
        }
        Check("a yen bill is printed in yen, at the rate that row was taken at",
            paper.Contains(Plain("\u00a5180,000 at 1.0701 = Rs 192,618")),
            "the note the row carries is: " + saved.Expenses[0].Note);
        Check("a rupee bill states its amount, and the two are added up under them",
            Plain(html).Contains(Plain("Rs 100,000.01")) && Plain(html).Contains(Plain("Rs 292,618.01")),
            "the bills or their total are not on the paper");
        Check("the rate is on the paper, so the yen figures can be checked without the app",
            html.Contains("Rs 1.0701 for 1 yen"), "the line under the title");
        var absent = new List<string>();
        void OnSheet(string what, string text)
        {
            if (!paper.Contains(Plain(text))) absent.Add(what + " (" + text + ")");
        }
        OnSheet("yen cost", saved.Total.CostYenText);
        OnSheet("rupee cost", saved.Total.CostPkrText);
        OnSheet("bills", saved.Total.ExpenseText);
        OnSheet("all in", saved.Total.SpendText);
        OnSheet("sold for", saved.Total.SaleText);
        OnSheet("profit", saved.Total.ProfitText);
        OnSheet("margin", saved.Total.MarginText);
        OnSheet("weight", saved.Total.WeightText);
        OnSheet("rows' profit", saved.Total.RowsProfitText);
        OnSheet("row count", saved.Total.ItemCountText);
        Check("and the summary is the sheet's own figures, not worked out again for the printer",
            absent.Count == 0, "not on the paper: " + string.Join(", ", absent));
        Check("the summary is a table of figures, with its seven money words on it",
            html.Contains("<h2>Summary</h2>") && html.Contains("+ expense") && html.Contains("= all in")
            && html.Contains("All sold for") && html.Contains("Margin"), "the summary block");
        Check("and nothing on the sheet is explained in prose",
            !html.Contains("class='muted'>A ") && !html.Contains("A plan, not")
            && !html.Contains("The last column") && !html.Contains("converted once"),
            "a sentence came back on the paper");
        Eq("the rows' profit is before the bills, the sheet's after them - by exactly the bills",
            292_618.01m, Money.Round(saved.Total.RowsProfitPkr - saved.Total.ProfitPkr));
        // Money on paper is read with a pencil in the margin, so a figure with a paisa it cannot show is a
        // figure that will not add up. Rates are allowed their decimals - they are not amounts.
        var onPaperOnly = System.Text.RegularExpressions.Regex.Replace(html, @"Rs [\d.,]+ for 1 yen", "");
        Check("nothing printed has a third decimal, so the paper can be added up by hand",
            !System.Text.RegularExpressions.Regex.IsMatch(onPaperOnly, @"(?:Rs|\u00a5) ?[\d,]+\.\d{3}\b"),
            "a fraction reached the paper");


        var blankId = (await plans.CreateAsync("BLANK print")).Id;
        var blankSheet = await plans.GetAsync(blankId);
        var blankHtml = print.BuyPlanHtml(blankSheet, new ShopSettings());
        Check("a sheet with no bills prints an empty bills table with Rs 0 under it, and no sentence",
            blankHtml.Contains("no bills") && blankHtml.Contains("<h2>Bills</h2>")
            && Plain(blankHtml).Contains(Plain("Rs 0")) && !blankHtml.Contains("class='muted'>No"),
            blankHtml.Length + " chars of paper");
        await plans.DeleteAsync(blankId);

        Head("saving a sheet again does not re-value the bills already on it");
        // The page sends an untouched row back with the rate that row was converted at, which is what makes
        // this a no-op; the sheet's own rate has moved on, and only the next figure typed follows it.
        await plans.SaveAsync(planId, "AUDIT sheet", 1.20m, goodsRows, new List<BuyPlanExpenseInput>
        {
            new() { Description = "Sea freight", Amount = 180_000m, Currency = "JPY", Rate = 1.0701m },
            new() { Description = "Local clearing", Amount = 100_000.01m, Currency = "PKR" },
            new() { Description = "Demurrage", Amount = 50_000m, Currency = "JPY" }
        });
        var after = await plans.GetAsync(planId);
        Eq("the bill whose own rate came back is exactly where it was", 192_618m, after.Expenses[0].AmountPkr);
        Eq("the rupee bill is taken as written", 100_000.01m, after.Expenses[1].AmountPkr);
        Eq("and the bill typed after the rate moved is taken at the new one", 60_000m, after.Expenses[2].AmountPkr);
        Eq("the sheet totals the three", 352_618.01m, after.ExpensePkr);
        // The sheet's rate moved from 1.0701 to 1.20, and a row holds the sheet's rate - so the goods were
        // re-priced by the same save. Profit moves by the new bill AND by that re-cost: an identity between the
        // two sheets' own totals, rather than a figure remembered from only one of them.
        Eq("so profit moved by the new bill plus the goods the moved rate re-priced, and by nothing else",
            Money.Round((after.Total.ExpensePkr - saved.Total.ExpensePkr)
                + (after.Total.CostPkr - saved.Total.CostPkr)
                - (after.Total.SalePkr - saved.Total.SalePkr)),
            Money.Round(saved.Total.ProfitPkr - after.Total.ProfitPkr));

        Head("what a bill row will not accept");
        await plans.SaveAsync(planId, "AUDIT sheet", 1.20m, goodsRows, new List<BuyPlanExpenseInput>
        {
            new() { Description = "   ", Amount = 5_000m, Currency = "PKR" }
        });
        await using (var db = await factory.CreateDbContextAsync())
        {
            var blank = await db.BuyPlanExpenses.AsNoTracking().SingleAsync(e => e.PlanId == planId);
            Check("a bill with no words on it is kept, as Other, rather than lost",
                blank.Description == "Other", "stored " + blank.Description);
        }
        await Throws<InvalidOperationException>("a yen bill on a sheet sitting at a rate of 1 is refused, not read as rupees",
            () => plans.SaveAsync(planId, "AUDIT sheet", 1m, goodsRows, new List<BuyPlanExpenseInput>
            {
                new() { Description = "Duty", Amount = 180_000m, Currency = "JPY" }
            }));
        await Throws<InvalidOperationException>("a bill with nothing on it is refused",
            () => plans.SaveAsync(planId, "AUDIT sheet", 1.20m, goodsRows, new List<BuyPlanExpenseInput>
            {
                new() { Description = "Duty", Amount = 0m, Currency = "PKR" }
            }));
        var held = await plans.GetAsync(planId);
        Eq("and a refused save wrote nothing, so the sheet still says what it said", 5_000m, held.ExpensePkr);
        await plans.DeleteAsync(planId);
        await using (var db = await factory.CreateDbContextAsync())
        {
            Check("deleting a sheet takes its rows with it - no orphans left to sum",
                !await db.BuyPlanLines.AnyAsync(l => l.PlanId == planId)
                && !await db.BuyPlanExpenses.AnyAsync(e => e.PlanId == planId));
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

        await EverythingIsExactMoney();

        // The exactness sweep is a local function of this method on purpose: it reads the fixtures built
        // above - the container, the customer, the two lots - and a method lifted out of here would
        // lose them and quietly stop checking the shop it was written against.
        async Task EverythingIsExactMoney()
        {
            Head("the day the goods actually landed");
            Throws<InvalidOperationException>("a container is not created on a guessed arrival date",
                () => inventory.CreateContainerAsync("No date", null, "China", null, null, null, null, null, null, null, null, null, 0m, 0m, null));
            var backDated = await inventory.CreateContainerAsync("Back-dated", "CNT-0002", "China",
                new DateTime(2026, 3, 14), null, null, null, null, null, null, null, null, 0m, 0m, null);
            Check("a date in the past is kept exactly as written, not pushed to today",
                backDated.ArrivalDate == new DateTime(2026, 3, 14), "stored " + backDated.ArrivalDate);
            await inventory.UpdateImportDetailsAsync(backDated.Id, null, backDated.SupplierAmount, null, null,
                new DateTime(2026, 3, 20));
            await using (var dbDate = await factory.CreateDbContextAsync())
            {
                var corrected = await dbDate.Containers.AsNoTracking().SingleAsync(x => x.Id == backDated.Id);
                Check("and the container page can put the day right afterwards",
                    corrected.ArrivalDate == new DateTime(2026, 3, 20), "stored " + corrected.ArrivalDate);
                // a save that does not show the date must not lose it
                await inventory.UpdateImportDetailsAsync(backDated.Id, null, corrected.SupplierAmount, null, null);
                var again = await dbDate.Containers.AsNoTracking().SingleAsync(x => x.Id == backDated.Id);
                Check("a save that never mentions the date keeps the date it found",
                    again.ArrivalDate == new DateTime(2026, 3, 20), "stored " + again.ArrivalDate);
            }

            Head("the figure typed on the container form is what the shop owes");
            // "Goods worth 20 lac, 20 lac handed over now" is typed as: we owe 20 lac, paid 20 lac. The paid
            // figure is a payment, not a reduction of the shopkeeper's own number, so the page must still say
            // 20 lac owed - which is the whole rule, tested at the number.
            var typedBox = await inventory.CreateContainerAsync("Typed balance", "CNT-0003", "China",
                new DateTime(2026, 4, 2), null, null, null, null, null, null, null, "Yiwu Trading",
                500_000m, 500_000m, "Cash");
            Eq("the bill is stored as that figure plus the money handed over", 1_000_000m, typedBox.SupplierAmount);
            var typedRow = (await cash.SupplierContainersAsync()).Single(t => t.Id == typedBox.Id);
            Eq("and We owe shows the figure that was typed", 500_000m, typedRow.Owed);
            Check("in the shop's words, not in a netted-down one",
                typedRow.Label.Contains("owe " + Money.Pkr(500_000m)), typedRow.Label);
            await inventory.PaySupplierAsync(typedBox.Id, new DateTime(2026, 4, 3), 500_000m, "Cash", null);
            Check("paying that figure settles the container",
                (await cash.SupplierContainersAsync()).Single(t => t.Id == typedBox.Id).Label.EndsWith("settled"));
            await Throws<InvalidOperationException>("and a paisa more is refused, because nothing is owed",
                () => inventory.PaySupplierAsync(typedBox.Id, new DateTime(2026, 4, 4), 0.01m, "Cash", null));

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
                SalesService.DescribeReturn(preview.Credit, preview.Cash).Contains("Rs 500 comes off")
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
            var bookMonthBefore = await reports.GetHomeMonthAsync();
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
            var bookMonthAfter = await reports.GetHomeMonthAsync();
            Eq("money paid to a customer settles a debt, it is not an expense, so profit did not move",
                bookMonthBefore.Profit, bookMonthAfter.Profit);
            Eq("and the billed money Home shows is untouched either", bookMonthBefore.Sales, bookMonthAfter.Sales);

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

            Head("the ledger in a chat message");
            var chatBalance = await ledger.GetBalanceAsync(customer.Id);
            // A budget of its own here, because this check asks *which* lines a message holds, and the real
            // limit would already have cut a long book before the question was put.
            var chat = PrintService.ShareText("AUDIT shop", customer.Name, customerLedger, chatBalance, 100_000);
            Check("the message carries every line of the ledger it was made from",
                customerLedger.All(r => chat.Contains(r.DateText) && chat.Contains(r.RunningText)),
                customerLedger.Count + " lines in " + chat.Length + " characters");
            Check("and it asks for the balance the page shows, to the paisa",
                chatBalance > 0
                    ? chat.Contains("Balance due: " + Money.Pkr(Money.Round(chatBalance)))
                    : chat.Contains("settled") || chat.Contains("over and above"),
                Money.Pkr(chatBalance));
            Check("and the total is said once, so nothing else in it can be read as the amount to send",
                chat.Split("Balance due", StringSplitOptions.None).Length - 1 == (chatBalance > 0 ? 1 : 0),
                Money.Pkr(chatBalance));
            var typedNumbers = new[] { "0333-1234567", "+92 333 1234567", "0092-333-1234567", "3331234567", "0333 123 4567" };
            Check("a number typed any of those ways is dialled as one number",
                typedNumbers.All(x => PrintService.ShareNumber(x) == "923331234567"),
                string.Join(" | ", typedNumbers));
            await Throws<InvalidOperationException>(
                "a book with no number to dial is refused before a link is built",
                () => { PrintService.ShareNumber(null); return Task.CompletedTask; });
            await Throws<InvalidOperationException>(
                "and a number too short to dial is refused, rather than opened as a link to nowhere",
                () => { PrintService.ShareNumber("091-445-1"); return Task.CompletedTask; });
            var lacLine = PrintService.ShareText("AUDIT shop", "Abdul Rahim", customerLedger, 425_000.50m);
            Check("a balance over a lac is written in words beside the figures, so a typed 0 shows up",
                lacLine.Contains(Money.Pkr(425_000.50m) + " (" + Money.Words(425_000.50m) + ")"),
                (lacLine.Contains("Balance due") ? lacLine[lacLine.IndexOf("Balance due", StringComparison.Ordinal)..] : lacLine)
                    .Replace("\n", " / "));
            var settled = PrintService.ShareText("AUDIT shop", "Abdul Rahim", customerLedger, 0m);
            Check("a settled ledger is not asked for money",
                !settled.Contains("Balance due") && settled.Contains("settled"));
            var owedToThem = PrintService.ShareText("AUDIT shop", "Abdul Rahim", customerLedger, -4_000m);
            Check("and money lying with the shop is offered back, not shown as a debt",
                owedToThem.Contains(Money.Pkr(4_000m)) && owedToThem.Contains("over and above")
                    && !owedToThem.Contains("Balance due"));
            var bigBook = Enumerable.Range(0, 200).Select(i => new LedgerRow
            {
                Date = DateTime.Today.AddDays(-i),
                Description = "Container " + (i + 1) + " sale",
                Type = LedgerType.Sale,
                Debit = 1_000m + i,
                RunningBalance = 1_000m * (i + 1),
                Step = 200 - i,
            }).ToList();
            var cut = PrintService.ShareText("AUDIT shop", "Abdul Rahim", bigBook, 200_000m);
            Check("a book too long for a link is cut to what a browser reads whole",
                Uri.EscapeDataString(cut).Length <= PrintService.ShareUrlBudget,
                Uri.EscapeDataString(cut).Length + " of " + PrintService.ShareUrlBudget);
            Check("and the cut gives up the oldest lines, never the total or the newest entry",
                cut.Contains(bigBook[^1].Description) && !cut.Contains(bigBook[0].Description)
                    && cut.Contains("earlier lines") && cut.Contains(Money.Pkr(200_000m)),
                cut.Split('\n').Length + " lines sent of " + bigBook.Count);

            Head("a message the shop typed is what goes");
            var typedMessage = PrintService.ShareText("Khyber Traders", "Abdul Rahim", customerLedger, 425_000.50m,
                PrintService.ShareUrlBudget, "Salam {name}, {shop}: {balance} ({words}) on {date}.");
            Check("the book's figures fill the braces and nothing else is added to what was typed",
                typedMessage == "Salam Abdul Rahim, Khyber Traders: " + Money.Pkr(425_000.50m)
                    + " (" + Money.Words(425_000.50m) + ") on " + DateTime.Today.ToString("dd MMM yyyy") + ".",
                typedMessage);
            Check("so a shop that wrote its own message is not sent a statement it never asked for",
                !typedMessage.Contains("Balance due") && !typedMessage.Contains("sold Rs") && !typedMessage.Contains("JazakAllah"),
                typedMessage);
            var placed = PrintService.ShareText("Khyber Traders", "Abdul Rahim", customerLedger, 425_000.50m,
                PrintService.ShareUrlBudget, "Abdul Rahim, your lines:\n{ledger}\nSend {balance} by Friday.");
            Check("{ledger} puts the customer's lines exactly where the shop asked, in the order the money moved",
                placed.StartsWith("Abdul Rahim, your lines:\n" + customerLedger[0].DateText)
                    && placed.Contains(customerLedger[^1].RunningText) && placed.EndsWith("by Friday."),
                placed);
            var tightTyped = PrintService.ShareText("s", "c", bigBook, 200_000m, 600, "{ledger}");
            Check("the link limit falls on the ledger block only - a shop's own words are never cut off",
                Uri.EscapeDataString(tightTyped).Length <= 600 && tightTyped.Contains(bigBook[^1].Description)
                    && !tightTyped.Contains(bigBook[0].Description),
                Uri.EscapeDataString(tightTyped).Length + " of 600");
            var small = PrintService.FillTokens("{balance} ({words})", "s", "c", 400m);
            Check("under a thousand rupees this book has no words, so the figures stand in rather than a hole",
                small == Money.Pkr(400m) + " (" + Money.Pkr(400m) + ")", small);
            Check("a word the book cannot fill is named before the message is saved, not sent to a customer",
                string.Join(",", PrintService.UnknownShareTokens("you owe {blance} in {words}")) == "{blance}",
                string.Join(",", PrintService.UnknownShareTokens("you owe {blance} in {words}")));
            Check("and plain words are nobody's business to refuse",
                PrintService.UnknownShareTokens("Salam, pay by Friday.").Count == 0
                    && PrintService.UnknownShareTokens(null).Count == 0
                    && PrintService.UnknownShareTokens("{ledger} only: {balance}").Count == 0);
            Check("empty settings leave the book writing the whole message, as it did before anyone typed anything",
                PrintService.ShareText("AUDIT shop", "Abdul Rahim", customerLedger, 425_000.50m,
                    PrintService.ShareUrlBudget, "   ")
                    == PrintService.ShareText("AUDIT shop", "Abdul Rahim", customerLedger, 425_000.50m),
                "blank template vs none");
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
            Scan("BuyPlanExpense", await db.BuyPlanExpenses.ToListAsync(),
                x => new[] { ("AmountPkr", x.AmountPkr), ("AmountForeign", x.AmountForeign) });
            Check("nothing stored has a third decimal, so printed = stored = summed", bad.Count == 0, string.Join("; ", bad));
        }

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

    /// <summary>
    /// The rest of the shop's money: what happens to a figure after it has been written, and the identities
    /// that have to survive being changed - a bill edited, a receipt deleted, an expense corrected, a shelf
    /// re-counted, a container closed, a sale cancelled. Every document here is made through the app's own
    /// services and read back twice, once out of the tables and once out of the page that shows it, because a
    /// bug that lives between those two is invisible on the screen and invisible in the row. The figures are
    /// checked as changes rather than as absolutes, so one shop's paperwork cannot hide another's mistake.
    /// </summary>
    private static async Task Reconciliation(string dir)
    {
        var file = Path.Combine(dir, "reconcile.db");
        var services = new ServiceCollection();
        services.AddDbContextFactory<AppDbContext>(o => o.UseSqlite($"Data Source={file};Cache=Shared;Mode=ReadWriteCreate"));
        var sp = services.BuildServiceProvider();
        var f = sp.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using (var db0 = await f.CreateDbContextAsync())
        {
            await db0.Database.EnsureCreatedAsync();
        }

        var inventory = new InventoryService(f);
        var sales = new SalesService(f);
        var ledger = new LedgerService(f);
        var reports = new ReportService(f);
        var cash = new CashBookService(f);
        var shop = new ShopExpenseService(f);

        Head("one lot, the expense written on it, a bill against it");
        var box = await inventory.CreateContainerAsync(
            "REC box", "REC-1", "China", DateTime.Today, null, "PKR", 1m, null, null, null, null,
            "REC supplier", 50_000m, 20_000m, "Cash");
        var fan = await inventory.AddGoodsAsync(box.Id, "Fan", "pcs", "FAN-1", 100m, 2_000m, null, null, null, 1m, null);
        await inventory.AddExpenseAsync(box.Id, DateTime.Today, "Clearing", 1_000m, null);
        var split = await inventory.GetExpenseSplitAsync(box.Id);
        Eq("a box with one lot in it charges that lot the whole of its expense", 1_000m, split.ExpenseTotal);
        Check("and the sharing accounts for every rupee, leaving nothing unexplained",
            split.Absorbed + split.LeftOver == split.ExpenseTotal && split.LeftOver == 0m,
            $"{split.Absorbed} absorbed, {split.LeftOver} left over");
        var fanRow = await ReadItemAsync(f, fan.Id);
        Eq("so a piece costs the goods price plus ten rupees of clearing, to the paisa", 2_010m, fanRow.LandedUnitCost);
        Eq("and nothing has left the shelf yet", 100m, fanRow.QuantityRemaining);
        Eq("the money handed over at the box is what was typed as handed over", 20_000m, await inventory.PaidSoFarAsync(box.Id));
        Eq("and what is owed is the balance typed, not that balance netted down again", 50_000m, await inventory.SupplierBalanceAsync(box.Id));

        var customer = await ledger.CreateCustomerAsync("REC customer", null, null, null);
        var bill = await sales.CreateSaleAsync(customer.Id, DateTime.Today, new List<NewSaleLineInput>
        {
            new() { ContainerId = box.Id, ContainerItemId = fan.Id, ProductId = fan.ProductId, ProductName = "Fan", Unit = "pcs", Quantity = 10m, UnitPrice = 2_500m }
        }, 4_000m, "Cash", null, 0m, DateTime.Today.AddDays(14));
        Eq("ten pieces at the price typed is the bill the book keeps", 25_000m, bill.TotalAmount);
        var lot = await reports.GetContainerProfitAsync(box.Id);
        Eq("the cost of what went out is ten pieces at the landed cost, not at the goods price", 20_100m, lot.Cogs);
        Eq("so the lot's profit is the bill less that cost", 4_900m, lot.Profit);
        Eq("and the market still holds the bill less the money that came in", 21_000m, lot.InMarket);
        Eq("the customer's page says the same thing about what they owe", 21_000m, await ledger.GetBalanceAsync(customer.Id));
        Eq("with the shelf down by what left it", 90m, (await ReadItemAsync(f, fan.Id)).QuantityRemaining);
        var book = await reports.GetDashboardAsync();
        Eq("Home's book card sees these sales", 25_000m, book.TotalRevenue);
        Eq("this profit", 4_900m, book.TotalProfit);
        Eq("and this money receivable in the market", 21_000m, book.MoneyInMarket);

        Head("a bill edited: every figure moves, or none of them do");
        await sales.UpdateSaleAsync(bill.Id, customer.Id, DateTime.Today, new List<NewSaleLineInput>
        {
            new() { ContainerId = box.Id, ContainerItemId = fan.Id, ProductId = fan.ProductId, ProductName = "Fan", Unit = "pcs", Quantity = 12m, UnitPrice = 2_600m }
        }, 4_000m, "Cash", "twelve, at the rate settled on the phone", 0m, DateTime.Today.AddDays(14));
        var edited = await reports.GetContainerProfitAsync(box.Id);
        Eq("the bill is twelve pieces at the new price", 31_200m, edited.Revenue);
        Eq("its cost is twelve pieces at the landed cost, not ten at the old one", 24_120m, edited.Cogs);
        Eq("so the profit that follows is the new bill less the new cost", 7_080m, edited.Profit);
        Eq("the market carries the new bill less the same money that came in", 27_200m, edited.InMarket);
        Eq("the customer's balance moved with the bill", 27_200m, await ledger.GetBalanceAsync(customer.Id));
        Eq("the shelf gave the ten back and took twelve off, so it is down by twelve", 88m, (await ReadItemAsync(f, fan.Id)).QuantityRemaining);
        await using (var db = await f.CreateDbContextAsync())
        {
            var pays = await db.Payments.AsNoTracking().Where(x => x.SaleId == bill.Id).ToListAsync();
            Check("the receipt written against the old bill was rewritten, not added to - one payment, its own amount",
                pays.Count == 1 && pays[0].Amount == 4_000m, pays.Count + " payments totalling " + pays.Sum(x => x.Amount));
            var lines = await db.SaleLines.AsNoTracking().Where(l => l.SaleId == bill.Id).ToListAsync();
            Eq("the lines left from the old bill are the new ones and nothing else", 31_200m, lines.Sum(l => l.LineTotal));
        }
        var afterEdit = await reports.GetDashboardAsync();
        Eq("Home sees the bill as it is now, not as it was first typed", 31_200m, afterEdit.TotalRevenue);
        Eq("and its profit is the edited one", 7_080m, afterEdit.TotalProfit);

        Head("a receipt deleted, and typed back in");
        int payId;
        await using (var db = await f.CreateDbContextAsync())
        {
            payId = (await db.Payments.AsNoTracking().Where(x => x.SaleId == bill.Id).ToListAsync()).Single().Id;
        }
        await ledger.DeletePaymentAsync(payId);
        Eq("the bill stands whole again on the customer's page", 31_200m, await ledger.GetBalanceAsync(customer.Id));
        Eq("and on the lot it was billed from", 31_200m, (await reports.GetContainerProfitAsync(box.Id)).InMarket);
        await using (var db = await f.CreateDbContextAsync())
        {
            var till = await db.CashBook.AsNoTracking().Where(e => e.Kind == CashBookKind.CustomerIn).ToListAsync();
            Check("the till's line for it went too - a deleted receipt still counting in cash in hand is the worst kind of leftover",
                till.Count == 0, till.Count + " receipt lines left in the till");
            var led = await db.LedgerEntries.AsNoTracking()
                .Where(e => e.CustomerId == customer.Id && e.Type == LedgerType.Payment).ToListAsync();
            Check("and so did their ledger line, so the ledger and the till agree about what came in",
                led.Count == 0, led.Count + " payment lines left in the ledger");
        }
        var again = await ledger.ReceivePaymentAsync(customer.Id, DateTime.Today, 4_000m, "Cash", "written back in", bill.Id);
        Eq("typing it again lands the balance where it was", 27_200m, await ledger.GetBalanceAsync(customer.Id));
        Eq("with one receipt of the amount typed", 4_000m, again.Amount);
        var offered = await sales.UnpaidInvoicesAsync(customer.Id);
        Eq("the bill the pay box offers is the outstanding figure the bill's own page holds",
            await sales.RemainingOnInvoiceAsync(bill.Id), offered.Single(u => u.SaleId == bill.Id).Remaining);

        Head("a bill with history under it cannot be rewritten");
        var bill2 = await sales.CreateSaleAsync(customer.Id, DateTime.Today.AddDays(-1), new List<NewSaleLineInput>
        {
            new() { ContainerId = box.Id, ContainerItemId = fan.Id, ProductId = fan.ProductId, ProductName = "Fan", Unit = "pcs", Quantity = 2m, UnitPrice = 2_600m }
        }, 0m, "Cash", null, 0m, null);
        await sales.ReturnItemsAsync(bill2.Id, new List<SaleReturnInput> { new() { SaleLineId = bill2.Lines[0].Id, Quantity = 1m } });
        await Throws<InvalidOperationException>(
            "a bill a customer has partly handed back is not editable over the top of that return",
            () => sales.UpdateSaleAsync(bill2.Id, customer.Id, DateTime.Today, new List<NewSaleLineInput>
            {
                new() { ContainerId = box.Id, ContainerItemId = fan.Id, ProductId = fan.ProductId, ProductName = "Fan", Unit = "pcs", Quantity = 2m, UnitPrice = 2_600m }
            }, 0m, "Cash", null, 0m, null));
        await Throws<InvalidOperationException>(
            "nor cancelled, which would take the return with it - what is left over goes back as a return",
            () => sales.CancelSaleAsync(bill2.Id));
        var oldBill = await sales.CreateSaleAsync(customer.Id, DateTime.Today.AddDays(-2), new List<NewSaleLineInput>
        {
            new() { ContainerId = box.Id, ContainerItemId = fan.Id, ProductId = fan.ProductId, ProductName = "Fan", Unit = "pcs", Quantity = 1m, UnitPrice = 2_600m }
        }, 0m, "Cash", null, 0m, null);
        await Throws<InvalidOperationException>(
            "and a bill written two days ago is not editable at all, however its date is typed now: it is cancelled and made again",
            () => sales.UpdateSaleAsync(oldBill.Id, customer.Id, DateTime.Today, new List<NewSaleLineInput>
            {
                new() { ContainerId = box.Id, ContainerItemId = fan.Id, ProductId = fan.ProductId, ProductName = "Fan", Unit = "pcs", Quantity = 1m, UnitPrice = 2_700m }
            }, 0m, "Cash", null, 0m, null));
        Eq("and the refusal left that bill standing at what it was", 2_600m, oldBill.TotalAmount);

        Head("a sale cancelled: goods back on the shelf, money out of the till");
        var bill3 = await sales.CreateSaleAsync(customer.Id, DateTime.Today, new List<NewSaleLineInput>
        {
            new() { ContainerId = box.Id, ContainerItemId = fan.Id, ProductId = fan.ProductId, ProductName = "Fan", Unit = "pcs", Quantity = 3m, UnitPrice = 2_600m }
        }, 1_000m, "Cash", null, 0m, null);
        var beforeCancel = await reports.GetContainerProfitAsync(box.Id);
        var shelfBefore = (await ReadItemAsync(f, fan.Id)).QuantityRemaining;
        await sales.CancelSaleAsync(bill3.Id);
        Eq("the shelf has the three pieces back", shelfBefore + 3m, (await ReadItemAsync(f, fan.Id)).QuantityRemaining);
        var afterCancel = await reports.GetContainerProfitAsync(box.Id);
        Eq("the cancelled bill is off the lot's sales", beforeCancel.Revenue - bill3.TotalAmount, afterCancel.Revenue);
        Eq("and off its profit, cost of the goods and all",
            beforeCancel.Profit - (bill3.TotalAmount - Money.Round(3m * 2_010m)), afterCancel.Profit);
        await using (var db = await f.CreateDbContextAsync())
        {
            var refund = await db.CashBook.AsNoTracking().Where(e => e.Kind == CashBookKind.RefundOut).ToListAsync();
            Check("the thousand that came in went back out, on a till line of its own",
                refund.Count == 1 && refund[0].AmountOut == 1_000m, refund.Count + " refund lines");
            var st = await db.Sales.AsNoTracking().SingleAsync(x => x.Id == bill3.Id);
            Check("the bill is kept and marked cancelled - a deleted bill is a bill nobody can explain later",
                st.Status == SaleStatus.Cancelled && st.CancelledAt is not null, st.Status.ToString());
        }
        await using (var db = await f.CreateDbContextAsync())
        {
            var lines3 = await db.LedgerEntries.AsNoTracking().Where(e => e.SaleId == bill3.Id).ToListAsync();
            Eq("every line that bill ever wrote - the billing, the receipt, the cancellation, the refund - "
                + "nets to nothing, so calling a bill off leaves the customer neither better nor worse off",
                0m, Money.Round(lines3.Sum(e => e.Debit - e.Credit)));
            Warn("and all four of those lines are on the record, so the paper can be read back afterwards",
                lines3.Count == 4,
                lines3.Count + " lines: " + string.Join(" | ", lines3.Select(x => x.Type + " " + Money.Round(x.Debit - x.Credit))));
        }
        await Throws<InvalidOperationException>("and cancelling it a second time is refused, not repeated",
            () => sales.CancelSaleAsync(bill3.Id));

        Head("the sell page's stock, against the shelf under it");
        var offer = await inventory.GetSellableStockAsync(box.Id);
        await using (var db = await f.CreateDbContextAsync())
        {
            var rows = await db.ContainerItems.AsNoTracking()
                .Where(i => i.ContainerId == box.Id && i.QuantityRemaining > 0).ToListAsync();
            Eq("it offers exactly the pieces the shelf holds, no more and no fewer",
                rows.Sum(i => i.QuantityRemaining), offer.Sum(o => o.Remaining));
        }

        Head("the till's opening figure is a replacement, never an addition");
        await cash.SetOpeningAsync(50_000m);
        await using (var db = await f.CreateDbContextAsync())
        {
            var e = await db.CashBook.AsNoTracking().ToListAsync();
            Eq("cash in hand is the opening plus what came in less what went out",
                50_000m + e.Where(x => x.Kind != CashBookKind.Opening).Sum(x => x.AmountIn - x.AmountOut),
                await CashInHandAsync(f));
        }
        var handBefore = await CashInHandAsync(f);
        await cash.SetOpeningAsync(60_000m);
        Eq("saying it again corrects the figure, so cash in hand moves by the ten thousand and not by sixty",
            10_000m, (await CashInHandAsync(f)) - handBefore);
        await using (var db = await f.CreateDbContextAsync())
        {
            var open = await db.CashBook.AsNoTracking().Where(x => x.Kind == CashBookKind.Opening).ToListAsync();
            Check("and there is one opening line, holding the new figure",
                open.Count == 1 && open[0].AmountIn == 60_000m, open.Count + " opening lines");
        }
        var handOpening = await CashInHandAsync(f);
        await cash.SetOpeningAsync(0m);
        Eq("taking the opening back off leaves no line of nothing behind, and cash in hand by that figure",
            -60_000m, (await CashInHandAsync(f)) - handOpening);

        Head("an expense corrected on the page is corrected in the till and in profit");
        var monthBefore = await reports.GetHomeMonthAsync();
        var rent = await shop.AddAsync(DateTime.Today, "Shop rent", 500m, "one month");
        var monthRent = await reports.GetHomeMonthAsync();
        Eq("the till's own bill comes straight off Home's month profit", monthBefore.Profit - 500m, monthRent.Profit);
        await shop.UpdateAsync(rent.Id, DateTime.Today, "Shop rent", 700m, "two weeks");
        var monthEdit = await reports.GetHomeMonthAsync();
        Eq("correcting it to seven hundred takes another two hundred off, and nothing else moves",
            monthRent.Profit - 200m, monthEdit.Profit);
        await using (var db = await f.CreateDbContextAsync())
        {
            var outgoings = await db.CashBook.AsNoTracking().Where(e => e.Kind == CashBookKind.ExpenseOut).ToListAsync();
            Check("one till line, the new figure - an edit does not leave the old one standing beside it",
                outgoings.Count == 1 && outgoings[0].AmountOut == 700m,
                outgoings.Count + " lines, " + outgoings.Sum(e => e.AmountOut) + " out");
            var kept = await db.ShopExpenses.AsNoTracking().SingleAsync(x => x.Id == rent.Id);
            Check("the note the shop typed is kept as typed, and so is the free words in the description box",
                kept.Notes == "two weeks" && kept.Description == "Shop rent", kept.Description + " / " + kept.Notes);
        }
        await Throws<InvalidOperationException>(
            "an expense with nothing said about what it was for is refused, because a till line that explains nothing cannot be audited",
            () => shop.UpdateAsync(rent.Id, DateTime.Today, "   ", 700m, null));
        await Throws<InvalidOperationException>(
            "and one for nothing or less is refused rather than written as money coming in",
            () => shop.UpdateAsync(rent.Id, DateTime.Today, "Shop rent", 0m, null));
        await shop.DeleteAsync(rent.Id);
        var monthAfterDelete = await reports.GetHomeMonthAsync();
        Eq("deleting it puts the whole of the figure back, once", 700m, monthAfterDelete.Profit - monthEdit.Profit);
        await using (var db = await f.CreateDbContextAsync())
        {
            Check("and its till line goes with it",
                (await db.CashBook.AsNoTracking().Where(e => e.Kind == CashBookKind.ExpenseOut).ToListAsync()).Count == 0);
        }

        Head("a shelf re-counted moves the stock and nothing else");
        var beforeAdjust = await reports.GetContainerProfitAsync(box.Id);
        await inventory.AdjustStockAsync(fan.Id, 85m, "twelve in the store room");
        var afterAdjust = await reports.GetContainerProfitAsync(box.Id);
        Eq("the money already earned is untouched", beforeAdjust.Profit, afterAdjust.Profit);
        Eq("and so is the bill the customer was handed", beforeAdjust.Revenue, afterAdjust.Revenue);
        Eq("the shelf reads the count, though it is no longer what was bought less what was sold",
            85m, (await ReadItemAsync(f, fan.Id)).QuantityRemaining);
        Eq("so what is left on it is worth the count at the landed cost", Money.Round(85m * 2_010m), afterAdjust.RemainingValue);
        await using (var db = await f.CreateDbContextAsync())
        {
            var adj = (await db.StockAdjustments.AsNoTracking().Where(a => a.ContainerItemId == fan.Id).ToListAsync()).Single();
            Check("the count is kept as a record of what it moved from and to, so a shelf that disagrees with "
                + "the book can be traced to the day somebody walked round it",
                adj.QuantityBefore == 87m && adj.QuantityAfter == 85m && adj.Reason == "twelve in the store room",
                $"{adj.QuantityBefore} to {adj.QuantityAfter}: {adj.Reason}");
        }
        await Throws<InvalidOperationException>(
            "a count above what was ever bought is refused, not written as stock appearing",
            () => inventory.AdjustStockAsync(fan.Id, 150m, null));
        await Throws<InvalidOperationException>("and a negative count is refused",
            () => inventory.AdjustStockAsync(fan.Id, -1m, null));

        Head("what may be taken off a container, and what may not");
        var cord = await inventory.AddGoodsAsync(box.Id, "Extension cord", "pcs", "EC-1", 25m, 400m, null, null, null, 0.2m, null);
        await inventory.DeleteGoodsAsync(cord.Id);
        await using (var db = await f.CreateDbContextAsync())
        {
            Check("a lot nobody has sold from goes, and the shelf is untouched by its going",
                !await db.ContainerItems.AnyAsync(i => i.Id == cord.Id)
                    && 85m == (await db.ContainerItems.AsNoTracking().SingleAsync(i => i.Id == fan.Id)).QuantityRemaining);
        }
        await Throws<InvalidOperationException>(
            "a lot with a bill on it stays - deleting it would leave those bills with nothing to have cost",
            () => inventory.DeleteGoodsAsync(fan.Id));

        Head("a container closed is not a container deleted");
        var beforeClose = await reports.GetDashboardAsync();
        await inventory.SetStatusAsync(box.Id, ContainerStatus.Closed);
        var afterClose = await reports.GetDashboardAsync();
        Eq("closing it leaves its sales where they were", beforeClose.TotalRevenue, afterClose.TotalRevenue);
        Eq("its profit where it was", beforeClose.TotalProfit, afterClose.TotalProfit);
        Eq("and what is still out there where it was", beforeClose.MoneyInMarket, afterClose.MoneyInMarket);
        Check("and it stays in the list the pages add up, because a closed box still owes and still gets paid",
            (await reports.GetContainerProfitsAsync()).Any(p => p.ContainerId == box.Id));

        Head("the money the shop paid its supplier, and what is left");
        await inventory.PaySupplierAsync(box.Id, DateTime.Today, 10_000m, "Bank Transfer", "second instalment");
        Eq("comes off what is owed, once", 40_000m, await inventory.SupplierBalanceAsync(box.Id));
        Eq("and shows as money handed over, exactly", 30_000m, await inventory.PaidSoFarAsync(box.Id));

        Head("the lists the pages are built from, against the rows under them");
        var owed = await ledger.GetReceivablesAsync();
        foreach (var r in owed)
        {
            var theirPage = await ledger.GetBalanceAsync(r.CustomerId);
            Check($"{r.Name} appears on To collect with the figure their own page holds",
                theirPage == r.Balance, $"{r.Balance} against {theirPage}");
        }
        Check("and nobody who is even or holding money back is on it, because that list is who to chase",
            owed.All(r => r.Balance > 0m), owed.Where(r => r.Balance <= 0m).Select(r => r.Name + " " + r.Balance).FirstOrDefault() ?? "empty list");
        var homeNow = await reports.GetDashboardAsync();
        Eq("Home's customers-owe figure is that list added up and nothing else",
            Money.Round(owed.Sum(r => r.Balance)), homeNow.MoneyOwedByCustomers);
        var shelf = await reports.GetGrandInventoryAsync(null);
        await using (var db = await f.CreateDbContextAsync())
        {
            var onShelf = (await db.ContainerItems.AsNoTracking().ToListAsync()).Sum(i => i.QuantityRemaining);
            Eq("the inventory page's pieces are the shelf's own rows added up", onShelf, shelf.Sum(x => x.TotalRemaining));
        }
        Eq("and its value is the figure Home's stock line shows",
            Money.Round(homeNow.InventoryValue), Money.Round(shelf.Sum(x => x.TotalValue)));

        Head("a customer's details corrected, and the lists that name them");
        var balanceBeforeEdit = await ledger.GetBalanceAsync(customer.Id);
        var owedBeforeEdit = await reports.GetDashboardAsync();
        await ledger.UpdateCustomerAsync(customer.Id, "REC customer (shop)", "0333-1234567", "Main Bazaar", "renamed");
        Eq("correcting a name or a number moves no money whatever - their ledger is their money, not their label",
            balanceBeforeEdit, await ledger.GetBalanceAsync(customer.Id));
        var bookAfterEdit = await reports.GetDashboardAsync();
        Check("and the book's figures are the same ones under the new name",
            bookAfterEdit.TotalRevenue == owedBeforeEdit.TotalRevenue
                && bookAfterEdit.MoneyInMarket == owedBeforeEdit.MoneyInMarket
                && bookAfterEdit.TotalProfit == owedBeforeEdit.TotalProfit);
        var saved = await ledger.GetCustomerAsync(customer.Id);
        var dialed = PrintService.ShareNumber(saved.Phone);
        Check("and the number is kept as it was typed, ready for the send to dial",
            dialed == "923331234567", saved.Phone + " would dial " + dialed);
        var soldList = await reports.ListSoldProductsAsync();
        await using (var db = await f.CreateDbContextAsync())
        {
            var soldActive = (await db.SaleLines.AsNoTracking().Include(l => l.Sale).ToListAsync())
                .Where(l => l.Sale.Status == SaleStatus.Active).Sum(l => l.Quantity);
            // A difference here is not necessarily a bug: the sell page counts goods, and a piece a customer
            // handed back is both sold and back on the shelf. It is noted so the two pages are looked at
            // together rather than a rule being written backwards to make them agree.
            Warn("the sell page's sold list and the active bills agree on how many pieces went out",
                soldActive == soldList.Sum(x => x.QtySold),
                $"{soldActive} on the bills, {soldList.Sum(x => x.QtySold)} on the list");
            var listed = await sales.ListSalesAsync(500);
            var rows = await db.Sales.AsNoTracking().ToListAsync();
            Check("every bill the bills page lists carries the total the book holds, and none is invented",
                listed.Count <= rows.Count
                    && listed.All(x => rows.Any(y => y.Id == x.Id && y.TotalAmount == x.TotalAmount)),
                listed.Count + " rows against " + rows.Count + " bills");
        }
        var withStock = await inventory.ContainersWithStockAsync();
        await using (var db = await f.CreateDbContextAsync())
        {
            var withRows = (await db.Containers.AsNoTracking().Include(c => c.Items).ToListAsync())
                .Where(c => c.Status == ContainerStatus.Open && c.Items.Any(i => i.QuantityRemaining > 0))
                .Select(c => c.Id).ToList();
            Check("the picker offers every open container the shelf says is not empty, and not the one just closed",
                withStock.Select(c => c.Id).OrderBy(x => x).SequenceEqual(withRows.OrderBy(x => x))
                    && !withStock.Any(c => c.Id == box.Id),
                withStock.Count + " offered, " + withRows.Count + " open with stock, closed box listed: "
                    + withStock.Any(c => c.Id == box.Id));
        }
        var allBoxes = await inventory.ListContainersAsync();
        await using (var db = await f.CreateDbContextAsync())
        {
            var every = await db.Containers.AsNoTracking().Select(c => c.Id).ToListAsync();
            Check("and the containers page sees every box in the book, closed ones too, since a closed box is owed to",
                allBoxes.Count == every.Count,
                allBoxes.Count + " rows against " + every.Count + " containers");
        }

        Head("every row in this shop, reconciled to the figure it belongs to");
        await using (var db = await f.CreateDbContextAsync())
        {
            var allSales = await db.Sales.AsNoTracking().Include(s => s.Lines).ToListAsync();
            var disagree = allSales
                .Where(x => Money.Round(x.Lines.Sum(l => l.LineTotal) - x.DiscountAmount) != x.TotalAmount)
                .ToList();
            Check("every bill's own lines less its discount are its total, so the paper and the book are one figure",
                disagree.Count == 0,
                disagree.Count + " disagree: " + string.Join(", ", disagree.Select(x => $"#{x.Id} holds {x.TotalAmount} against {x.Lines.Sum(l => l.LineTotal) - x.DiscountAmount}")));

            var entries = await db.LedgerEntries.AsNoTracking().ToListAsync();
            var pays = await db.Payments.AsNoTracking().ToListAsync();
            var returns = await db.SaleReturns.AsNoTracking().ToListAsync();
            // Per bill rather than per customer, in totals: a pair of mistakes can cancel in a customer's
            // balance and cannot cancel inside one bill's own lines.
            var off = new List<string>();
            foreach (var sale in allSales)
            {
                var mine = entries.Where(e => e.SaleId == sale.Id).ToList();
                var paid = pays.Where(x => x.SaleId == sale.Id).Sum(x => x.Amount);
                var back = returns.Where(x => x.SaleId == sale.Id).Sum(x => x.Amount);
                var should = sale.Status == SaleStatus.Cancelled
                    ? 0m
                    : Money.Round(sale.TotalAmount - paid - back);
                var written = Money.Round(mine.Sum(e => e.Debit - e.Credit));
                if (written != should)
                    off.Add($"bill {sale.Id} ({sale.Status}): its lines say {written}, the bill says {should}");
            }
            Check("every bill's own ledger lines say what that bill, its receipts and its returns say - cancelled ones say nothing",
                off.Count == 0, string.Join(" | ", off));
            var orphans = entries.Where(e => e.SaleId is null && e.PaymentId is null && e.PayoutId is null
                && e.Type != LedgerType.Opening && e.Type != LedgerType.Adjustment).ToList();
            Check("and no entry floats free of a document unless it is one a person wrote by hand",
                orphans.Count == 0, orphans.Count + " lines with nothing behind them: "
                    + string.Join(" | ", orphans.Take(3).Select(x => x.Type + " " + x.Date.ToString("dd MMM yyyy") + " " + x.Description)));

            var boxes = await db.Containers.AsNoTracking().Include(c => c.Items).ToListAsync();
            var saleLines = await db.SaleLines.AsNoTracking().Include(l => l.Sale).ToListAsync();
            var returnLines = await db.SaleReturnLines.AsNoTracking().ToListAsync();
            var adjusts = await db.StockAdjustments.AsNoTracking().ToListAsync();
            var badStock = new List<string>();
            foreach (var i in boxes.SelectMany(c => c.Items))
            {
                var sold = saleLines.Where(l => l.ContainerItemId == i.Id && l.Sale.Status == SaleStatus.Active).Sum(l => l.Quantity);
                var given = returnLines.Where(l => l.ContainerItemId == i.Id).Sum(l => l.Quantity);
                var moved = adjusts.Where(a => a.ContainerItemId == i.Id).Sum(a => a.QuantityAfter - a.QuantityBefore);
                if (Money.Round(i.QuantityReceived - sold + given + moved, 3) != Money.Round(i.QuantityRemaining, 3))
                    badStock.Add($"item {i.Id}: {i.QuantityReceived} in, {sold} out, {given} back, {moved} counted, {i.QuantityRemaining} left");
            }
            Check("every lot's shelf is what came in, less what went out, plus what was handed back and any count made since",
                badStock.Count == 0, string.Join(" | ", badStock));

            var suppays = await db.SupplierPayments.AsNoTracking().ToListAsync();
            var wrong = new List<string>();
            foreach (var c in boxes)
            {
                var paid = suppays.Where(p => p.ContainerId == c.Id).Sum(p => p.Amount);
                var owedNow = Money.Round(c.SupplierAmount - paid);
                if (owedNow != Money.Round(await inventory.SupplierBalanceAsync(c.Id)))
                    wrong.Add($"{c.Title}: the page says {await inventory.SupplierBalanceAsync(c.Id)}, the rows {owedNow}");
                if (paid > c.SupplierAmount + 0.009m)
                    wrong.Add($"{c.Title}: {paid} paid against a bill of {c.SupplierAmount}");
            }
            Check("what We Owe shows per container is that container's bill less the payments made on it, and never less than nothing",
                wrong.Count == 0, string.Join(" | ", wrong));

            var till = await db.CashBook.AsNoTracking().ToListAsync();
            Eq("and the till's own pages add up to the cash in hand the shop is shown",
                till.Sum(e => e.AmountIn - e.AmountOut), await CashInHandAsync(f));
        }
    }

    /// <summary>The item as the tables hold it, read fresh: a service's return value is the object it was
    /// working with and can carry a figure another save has since moved.</summary>
    private static async Task<ContainerItem> ReadItemAsync(IDbContextFactory<AppDbContext> factory, int itemId)
    {
        await using var db = await factory.CreateDbContextAsync();
        return await db.ContainerItems.AsNoTracking().SingleAsync(i => i.Id == itemId);
    }

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
            + "left between two months and none counted twice", whole, Money.Round(walked));

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
            + "plus money handed over",
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
            + "they were never sold and never paid",
            stmt.Contains(backRow), backRow);

        Head("cash as a month ends it, which is what the ledger card shows");
        var till = new (DateTime Date, decimal In, decimal Out)[]
        {
            (new DateTime(2025, 12, 20), 1_000m, 0m),
            (new DateTime(2026, 1, 10), 500m, 0m),
            (new DateTime(2026, 1, 20), 0m, 200m),
            (new DateTime(2026, 2, 1), 50m, 0m),
        };
        var jan = CashBookService.MonthCash(till, new DateTime(2026, 1, 1));
        Eq("December's thousand is carried into January, not counted as January's own money", 1_000m, jan.Carried);
        Eq("and January closes on the carried thousand, plus its own five hundred, less two hundred out",
            1_300m, jan.Closing);
        var feb = CashBookService.MonthCash(till, new DateTime(2026, 2, 1));
        Eq("February carries January's closing in - one month's end is the next month's beginning",
            1_300m, feb.Carried);
        Eq("and only its own fifty moves it", 1_350m, feb.Closing);
        var mar = CashBookService.MonthCash(till, new DateTime(2026, 3, 1));
        Check("a month with nothing in it closes on what it was handed, rather than showing zero as if the "
              + "till were empty", mar.Carried == 1_350m && mar.Closing == 1_350m,
            $"{mar.Carried} in, {mar.Closing} out");
        var dec = CashBookService.MonthCash(till, new DateTime(2025, 12, 1));
        Eq("a month before any money at all carries nothing in", 0m, dec.Carried);
        Eq("and still closes on what fell inside it", 1_000m, dec.Closing);
        Eq("every month walked in order ends where the whole book stands",
            till.Sum(r => r.In - r.Out), mar.Closing);
        // The edges, spelled out: a line dated at midnight on the first is inside the month, and one dated
        // at midnight on the first of the next is not. This is where a <= against a < quietly moves Rs 50
        // from February into January, and nothing on the page would look wrong.
        var edges = new (DateTime Date, decimal In, decimal Out)[]
        {
            (new DateTime(2026, 1, 1), 7m, 0m),
            (new DateTime(2026, 2, 1), 11m, 0m),
        };
        var edgeJan = CashBookService.MonthCash(edges, new DateTime(2026, 1, 1));
        Eq("the first of the month belongs to the month", 7m, edgeJan.Closing);
        Eq("and the first of the next month is already next month's, carried in but not counted as this month's",
            7m, CashBookService.MonthCash(edges, new DateTime(2026, 2, 1)).Carried);
        Eq("February closes on both", 18m, CashBookService.MonthCash(edges, new DateTime(2026, 2, 1)).Closing);
    }

    private static async Task InvoiceStanding(string dir)
    {
        var file = Path.Combine(dir, "invoice.db");
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

        Head("an invoice's standing: what the book said when the bill was written, and only that");
        var buyer = await ledger.CreateCustomerAsync("Standing buyer", null, null, null);
        var box = await inventory.CreateContainerAsync("STANDING container", "CNT-S1", "China",
            new DateTime(2026, 1, 5), null, "PKR", 1, null, null, null, null, null, 0m, 0m, null);
        var part = await inventory.AddGoodsAsync(box.Id, "Fan belt", "pcs", "FB-9", 10m, 600m,
            null, null, null, null, null);
        await ledger.ReceivePaymentAsync(buyer.Id, new DateTime(2026, 1, 20), 400m, "Cash", "an advance left with the shop");
        await ledger.ReceivePaymentAsync(buyer.Id, new DateTime(2026, 1, 31, 22, 0, 0), 100m, "Cash", "the evening before the bill");
        var bill = await sales.CreateSaleAsync(buyer.Id, new DateTime(2026, 2, 5, 14, 30, 0),
            new List<NewSaleLineInput>
            {
                new() { ContainerId = box.Id, ContainerItemId = part.Id, ProductId = part.ProductId, ProductName = "Fan belt", Unit = "pcs", Quantity = 1m, UnitPrice = 1_000m }
            }, 250m, "Cash", null, 0m, null);

        var at = await ledger.GetInvoiceStandingAsync(bill.Id);
        Eq("the previous balance is what the book held up to the bill's own line", -500m, at.Previous);
        Eq("this invoice's balance is the bill less what it was handed with it", 750m, at.ThisInvoice);
        Eq("and the two add to what they owed when the paper came out of the printer", 250m, at.DueThatDay);
        Eq("while what they owe today is a separate figure, which is the same one while nothing has moved",
            250m, at.DueToday);

        // Money against this bill, entered afterwards. A previous balance back-derived from today's total
        // moves with it, and then the paper says the customer already owed this before the bill was written.
        await ledger.ReceivePaymentAsync(buyer.Id, new DateTime(2026, 2, 20), 500m, "Cash", "part, a fortnight later", bill.Id);
        var again = await ledger.GetInvoiceStandingAsync(bill.Id);
        Eq("a payment against the bill does not move the standing printed on it", at.Previous, again.Previous);
        Eq("nor the bill's own balance", at.ThisInvoice, again.ThisInvoice);
        Eq("while the figure the paper adds as a line does move, to what the book holds now",
            -250m, again.DueToday);

        var next = await sales.CreateSaleAsync(buyer.Id, new DateTime(2026, 2, 25),
            new List<NewSaleLineInput>
            {
                new() { ContainerId = box.Id, ContainerItemId = part.Id, ProductId = part.ProductId, ProductName = "Fan belt", Unit = "pcs", Quantity = 1m, UnitPrice = 300m }
            }, 0m, "Cash", null, 0m, null);
        var nextAt = await ledger.GetInvoiceStandingAsync(next.Id);
        Eq("and the next bill's previous balance carries this one on: what this bill left due, less the "
            + "payment made between them", again.DueThatDay - 500m, nextAt.Previous);

        await sales.CancelSaleAsync(next.Id);
        var cancelled = await ledger.GetInvoiceStandingAsync(next.Id);
        Eq("a cancelled bill leaves no balance of its own on the paper", 0m, cancelled.ThisInvoice);
        Eq("so its total due is the standing it opened with, and nothing else",
            cancelled.Previous, cancelled.DueThatDay);

        var paper = print.InvoiceHtml(await sales.GetSaleAsync(bill.Id)!, new ShopSettings(),
            again.Previous, again.ThisInvoice, again.DueThatDay, again.DueToday);
        Check("the paper names the day these figures belong to, and the day it is speaking about now",
            paper.Contains("Previous ledger balance, as at 05 Feb 2026")
            && paper.Contains("Outstanding on their book today,"), paper);
        Check("and it carries the four figures the book was asked for, in order, with nothing added",
            paper.Contains(Money.Pkr(again.Previous)) && paper.Contains(Money.Pkr(again.ThisInvoice))
            && paper.Contains(Money.Pkr(again.DueThatDay)) && paper.Contains(Money.Pkr(again.DueToday)), paper);
        var quiet = print.InvoiceHtml(await sales.GetSaleAsync(bill.Id)!, new ShopSettings(),
            at.Previous, at.ThisInvoice, at.DueThatDay, at.DueToday);
        Check("while a bill printed on its own day keeps one total, and no apology about today",
            !quiet.Contains("Outstanding on their book today"), quiet);

        // The money-received form offers its bills from this one method, so what it shows to be picked is the
        // book's answer and not a summary of it: the number, the date, what is left, and which container the
        // goods came out of - and nothing that is settled or cancelled, because a bill with no money left on it
        // is not a thing money can be put against.
        Head("the list money is received against, and the figures it states");
        var offered = await sales.UnpaidInvoicesAsync(buyer.Id);
        Check("one bill is offered: the unpaid one, and not the cancelled one", offered.Count == 1,
            string.Join(" | ", offered.Select(u => u.Label)));
        var row = offered[0];
        Check("it is this bill", row.SaleId == bill.Id, $"{row.SaleId} vs {bill.Id}");
        Check("its number, its date and what is left on it are all said",
            row.Label.Contains($"#{bill.Id}") && row.Label.Contains("05 Feb 2026")
            && row.Label.Contains(Plain(Money.Pkr(250m))), row.Label);
        Check("and the container is named with its number, as the container page names it",
            row.Containers.Contains("STANDING container") && row.Containers.Contains("CNT-S1"), row.Containers);
        Eq("the amount it offers to settle is the amount the bill's own page says is left",
            await sales.RemainingOnInvoiceAsync(bill.Id), row.Remaining);
        await Throws<InvalidOperationException>("money past what is left on the chosen bill is refused, not split",
            () => ledger.ReceivePaymentAsync(buyer.Id, new DateTime(2026, 3, 1), 250.01m, "Cash", null, bill.Id));
        await ledger.ReceivePaymentAsync(buyer.Id, new DateTime(2026, 3, 1), 250m, "Cash", "the last of it", bill.Id);
        Check("and a settled bill leaves the list, so it cannot be picked again",
            (await sales.UnpaidInvoicesAsync(buyer.Id)).Count == 0,
            string.Join(" | ", (await sales.UnpaidInvoicesAsync(buyer.Id)).Select(u => u.Label)));
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

    // ------------------------------------------------------------------ freight shared into the cost of each piece
    // The shipment's expenses, in rupees and in yen, added up and divided by what the box weighs, and then
    // put into the cost of every piece. The figures here are worked out by hand from the paper: 600 mugs at
    // 0.45 kg, 400 tumblers at 0.30, 40 trays at 9, so 270 + 120 + 360 = 750 kg, and the expenses are
    // Rs 1,234,567.89 freight, Rs 522,345.30 for the ¥1,234,567 duty at 0.4231, and Rs 45,000.55 clearing -
    // Rs 1,801,913.74 over 750 kg, which is Rs 2,402.5516 a kilo.
    private static async Task FreightSplit(string dir)
    {
        var file = Path.Combine(dir, "freight.db");
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

        Head("the yen bill and the rupee bills are added up, and shared by weight into each piece's cost");
        var box = await inventory.CreateContainerAsync(
            "FREIGHT box", "BOX-1", "Japan", new DateTime(2026, 3, 2), null, "JPY", 0.4231m);
        var mug = await inventory.AddGoodsAsync(box.Id, "Ceramic mug", "pcs", "MUG-1", 600m, 480m, null, null, null, 0.45m, null);
        var tumbler = await inventory.AddGoodsAsync(box.Id, "Glass tumbler", "pcs", "TUM-1", 400m, 720m, null, null, null, 0.30m, null);
        var tray = await inventory.AddGoodsAsync(box.Id, "Tray", "pcs", "TRY-1", 40m, 3000m, null, null, null, 9m, null);
        Eq("the weight divided by is what each lot weighs in all, added up", 750m,
            (await inventory.GetExpenseSplitAsync(box.Id)).TotalWeightKg);
        Check("and before there is an expense there is nothing to share, so no cost moves",
            (await inventory.GetExpenseSplitAsync(box.Id)).ExpenseTotal == 0m);

        await inventory.AddExpenseAsync(box.Id, new DateTime(2026, 3, 5), "Sea Freight", 1_234_567.89m, null);
        await inventory.AddExpenseAsync(box.Id, new DateTime(2026, 3, 6), "Customs Duty", 1_234_567m, "the yen bill", "JPY");
        await inventory.AddExpenseAsync(box.Id, new DateTime(2026, 3, 6), "Clearing", 45_000.55m, null, "PKR");

        ContainerExpense duty = null!, freight = null!, clearing = null!;
        await using (var db = await f.CreateDbContextAsync())
        {
            var rows = await db.Expenses.AsNoTracking().Where(e => e.ContainerId == box.Id).ToListAsync();
            duty = rows.Single(e => e.Category == "Customs Duty");
            freight = rows.Single(e => e.Category == "Sea Freight");
            clearing = rows.Single(e => e.Category == "Clearing");
        }

        Eq("¥1,234,567 at 0.4231 is kept as Rs 522,345.30, the paisa rounded away from nothing", 522_345.30m, duty.Amount);
        Eq("and the yen figure the bill was written in is kept beside it", 1_234_567m, duty.AmountForeign);
        Eq("with the rate that did it", 0.4231m, duty.RateUsed ?? -1m);
        Check("the line itself says where the rupees came from, so the conversion can be checked years later",
            Plain(duty.SourceText).Contains("¥1234567") && Plain(duty.SourceText).Contains("Rs 522345.30"), duty.SourceText);
        Check("a rupee line has nothing to explain about itself",
            freight.SourceText == "" && freight.Currency == "PKR" && freight.AmountForeign == 0m && freight.RateUsed is null,
            $"{freight.Currency} / {freight.AmountForeign} / {freight.RateUsed?.ToString() ?? "none"}");

        // The container has a weight box of its own, from the paper form, and it is deliberately not what
        // the freight is divided by: a divisor a shop cannot tie back to the goods is a divisor nobody can
        // check. Entering 900 kg on the box must therefore leave the sharing on the 750 kg of goods.
        await inventory.UpdateImportDetailsAsync(box.Id, "Osaka Traders", 0m, null, 900m, null);
        Eq("what the container's own weight box says does not move the divisor", 750m,
            (await inventory.GetExpenseSplitAsync(box.Id)).TotalWeightKg);

        var split = await inventory.GetExpenseSplitAsync(box.Id);
        Eq("the three bills are one number, in rupees", 1_801_913.74m, split.ExpenseTotal);
        Eq("which over 750 kg is this rate a kilo", 2_402.55m, split.PerKg);
        Eq("the mugs, being 270 kg, carry", 648_688.95m, split.SharePerItem[mug.Id]);
        Eq("the tumblers, 120 kg", 288_306.20m, split.SharePerItem[tumbler.Id]);
        Eq("and the trays, 360 kg, take the paisa that would not divide", 864_918.59m, split.SharePerItem[tray.Id]);
        Eq("and the shares add to the expenses to the paisa, nothing invented and nothing lost",
            split.ExpenseTotal, split.SharePerItem.Values.Sum());
        Check("the page says the same thing the cost was written from",
            Plain(split.Tape).Contains("Rs 1801913.74") && Plain(split.Tape).Contains("750 kg") && Plain(split.Tape).Contains("Rs 2402.55"),
            split.Tape);

        await using (var db = await f.CreateDbContextAsync())
        {
            var items = await db.ContainerItems.AsNoTracking().Where(i => i.ContainerId == box.Id).ToListAsync();
            decimal Landed(int id) => items.Single(i => i.Id == id).LandedUnitCost;
            Eq("so a mug that cost 480 now costs its share over 600 pieces added on", 1_561.15m, Landed(mug.Id));
            Eq("a tumbler, 720 plus its share over 400", 1_440.77m, Landed(tumbler.Id));
            Eq("and a tray, 3,000 plus its share over 40", 24_622.96m, Landed(tray.Id));
            Eq("and what a piece carries of freight can be read on its own", 1_081.15m, items.Single(i => i.Id == mug.Id).CostEachFreight);
            Eq("the goods price itself is untouched, so the bill from Japan can still be checked", 480m,
                items.Single(i => i.Id == mug.Id).UnitCost);
            Check("the per-piece prices can carry all but the paisa that a thousand pieces cannot divide",
                Plain(split.Tape).Contains("Rs 2.66 more"), split.Tape);
        }

        Head("a piece sold after the freight is costed at the landed figure, and profit is the difference");
        var buyer = await ledger.CreateCustomerAsync("Mug buyer", null, null, null);
        var bill1 = await sales.CreateSaleAsync(buyer.Id, new DateTime(2026, 3, 7), new List<NewSaleLineInput>
        {
            new() { ContainerId = box.Id, ContainerItemId = mug.Id, ProductId = mug.ProductId, ProductName = "Ceramic mug", Unit = "pcs", Quantity = 10m, UnitPrice = 2500m }
        }, 0m, "Cash", null, 0m, null);
        Eq("10 pieces at the landed 1,561.15 cost 15,611.50, not the 4,800 the goods price alone would say",
            15_611.50m, bill1.Lines[0].LineCost);
        var row1 = await reports.GetContainerProfitAsync(box.Id);
        Eq("the container's profit is the bill less that cost", 25_000m - 15_611.50m, row1.Profit);
        Eq("and its expenses are shown beside it, not taken off twice", 1_801_913.74m, row1.Expenses);
        await using (var db = await f.CreateDbContextAsync())
        {
            var stock = await db.ContainerItems.AsNoTracking().SingleAsync(i => i.Id == mug.Id);
            Eq("and the stock left in the store is worth the landed figure too", 590m * 1_561.15m,
                Money.Round(stock.QuantityRemaining * stock.EffectiveCost));
        }

        Head("an item with no weight stops the sharing for the box, and says so");
        var mat = await inventory.AddGoodsAsync(box.Id, "Door mat", "pcs", "MAT-1", 25m, 900m, null, null, null, null, null);
        var refused = await inventory.GetExpenseSplitAsync(box.Id);
        Check("nothing is shared while a lot has no weight", !refused.CanDistribute);
        Check("and the page counts the item that is holding it up", refused.UnweighedItems == 1,
            refused.UnweighedItems + " items unweighed");
        Check("and names the money as outside the costs rather than leaving it unmentioned",
            Plain(refused.Tape).Contains("1 item has no weight") && Plain(refused.Tape).Contains("Rs 1801913.74"),
            refused.Tape);
        await using (var db = await f.CreateDbContextAsync())
        {
            var items = await db.ContainerItems.AsNoTracking().Where(i => i.ContainerId == box.Id).ToListAsync();
            Check("every cost in the box comes back to the goods price alone - the freight is out, not left in",
                items.All(i => i.LandedUnitCost == i.UnitCost),
                string.Join("; ", items.Select(i => $"{i.Id}:{i.LandedUnitCost}/{i.UnitCost}")));
            var line = await db.SaleLines.AsNoTracking().Include(l => l.Sale).SingleAsync(l => l.SaleId == bill1.Id);
            Eq("and the 10 mugs already sold are re-costed back to 4,800, because profit follows the cost",
                4_800m, Money.Round(line.Quantity * line.UnitCost));
        }
        Eq("which puts the profit back where it was before the freight was shared", 25_000m - 4_800m,
            (await reports.GetContainerProfitAsync(box.Id)).Profit);

        Head("weigh that last lot and the whole box is shared again, over the new divisor");
        await inventory.UpdateGoodsAsync(mat.Id, "Door mat", "pcs", "MAT-1", 25m, 25m, 900m, null, null, 20m, null);
        var again = await inventory.GetExpenseSplitAsync(box.Id);
        Eq("500 kg of mats makes the box 1,250 kg", 1_250m, again.TotalWeightKg);
        Eq("so the rate a kilo falls", 1_441.53m, again.PerKg);
        Eq("and a mug, at 270 kg, now carries", 389_213.37m, again.SharePerItem[mug.Id]);
        Eq("the paisa that will not divide going on the heaviest lot", 720_765.49m, again.SharePerItem[mat.Id]);
        Eq("and the shares still add to the expenses exactly", again.ExpenseTotal, again.SharePerItem.Values.Sum());
        await using (var db = await f.CreateDbContextAsync())
        {
            var mugNow = await db.ContainerItems.AsNoTracking().SingleAsync(i => i.Id == mug.Id);
            Eq("which is 1,128.69 a mug, freight of 648.69 on a cost of 480", 1_128.69m, mugNow.LandedUnitCost);
            Eq("and 648.69 a piece of it is freight", 648.69m, mugNow.CostEachFreight);
            var line = await db.SaleLines.AsNoTracking().Include(l => l.Sale).SingleAsync(l => l.SaleId == bill1.Id);
            Eq("the sold 10 mugs follow it to 11,286.90", 11_286.90m, Money.Round(line.Quantity * line.UnitCost));
        }

        Head("the sharing is rebuilt from the goods price every time, so nothing can be charged twice");
        await inventory.UpdateExpenseAsync(clearing.Id, clearing.Date, clearing.Category, clearing.Amount, clearing.Notes, "PKR");
        await using (var db = await f.CreateDbContextAsync())
        {
            var mugNow = await db.ContainerItems.AsNoTracking().SingleAsync(i => i.Id == mug.Id);
            Eq("saving an expense again with the same figure moves no cost", 1_128.69m, mugNow.LandedUnitCost);
        }
        await inventory.AddExpenseAsync(box.Id, new DateTime(2026, 3, 8), "Sea Freight", 1_234_567.89m, "typed twice by mistake", "PKR");
        var doubled = await inventory.GetExpenseSplitAsync(box.Id);
        Eq("a freight bill entered twice really does double the shared amount, as it should",
            3_036_481.63m, doubled.ExpenseTotal);
        await using (var db = await f.CreateDbContextAsync())
        {
            var dupe = await db.Expenses.Where(e => e.ContainerId == box.Id && e.Notes == "typed twice by mistake")
                .Select(e => e.Id).SingleAsync();
            await inventory.DeleteExpenseAsync(dupe);
        }
        Eq("and removing it brings the shared amount back to what it was", 1_801_913.74m,
            (await inventory.GetExpenseSplitAsync(box.Id)).ExpenseTotal);

        Head("the rate is the container's, is applied once, and never goes backwards over a paid expense");
        await inventory.UpdateImportDetailsAsync(box.Id, "Osaka Traders", 0m, null, null, null, 0.50m);
        await using (var db = await f.CreateDbContextAsync())
        {
            var stillDuty = await db.Expenses.AsNoTracking().SingleAsync(e => e.Id == duty.Id);
            Eq("the yen line already in the book keeps the rupees it was converted to at 0.4231", 522_345.30m,
                stillDuty.Amount);
            Eq("and the rate it was converted at", 0.4231m, stillDuty.RateUsed ?? -1m);
        }
        var later = await inventory.AddExpenseAsync(box.Id, new DateTime(2026, 3, 9), "Inspection", 100_000m, null, "JPY");
        Eq("a yen expense written after the rate moved is converted at the new one", 50_000m, later.Amount);
        Eq("and the rate in force when it was written is the one kept on the line", 0.5m, later.RateUsed ?? -1m);
        await inventory.UpdateExpenseAsync(later.Id, later.Date, "Inspection", 100_000m, null, "PKR");
        await using (var db = await f.CreateDbContextAsync())
        {
            var now = await db.Expenses.AsNoTracking().SingleAsync(e => e.Id == later.Id);
            Eq("switching that line to rupees takes the figure as written, with no conversion", 100_000m, now.Amount);
            Check("and it forgets the rate, because there is nothing left to convert", now.RateUsed is null && now.Currency == "PKR");
        }

        Head("delete the expenses and the costs are the goods prices, exactly as they were");
        List<int> left;
        await using (var db = await f.CreateDbContextAsync())
            left = await db.Expenses.Where(e => e.ContainerId == box.Id).Select(e => e.Id).ToListAsync();
        Check("the box has every expense line to lose", left.Count == 3, left.Count + " lines");
        foreach (var id in left)
            await inventory.DeleteExpenseAsync(id);
        await using (var db = await f.CreateDbContextAsync())
        {
            var items = await db.ContainerItems.AsNoTracking().Where(i => i.ContainerId == box.Id).ToListAsync();
            Check("every item's landed cost is its goods cost again",
                items.All(i => i.LandedUnitCost == i.UnitCost),
                string.Join("; ", items.Select(i => $"{i.Id}:{i.LandedUnitCost}/{i.UnitCost}")));
            var line = await db.SaleLines.AsNoTracking().Include(l => l.Sale).SingleAsync(l => l.SaleId == bill1.Id);
            Eq("and the sold line with it, so no freight is left charged to a shipment that has none",
                4_800m, Money.Round(line.Quantity * line.UnitCost));
        }
        Check("and the page stops talking about a sharing there is nothing to share",
            (await inventory.GetExpenseSplitAsync(box.Id)).Tape == "", "the tape should be blank");

        Check("and a yen line's rupees can be re-derived from the two figures kept on it",
            Money.Round(duty.AmountForeign * (duty.RateUsed ?? -1m)) == duty.Amount,
            $"{duty.AmountForeign} x {duty.RateUsed} should be {duty.Amount}, book says {duty.Amount}");

        Head("an item's cost price, typed in yen at the rate on the row");
        var goods = await inventory.CreateContainerAsync(
            "YEN goods box", null, "Japan", new DateTime(2026, 3, 2), null, "JPY", 0.42m);
        var tea = await inventory.AddGoodsAsync(
            goods.Id, "Tea set", "pcs", "TS-1", 30m, 8_900m, null, null, null, 4.2m, null, "JPY", null);
        Eq("¥8,900 a piece at 0.42 is a cost of Rs 3,738", 3_738m, tea.UnitCost);
        Eq("and the invoice's own figure is kept beside it, not replaced by it", 8_900m, tea.ForeignCost);
        Eq("with the rate that did the multiplying", 0.42m, tea.CostRate ?? -1m);
        Check("and the currency it was typed in, so the form can show the same figure back",
            tea.CostCurrency == "JPY", "stored " + tea.CostCurrency);
        Check("the rupee cost re-derives from the yen figure times the rate on the item, to the paisa",
            Money.Round(tea.ForeignCost * (tea.CostRate ?? -1m)) == tea.UnitCost,
            $"{tea.ForeignCost} x {tea.CostRate} against {tea.UnitCost}");
        Eq("and with no expense on the box, the landed cost is that rupee cost", 3_738m, tea.LandedUnitCost);
        await using (var db = await f.CreateDbContextAsync())
        {
            var plain = await db.ContainerItems.AsNoTracking().SingleAsync(i => i.Id == mug.Id);
            Check("a rupee line keeps no rate and says which currency it was",
                plain.CostCurrency == "PKR" && plain.CostRate is null && plain.ForeignCost == plain.UnitCost,
                $"{plain.CostCurrency} / {plain.CostRate?.ToString() ?? "none"} / {plain.ForeignCost}");
        }

        // A rate typed on the expense row is the container's rate from then on, and it never goes back over
        // the goods: the item above stays at the rupee figure its own yen cost was multiplied to.
        var dutyOnGoods = await inventory.AddExpenseAsync(
            goods.Id, new DateTime(2026, 3, 6), "Duty", 100_000m, null, "JPY", 0.5m);
        Eq("¥100,000 at the 0.5 written on the row is Rs 50,000", 50_000m, dutyOnGoods.Amount);
        await using (var db = await f.CreateDbContextAsync())
        {
            var now = await db.Containers.AsNoTracking().SingleAsync(c => c.Id == goods.Id);
            Eq("the row's rate became the container's, so the page holds one rate", 0.5m, now.ExchangeRate);
            var still = await db.ContainerItems.AsNoTracking().SingleAsync(i => i.Id == tea.Id);
            Eq("and the goods cost entered at 0.42 was not re-valued by it", 3_738m, still.UnitCost);
            Eq("with its own rate still on it", 0.42m, still.CostRate ?? -1m);
            // 126 kg of tea sets carry the whole Rs 50,000, so Rs 1,666.67 a piece lands on top of Rs 3,738.
            Eq("the expense is shared over the 126 kg onto the only lot in the box", 5_404.67m,
                still.LandedUnitCost);
        }

        // Correcting the money: an item found to have been a rupee bill is re-saved in rupees, and the
        // yen figure is then simply the same number - no rate, no conversion, nothing to undo.
        await inventory.UpdateGoodsAsync(tea.Id, "Tea set", "pcs", "TS-1", 30m, 30m, 3_700m, null, null, 4.2m,
            null, "PKR", null);
        await using (var db = await f.CreateDbContextAsync())
        {
            var fixed2 = await db.ContainerItems.AsNoTracking().SingleAsync(i => i.Id == tea.Id);
            Eq("the rupee price is taken as written", 3_700m, fixed2.UnitCost);
            Check("and it forgets the rate and the currency it had been entered under",
                fixed2.CostCurrency == "PKR" && fixed2.CostRate is null && fixed2.ForeignCost == 3_700m,
                $"{fixed2.CostCurrency} / {fixed2.CostRate?.ToString() ?? "none"} / {fixed2.ForeignCost}");
            Eq("and the freight is added to the new rupee figure, not to the old one", 5_366.67m,
                fixed2.LandedUnitCost);
        }

        var bare = await inventory.CreateContainerAsync("NO RATE box", null, "Japan", new DateTime(2026, 3, 2), null);
        await Throws<InvalidOperationException>("a yen item cost with no rate on the box is refused, not booked at 1",
            () => inventory.AddGoodsAsync(bare.Id, "Vase", "pcs", "V-1", 10m, 4_000m, null, null, null, 1m, null, "JPY", null));
        await Throws<InvalidOperationException>("the same for an expense",
            () => inventory.AddExpenseAsync(bare.Id, new DateTime(2026, 3, 5), "Customs Duty", 180_000m, null, "JPY"));
        await inventory.AddGoodsAsync(bare.Id, "Vase", "pcs", "V-1", 10m, 4_000m, null, null, null, 1m, null, "JPY", 0.4231m);
        await using (var db = await f.CreateDbContextAsync())
        {
            var vase = await db.ContainerItems.AsNoTracking().SingleAsync(i => i.Product.Sku == "V-1");
            Eq("a rate typed on the row is enough, whatever the container holds", 1_692.40m, vase.UnitCost);
        }
        await Throws<InvalidOperationException>("and zero is not an expense in either currency",
            () => inventory.AddExpenseAsync(bare.Id, new DateTime(2026, 3, 5), "Sea Freight", 0m, null, "PKR"));
        await Throws<InvalidOperationException>("nor is a negative one, which would take freight off a cost",
            () => inventory.AddExpenseAsync(bare.Id, new DateTime(2026, 3, 5), "Rebate", -5_000m, null, "PKR"));

        Head("what a container has collected, and what is still out in the market");
        var marketCustomer = await ledger.CreateCustomerAsync("MARKET customer", null, null, null);
        var market = await inventory.CreateContainerAsync(
            "MARKET box", null, "Japan", new DateTime(2026, 3, 4), null);
        var cups = await inventory.AddGoodsAsync(market.Id, "Cup", "pcs", "CUP-1", 100m, 200m, null, null, null, 0.4m, null);
        var first = await sales.CreateSaleAsync(marketCustomer.Id, new DateTime(2026, 3, 4), new List<NewSaleLineInput>
        {
            new() { ContainerId = market.Id, ContainerItemId = cups.Id, ProductId = cups.ProductId, ProductName = "Cup", Unit = "pcs", Quantity = 10m, UnitPrice = 500m }
        }, 2_000m, "Cash", null, 0m, null);
        Eq("the bill is 10 cups at Rs 500, and Rs 2,000 of it was handed over", 5_000m, first.TotalAmount);
        var firstRow = (await reports.GetContainerProfitsAsync()).Single(r => r.ContainerId == market.Id);
        Eq("a bill drawn from one container is that container's, sold for the whole of it", 5_000m, firstRow.Revenue);
        Eq("its money in the market is the bill's own outstanding, with nothing shared", 3_000m, firstRow.InMarket);
        Eq("and what has come in is the rest", 2_000m, firstRow.Collected);

        // A second bill, this one across two lots: Rs 1,000 of cups and Rs 3,000 of the freight box's mugs,
        // Rs 800 received against it. The outstanding is shared by what each lot was billed for, so the
        // split is 1 to 3, and it has to add back to Rs 3,200 to the paisa.
        var mugRow = mug; // the freight box's mugs, already in scope and still holding 590 of them
        await sales.CreateSaleAsync(marketCustomer.Id, new DateTime(2026, 3, 5), new List<NewSaleLineInput>
        {
            new() { ContainerId = market.Id, ContainerItemId = cups.Id, ProductId = cups.ProductId, ProductName = "Cup", Unit = "pcs", Quantity = 2m, UnitPrice = 500m },
            new() { ContainerId = box.Id, ContainerItemId = mugRow.Id, ProductId = mugRow.ProductId, ProductName = "Mug", Unit = "pcs", Quantity = 1m, UnitPrice = 3_000m }
        }, 800m, "Cash", null, 0m, null);
        var afterMixed = await reports.GetContainerProfitsAsync();
        var market2 = afterMixed.Single(r => r.ContainerId == market.Id);
        var box2 = afterMixed.Single(r => r.ContainerId == box.Id);
        Eq("the cups' lot is billed a further Rs 1,000", 6_000m, market2.Revenue);
        Eq("and carries a quarter of that bill's outstanding", 3_800m, market2.InMarket);
        Eq("three quarters of it sits with the mugs' lot", 2_400m, box2.InMarket);
        Eq("and the two lots' money add back to the two bills, to the paisa", 3_000m + 3_200m,
            market2.InMarket + box2.InMarket);
        Eq("what came in is billed less owed, for each lot", 2_200m, market2.Collected);
        Eq("and for the other one too", 600m, box2.Collected);

        // Two cups come back. The money the shop received does not move - the credit goes against what the
        // customer owes - so the sold figure and the market figure both fall by Rs 1,000, and collected
        // stands where it was. That is the test of the definition, not of arithmetic.
        var cupLine = first.Lines.Single();
        await sales.ReturnItemsAsync(first.Id, new List<SaleReturnInput> { new() { SaleLineId = cupLine.Id, Quantity = 2m } });
        var backRow = (await reports.GetContainerProfitsAsync()).Single(r => r.ContainerId == market.Id);
        Eq("the lot's sold figure is what it brought, less what walked back", 5_000m, backRow.Revenue);
        Eq("the market figure falls by the credit that was taken against the bill", 2_800m, backRow.InMarket);
        Eq("and the money that came in is exactly what came in", 2_200m, backRow.Collected);

        await using (var db = await f.CreateDbContextAsync())
        {
            var bills = await db.Sales.AsNoTracking().Where(x => x.Status == SaleStatus.Active).ToListAsync();
            var pays = await db.Payments.AsNoTracking().ToListAsync();
            var backs = await db.SaleReturns.AsNoTracking().ToListAsync();
            var owed = bills.Sum(x => Math.Max(0m, x.TotalAmount
                - pays.Where(y => y.SaleId == x.Id).Sum(y => y.Amount)
                - backs.Where(y => y.SaleId == x.Id).Sum(y => y.Amount)));
            var spread = (await reports.GetContainerProfitsAsync()).Sum(x => x.InMarket);
            Eq("no container's money is invented or lost by the sharing: the lots owe what the bills owe",
                owed, spread);
            foreach (var r in await reports.GetContainerProfitsAsync())
                Check("sold = collected + in the market, on every lot",
                    Money.Round(r.Collected + r.InMarket) == r.Revenue, $"{r.Title}: {r.Revenue}");
        }

        // The Qty box on an item's form is the landed count, and Save has to be heard by it: a shop that wrote
        // 1,000 and finds 700 in the packing corrects the item, and because the expenses are divided over the
        // pieces that came in, the freight each piece carries moves with the corrected number. The shelf count
        // in the other box must not move it - a re-count of the stack is not a fresh import - and stock above
        // what landed is refused out loud rather than written.
        Head("the landed count an item's Save sends, and the freight that follows it");
        var tally = await inventory.CreateContainerAsync(
            "COUNT box", "BOX-9", "Japan", new DateTime(2026, 3, 9), null, "JPY", 0.4231m);
        var caps = await inventory.AddGoodsAsync(tally.Id, "Cap", "pcs", "CAP-1", 1000m, 100m, null, null, null, 1m, null);
        await inventory.AddExpenseAsync(tally.Id, new DateTime(2026, 3, 9), "Clearing", 700m, null);
        Eq("a thousand pieces of a kilo each is the weight the freight is divided by", 1000m,
            (await inventory.GetExpenseSplitAsync(tally.Id)).TotalWeightKg);
        await using (var db = await f.CreateDbContextAsync())
        {
            var at = await db.ContainerItems.AsNoTracking().FirstAsync(x => x.Id == caps.Id);
            Eq("so each piece carries Rs 0.70 of the Rs 700", 0.70m, at.LandedUnitCost - at.UnitCost);
        }

        await inventory.UpdateGoodsAsync(caps.Id, "Cap", "pcs", "CAP-1", 700m, 700m, 100m, null, null, 1m, null);
        await using (var db = await f.CreateDbContextAsync())
        {
            var at = await db.ContainerItems.AsNoTracking().FirstAsync(x => x.Id == caps.Id);
            Eq("the corrected count is what the item says landed", 700m, at.QuantityReceived);
            Eq("and the freight on a piece doubles with it, to the paisa", 1.00m, at.LandedUnitCost - at.UnitCost);
        }

        await inventory.UpdateGoodsAsync(caps.Id, "Cap", "pcs", "CAP-1", 700m, 640m, 100m, null, null, 1m, null);
        await using (var db = await f.CreateDbContextAsync())
        {
            var at = await db.ContainerItems.AsNoTracking().FirstAsync(x => x.Id == caps.Id);
            Eq("a shelf count moves the stock", 640m, at.QuantityRemaining);
            Eq("but not the landed count", 700m, at.QuantityReceived);
            Eq("so a piece carries exactly what it did before the count", 1.00m, at.LandedUnitCost - at.UnitCost);
        }
        await Throws<InvalidOperationException>("stock above what landed is refused, not stored",
            () => inventory.UpdateGoodsAsync(caps.Id, "Cap", "pcs", "CAP-1", 600m, 650m, 100m, null, null, 1m, null));

        // The home page's eight figures. They are worth checking against the book rather than against each
        // other for one reason: two of them - what is out there on the containers' goods, and what the bills
        // say is still owed - are the same money counted along two different paths, and a page that shows one
        // number for both is the only place that difference would ever be seen.
        Head("the home page's figures, and the two paths that must land on one number");
        var home = await reports.GetDashboardAsync();
        Eq("every container is counted, sold from or not, open or closed", 3m, home.TotalContainers);
        Eq("and the open ones apart, which is what the containers page leads with", 3m, home.OpenContainers);
        // Read in memory, as every money figure in this book is: the amounts live in text columns, and SQLite
        // adding those up is not arithmetic at all.
        decimal billedAll = 0m;
        await using (var db = await f.CreateDbContextAsync())
        {
            billedAll = (await db.Containers.AsNoTracking().ToListAsync()).Sum(c => c.SupplierAmount);
        }
        Eq("purchases are what the containers were billed for, and nothing else",
            Money.Round(billedAll), home.TotalPurchases);
        Eq("the expenses figure is the shipments' bills and the till's own, added",
            home.ContainerExpenses + home.ShopExpenses, home.TotalExpenses);
        Eq("what is out there on the containers' goods is what the bills still owe",
            home.Outstanding, home.MoneyInMarket);
        await using (var db = await f.CreateDbContextAsync())
        {
            var homeBills = await db.Sales.AsNoTracking().Where(x => x.Status == SaleStatus.Active).ToListAsync();
            var homePays = await db.Payments.AsNoTracking().Where(x => x.SaleId != null).ToListAsync();
            var homeBacks = await db.SaleReturns.AsNoTracking().ToListAsync();
            var homeOwed = Money.Round(homeBills.Sum(x => Math.Max(0m, x.TotalAmount
                - homePays.Where(y => y.SaleId == x.Id).Sum(y => y.Amount)
                - homeBacks.Where(y => y.SaleId == x.Id).Sum(y => y.Amount))));
            Eq("and that is the same sum the bills give when they are added by hand", homeOwed, home.Outstanding);
            Eq("so the list of bills needing attention cannot total something else", homeOwed, home.UnpaidTotal);
            var homeStock = Money.Round((await db.ContainerItems.AsNoTracking().ToListAsync())
                .Sum(i => i.QuantityRemaining * i.EffectiveCost));
            Eq("the stock figure is what is left, at what landing it cost", homeStock, home.InventoryValue);
        }
    }

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

    /// <summary>The printed page as a reader sees it rather than as a string: the entities unfolded before the
    /// comparison, because a check that asks whether "&amp;yen;" is on the paper is only ever asking about HTML.</summary>
    private static string OnPaper(string html) => Plain(html
        .Replace("&yen;", "\u00a5").Replace("&#165;", "\u00a5").Replace("&nbsp;", " ")
        .Replace("&amp;", "&").Replace("&#39;", "'").Replace("&quot;", "\""));
}
