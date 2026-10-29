using System.Collections.Concurrent;
using System.Globalization;
using System.Windows.Data;

namespace SpaceSnoop.Wpf.Converters;

public sealed class ThemeSwatchBrushConverter : IValueConverter
{
    private static readonly ConcurrentDictionary<Uri, ResourceDictionary> Palettes = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not AppTheme theme || parameter is not string key)
        {
            return DependencyProperty.UnsetValue;
        }

        return PaletteOf(theme)?[key] ?? DependencyProperty.UnsetValue;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }

    internal static ResourceDictionary? PaletteOf(AppTheme theme)
    {
        if (ThemeManager.Find(AppThemes.ToKey(theme)) is not { } definition)
        {
            return null;
        }

        return Palettes.GetOrAdd(
            definition.Palette,
            static source => new()
            {
                Source = source,
            });
    }
}
