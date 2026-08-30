using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ContainerManagement.Data;

namespace ContainerManagement.Services;

public class ShopLicenseFile
{
    public string CustomerId { get; set; } = "";
    public string BusinessName { get; set; } = "";
    public string Key { get; set; } = "";
    public DateTime IssuedUtc { get; set; }
    public DateTime ExpiresUtc { get; set; }
    public DateTime? LastOnlineUtc { get; set; }
}

public class LicenseService
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };
    private static readonly JsonSerializerOptions Compact = new() { WriteIndented = false };
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(8) };
    private ShopLicenseFile? _file;

    public static string FilePath => Path.Combine(DbPaths.DirectoryPath, "license.json");

    public bool IsActivated => _file is not null && !string.IsNullOrWhiteSpace(_file.Key);
    public bool IsPaused { get; private set; }
    public string LockReason { get; private set; } = "";
    public string BusinessName => string.IsNullOrWhiteSpace(_file?.BusinessName) ? "CargoKhata" : _file!.BusinessName;
    public string CustomerId => _file?.CustomerId ?? "";
    public string ExpiryText => _file is null ? "—" : _file.ExpiresUtc.ToLocalTime().ToString("dd MMM yyyy");
    public string Key => _file?.Key ?? "";

    public LicenseService()
    {
        Load();
        ApplyLocalLock();
    }

    public void Load()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                _file = null;
                return;
            }
            _file = JsonSerializer.Deserialize<ShopLicenseFile>(File.ReadAllText(FilePath), JsonOpts);
        }
        catch
        {
            _file = null;
        }
    }

    public static string IssueKey(string businessName, int months)
    {
        if (string.IsNullOrWhiteSpace(businessName))
            throw new InvalidOperationException("Business name is required.");
        if (months < 1)
            throw new InvalidOperationException("Months must be at least 1.");

        var id = "CK-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(3));
        var exp = DateTime.UtcNow.Date.AddMonths(months);
        var payload = JsonSerializer.Serialize(new LicensePayload
        {
            Id = id,
            Name = businessName.Trim(),
            Exp = exp.ToString("yyyy-MM-dd")
        }, Compact);
        return "CK1." + B64(Encoding.UTF8.GetBytes(payload)) + "." + Sign(payload);
    }

    public bool TryActivate(string key, out string error)
    {
        error = "";
        if (!TryParse(key, out var payload, out error))
            return false;
        if (!DateTime.TryParse(payload.Exp, out var exp))
        {
            error = "License has a bad expiry date.";
            return false;
        }
        if (exp.Date < DateTime.UtcNow.Date)
        {
            error = "This license has expired. Ask for a new one.";
            return false;
        }

        _file = new ShopLicenseFile
        {
            CustomerId = payload.Id,
            BusinessName = payload.Name,
            Key = key.Trim(),
            IssuedUtc = DateTime.UtcNow,
            ExpiresUtc = DateTime.SpecifyKind(exp, DateTimeKind.Utc)
        };
        Save();
        ApplyShopName();
        IsPaused = false;
        LockReason = "";
        ApplyLocalLock();
        return !IsPaused;
    }

    public bool TryIssueAndActivate(string vendorPin, string businessName, int months, out string key, out string error)
    {
        key = "";
        error = "";
        if (!string.Equals((vendorPin ?? "").Trim(), LicenseSecrets.VendorPin, StringComparison.Ordinal))
        {
            error = "Seller PIN is wrong.";
            return false;
        }
        try
        {
            key = IssueKey(businessName, months);
            return TryActivate(key, out error);
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public async Task RefreshRemoteAsync()
    {
        if (!IsActivated || _file is null)
            return;

        ApplyLocalLock();
        if (IsPaused)
            return;

        try
        {
            var json = await _http.GetStringAsync(LicenseSecrets.StatusUrl);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("shops", out var shops) &&
                shops.TryGetProperty(_file.CustomerId, out var shop))
            {
                var active = !shop.TryGetProperty("active", out var a) || a.GetBoolean();
                if (!active)
                {
                    IsPaused = true;
                    LockReason = shop.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String
                        ? m.GetString() ?? "This copy is paused. Contact CargoKhata."
                        : "This copy is paused. Contact CargoKhata.";
                    return;
                }
            }
            _file.LastOnlineUtc = DateTime.UtcNow;
            Save();
        }
        catch
        {
            // Stay on local expiry if the status file cannot be reached.
        }
    }

    private void ApplyLocalLock()
    {
        IsPaused = false;
        LockReason = "";
        if (_file is null)
            return;
        if (_file.ExpiresUtc.Date < DateTime.UtcNow.Date)
        {
            IsPaused = true;
            LockReason = "This license expired on " + _file.ExpiresUtc.ToLocalTime().ToString("dd MMM yyyy") +
                         ". Ask CargoKhata for a new key.";
        }
    }

    private void ApplyShopName()
    {
        if (_file is null) return;
        var s = ShopSettings.Load();
        s.CompanyName = _file.BusinessName;
        s.Save();
    }

    private void Save()
    {
        if (_file is null) return;
        File.WriteAllText(FilePath, JsonSerializer.Serialize(_file, JsonOpts));
    }

    private static bool TryParse(string? key, out LicensePayload payload, out string error)
    {
        payload = new LicensePayload();
        error = "";
        var raw = (key ?? "").Trim();
        var parts = raw.Split('.');
        if (parts.Length != 3 || parts[0] != "CK1")
        {
            error = "That is not a CargoKhata license key.";
            return false;
        }
        string json;
        try { json = Encoding.UTF8.GetString(FromB64(parts[1])); }
        catch
        {
            error = "License key is damaged.";
            return false;
        }
        if (!string.Equals(Sign(json), parts[2], StringComparison.Ordinal))
        {
            error = "License key is not valid.";
            return false;
        }
        var parsed = JsonSerializer.Deserialize<LicensePayload>(json, Compact);
        if (parsed is null || string.IsNullOrWhiteSpace(parsed.Name) || string.IsNullOrWhiteSpace(parsed.Id))
        {
            error = "License key is incomplete.";
            return false;
        }
        payload = parsed;
        return true;
    }

    private static string Sign(string payload)
    {
        using var h = new HMACSHA256(Encoding.UTF8.GetBytes(LicenseSecrets.HmacKey));
        var hash = h.ComputeHash(Encoding.UTF8.GetBytes(payload));
        return B64(hash.AsSpan(0, 16).ToArray());
    }

    private static string B64(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] FromB64(string s)
    {
        var pad = s.Replace('-', '+').Replace('_', '/');
        switch (pad.Length % 4)
        {
            case 2: pad += "=="; break;
            case 3: pad += "="; break;
        }
        return Convert.FromBase64String(pad);
    }

    private sealed class LicensePayload
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Exp { get; set; } = "";
    }
}
