using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ContainerManagement.Data;

public class ShopSettings
{
    public string CompanyName { get; set; } = "CargoKhata";
    public string Phone { get; set; } = "";
    public string Address { get; set; } = "";
    public string OwnerPinHash { get; set; } = "";
    public string StaffPinHash { get; set; } = "";
    public decimal LowStockQty { get; set; } = 10;
    public int DefaultDueDays { get; set; } = 30;
    public bool DemoWiped { get; set; }

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public static string FilePath => Path.Combine(DbPaths.DirectoryPath, "shop.json");

    public static ShopSettings Load()
    {
        try
        {
            if (!File.Exists(FilePath))
                return new ShopSettings();
            return JsonSerializer.Deserialize<ShopSettings>(File.ReadAllText(FilePath), JsonOpts)
                   ?? new ShopSettings();
        }
        catch
        {
            return new ShopSettings();
        }
    }

    public void Save() => File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOpts));

    public bool PinRequired =>
        !string.IsNullOrWhiteSpace(OwnerPinHash) || !string.IsNullOrWhiteSpace(StaffPinHash);

    public static string HashPin(string pin)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes("cargokhata|" + (pin ?? "").Trim()));
        return Convert.ToHexString(bytes);
    }
}
