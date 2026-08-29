using CommunityToolkit.Mvvm.ComponentModel;

namespace ContainerManagement.ViewModels;

public abstract class ViewModelBase : ObservableObject
{
    public virtual Task LoadAsync() => Task.CompletedTask;
}

public interface IAppShell
{
    bool IsOwner { get; }
    void Notify(string message, bool error = false);
    void GoDashboard();
    void GoContainers();
    void OpenContainer(int id);
    void GoInventory();
    void GoNewSale();
    void GoSales();
    void OpenSale(int id);
    void EditSale(int id);
    void GoCustomers();
    void OpenCustomer(int id);
    void GoReceivables();
    void GoProfit();
    void GoBackup();
    void GoCash();
    void GoSettings();
}

public sealed class NavItem
{
    public NavItem(string title, string key)
    {
        Title = title;
        Key = key;
    }

    public string Title { get; }
    public string Key { get; }
}
