namespace SpaceSnoop.Wpf.ViewModels.Scan;

public sealed partial class ScanNodeViewModel
{
    [ObservableProperty]
    private bool _isIgnored;

    public bool CanIgnore => Space is not null && !IsRoot && !IsIgnored;

    public string UnignoreCaption => Space is { } space && _factory?.Visibility.IgnoredAncestor(space) is { } ancestor
        && !ReferenceEquals(ancestor, space)
        ? $"Снять игнорирование папки «{ancestor.Name}»"
        : "Снять игнорирование";

    public string IgnoreHint => IsIgnored ? "Игнорируется: размер не участвует в долях, подсветке и карте" : string.Empty;

    [RelayCommand]
    private void Ignore()
    {
        if (CanIgnore)
        {
            _factory?.RequestIgnore(this);
        }
    }

    [RelayCommand]
    private void Unignore()
    {
        if (IsIgnored)
        {
            _factory?.RequestUnignore(this);
        }
    }
}
