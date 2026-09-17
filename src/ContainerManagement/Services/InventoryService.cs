using ContainerManagement.Data;
using ContainerManagement.Models;
using Microsoft.EntityFrameworkCore;

namespace ContainerManagement.Services;

public class InventoryService
{
    /// <summary>How a settlement that was not cash is written on the lot's payments, so the lot's balance moves
    /// and the till does not. One string, taken from the list of methods the pages offer, because a word spelled
    /// out twice in two files is a word that eventually gets spelled two ways.</summary>
    public const string CreditNote = SupplierPayMethods.CreditNote;

    private readonly IDbContextFactory<AppDbContext> _factory;

    public InventoryService(IDbContextFactory<AppDbContext> factory) => _factory = factory;

    /// <summary>Whether a line on a lot's payments says money left the till. A credit note settles what is owed
    /// without any cash moving, so a repair that fills in the till from the payments must leave it alone - the
    /// same distinction a customer's adjustments already hold on their own page.</summary>
    public static bool MovesCash(string? method) => method != CreditNote;

    /// <summary>The one way this book works out what a container still owes: the figure written on the lot less
    /// everything settled against it, in cash or not. The container's own page, the pay-a-supplier list and a
    /// goods returned here all read this, because three pages that each subtracted in their own way would be
    /// three answers to one question.</summary>
    public static decimal OwedOnContainer(decimal billed, IEnumerable<SupplierPayment> payments) =>
        Money.Round(billed - payments.Sum(p => p.Amount));

    /// <summary>The most a physical count can say a goods line holds: what was landed, less whatever has since
    /// gone back to the supplier. Without the subtraction a count could put goods that left the shop back in it.</summary>
    public static decimal MaxCountable(decimal received, decimal sentBack) =>
        Money.Round(received - sentBack, 3);

    public async Task<List<CargoContainer>> ListContainersAsync()
    {
        await using var db = await _factory.CreateDbContextAsync();
        return await db.Containers
            .AsNoTracking()
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync();
    }

    public async Task<CargoContainer?> GetContainerAsync(int id)
    {
        await using var db = await _factory.CreateDbContextAsync();
        return await db.Containers
            .AsNoTracking()
            .Include(c => c.Items).ThenInclude(i => i.Product)
            .Include(c => c.Expenses)
            .Include(c => c.Supplier)
            .Include(c => c.SupplierPayments)
            .Include(c => c.SupplierReturns).ThenInclude(r => r.Item).ThenInclude(i => i!.Product)
            .FirstOrDefaultAsync(c => c.Id == id);
    }

    public async Task<CargoContainer> CreateContainerAsync(
        string title, string? number, string origin, DateTime? arrival, string? notes,
        string? currency = null, decimal? rate = null, string? bl = null,
        decimal? cartons = null, decimal? cbm = null, decimal? weight = null,
        string? supplierName = null, decimal supplierAmount = 0, decimal paidNow = 0, string? paidMethod = null)
    {
        if (string.IsNullOrWhiteSpace(title))
            throw new InvalidOperationException("Container title is required.");
        supplierAmount = Money.Round(supplierAmount);
        paidNow = Money.Round(paidNow);
        if (paidNow < 0)
            throw new InvalidOperationException("Amount paid cannot be negative.");
        if (paidNow > 0 && string.IsNullOrWhiteSpace(supplierName))
            throw new InvalidOperationException("Write the supplier name to record what was paid.");
        // The paper form has an arrival date and so does this book: it is what the container page and
        // the lists print, and a shipment without it is a fact nobody can check later. Defaulting it to
        // today is harmless on the day and wrong whenever the entry is made afterwards, which is when
        // the mistake gets noticed. So the date has to be given, like the title.
        if (arrival is null)
            throw new InvalidOperationException("Select the date the container arrived.");

        await using var db = await _factory.CreateDbContextAsync();
        var c = new CargoContainer
        {
            Title = title.Trim(),
            ContainerNumber = TrimOrNull(number),
            Origin = string.IsNullOrWhiteSpace(origin) ? "China" : origin.Trim(),
            ArrivalDate = arrival,
            Notes = notes?.Trim(),
            Currency = string.IsNullOrWhiteSpace(currency) ? "PKR" : currency.Trim().ToUpperInvariant(),
            ExchangeRate = rate is > 0 ? rate.Value : 1,
            BlNumber = TrimOrNull(bl),
            // The figure in the "we owe" box is what the We owe page must show, so it is read as the
            // balance - what is still owed after the money handed over here. The bill is therefore the
            // balance plus that money, and the payment below cancels itself out of the owed figure
            // instead of shaving the shopkeeper's own number a second time. Writing "goods 20 lac, paid
            // 20 lac, still owe 20 lac" is exactly right under this rule: the box is what they owe.
            Cartons = cartons,
            Cbm = cbm,
            WeightKg = weight,
            SupplierAmount = Money.Round(supplierAmount + paidNow)
        };
        if (!string.IsNullOrWhiteSpace(supplierName))
            c.SupplierId = await FindOrCreateSupplierId(db, supplierName, null);
        db.Containers.Add(c);
        await db.SaveChangesAsync();

        // Money already handed over when the container was set up is a payment like any other: it
        // is recorded here so We owe starts at what is genuinely left, and cash drops by the same
        // amount on the day.
        if (paidNow > 0)
        {
            var pay = new SupplierPayment
            {
                SupplierId = c.SupplierId!.Value,
                ContainerId = c.Id,
                Date = DateTime.Today,
                Amount = paidNow,
                Method = string.IsNullOrWhiteSpace(paidMethod) ? "TT" : paidMethod.Trim(),
                Notes = "Paid at creation"
            };
            db.SupplierPayments.Add(pay);
            await db.SaveChangesAsync();
            var supplier = await db.Suppliers.FindAsync(c.SupplierId!.Value);
            if (supplier is not null)
                CashBookService.PostSupplierPayment(db, pay, supplier.Name, c.Title);
            await db.SaveChangesAsync();
        }
        return c;
    }

