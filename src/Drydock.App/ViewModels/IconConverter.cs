using System.Globalization;
using System.Windows.Data;

namespace Drydock.App.ViewModels;

/// <summary>Turns an icon path into a picture for an Image, or nothing.</summary>
public sealed class IconConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => IconCache.Load(value as string);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
