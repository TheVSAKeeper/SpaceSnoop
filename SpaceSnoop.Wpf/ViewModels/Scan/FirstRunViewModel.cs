using MahApps.Metro.IconPacks;
using System.ComponentModel;

namespace SpaceSnoop.Wpf.ViewModels.Scan;

public sealed partial class FirstRunViewModel : ObservableObject
{
    private readonly ISettingsStore _settings;
    private readonly ScanPreferences _scan;
    private readonly ThemeViewModel _theme;
    private readonly UpdatePreferences _updates;
    private readonly Func<Task<bool>> _restart;

    [ObservableProperty]
    private bool _isVisible;

    public FirstRunViewModel(
        ISettingsStore settings,
        ScanPreferences scan,
        ThemeViewModel theme,
        UpdatePreferences updates,
        bool isElevated,
        Func<Task<bool>> restart)
    {
        _settings = settings;
        _scan = scan;
        _theme = theme;
        _updates = updates;
        _restart = restart;

        IsElevated = isElevated;
        IsVisible = settings.GetBool(SettingsKeys.WelcomePending, AppDefaults.WelcomePendingDefault);
        ThemeSegments = SettingsOptions.Themes.Select(static option => new SegmentOption(IconFor(option.Value), option.Label, $"Тема «{option.Label}»")).ToList();

        scan.PropertyChanged += OnScanChanged;
        theme.PropertyChanged += OnThemeChanged;
        updates.PropertyChanged += OnUpdatesChanged;
    }

    public bool IsElevated { get; private set; }

    public bool NeedsElevation => !IsElevated;

    public string FastScanHint => IsElevated
        ? "Целый диск читается по таблице файлов, а не папка за папкой: в несколько раз быстрее, и закрытые системные папки тоже попадают в итог. Отдельную папку программа обходит как обычно."
        : "Целый диск читается по таблице файлов, а не папка за папкой: в несколько раз быстрее, и закрытые системные папки тоже попадают в итог. Нужны права администратора – программа перезапустится, Windows спросит разрешение.";

    public bool FastScan
    {
        get => _scan.MftEnabled;
        set => _scan.MftEnabled = value;
    }

    public IReadOnlyList<SegmentOption> ThemeSegments { get; }

    public int SelectedThemeIndex
    {
        get => IndexOf(_theme.Current);
        set
        {
            if (value >= 0 && value < SettingsOptions.Themes.Count && SettingsOptions.Themes[value].Value != _theme.Current)
            {
                _theme.ApplyCommand.Execute(SettingsOptions.Themes[value].Value);
            }
        }
    }

    public bool CheckUpdates
    {
        get => _updates.CheckOnStartup;
        set => _updates.CheckOnStartup = value;
    }

    internal void ShowForAutomation(bool isElevated)
    {
        IsElevated = isElevated;
        IsVisible = true;
        OnPropertyChanged(nameof(IsElevated));
        OnPropertyChanged(nameof(NeedsElevation));
        OnPropertyChanged(nameof(FastScanHint));
    }

    [RelayCommand]
    private void Dismiss()
    {
        Complete();
    }

    [RelayCommand]
    private async Task RestartAsAdminAsync()
    {
        var fastScan = _scan.MftEnabled;
        _scan.MftEnabled = true;
        Complete();
        _settings.Flush();

        if (await _restart())
        {
            return;
        }

        _scan.MftEnabled = fastScan;
        IsVisible = true;
        _settings.SetBool(SettingsKeys.WelcomePending, true);
    }

    private void Complete()
    {
        IsVisible = false;
        _settings.SetBool(SettingsKeys.WelcomePending, false);
    }

    private static int IndexOf(AppTheme theme)
    {
        for (var i = 0; i < SettingsOptions.Themes.Count; i++)
        {
            if (SettingsOptions.Themes[i].Value == theme)
            {
                return i;
            }
        }

        return -1;
    }

    private static PackIconLucideKind IconFor(AppTheme theme)
    {
        return theme switch
        {
            AppTheme.Light => PackIconLucideKind.Sun,
            AppTheme.Dark => PackIconLucideKind.Moon,
            AppTheme.Tarkov => PackIconLucideKind.Target,
            AppTheme.System => PackIconLucideKind.Monitor,
            _ => PackIconLucideKind.Palette,
        };
    }

    private void OnScanChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ScanPreferences.MftEnabled))
        {
            OnPropertyChanged(nameof(FastScan));
        }
    }

    private void OnThemeChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ThemeViewModel.Current))
        {
            OnPropertyChanged(nameof(SelectedThemeIndex));
        }
    }

    private void OnUpdatesChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(UpdatePreferences.CheckOnStartup))
        {
            OnPropertyChanged(nameof(CheckUpdates));
        }
    }
}
