using ContainerManagement.Models;

namespace ContainerManagement.Data;

public static class DbSeeder
{
    public static void SeedMinimal(AppDbContext db)
    {
        if (db.Customers.Any())
            return;
        db.Customers.Add(new Customer
        {
            Name = "Walk-in / Cash",
            IsWalkIn = true,
            Notes = "Default cash customer. Use for counter sales."
        });
        db.SaveChanges();
    }

    public static void Seed(AppDbContext db)
    {
        if (db.Customers.Any())
            return;

        var walkIn = new Customer
        {
            Name = "Walk-in / Cash",
            IsWalkIn = true,
            Notes = "Default cash customer. Use for counter sales."
        };

        var ahmed = new Customer
        {
            Name = "Ahmed Traders",
            Phone = "0300-1112233",
            Address = "Raja Bazaar, Rawalpindi",
            Notes = "Weekly collection on Saturdays."
        };

        var fatima = new Customer
        {
            Name = "Fatima Electronics",
            Phone = "0321-5556677",
            Address = "Blue Area, Islamabad"
        };

        var karachi = new Customer
        {
            Name = "Karachi Light House",
            Phone = "021-34567890",
            Address = "Saddar, Karachi"
        };

        db.Customers.AddRange(walkIn, ahmed, fatima, karachi);

        var pBank = new Product { Name = "20,000 mAh Power Bank", Unit = "pcs", Sku = "PB-20K" };
        var buds = new Product { Name = "TWS Earphones", Unit = "pcs", Sku = "TWS-01" };
        var cable = new Product { Name = "USB-C Fast Cable", Unit = "pcs", Sku = "CBL-C" };
        var pan = new Product { Name = "Non-stick Fry Pan 28cm", Unit = "pcs", Sku = "PAN-28" };
        var lights = new Product { Name = "LED Bulb 12W (box of 10)", Unit = "box", Sku = "LED-12" };
        var mixer = new Product { Name = "Hand Mixer", Unit = "pcs", Sku = "MIX-01" };

        db.Products.AddRange(pBank, buds, cable, pan, lights, mixer);

        var c1 = new CargoContainer
        {
            Title = "Yiwu Electronics — Spring lot",
            ContainerNumber = "MSKU-4419821",
            Origin = "Yiwu, China",
            ArrivalDate = new DateTime(2026, 7, 15),
            Notes = "Cleared at Karachi port. Goods moved to warehouse."
        };

        var c2 = new CargoContainer
        {
            Title = "Guangzhou Home & Kitchen",
            ContainerNumber = "CMAU-1183340",
            Origin = "Guangzhou, China",
            ArrivalDate = new DateTime(2026, 8, 2)
        };

        var c3 = new CargoContainer
        {
            Title = "Shanghai Mix — August",
            ContainerNumber = "COSCO-203881",
            Origin = "Shanghai, China",
            ArrivalDate = new DateTime(2026, 8, 20),
            Notes = "Still unpacking."
        };

        db.Containers.AddRange(c1, c2, c3);
        db.SaveChanges();

        var i1 = new ContainerItem { ContainerId = c1.Id, ProductId = pBank.Id, QuantityReceived = 400, QuantityRemaining = 400, UnitCost = 1850 };
        var i2 = new ContainerItem { ContainerId = c1.Id, ProductId = buds.Id, QuantityReceived = 800, QuantityRemaining = 800, UnitCost = 420 };
        var i3 = new ContainerItem { ContainerId = c1.Id, ProductId = cable.Id, QuantityReceived = 2000, QuantityRemaining = 2000, UnitCost = 55 };
        var i4 = new ContainerItem { ContainerId = c2.Id, ProductId = pan.Id, QuantityReceived = 300, QuantityRemaining = 300, UnitCost = 980 };
        var i5 = new ContainerItem { ContainerId = c2.Id, ProductId = lights.Id, QuantityReceived = 500, QuantityRemaining = 500, UnitCost = 650 };
        var i6 = new ContainerItem { ContainerId = c2.Id, ProductId = mixer.Id, QuantityReceived = 150, QuantityRemaining = 150, UnitCost = 2100 };
        var i7 = new ContainerItem { ContainerId = c3.Id, ProductId = pBank.Id, QuantityReceived = 200, QuantityRemaining = 200, UnitCost = 1790 };
        var i8 = new ContainerItem { ContainerId = c3.Id, ProductId = buds.Id, QuantityReceived = 400, QuantityRemaining = 400, UnitCost = 390 };

        db.ContainerItems.AddRange(i1, i2, i3, i4, i5, i6, i7, i8);

        db.Expenses.AddRange(
            new ContainerExpense { ContainerId = c1.Id, Date = new DateTime(2026, 6, 20), Category = "Sea Freight", Amount = 185_000, Notes = "Qingdao–Karachi" },
            new ContainerExpense { ContainerId = c1.Id, Date = new DateTime(2026, 7, 16), Category = "Customs Duty", Amount = 142_000 },
            new ContainerExpense { ContainerId = c1.Id, Date = new DateTime(2026, 7, 17), Category = "Clearing & Forwarding", Amount = 28_500 },
            new ContainerExpense { ContainerId = c1.Id, Date = new DateTime(2026, 7, 18), Category = "Local Transport", Amount = 22_000 },
            new ContainerExpense { ContainerId = c2.Id, Date = new DateTime(2026, 7, 10), Category = "Sea Freight", Amount = 164_000 },
            new ContainerExpense { ContainerId = c2.Id, Date = new DateTime(2026, 8, 3), Category = "Customs Duty", Amount = 118_000 },
            new ContainerExpense { ContainerId = c2.Id, Date = new DateTime(2026, 8, 4), Category = "Labour", Amount = 12_000 },
            new ContainerExpense { ContainerId = c3.Id, Date = new DateTime(2026, 8, 1), Category = "Sea Freight", Amount = 171_000 },
            new ContainerExpense { ContainerId = c3.Id, Date = new DateTime(2026, 8, 21), Category = "Customs Duty", Amount = 96_000 }
        );

        db.SaveChanges();

        AddSale(db, ahmed, new DateTime(2026, 7, 22), 80_000, "First lift after arrival",
            (i1, 40, 2800m), (i2, 80, 750m));

        AddSale(db, fatima, new DateTime(2026, 7, 28), 50_000, null,
            (i1, 50, 2750m), (i3, 200, 95m));

        AddSale(db, ahmed, new DateTime(2026, 8, 8), 0, "Credit — collect Saturday",
            (i2, 120, 740m), (i3, 100, 90m));

        AddSale(db, karachi, new DateTime(2026, 8, 12), 100_000, null,
            (i4, 60, 1650m), (i5, 80, 1100m), (i6, 25, 3400m));

        AddSale(db, walkIn, new DateTime(2026, 8, 18), 46_000, "Counter sale",
            (i1, 10, 3000m), (i6, 5, 3600m));

        AddSale(db, fatima, new DateTime(2026, 8, 24), 20_000, null,
            (i2, 90, 760m), (i5, 40, 1080m));

        AddPayment(db, ahmed, new DateTime(2026, 8, 2), 40_000, "Cash", "Saturday collection");
        AddPayment(db, karachi, new DateTime(2026, 8, 20), 50_000, "Bank Transfer", null);
        AddPayment(db, fatima, new DateTime(2026, 8, 26), 30_000, "JazzCash", null);
    }

