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
    public bool RemotelyPaused { get; set; }
    public string PauseMessage { get; set; } = "";
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
    public string Key => _file?.Key ?? "";

    public LicenseService()
    {
        Load();
        ApplyStoredPause();
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

    public static string IssueKey(string businessName)
    {
        if (string.IsNullOrWhiteSpace(businessName))
            throw new InvalidOperationException("Business name is required.");

        var id = "CK-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(3));
        var payload = JsonSerializer.Serialize(new LicensePayload
        {
            Id = id,
            Name = businessName.Trim()
        }, Compact);
        return "CK1." + B64(Encoding.UTF8.GetBytes(payload)) + "." + Sign(payload);
    }

    public bool TryActivate(string key, out string error)
    {
        error = "";
        if (!TryParse(key, out var payload, out error))
            return false;

        _file = new ShopLicenseFile
        {
            CustomerId = payload.Id,
            BusinessName = payload.Name,
            Key = key.Trim(),
            IssuedUtc = DateTime.UtcNow
        };
        Save();
        ApplyShopName();
        IsPaused = false;
        LockReason = "";
        return true;
    }

    public bool TryIssueAndActivate(string vendorPin, string businessName, out string key, out string error)
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
            key = IssueKey(businessName);
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

        try
        {
            var json = await _http.GetStringAsync(LicenseSecrets.StatusUrl);
            using var doc = JsonDocument.Parse(json);
            var paused = false;
            var message = "";
            if (doc.RootElement.TryGetProperty("shops", out var shops) &&
                shops.TryGetProperty(_file.CustomerId, out var shop))
            {
                var active = !shop.TryGetProperty("active", out var a) || a.GetBoolean();
                if (!active)
                {
                    paused = true;
                    message = shop.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String
                        ? m.GetString() ?? "This copy is paused. Contact CargoKhata."
                        : "This copy is paused. Contact CargoKhata.";
                }
            }

            _file.RemotelyPaused = paused;
            _file.PauseMessage = message;
            _file.LastOnlineUtc = DateTime.UtcNow;
            Save();
            ApplyStoredPause();
        }
        catch
        {
            ApplyStoredPause();
        }
    }

    private void ApplyStoredPause()
    {
        if (_file is { RemotelyPaused: true })
        {
            IsPaused = true;
            LockReason = string.IsNullOrWhiteSpace(_file.PauseMessage)
                ? "This copy is paused. Contact CargoKhata."
                : _file.PauseMessage;
            return;
        }
        IsPaused = false;
        LockReason = "";
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
        public string? Exp { get; set; }
    }
}
