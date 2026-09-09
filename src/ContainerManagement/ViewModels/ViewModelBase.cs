using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ContainerManagement.ViewModels;

public abstract class ViewModelBase : ObservableObject
{
    public bool HasLoaded { get; set; }

    /// <summary>
    /// The maker's mark, for any page that carries a signature. Two properties on the base rather than a
    /// converter, because a plate has exactly one question to ask: is there artwork, or do we typeset the
    /// wordmark. Null picture, no broken-image box.
    /// </summary>
    public Bitmap? BrandArt => Data.Brand.Artwork;

    public Bitmap? BrandArtOnDark => Data.Brand.ArtworkOnDark;

    public bool HasBrandArt => BrandArt is not null;

    /// <summary>
    /// When false, leaving and coming back keeps typed fields as they were.
    /// List pages stay true so numbers can refresh without wiping filters.
    /// </summary>
    public virtual bool ReloadOnShow => true;

    /// <summary>
    /// When true the page fills the window and scrolls inside tables, so a pinned panel stays in view.
    /// </summary>
    public virtual bool FillsPage => false;

    public virtual Task LoadAsync() => Task.CompletedTask;
}

public interface IAppShell
{
    bool IsOwner { get; }
    void Notify(string message, bool error = false);
    void Back();
    void GoDashboard();
    void GoContainers();
    void OpenContainer(int id);
    void GoBuyPlans();
    void OpenBuyPlan(int id);

    /// <summary>
    /// Called by a page that changed saved data, so the page behind it reloads on Back
    /// instead of showing the numbers from before the edit.
    /// </summary>
    void MarkChanged();
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
