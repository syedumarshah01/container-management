using System.Net.Http.Headers;
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
    private readonly HttpClient _http;
    private ShopLicenseFile? _file;

    public static string FilePath => Path.Combine(DbPaths.DirectoryPath, "license.json");

    public bool IsActivated => _file is not null && !string.IsNullOrWhiteSpace(_file.Key);
    public bool IsPaused { get; private set; }
    public string LockReason { get; private set; } = "";
    public string BusinessName => string.IsNullOrWhiteSpace(_file?.BusinessName) ? AppInfo.ProductName : _file!.BusinessName;
    public string CustomerId => _file?.CustomerId ?? "";
    public string Key => _file?.Key ?? "";
    public DateTime? LastOnlineUtc => _file?.LastOnlineUtc;

    public LicenseService()
    {
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("ProBooks/1.0");
        _http.DefaultRequestHeaders.CacheControl = new CacheControlHeaderValue { NoCache = true, NoStore = true };
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

        var previous = _file;
        var keepPause = false;
        var keepMessage = "";
        if (previous is not null &&
            string.Equals(previous.CustomerId, payload.Id, StringComparison.OrdinalIgnoreCase))
        {
            keepPause = previous.RemotelyPaused;
            keepMessage = previous.PauseMessage;
        }

        _file = new ShopLicenseFile
        {
            CustomerId = payload.Id,
            BusinessName = payload.Name,
            Key = key.Trim(),
            IssuedUtc = DateTime.UtcNow,
            RemotelyPaused = keepPause,
            PauseMessage = keepMessage
        };
        Save();
        ApplyShopName();
        ApplyStoredPause();
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
            var json = await DownloadStatusJsonAsync();
            if (json is null)
            {
                Console.WriteLine("License check: could not reach GitHub, keeping last state.");
                ApplyStoredPause();
                return;
            }

            using var doc = JsonDocument.Parse(json);
            var paused = false;
            var message = "";
            if (doc.RootElement.TryGetProperty("shops", out var shops) &&
                TryGetShop(shops, _file.CustomerId, out var shop))
            {
                var active = !shop.TryGetProperty("active", out var a) || a.ValueKind != JsonValueKind.False;
                if (!active)
                {
                    paused = true;
                    message = shop.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String
                        ? m.GetString() ?? "This copy is paused. Contact ProBooks."
                        : "This copy is paused. Contact ProBooks.";
                }
            }

            _file.RemotelyPaused = paused;
            _file.PauseMessage = message;
            _file.LastOnlineUtc = DateTime.UtcNow;
            Save();
            ApplyStoredPause();
            Console.WriteLine(paused
                ? "License check: paused (" + _file.CustomerId + ")"
                : "License check: active (" + _file.CustomerId + ")");
        }
        catch (Exception ex)
        {
            Console.WriteLine("License check failed: " + ex.Message);
            ApplyStoredPause();
        }
    }

    private async Task<string?> DownloadStatusJsonAsync()
    {
        foreach (var baseUrl in LicenseSecrets.StatusUrls)
        {
            try
            {
                var sep = baseUrl.Contains('?', StringComparison.Ordinal) ? "&" : "?";
                using var req = new HttpRequestMessage(HttpMethod.Get, baseUrl + sep + "t=" + DateTime.UtcNow.Ticks);
                req.Headers.TryAddWithoutValidation("User-Agent", "ProBooks/1.0");
                req.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true, NoStore = true };
                req.Headers.Pragma.ParseAdd("no-cache");
                if (baseUrl.Contains("api.github.com", StringComparison.OrdinalIgnoreCase))
                    req.Headers.Accept.ParseAdd("application/vnd.github.raw");

                using var resp = await _http.SendAsync(req);
                if (!resp.IsSuccessStatusCode)
                {
                    Console.WriteLine("License check HTTP " + (int)resp.StatusCode + " from " + ShortUrl(baseUrl));
                    continue;
                }

                var body = await resp.Content.ReadAsStringAsync();
                var json = NormalizeStatusBody(body);
                if (json is not null)
                    return json;
            }
            catch (Exception ex)
            {
                Console.WriteLine("License check skip " + ShortUrl(baseUrl) + ": " + ex.Message);
            }
        }

        return null;
    }

    private static string? NormalizeStatusBody(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return null;
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.TryGetProperty("shops", out _))
                return body;
            if (root.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.String)
            {
                var b64 = (c.GetString() ?? "").Replace("\n", "", StringComparison.Ordinal);
                var inner = Encoding.UTF8.GetString(Convert.FromBase64String(b64));
                using var innerDoc = JsonDocument.Parse(inner);
                if (innerDoc.RootElement.TryGetProperty("shops", out _))
                    return inner;
            }
        }
        catch
        {
            /* not the status file */
        }
        return null;
    }

    private static bool TryGetShop(JsonElement shops, string id, out JsonElement shop)
    {
        shop = default;
        if (shops.ValueKind != JsonValueKind.Object || string.IsNullOrWhiteSpace(id))
            return false;
        foreach (var p in shops.EnumerateObject())
        {
            if (string.Equals(p.Name, id, StringComparison.OrdinalIgnoreCase))
            {
                shop = p.Value;
                return true;
            }
        }
        return false;
    }

    private static string ShortUrl(string url)
    {
        try { return new Uri(url).Host + new Uri(url).AbsolutePath; }
        catch { return url; }
    }

    private void ApplyStoredPause()
    {
        if (_file is { RemotelyPaused: true })
        {
            IsPaused = true;
            LockReason = string.IsNullOrWhiteSpace(_file.PauseMessage)
                ? "This copy is paused. Contact ProBooks."
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
            error = "That is not a ProBooks license key.";
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
