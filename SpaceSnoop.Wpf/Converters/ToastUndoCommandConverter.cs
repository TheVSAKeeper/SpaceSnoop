using System.Globalization;
using System.Windows.Data;

namespace SpaceSnoop.Wpf.Converters;

public sealed class ToastUndoCommandConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return ToastNotifier.GetUndoCommand(value as ToastViewModel);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
