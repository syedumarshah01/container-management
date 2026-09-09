using System.Globalization;
using Avalonia.Data.Converters;
using ContainerManagement.Models;

namespace ContainerManagement.Views;

/// <summary>
/// Reads an amount box back in the units a shop counts in - "15 lac 73 thousand 250" - so an extra
/// zero shows itself before the figure is saved. It binds to the same property the box binds to, so
/// it moves while typing. Small figures and empty boxes come back blank: three digits are easy to
/// see, a supplier bill is not.
/// </summary>
public sealed class AmountWordsConverter : IValueConverter
{
    public static readonly AmountWordsConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is decimal amount ? Money.Words(amount) : "";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