    /// <summary>
    /// The container's own form: the supplier, what is still owed, what has been paid, the weight and the
    /// day it arrived. The boxes are read as the shop reads them, so "we owe" here is the balance left to
    /// pay, not the invoice total - which is why the bill this table stores is built from the two money
    /// boxes together: what is owed, plus everything paid against this container. That is what keeps the
    /// We owe page showing the figure typed here. A payment is money moving; it is not a licence to
    /// shave the shopkeeper's own number down behind their back.
    ///
    /// What has been paid is not a figure written on the container, it is the pile of payments against
    /// it, so editing it edits the pile. Typing more records one more payment, dated today, marked as
    /// coming from this form. Typing less takes the newest payments back - which is what "less was
    /// paid than I recorded" means - and an in-between amount leaves the newest payment trimmed to fit.
    /// Either way the cash book moves with it, line for line, because money that is recorded as paid
    /// but not as spent is the kind of difference that takes a week to find. Leaving the box empty
    /// leaves the payments alone: an empty box is "not now", never "nothing".
    /// </summary>
    public async Task UpdateImportDetailsAsync(
        int id, string? supplierName, decimal supplierAmount, decimal? paidSoFar, decimal? weight,
        DateTime? arrival = null, decimal? yenRate = null)
    {
        supplierAmount = Money.Round(supplierAmount);
        if (paidSoFar is decimal typed && typed < 0)
            throw new InvalidOperationException("Amount paid cannot be negative.");
        if (supplierAmount < 0)
            throw new InvalidOperationException("The amount owed cannot be negative.");

        await using var db = await _factory.CreateDbContextAsync();
        var c = await db.Containers.FindAsync(id)
            ?? throw new InvalidOperationException("Container not found.");

        var payments = await db.SupplierPayments
            .Where(p => p.ContainerId == id)
            .OrderBy(p => p.Date).ThenBy(p => p.Id)
            .ToListAsync();
        var paid = payments.Sum(p => p.Amount);
        var target = paidSoFar is decimal entered ? Money.Round(entered) : paid;

        // There is no guard here between the two boxes, and there should not be one: the bill is built
        // from both of them (the balance typed plus the payments recorded), so they cannot contradict
        // each other. A figure below what was paid is not an error to refuse - it is what settles a
        // container, which is the ordinary case for goods paid for in full.

        await using var tx = await db.Database.BeginTransactionAsync();

        // Only what the form shows. Cartons and CBM stay as they were set at creation, because a save
        // that writes nulls nobody can see or cancel is how figures quietly disappear from a book.
        c.WeightKg = weight;
        c.SupplierAmount = Money.Round(supplierAmount + target);
        c.SupplierId = string.IsNullOrWhiteSpace(supplierName)
            ? null
            : await FindOrCreateSupplierId(db, supplierName, null);
        // A date can be added or corrected here - a container recorded without one, or with the wrong
        // day, needs a way to be put right without touching the database. An empty picker clears nothing:
        // clearing a date is not something this form offers, so it must not do it by accident.
        if (arrival is DateTime when)
            c.ArrivalDate = when;
        // The rate the yen expenses convert at. Left alone when the box is empty, as the date is; and a
        // rate changed here does not go back over the expenses already recorded, since each keeps the rate
        // it was converted at. No item's cost moves either: the shares are in rupees, fixed when written.
        if (yenRate is decimal rate && rate > 0m)
            c.ExchangeRate = Money.Round(rate, 4);

        if (target > paid)
        {
            if (c.SupplierId is null)
                throw new InvalidOperationException("Write the supplier name to record what was paid.");
            var supplier = await db.Suppliers.FindAsync(c.SupplierId.Value);
            var extra = new SupplierPayment
            {
                SupplierId = c.SupplierId.Value,
                ContainerId = id,
                Date = DateTime.Today,
                Amount = Money.Round(target - paid),
                Method = "Other",
                Notes = "Recorded on the container form"
            };
            db.SupplierPayments.Add(extra);
            await db.SaveChangesAsync();
            if (supplier is not null)
                CashBookService.PostSupplierPayment(db, extra, supplier.Name, c.Title);
        }
        else if (target < paid)
        {
            var back = Money.Round(paid - target);
            for (var i = payments.Count - 1; i >= 0 && back > 0; i--)
            {
                var p = payments[i];
                if (p.Amount <= back)
                {
                    back = Money.Round(back - p.Amount);
                    db.CashBook.RemoveRange(db.CashBook.Where(e => e.SupplierPaymentId == p.Id));
                    db.SupplierPayments.Remove(p);
                }
                else
                {
                    p.Amount = Money.Round(p.Amount - back);
                    back = 0;
                    var line = await db.CashBook.FirstOrDefaultAsync(e => e.SupplierPaymentId == p.Id);
                    if (line is not null)
                        line.AmountOut = p.Amount;
                }
            }
        }

        await db.SaveChangesAsync();
        await tx.CommitAsync();
    }

