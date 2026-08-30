namespace ContainerManagement.Data;

/// <summary>
/// Change these before you ship a copy to a customer.
/// VendorPin is what you type on first install to stamp the shop name.
/// StatusUrl is a JSON file you edit to pause a shop that has not paid.
/// </summary>
internal static class LicenseSecrets
{
    public const string HmacKey = "cargokhata-license-v1-change-me-umar";
    public const string VendorPin = "ck-seller-2026";
    public const string StatusUrl =
        "https://raw.githubusercontent.com/syedumarshah01/container-management/main/license-status.json";
}
