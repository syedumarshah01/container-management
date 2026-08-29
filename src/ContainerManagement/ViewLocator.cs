using Avalonia.Controls;
using Avalonia.Controls.Templates;
using ContainerManagement.ViewModels;

namespace ContainerManagement;

public sealed class ViewLocator : IDataTemplate
{
    public bool Match(object? data) => data is ViewModelBase;

    public Control? Build(object? data)
    {
        if (data is null)
            return null;

        var name = data.GetType().FullName;
        if (string.IsNullOrEmpty(name))
            return new TextBlock { Text = "Missing page" };

        var viewName = name
            .Replace(".ViewModels.", ".Views.", StringComparison.Ordinal)
            .Replace("ViewModel", "View", StringComparison.Ordinal);

        var type = typeof(App).Assembly.GetType(viewName);
        if (type is null)
            return new TextBlock { Text = "Missing page: " + viewName };

        return (Control)Activator.CreateInstance(type)!;
    }
}
