using System.Text;
using ContainerManagement.Data;
using ContainerManagement.Models;

namespace ContainerManagement.Services;

public class ExportService
{
    public string WriteCsv(string name, IEnumerable<string> headers, IEnumerable<IEnumerable<string>> rows)
    {
        var path = Path.Combine(DbPaths.PrintDirectory, name);
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(",", headers.Select(Csv)));
        foreach (var row in rows)
            sb.AppendLine(string.Join(",", row.Select(Csv)));
        File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true
        });
        return path;
    }

    public void ProfitWorkbook(
        IReadOnlyList<ContainerProfitRow> containers,
        IReadOnlyList<ItemProfitRow> items,
        IReadOnlyList<DailySummaryRow> days)
    {
        WriteCsv("cargokhata-containers.csv",
            ["Container", "Status", "Sales", "COGS", "Expenses", "Profit", "Stock left"],
            containers.Select(c => new[]
            {
                c.Title, c.StatusText, c.Revenue.ToString("0.00"), c.Cogs.ToString("0.00"),
                c.Expenses.ToString("0.00"), c.Profit.ToString("0.00"), c.RemainingValue.ToString("0.00")
            }));
        WriteCsv("cargokhata-items.csv",
            ["Item", "SKU", "Qty sold", "Sales", "Cost", "Profit"],
            items.Select(i => new[]
            {
                i.ProductName, i.Sku ?? "", i.QtySold.ToString("0.###"), i.Revenue.ToString("0.00"),
                i.Cogs.ToString("0.00"), i.Profit.ToString("0.00")
            }));
        WriteCsv("cargokhata-daily.csv",
            ["Date", "Bills", "Sales", "Cash in", "Credit given"],
            days.Select(d => new[]
            {
                d.Date.ToString("yyyy-MM-dd"), d.Bills.ToString(), d.Sales.ToString("0.00"),
                d.CashIn.ToString("0.00"), d.Credit.ToString("0.00")
            }));
    }

    private static string Csv(string? value)
    {
        var s = value ?? "";
        if (s.Contains(',') || s.Contains('"') || s.Contains('\n'))
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        return s;
    }
}
