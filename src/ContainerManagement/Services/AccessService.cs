using ContainerManagement.Data;

namespace ContainerManagement.Services;

public enum ShopRole
{
    Owner,
    Staff
}

public class AccessService
{
    public ShopSettings Settings { get; private set; } = ShopSettings.Load();
    public ShopRole Role { get; private set; } = ShopRole.Owner;
    public bool Unlocked { get; private set; }

    public bool PinRequired => Settings.PinRequired;
    public bool IsOwner => !PinRequired || (Unlocked && Role == ShopRole.Owner);
    public bool IsStaff => PinRequired && Unlocked && Role == ShopRole.Staff;

    public void Reload() => Settings = ShopSettings.Load();

    public bool TryUnlock(string pin)
    {
        Reload();
        var hash = ShopSettings.HashPin(pin);
        if (!string.IsNullOrWhiteSpace(Settings.OwnerPinHash) &&
            string.Equals(hash, Settings.OwnerPinHash, StringComparison.OrdinalIgnoreCase))
        {
            Role = ShopRole.Owner;
            Unlocked = true;
            return true;
        }

        if (!string.IsNullOrWhiteSpace(Settings.StaffPinHash) &&
            string.Equals(hash, Settings.StaffPinHash, StringComparison.OrdinalIgnoreCase))
        {
            Role = ShopRole.Staff;
            Unlocked = true;
            return true;
        }

        return false;
    }

    public void UnlockOpen()
    {
        Reload();
        if (!PinRequired)
        {
            Role = ShopRole.Owner;
            Unlocked = true;
        }
    }

    public void RequireOwner(string action)
    {
        if (!IsOwner)
            throw new InvalidOperationException("Owner PIN needed to " + action + ".");
    }
}