    /// <summary>What has been handed over for this container so far, from its payments.</summary>
    public async Task<decimal> PaidSoFarAsync(int containerId)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var paid = await db.SupplierPayments.AsNoTracking()
            .Where(p => p.ContainerId == containerId)
            .ToListAsync();
        return paid.Sum(p => p.Amount);
    }

    public async Task SetStatusAsync(int id, ContainerStatus status)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var c = await db.Containers.FindAsync(id)
            ?? throw new InvalidOperationException("Container not found.");
        c.Status = status;
        await db.SaveChangesAsync();
    }

    public async Task<ContainerItem> AddGoodsAsync(
        int containerId, string productName, string unit, string? sku, decimal qty, decimal costEntered,
        string? notes, decimal? cartons, decimal? cbm, decimal? weight, string? photoPath,
        string? costCurrency = null, decimal? rate = null)
    {
        if (string.IsNullOrWhiteSpace(productName))
            throw new InvalidOperationException("Item name is required.");
        if (qty <= 0)
            throw new InvalidOperationException("Quantity must be greater than zero.");

        await using var db = await _factory.CreateDbContextAsync();
        var container = await db.Containers.FindAsync(containerId)
            ?? throw new InvalidOperationException("Container not found.");
        if (container.Status == ContainerStatus.Closed)
            throw new InvalidOperationException("This container is closed. Re-open it to add items.");

        // The price as typed, and the same figure in rupees: the rupee cost is what the rest of the book
        // works in, and ForeignCost keeps what the invoice said, so the yen figure survives the entry.
        var (pkr, costCode, foreign, usedRate) = ConvertGoodsCost(container, costCurrency, costEntered, rate);
        KeepRate(container, costCode, rate);

        var product = await FindOrCreateProductAsync(db, productName, unit, sku);
        if (!string.IsNullOrWhiteSpace(photoPath))
            product.PhotoPath = photoPath;

        var item = new ContainerItem
        {
            ContainerId = containerId,
            ProductId = product.Id,
            QuantityReceived = qty,
            QuantityRemaining = qty,
            ForeignCost = foreign,
            UnitCost = pkr,
            LandedUnitCost = pkr,
            CostCurrency = costCode,
            CostRate = usedRate,
            Notes = notes?.Trim(),
            Cartons = cartons,
            Cbm = cbm,
            WeightKg = weight,
            PhotoPath = photoPath
        };
        db.ContainerItems.Add(item);
        await db.SaveChangesAsync();
        // A new item changes what the container weighs, and so what every other item carries of the freight.
        await ApplyLandedCostsAsync(db, containerId);
        await db.SaveChangesAsync();
        return item;
    }

    /// <returns>How many lines already sold were re-costed along with the item.</returns>
    public async Task<int> UpdateGoodsAsync(
        int itemId, string productName, string unit, string? sku, decimal received, decimal remaining,
        decimal costEntered, decimal? cartons, decimal? cbm, decimal? weight, string? photoPath,
        string? costCurrency = null, decimal? rate = null)
    {
        if (string.IsNullOrWhiteSpace(productName))
            throw new InvalidOperationException("Item name is required.");
        if (received < 0)
            throw new InvalidOperationException("Purchased quantity cannot be negative.");
        if (remaining < 0)
            throw new InvalidOperationException("In stock cannot be negative.");
        if (remaining > received)
            throw new InvalidOperationException("In stock cannot be more than purchased.");

        await using var db = await _factory.CreateDbContextAsync();
        var item = await db.ContainerItems.Include(i => i.Container).FirstOrDefaultAsync(i => i.Id == itemId)
            ?? throw new InvalidOperationException("Item not found.");

        // The same conversion as a new item: the price is taken as typed, in the currency chosen, and the
        // rupee figure is what the book then works in. Re-saving an item in rupees after entering it in
        // yen is how a shop corrects a rate it got wrong, so the currency is not fixed at the first entry.
        var (pkr, costCode, foreign, usedRate) = ConvertGoodsCost(item.Container, costCurrency, costEntered, rate);
        KeepRate(item.Container, costCode, rate);

        var product = await FindOrCreateProductAsync(db, productName, unit, sku);

        item.ProductId = product.Id;
        item.QuantityReceived = received;
        item.QuantityRemaining = remaining;
        item.ForeignCost = foreign;
        item.UnitCost = pkr;
        item.LandedUnitCost = pkr;
        item.CostCurrency = costCode;
        item.CostRate = usedRate;
        item.Cartons = cartons;
        item.Cbm = cbm;
        item.WeightKg = weight;
        if (!string.IsNullOrWhiteSpace(photoPath))
        {
            item.PhotoPath = photoPath;
            product.PhotoPath = photoPath;
        }

        await db.SaveChangesAsync();
        // The cost of the lines already sold is rewritten from the landed figure - goods price plus this
        // container's freight - because a cost the shop corrects is a cost the profit reports have to
        // follow. Only the items whose cost actually moved are touched, so a save that changed nothing
        // reports nothing.
        var repriced = await ApplyLandedCostsAsync(db, item.ContainerId);
        await db.SaveChangesAsync();
        return repriced;
    }

    public async Task DeleteGoodsAsync(int itemId)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var item = await db.ContainerItems.FindAsync(itemId)
            ?? throw new InvalidOperationException("Item not found.");

        var onASale = await db.SaleLines.AnyAsync(l => l.ContainerItemId == itemId);
        if (onASale)
            throw new InvalidOperationException("Cannot delete this item — it is already on a sale.");
        if (item.QuantityRemaining != item.QuantityReceived)
            throw new InvalidOperationException("Cannot delete this item — stock has already moved.");

        var adjustments = await db.StockAdjustments.Where(a => a.ContainerItemId == itemId).ToListAsync();
        db.StockAdjustments.RemoveRange(adjustments);
        var containerId = item.ContainerId;
        db.ContainerItems.Remove(item);
        await db.SaveChangesAsync();
        // The freight this item was carrying does not vanish with it: it belongs to the shipment, and the
        // weight it no longer contributes was part of the divisor. So the rest are shared out again.
        await ApplyLandedCostsAsync(db, containerId);
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// A sale keeps the cost it went out at, which is right when a lot's cost genuinely moves
    /// mid-container. It is wrong for a cost that was simply never filled in - 0 typed to get the
    /// bill out, then corrected later - because profit then stays glued to the old figure forever.
    /// So a corrected cost is applied to the lines already sold from this lot as well, and Home,
    /// Profit and the container all move together. What a customer owes is untouched: only cost
    /// moves, never a price or a bill total.
    /// </summary>
    private static async Task<int> RepriceSoldLinesAsync(AppDbContext db, int containerItemId, decimal cost)
    {
        var lines = await db.SaleLines.Where(l => l.ContainerItemId == containerItemId).ToListAsync();
        foreach (var line in lines)
            line.UnitCost = cost;

        var returned = await db.SaleReturnLines.Where(l => l.ContainerItemId == containerItemId).ToListAsync();
        foreach (var r in returned)
            r.UnitCost = cost;

        return lines.Count + returned.Count;
    }

    public async Task AdjustStockAsync(int itemId, decimal counted, string reason)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var item = await db.ContainerItems.FindAsync(itemId)
            ?? throw new InvalidOperationException("Item not found.");
        if (counted < 0)
            throw new InvalidOperationException("Count cannot be negative.");
        // Goods that went back to the supplier are gone from the shop for good, so they are taken off what a
        // count is allowed to say before the count is believed - otherwise a counted figure could put units
        // that left the building back on the shelf, and the lot would show stock nobody can sell.
        var sentBack = (await db.SupplierReturns.AsNoTracking()
            .Where(r => r.ContainerItemId == itemId).ToListAsync()).Sum(r => r.Quantity);
        var ceiling = MaxCountable(item.QuantityReceived, sentBack);
        if (counted > ceiling)
            throw new InvalidOperationException(counted > item.QuantityReceived
                ? "Count cannot be more than purchased."
                : $"Only {Money.Qty3(ceiling)} can be counted here: {Money.Qty3(item.QuantityReceived)} were landed "
                  + $"and {Money.Qty3(sentBack)} went back to the supplier.");

        db.StockAdjustments.Add(new StockAdjustment
        {
            ContainerItemId = itemId,
            Date = DateTime.Now,
            QuantityBefore = item.QuantityRemaining,
            QuantityAfter = counted,
            Reason = string.IsNullOrWhiteSpace(reason) ? "Physical count" : reason.Trim()
        });
        item.QuantityRemaining = counted;
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// How a container's expenses fall on its goods: shared by weight, since that is how freight and customs
    /// are charged. The expenses are added up, divided by what the items weigh between them, and each item
    /// takes the rate back multiplied by its own weight. The paisa that will not divide goes on the heaviest
    /// item, exactly as a discount's goes on its biggest line, so the shares add to the expenses to the
    /// paisa: not one rupee invented on the way, not one lost.
    ///
    /// An item with no weight stops the sharing rather than being left out of it. The rate is the whole
    /// expense over the whole weight, so the weight those items should have carried would have to be
    /// charged onto the ones that were weighed - and a cost price swollen by a stranger's freight is not
    /// visible on the page that made it wrong.
    /// </summary>
    internal static ExpenseSplit SplitExpense(List<ContainerItem> items, List<ContainerExpense> expenses)
    {
        var split = new ExpenseSplit
        {
            ExpenseTotal = Money.Round(expenses.Sum(e => e.Amount))
        };
        // Only a lot that was actually received can carry anything.
        var lots = items.Where(i => i.QuantityReceived > 0m).ToList();
        split.Items = lots.Count;
        var weighed = lots.Where(i => i.WeightKg is decimal w && w > 0m).ToList();
        split.TotalWeightKg = Money.Round(weighed.Sum(TotalKg), 3);
        split.UnweighedItems = lots.Count - weighed.Count;
        if (!split.CanDistribute)
            return split;

        // Unrounded on purpose: paisa-rounding Rs 459.7701 a kilo before multiplying it by 375 kg loses
        // rupees, and the item costs would then have to invent them back.
        var perKg = split.ExpenseTotal / split.TotalWeightKg;
        split.PerKg = Money.Round(perKg);
        foreach (var i in weighed)
            split.SharePerItem[i.Id] = Money.Round(TotalKg(i) * perKg);
        var left = Money.Round(split.ExpenseTotal - split.SharePerItem.Values.Sum());
        if (left != 0m)
        {
            var heaviest = weighed.OrderByDescending(TotalKg).ThenBy(i => i.Id).First();
            split.SharePerItem[heaviest.Id] = Money.Round(split.SharePerItem[heaviest.Id] + left);
        }

        // What the per-piece costs can actually carry. A share of Rs 172,413.79 over a thousand pieces is
        // Rs 172.41379 apiece, and a price with a third decimal is not a price this book keeps - every cost
        // figure here is paisa-exact so that a bill can be checked by hand against the cost in the grid. So
        // the pieces carry what they can, and the remainder is named rather than folded into a price.
        foreach (var i in weighed)
            split.Absorbed += Money.Round(PerPiece(i, split.SharePerItem[i.Id]) * i.QuantityReceived);
        split.Absorbed = Money.Round(split.Absorbed);
        split.LeftOver = Money.Round(split.ExpenseTotal - split.Absorbed);
        return split;
    }

    /// <summary>An item's weight in the sum: what a piece weighs, times how many were landed. The item form
    /// takes the weight a carton scale gives - the same figure the order sheet asks for - because it is the
    /// figure that is written on the packing, and a lot's total is arithmetic the shop should not have to
    /// do before typing.</summary>
    private static decimal TotalKg(ContainerItem i) => Money.Round(i.WeightKg!.Value * i.QuantityReceived, 3);

    /// <summary>What one piece of this lot carries of the shared expenses.</summary>
    internal static decimal PerPiece(ContainerItem i, decimal share) => Money.Round(share / i.QuantityReceived);

    /// <summary>The expense split for a container, as the pages read it.</summary>
    public async Task<ExpenseSplit> GetExpenseSplitAsync(int containerId)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var items = await db.ContainerItems.AsNoTracking().Where(i => i.ContainerId == containerId).ToListAsync();
        var expenses = await db.Expenses.AsNoTracking().Where(e => e.ContainerId == containerId).ToListAsync();
        return SplitExpense(items, expenses);
    }

    /// <summary>
    /// Writes the split into each item's cost. LandedUnitCost is built from the goods cost every single
    /// time and never added to - so an expense saved twice, or removed and put back, cannot charge a piece
    /// twice, and when the last expense goes the cost comes back to exactly what it was. Lines already sold
    /// follow the new cost, which is how a corrected cost already behaves: profit is about what the goods
    /// cost, and what it took to land them here is part of that.
    /// </summary>
    private static async Task<int> ApplyLandedCostsAsync(AppDbContext db, int containerId)
    {
        var items = await db.ContainerItems.Where(i => i.ContainerId == containerId).ToListAsync();
        var expenses = await db.Expenses.Where(e => e.ContainerId == containerId).ToListAsync();
        var split = SplitExpense(items, expenses);
        var repriced = 0;
        foreach (var item in items)
        {
            var share = split.SharePerItem.TryGetValue(item.Id, out var s) ? s : 0m;
            var landed = item.QuantityReceived > 0m
                ? Money.Round(item.UnitCost + PerPiece(item, share))
                : item.UnitCost;
            if (landed == item.LandedUnitCost)
                continue;
            item.LandedUnitCost = landed;
            repriced += await RepriceSoldLinesAsync(db, item.Id, landed);
        }
        return repriced;
    }

    /// <summary>
    /// An expense as it was written, and what it is in rupees. A yen figure is converted once, at the rate
    /// on the form or the container, and the rate is kept on the line. A container still sitting at the
    /// default rate of 1 would turn ¥180,000 into Rs 180,000, so that is refused out loud rather than
    /// believed - and refused in words that say what to do about it.
    /// </summary>
    private static (decimal Pkr, string Currency, decimal Foreign, decimal? Rate) ConvertExpense(
        CargoContainer container, string? currency, decimal amount, decimal? typedRate)
    {
        var code = Currencies.CodeOf(currency);
        amount = Money.Round(amount);
        if (amount <= 0)
            throw new InvalidOperationException("Expense amount must be greater than zero.");
        if (code != "JPY")
            return (amount, "PKR", 0m, null);
        var converted = Currencies.InRupees(amount, Currencies.RateFor(container.ExchangeRate, typedRate));
        if (converted is null)
            throw new InvalidOperationException(Currencies.NoRateMessage(amount));
        return (converted.Value.Pkr, "JPY", converted.Value.Foreign, converted.Value.Rate);
    }

    /// <summary>
    /// An item's cost price as it was written, and what it is per piece in rupees. The goods price is what
    /// the shop typed, in whichever currency the invoice was in, and the conversion happens once, here:
    /// everything else in the book - the freight shared onto it, what the stock is worth, what a sold line
    /// is costed at - reads rupees and never multiplies by a rate again, so a rate corrected tomorrow
    /// cannot move what was paid yesterday.
    /// </summary>
    private static (decimal Pkr, string Currency, decimal Foreign, decimal? Rate) ConvertGoodsCost(
        CargoContainer container, string? currency, decimal entered, decimal? typedRate)
    {
        var code = Currencies.CodeOf(currency);
        if (entered < 0m)
            throw new InvalidOperationException("Unit cost cannot be negative.");
        if (code != "JPY")
            return (Money.Round(entered), "PKR", Money.Round(entered), null);
        if (entered == 0m)
            return (0m, "JPY", 0m, null);
        var converted = Currencies.InRupees(entered, Currencies.RateFor(container.ExchangeRate, typedRate));
        if (converted is null)
            throw new InvalidOperationException(Currencies.NoRateMessage(entered));
        return (converted.Value.Pkr, "JPY", converted.Value.Foreign, converted.Value.Rate);
    }

    /// <summary>A yen figure was converted at a rate someone wrote on the form: keep that rate as the
    /// container's, so the page holds one rate and not a box that disagrees with the lines beside it.</summary>
    private static void KeepRate(CargoContainer container, string code, decimal? typedRate)
    {
        if (code == "JPY" && typedRate is decimal given && Currencies.UsableRate(given))
            container.ExchangeRate = Currencies.Rate(given);
    }

    public async Task<ContainerExpense> AddExpenseAsync(
        int containerId, DateTime date, string category, decimal amount, string? notes, string? currency = null,
        decimal? rate = null)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var container = await db.Containers.FindAsync(containerId)
            ?? throw new InvalidOperationException("Container not found.");

        var (pkr, code, foreign, used) = ConvertExpense(container, currency, amount, rate);
        KeepRate(container, code, rate);
        await using var tx = await db.Database.BeginTransactionAsync();
        var exp = new ContainerExpense
        {
            ContainerId = containerId,
            Date = date,
            Category = string.IsNullOrWhiteSpace(category) ? "Other" : category.Trim(),
            Amount = pkr,
            Currency = code,
            AmountForeign = foreign,
            RateUsed = used,
            Notes = notes?.Trim()
        };
        db.Expenses.Add(exp);
        // Saved first, because the sharing reads the container's expenses back out of the book: a line that
        // is only in memory is a line whose freight nobody was charged.
        await db.SaveChangesAsync();
        await ApplyLandedCostsAsync(db, containerId);
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return exp;
    }

    public async Task UpdateExpenseAsync(
        int expenseId, DateTime date, string category, decimal amount, string? notes, string? currency = null,
        decimal? rate = null)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var exp = await db.Expenses.FindAsync(expenseId)
            ?? throw new InvalidOperationException("Expense not found.");
        var container = await db.Containers.FindAsync(exp.ContainerId)
            ?? throw new InvalidOperationException("Container not found.");

        var (pkr, code, foreign, used) = ConvertExpense(container, currency, amount, rate);
        KeepRate(container, code, rate);
        await using var tx = await db.Database.BeginTransactionAsync();
        exp.Date = date;
        exp.Category = string.IsNullOrWhiteSpace(category) ? "Other" : category.Trim();
        exp.Amount = pkr;
        exp.Currency = code;
        exp.AmountForeign = foreign;
        exp.RateUsed = used;
        exp.Notes = notes?.Trim();
        await db.SaveChangesAsync();
        await ApplyLandedCostsAsync(db, exp.ContainerId);
        await db.SaveChangesAsync();
        await tx.CommitAsync();
    }

    public async Task DeleteExpenseAsync(int expenseId)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var exp = await db.Expenses.FindAsync(expenseId)
            ?? throw new InvalidOperationException("Expense not found.");
        var containerId = exp.ContainerId;

        await using var tx = await db.Database.BeginTransactionAsync();
        db.Expenses.Remove(exp);
        await db.SaveChangesAsync();
        // Every item's cost is rebuilt from its own goods price, so the freight this line was carrying is
        // taken back out of all of them rather than left in.
        await ApplyLandedCostsAsync(db, containerId);
        await db.SaveChangesAsync();
        await tx.CommitAsync();
    }

    public async Task PaySupplierAsync(int containerId, DateTime date, decimal amount, string method, string? notes)
    {
        amount = Money.Round(amount);
        if (amount <= 0)
            throw new InvalidOperationException("Amount must be greater than zero.");
        await using var db = await _factory.CreateDbContextAsync();
        var c = await db.Containers.FindAsync(containerId)
            ?? throw new InvalidOperationException("Container not found.");
        if (c.SupplierId is null)
            throw new InvalidOperationException("Set the supplier name on this container first.");
        var supplier = await db.Suppliers.FindAsync(c.SupplierId.Value)
            ?? throw new InvalidOperationException("Supplier not found.");
        // What the shop owes a container is the figure on the container, less what has been paid. Money
        // sent past that is not this container's business - an advance belongs to the next shipment, and
        // a bill that was under-recorded belongs on the container form - so the page says which box to
        // fix instead of filing a negative nobody asked for. Same rule the customer side already holds:
        // a payment is never more than the bill.
        var pays = await db.SupplierPayments.AsNoTracking()
            .Where(x => x.ContainerId == containerId).ToListAsync();
        var paid = pays.Sum(x => x.Amount);
        var owed = OwedOnContainer(c.SupplierAmount, pays);
        if (amount > owed)
            throw new InvalidOperationException(owed > 0.009m
                ? "This container is owed " + Money.Pkr(owed) + ". Record " + Money.Pkr(amount - owed)
                  + " against the next shipment, or put the right figure on the container form."
                : "Nothing is owed on this container - " + Money.Pkr(paid) + " has been paid against a "
                  + "figure of " + Money.Pkr(c.SupplierAmount) + ". Put the real amount owed on the "
                  + "container form first.");
        var pay = new SupplierPayment
        {
            SupplierId = c.SupplierId.Value,
            ContainerId = containerId,
            Date = date,
            Amount = amount,
            Method = string.IsNullOrWhiteSpace(method) ? "TT" : method.Trim(),
            Notes = notes?.Trim()
        };
        db.SupplierPayments.Add(pay);
        await db.SaveChangesAsync();
        // A line that settled the bill without cash - a credit note the supplier issued - has no business in
        // the till, and a till that says money went out on it is a till nobody can count.
        if (supplier is not null && MovesCash(pay.Method))
            CashBookService.PostSupplierPayment(db, pay, supplier.Name, c.Title);
        await db.SaveChangesAsync();
    }

    public async Task<decimal> SupplierBalanceAsync(int containerId)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var c = await db.Containers.AsNoTracking().FirstOrDefaultAsync(x => x.Id == containerId);
        if (c is null) return 0;
        var pays = await db.SupplierPayments.AsNoTracking()
            .Where(p => p.ContainerId == containerId)
            .ToListAsync();
        return OwedOnContainer(c.SupplierAmount, pays);
    }

    /// <summary>
    /// Goods the shop hands back to the supplier, and whatever comes back with them, on one row.
    ///
    /// What arrived on the container is never rewritten: a lot that landed 600 pieces landed 600, whatever
    /// happened after, and the bill the supplier sent stays the figure it was written as. This adds the two
    /// facts that follow - the units are no longer here, and money or a settlement arrived - so the lot's stock
    /// falls, the till rises if cash came, and what is owed on the lot falls if they adjusted the bill instead.
    /// The three money boxes are each typed and each kept as typed, and nothing here decides between them:
    /// where a shop's money lands is the shop's answer, not an inference from the goods.
    /// </summary>
    public async Task<SupplierReturn> ReturnToSupplierAsync(
        int containerItemId, DateTime date, decimal quantity, string? reason,
        decimal intoTillPkr, decimal againstBillPkr, decimal againstFreightPkr, string? notes)
    {
        quantity = Money.Round(quantity, 3);
        intoTillPkr = Money.Round(intoTillPkr);
        againstBillPkr = Money.Round(againstBillPkr);
        againstFreightPkr = Money.Round(againstFreightPkr);
        if (quantity <= 0m)
            throw new InvalidOperationException("Units to send back must be greater than zero.");
        if (intoTillPkr < 0m || againstBillPkr < 0m || againstFreightPkr < 0m)
            throw new InvalidOperationException("A figure sent back cannot be negative.");

        await using var db = await _factory.CreateDbContextAsync();
        var item = await db.ContainerItems.Include(i => i.Product).FirstOrDefaultAsync(i => i.Id == containerItemId)
            ?? throw new InvalidOperationException("Item not found.");
        var container = await db.Containers.FindAsync(item.ContainerId)
            ?? throw new InvalidOperationException("Container not found.");
        var productName = item.Product.Name;
        var unit = item.Product.Unit;
        if (container.Status == ContainerStatus.Closed)
            throw new InvalidOperationException("This container is closed. Re-open it to send goods back.");
        // The units have to be in the shop to be handed over, which is what the stock figure says: goods still
        // owed to a customer are not here, so a whole-container return is booked after their returns are.
        if (quantity > item.QuantityRemaining)
            throw new InvalidOperationException("Only " + Money.Qty3(item.QuantityRemaining) + " " + unit + " of "
                + productName + " are here to send back. Take the goods in from the customers first.");

        var pays = await db.SupplierPayments.Where(p => p.ContainerId == container.Id).ToListAsync();
        var owed = OwedOnContainer(container.SupplierAmount, pays);
        if (againstBillPkr > 0m && againstBillPkr > owed)
            throw new InvalidOperationException("Only " + Money.Pkr(owed) + " is still owed on this container to "
                + "settle against. Write " + Money.Pkr(owed) + " against the bill and the rest into the till.");
        var billedNow = (await db.Expenses.AsNoTracking()
            .Where(e => e.ContainerId == container.Id).ToListAsync()).Sum(e => e.Amount);
        if (againstFreightPkr > 0m && againstFreightPkr > billedNow)
            throw new InvalidOperationException(Money.Pkr(againstFreightPkr)
                + " was never spent on this container's bills, which add up to " + Money.Pkr(billedNow) + ".");

        await using var tx = await db.Database.BeginTransactionAsync();
        var row = new SupplierReturn
        {
            ContainerId = container.Id,
            ContainerItemId = item.Id,
            Date = date.Date,
            Quantity = quantity,
            Reason = TrimOrNull(reason),
            IntoTillPkr = intoTillPkr,
            AgainstBillPkr = againstBillPkr,
            AgainstFreightPkr = againstFreightPkr,
            Notes = TrimOrNull(notes)
        };
        item.QuantityRemaining = Money.Round(item.QuantityRemaining - quantity, 3);
        db.SupplierReturns.Add(row);
        await db.SaveChangesAsync();

        if (intoTillPkr > 0m)
        {
            var supplierName = container.SupplierId is int sid
                ? (await db.Suppliers.FindAsync(sid))?.Name ?? "Supplier"
                : "Supplier";
            CashBookService.PostSupplierRefund(db, row, supplierName, container.Title);
        }
        if (againstBillPkr > 0m)
        {
            if (container.SupplierId is null)
                throw new InvalidOperationException("Set the supplier name on this container to settle a bill with them.");
            var settlement = new SupplierPayment
            {
                SupplierId = container.SupplierId.Value,
                ContainerId = container.Id,
                Date = date.Date,
                Amount = againstBillPkr,
                Method = CreditNote,
                Notes = "Goods sent back — " + productName
            };
            db.SupplierPayments.Add(settlement);
            await db.SaveChangesAsync();
            row.SettlementPaymentId = settlement.Id;
        }
        if (againstFreightPkr > 0m)
        {
            // A bill line that takes money off the container, so the freight shared into every remaining piece
            // is rebuilt from what the lot actually cost. Written here rather than typed by hand, because a
            // recovery nobody can tie to the goods that caused it is a figure no auditor can follow.
            var recovery = new ContainerExpense
            {
                ContainerId = container.Id,
                Date = date.Date,
                Category = "Recovered",
                Amount = -againstFreightPkr,
                Currency = "PKR",
                AmountForeign = 0m,
                Notes = "Against goods sent back — " + productName
            };
            db.Expenses.Add(recovery);
            await db.SaveChangesAsync();
            row.FreightExpenseId = recovery.Id;
            await ApplyLandedCostsAsync(db, container.Id);
        }
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return row;
    }

    /// <summary>
    /// Undo a goods return, line and all: the units go back on the shelf, and whatever was recorded with them
    /// comes back out of the book. It is the same removal the container's bill lines allow, because a figure
    /// typed in a hurry on a night when the shipment was being unloaded is a mistake the shop has to be able
    /// to take back, and it cannot take it back by hand without going into the database.
    /// </summary>
    public async Task RemoveReturnAsync(int returnId)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var row = await db.SupplierReturns.FindAsync(returnId)
            ?? throw new InvalidOperationException("This entry is gone. It may have been removed.");
        var item = await db.ContainerItems.FindAsync(row.ContainerItemId)
            ?? throw new InvalidOperationException("Item not found.");
        var container = await db.Containers.FindAsync(row.ContainerId)
            ?? throw new InvalidOperationException("Container not found.");
        if (container.Status == ContainerStatus.Closed)
            throw new InvalidOperationException("This container is closed. Re-open it to change what went back.");
        var sentBack = (await db.SupplierReturns.AsNoTracking()
            .Where(r => r.ContainerItemId == row.ContainerItemId && r.Id != row.Id).ToListAsync()).Sum(r => r.Quantity);
        var back = Money.Round(item.QuantityRemaining + row.Quantity, 3);
        if (back > MaxCountable(item.QuantityReceived, sentBack))
            throw new InvalidOperationException("Only " + Money.Qty3(MaxCountable(item.QuantityReceived, sentBack))
                + " could be here if this return is removed, and " + Money.Qty3(back) + " would be. Units have "
                + "been sold since, or sent back again - remove the later line first.");

        // Each line the return wrote is taken back by its own id, so nothing is left behind and nothing of
        // another return's is taken by mistake: the till line that points at this one, the settlement line on
        // the lot, and the negative bill line whose absence the freight has to be shared out over again.
        // If the container form has since been saved with a lower "paid" figure, its reconciling loop may have
        // trimmed the settlement line away - that is the typed box being believed, as it is everywhere else in
        // the book - so a line that is no longer here is not an error to stop on, only one to leave alone.
        await using var tx = await db.Database.BeginTransactionAsync();
        var tillLines = await db.CashBook.Where(e => e.SupplierReturnId == row.Id).ToListAsync();
        db.CashBook.RemoveRange(tillLines);
        if (row.SettlementPaymentId is int payId)
        {
            var settlement = await db.SupplierPayments.FindAsync(payId);
            if (settlement is not null)
                db.SupplierPayments.Remove(settlement);
        }
        if (row.FreightExpenseId is int expId)
        {
            var recovery = await db.Expenses.FindAsync(expId);
            if (recovery is not null)
                db.Expenses.Remove(recovery);
        }
        db.SupplierReturns.Remove(row);
        item.QuantityRemaining = back;
        await db.SaveChangesAsync();
        await ApplyLandedCostsAsync(db, container.Id);
        await db.SaveChangesAsync();
        await tx.CommitAsync();
    }

    /// <summary>
    /// Take a container out of the book entirely. Only allowed on a lot nothing else was ever written against:
    /// a lot that sold, that was billed, that money moved on, or that goods came back from is a lot the year's
    /// totals and the printed pack already speak about, and removing it would leave the book with money in it
    /// and nothing to say why. Closing the lot does what the shop actually needs of it - it leaves the working
    /// pages and every stock picker - and keeps the paper.
    /// </summary>
    public async Task DeleteContainerAsync(int id)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var c = await db.Containers.Include(x => x.Items).Include(x => x.Expenses)
            .Include(x => x.SupplierPayments).Include(x => x.SupplierReturns)
            .FirstOrDefaultAsync(x => x.Id == id)
            ?? throw new InvalidOperationException("Container not found.");

        var onASale = await db.SaleLines.CountAsync(l => l.ContainerId == id);
        if (onASale > 0)
            throw new InvalidOperationException("This container has " + onASale + " invoice line"
                + (onASale == 1 ? "" : "s") + " on its goods. An invoice stays on the record even when it is "
                + "cancelled, so the lot has to stay with it - close the container instead, and it leaves every "
                + "working page.");
        if (c.Expenses.Count > 0)
            throw new InvalidOperationException("This container has " + c.Expenses.Count
                + " bill line" + (c.Expenses.Count == 1 ? "" : "s") + " on it. Remove"
                + (c.Expenses.Count == 1 ? " it" : " them") + " first, so the money is taken out of the book on purpose.");
        if (c.SupplierPayments.Count > 0)
            throw new InvalidOperationException("Money was settled to the supplier against this container, so the "
                + "till and their account both speak of it. Close the container instead.");
        if (c.SupplierReturns.Count > 0)
            throw new InvalidOperationException("Goods from this container went back to the supplier, and that "
                + "exchange is money the shop got back. Close the container instead.");

        await using var tx = await db.Database.BeginTransactionAsync();
        var itemIds = c.Items.Select(i => i.Id).ToList();
        var counts = await db.StockAdjustments.Where(a => itemIds.Contains(a.ContainerItemId)).ToListAsync();
        db.StockAdjustments.RemoveRange(counts);
        db.ContainerItems.RemoveRange(c.Items);
        db.Containers.Remove(c);
        await db.SaveChangesAsync();
        await tx.CommitAsync();
    }

    public async Task<List<StockOption>> GetSellableStockAsync(int? containerId = null)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var q = db.ContainerItems
            .AsNoTracking()
            .Include(i => i.Product)
            .Include(i => i.Container)
            .Where(i => i.QuantityRemaining > 0 && i.Container.Status == ContainerStatus.Open);

        if (containerId is > 0)
            q = q.Where(i => i.ContainerId == containerId);

        return await q
            .OrderBy(i => i.Container.Title)
            .ThenBy(i => i.Product.Name)
            .Select(i => new StockOption
            {
                ContainerItemId = i.Id,
                ContainerId = i.ContainerId,
                ContainerTitle = i.Container.Title,
                ProductId = i.ProductId,
                ProductName = i.Product.Name,
                Sku = i.Product.Sku,
                Unit = i.Product.Unit,
                Remaining = i.QuantityRemaining,
                UnitCost = i.UnitCost,
                // Spelled out rather than written as i.EffectiveCost, because this half is still a database
                // query and a property the columns do not have cannot be translated: the same condition,
                // in the same words as ContainerItem.EffectiveCost, which is what the pages read.
                LandedCost = i.LandedUnitCost > 0 ? i.LandedUnitCost : i.UnitCost,
                LastSalePrice = i.Product.LastSalePrice
            })
            .ToListAsync();
    }

    public async Task<List<CargoContainer>> ContainersWithStockAsync()
    {
        await using var db = await _factory.CreateDbContextAsync();
        return await db.Containers
            .AsNoTracking()
            .Where(c => c.Status == ContainerStatus.Open && c.Items.Any(i => i.QuantityRemaining > 0))
            .OrderBy(c => c.Title)
            .ToListAsync();
    }

    private static async Task<Product> FindOrCreateProductAsync(AppDbContext db, string name, string unit, string? sku)
    {
        name = name.Trim();
        unit = string.IsNullOrWhiteSpace(unit) ? "pcs" : unit.Trim();
        sku = TrimOrNull(sku);
        var existing = await db.Products.FirstOrDefaultAsync(p =>
            p.Name.ToLower() == name.ToLower() && (p.Sku ?? "") == (sku ?? ""));
        if (existing is not null)
        {
            existing.Unit = unit;
            return existing;
        }

        var p = new Product { Name = name, Unit = unit, Sku = sku };
        db.Products.Add(p);
        await db.SaveChangesAsync();
        return p;
    }

    private static async Task<int> FindOrCreateSupplierId(AppDbContext db, string name, string? phone)
    {
        name = name.Trim();
        var s = await db.Suppliers.FirstOrDefaultAsync(x => x.Name.ToLower() == name.ToLower());
        if (s is null)
        {
            s = new Supplier { Name = name, Phone = TrimOrNull(phone) };
            db.Suppliers.Add(s);
            await db.SaveChangesAsync();
        }
        return s.Id;
    }

    private static string? TrimOrNull(string? s) =>
        string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
