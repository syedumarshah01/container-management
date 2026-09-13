using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ContainerManagement.Data;

public class ShopSettings
{
    public string CompanyName { get; set; } = AppInfo.ProductName;
    public string Phone { get; set; } = "";
    public string Address { get; set; } = "";
    public string OwnerPinHash { get; set; } = "";
    public string StaffPinHash { get; set; } = "";
    public decimal LowStockQty { get; set; } = 10;
    public int DefaultDueDays { get; set; } = 30;

    /// <summary>
    /// The words the shop wants a customer to read, typed by the shop. Empty means the book writes the
    /// message itself - the customer's own lines and the balance. Nothing is folded into this text and
    /// nothing is added to it at the last moment: what is typed here is what goes, apart from the words in
    /// braces, which the book fills with its own figures.
    /// </summary>
    public string WhatsAppMessage { get; set; } = "";
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