    private static void AddSale(
        AppDbContext db,
        Customer customer,
        DateTime date,
        decimal paidNow,
        string? notes,
        params (ContainerItem item, decimal qty, decimal price)[] lines)
    {
        var sale = new Sale
        {
            CustomerId = customer.Id,
            Date = date,
            Notes = notes,
            PaidNow = paidNow
        };

        foreach (var (item, qty, price) in lines)
        {
            if (item.QuantityRemaining < qty)
                throw new InvalidOperationException($"Seed sale exceeds stock for item {item.Id}.");
            item.QuantityRemaining -= qty;
            sale.Lines.Add(new SaleLine
            {
                ContainerId = item.ContainerId,
                ContainerItemId = item.Id,
                ProductId = item.ProductId,
                Quantity = qty,
                UnitPrice = price,
                UnitCost = item.UnitCost
            });
        }

        sale.TotalAmount = sale.Lines.Sum(l => l.Quantity * l.UnitPrice);
        db.Sales.Add(sale);
        db.SaveChanges();

        db.LedgerEntries.Add(new LedgerEntry
        {
            CustomerId = customer.Id,
            Date = date,
            Type = LedgerType.Sale,
            Debit = sale.TotalAmount,
            Credit = 0,
            Description = $"Sale #{sale.Id}",
            SaleId = sale.Id
        });

        if (paidNow > 0)
        {
            var pay = new Payment
            {
                CustomerId = customer.Id,
                Date = date,
                Amount = paidNow,
                Method = "Cash",
                Notes = $"Against sale #{sale.Id}",
                SaleId = sale.Id
            };
            db.Payments.Add(pay);
            db.SaveChanges();

            db.LedgerEntries.Add(new LedgerEntry
            {
                CustomerId = customer.Id,
                Date = date,
                Type = LedgerType.Payment,
                Debit = 0,
                Credit = paidNow,
                Description = $"Payment against sale #{sale.Id}",
                SaleId = sale.Id,
                PaymentId = pay.Id
            });
        }

        db.SaveChanges();
    }

    private static void AddPayment(AppDbContext db, Customer customer, DateTime date, decimal amount, string method, string? notes)
    {
        var pay = new Payment
        {
            CustomerId = customer.Id,
            Date = date,
            Amount = amount,
            Method = method,
            Notes = notes
        };
        db.Payments.Add(pay);
        db.SaveChanges();

        db.LedgerEntries.Add(new LedgerEntry
        {
            CustomerId = customer.Id,
            Date = date,
            Type = LedgerType.Payment,
            Debit = 0,
            Credit = amount,
            Description = string.IsNullOrWhiteSpace(notes) ? $"{method} payment" : notes,
            PaymentId = pay.Id
        });
        db.SaveChanges();
    }
}
