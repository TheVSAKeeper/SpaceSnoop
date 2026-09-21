namespace SpaceSnoop.Wpf.Views.Settings;

public static class SettingsBlock
{
    public static readonly DependencyProperty KeyProperty = DependencyProperty.RegisterAttached(
        "Key",
        typeof(string),
        typeof(SettingsBlock),
        new PropertyMetadata(null));

    public static void SetKey(DependencyObject element, string value)
    {
        element.SetValue(KeyProperty, value);
    }

    public static string? GetKey(DependencyObject element)
    {
        return (string?)element.GetValue(KeyProperty);
    }
}
