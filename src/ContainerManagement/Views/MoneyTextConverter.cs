using System.Globalization;
using Avalonia.Data.Converters;
using ContainerManagement.Models;

namespace ContainerManagement.Views;

/// <summary>
/// Prints a stored amount in the voice the rest of the app uses - "Rs 1,000.5", paisa shown when there are
/// any, rupees grouped as every other figure on the page groups them. A grid that formats its own money
/// column with {0:N0} rounds the paisa away for the eye while the book keeps it, so the column no longer
/// adds up to the figure printed beside it; on a list of receipts, where the whole point is checking the
/// total against the lines under it, that is a wrong answer rather than a tidy one.
/// </summary>
public sealed class MoneyTextConverter : IValueConverter
{
    public static readonly MoneyTextConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is decimal amount ? Money.Pkr(amount) : "";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
