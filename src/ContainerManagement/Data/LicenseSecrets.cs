namespace ContainerManagement.Data;

/// <summary>
/// Change these before you ship a copy to a customer.
/// VendorPin is what you type on first install to stamp the shop name.
/// StatusUrls are tried in order until one returns license-status.json.
/// The running app fetches this itself — the shop does not git pull.
/// </summary>
internal static class LicenseSecrets
{
    public const string HmacKey = "cargokhata-license-v1-change-me-umar";
    public const string VendorPin = "ck-seller-2026";

    public static readonly string[] StatusUrls =
    [
        "https://api.github.com/repos/syedumarshah01/container-management/contents/license-status.json?ref=arena%2F01a04944-container-management",
        "https://raw.githubusercontent.com/syedumarshah01/container-management/refs/heads/arena/01a04944-container-management/license-status.json",
        "https://api.github.com/repos/syedumarshah01/container-management/contents/license-status.json",
        "https://raw.githubusercontent.com/syedumarshah01/container-management/main/license-status.json"
    ];
}
