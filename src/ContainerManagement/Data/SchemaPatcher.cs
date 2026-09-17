using Microsoft.Data.Sqlite;

namespace ContainerManagement.Data;

public static class SchemaPatcher
{
    public static void Apply(string connectionString)
    {
        using var con = new SqliteConnection(connectionString);
        con.Open();

        AddColumn(con, "Sales", "DiscountAmount", "REAL NOT NULL DEFAULT 0");
        AddColumn(con, "Sales", "DueDate", "TEXT");
        AddColumn(con, "Sales", "Status", "INTEGER NOT NULL DEFAULT 0");
        AddColumn(con, "Sales", "CancelledAt", "TEXT");

        AddColumn(con, "Products", "PhotoPath", "TEXT");
        AddColumn(con, "Products", "LastSalePrice", "REAL");

        AddColumn(con, "ContainerItems", "ForeignCost", "REAL NOT NULL DEFAULT 0");
        AddColumn(con, "ContainerItems", "LandedUnitCost", "REAL NOT NULL DEFAULT 0");
        AddColumn(con, "ContainerItems", "Cartons", "REAL");
        AddColumn(con, "ContainerItems", "Cbm", "REAL");
        AddColumn(con, "ContainerItems", "WeightKg", "REAL");
        AddColumn(con, "ContainerItems", "PhotoPath", "TEXT");

        AddColumn(con, "Containers", "Currency", "TEXT NOT NULL DEFAULT 'PKR'");
        AddColumn(con, "Containers", "ExchangeRate", "REAL NOT NULL DEFAULT 1");
        AddColumn(con, "Containers", "BlNumber", "TEXT");
        AddColumn(con, "Containers", "Cartons", "REAL");
        AddColumn(con, "Containers", "Cbm", "REAL");
        AddColumn(con, "Containers", "WeightKg", "REAL");
        AddColumn(con, "Containers", "SupplierId", "INTEGER");
        AddColumn(con, "Containers", "SupplierAmount", "REAL NOT NULL DEFAULT 0");

        // A container expense can be written in yen, so the line keeps the figure as typed, the rate it was
        // converted at and the rupee total the books use. Decimals are TEXT here, as EF Core's SQLite
        // provider writes them: a REAL column would put a money figure through a binary fraction.
        AddColumn(con, "Expenses", "Currency", "TEXT NOT NULL DEFAULT 'PKR'");
        AddColumn(con, "Expenses", "AmountForeign", "TEXT NOT NULL DEFAULT '0'");
        AddColumn(con, "Expenses", "RateUsed", "TEXT");

        // An item's cost price can be typed in yen, so the invoice's own figure and the rate it was taken
        // at stay on the item. TEXT again for the rate, as above: a float column would put the figure a
        // rupee total was multiplied by through a binary fraction.
        AddColumn(con, "ContainerItems", "CostCurrency", "TEXT NOT NULL DEFAULT 'PKR'");
        AddColumn(con, "ContainerItems", "CostRate", "TEXT");

        AddColumn(con, "LedgerEntries", "PayoutId", "INTEGER");
        AddColumn(con, "CashBook", "PayoutId", "INTEGER");

        Exec(con, """
            CREATE TABLE IF NOT EXISTS Suppliers (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL,
                Phone TEXT,
                Notes TEXT
            );
            """);

        Exec(con, """
            CREATE TABLE IF NOT EXISTS SupplierPayments (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                SupplierId INTEGER NOT NULL,
                Date TEXT NOT NULL,
                Amount REAL NOT NULL,
                Method TEXT,
                Notes TEXT,
                ContainerId INTEGER,
                FOREIGN KEY (SupplierId) REFERENCES Suppliers(Id)
            );
            """);

        Exec(con, """
            CREATE TABLE IF NOT EXISTS StockAdjustments (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                ContainerItemId INTEGER NOT NULL,
                Date TEXT NOT NULL,
                QuantityBefore REAL NOT NULL,
                QuantityAfter REAL NOT NULL,
                Reason TEXT,
                FOREIGN KEY (ContainerItemId) REFERENCES ContainerItems(Id)
            );
            """);

        Exec(con, """
            CREATE TABLE IF NOT EXISTS SaleReturns (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                SaleId INTEGER NOT NULL,
                CustomerId INTEGER NOT NULL,
                Date TEXT NOT NULL,
                Amount REAL NOT NULL,
                Notes TEXT
            );
            """);

        Exec(con, """
            CREATE TABLE IF NOT EXISTS SaleReturnLines (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                SaleReturnId INTEGER NOT NULL,
                SaleLineId INTEGER NOT NULL,
                ContainerId INTEGER NOT NULL,
                ContainerItemId INTEGER NOT NULL,
                ProductId INTEGER NOT NULL,
                Quantity REAL NOT NULL,
                UnitPrice REAL NOT NULL,
                UnitCost REAL NOT NULL,
                Amount REAL NOT NULL,
                FOREIGN KEY (SaleReturnId) REFERENCES SaleReturns(Id)
            );
            """);

        Exec(con, """
            CREATE TABLE IF NOT EXISTS CashMovements (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Date TEXT NOT NULL,
                Direction TEXT NOT NULL,
                Method TEXT,
                Amount REAL NOT NULL,
                Notes TEXT
            );
            """);

        Exec(con, """
            CREATE TABLE IF NOT EXISTS ShopExpenses (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Date TEXT NOT NULL,
                Description TEXT NOT NULL,
                Amount REAL NOT NULL,
                Notes TEXT
            );
            """);

        Exec(con, """
            CREATE TABLE IF NOT EXISTS CashBook (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Date TEXT NOT NULL,
                Kind INTEGER NOT NULL,
                Description TEXT NOT NULL,
                AmountIn REAL NOT NULL,
                AmountOut REAL NOT NULL,
                PaymentId INTEGER,
                SupplierPaymentId INTEGER,
                ShopExpenseId INTEGER,
                SaleId INTEGER
            );
            """);

        // Money the shop paid out to a customer, because their own ledger was in their favour.
        Exec(con, """
            CREATE TABLE IF NOT EXISTS CustomerPayouts (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                CustomerId INTEGER NOT NULL,
                Date TEXT NOT NULL,
                Amount TEXT NOT NULL,
                Method TEXT,
                Notes TEXT,
                FOREIGN KEY (CustomerId) REFERENCES Customers(Id)
            );
            """);

        Exec(con, "CREATE INDEX IF NOT EXISTS IX_CustomerPayouts_CustomerId ON CustomerPayouts(CustomerId, Date);");

        Exec(con, """
            UPDATE ContainerItems
            SET LandedUnitCost = UnitCost
            WHERE LandedUnitCost IS NULL OR LandedUnitCost = 0;
            """);

        // Buy plans (China order sheets) — a plan and its item rows.
        Exec(con, """
            CREATE TABLE IF NOT EXISTS BuyPlans (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Title TEXT NOT NULL,
                CreatedAt TEXT NOT NULL,
                YenRate TEXT NOT NULL,
                ExpensePkr TEXT NOT NULL
            );
            """);

        Exec(con, "CREATE INDEX IF NOT EXISTS IX_BuyPlans_CreatedAt ON BuyPlans(CreatedAt);");

        Exec(con, """
            CREATE TABLE IF NOT EXISTS BuyPlanLines (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                PlanId INTEGER NOT NULL,
                ItemName TEXT NOT NULL,
                Quantity TEXT NOT NULL,
                UnitCostYen TEXT NOT NULL,
                UnitWeightKg TEXT NOT NULL,
                SalePricePkr TEXT NOT NULL,
                FOREIGN KEY (PlanId) REFERENCES BuyPlans(Id) ON DELETE CASCADE
            );
            """);

        Exec(con, "CREATE INDEX IF NOT EXISTS IX_BuyPlanLines_PlanId ON BuyPlanLines(PlanId);");

        // One row per bill on a sheet, as a container keeps them. Made only when it is missing, and the day
        // it is made a sheet that had a single total typed into it keeps standing: that figure becomes one
        // row, so nobody opens an old sheet to find its expense gone, and the new rows start where the book
        // left off. The total itself is not re-typed - it is the sum of the rows from then on.
        if (!HasTable(con, "BuyPlanExpenses"))
        {
            Exec(con, """
                CREATE TABLE BuyPlanExpenses (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    PlanId INTEGER NOT NULL,
                    Description TEXT NOT NULL,
                    AmountPkr TEXT NOT NULL,
                    Currency TEXT NOT NULL,
                    AmountForeign TEXT NOT NULL,
                    RateUsed TEXT,
                    FOREIGN KEY (PlanId) REFERENCES BuyPlans(Id) ON DELETE CASCADE
                );
                """);
            Exec(con, "CREATE INDEX IF NOT EXISTS IX_BuyPlanExpenses_PlanId ON BuyPlanExpenses(PlanId);");
            Exec(con, """
                INSERT INTO BuyPlanExpenses (PlanId, Description, AmountPkr, Currency, AmountForeign, RateUsed)
                SELECT Id, 'Other', ExpensePkr, 'PKR', '0', NULL
                FROM BuyPlans
                WHERE ExpensePkr IS NOT NULL AND CAST(ExpensePkr AS REAL) > 0;
                """);
        }
    }

    private static void AddColumn(SqliteConnection con, string table, string column, string decl)
    {
        if (HasColumn(con, table, column))
            return;
        Exec(con, $"ALTER TABLE {table} ADD COLUMN {column} {decl};");
    }

    private static bool HasTable(SqliteConnection con, string table)
    {
        using var cmd = con.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = @t;";
        cmd.Parameters.AddWithValue("@t", table);
        return Convert.ToInt64(cmd.ExecuteScalar() ?? 0L) > 0L;
    }

    private static bool HasColumn(SqliteConnection con, string table, string column)
    {
        using var cmd = con.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({table});";
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            if (string.Equals(r.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static void Exec(SqliteConnection con, string sql)
    {
        using var cmd = con.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }
}
