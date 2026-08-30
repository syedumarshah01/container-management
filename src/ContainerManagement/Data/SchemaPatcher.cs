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
            UPDATE ContainerItems
            SET LandedUnitCost = UnitCost
            WHERE LandedUnitCost IS NULL OR LandedUnitCost = 0;
            """);
    }

    private static void AddColumn(SqliteConnection con, string table, string column, string decl)
    {
        if (HasColumn(con, table, column))
            return;
        Exec(con, $"ALTER TABLE {table} ADD COLUMN {column} {decl};");
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
