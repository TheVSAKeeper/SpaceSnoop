using MahApps.Metro.IconPacks;
using System.ComponentModel;
using System.Windows.Input;

namespace SpaceSnoop.Wpf.ViewModels.Cleanup;

public sealed partial class CleanupPageViewModel : ObservableObject, IPageHeader, IPageRefresh, IPageStatus
{
    [ObservableProperty]
    private int _selectedSectionIndex;

    public CleanupViewModel Windows { get; }

    public DockerViewModel Docker { get; }

    public DeveloperCleanupViewModel Development { get; }

    public IReadOnlyList<SegmentOption> Sections { get; } =
    [
        new(PackIconLucideKind.Trash2, "Windows", "Временные файлы, кэши и корзина"),
        new(PackIconLucideKind.Container, "Docker", "Образы, контейнеры, тома и кэш сборки"),
        new(PackIconLucideKind.Wrench, "Разработка", "Кэши NuGet и артефакты проектов .NET"),
    ];

    public bool IsDockerActive
    {
        get => SelectedSectionIndex == 1;
        set
        {
            if (value || IsDockerActive)
            {
                SelectedSectionIndex = value ? 1 : 0;
            }
        }
    }

    public bool IsWindowsActive => SelectedSectionIndex == 0;

    public bool IsDevelopmentActive => SelectedSectionIndex == 2;

    public bool CanSwitchSection => !Windows.IsBusy && !Docker.IsBusy && !Development.IsBusy;

    public string PageTitle => "Очистка";

    public string PageDescription => ActiveHeader.PageDescription;

    public string? RefreshTooltip => ActiveRefresh.RefreshTooltip;

    public string? StatusCaption => ActiveStatus.StatusCaption;

    public bool IsBusy => ActiveStatus.IsBusy;

    public bool IsIndeterminate => ActiveStatus.IsIndeterminate;

    public double ProgressValue => ActiveStatus.ProgressValue;

    public double ProgressMax => ActiveStatus.ProgressMax;

    public ICommand? CancelCommand => ActiveStatus.CancelCommand;

    private IPageHeader ActiveHeader => SelectedSectionIndex switch { 1 => Docker, 2 => Development, _ => Windows };

    private IPageStatus ActiveStatus => SelectedSectionIndex switch { 1 => Docker, 2 => Development, _ => Windows };

    private IPageRefresh ActiveRefresh => SelectedSectionIndex switch { 1 => Docker, 2 => Development, _ => Windows };

    ICommand IPageRefresh.RefreshCommand => RefreshCommand;

    public CleanupPageViewModel(CleanupViewModel windows, DockerViewModel docker, DeveloperCleanupViewModel development)
    {
        Windows = windows;
        Docker = docker;
        Development = development;

        Windows.PropertyChanged += OnSectionChanged;
        Docker.PropertyChanged += OnSectionChanged;
        Development.PropertyChanged += OnSectionChanged;
    }

    public async Task EnsureLoadedAsync()
    {
        if (IsDevelopmentActive)
        {
            await Development.EnsureLoadedAsync();
            return;
        }

        if (IsDockerActive)
        {
            await Docker.EnsureLoadedAsync();
            return;
        }

        await Windows.EnsureLoadedAsync();
    }

    public void ActivateDocker()
    {
        IsDockerActive = true;
    }

    async partial void OnSelectedSectionIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsWindowsActive));
        OnPropertyChanged(nameof(IsDockerActive));
        OnPropertyChanged(nameof(IsDevelopmentActive));
        NotifyProxied();

        await EnsureLoadedAsync();
    }

    private void OnSectionChanged(object? sender, PropertyChangedEventArgs args)
    {
        OnPropertyChanged(nameof(CanSwitchSection));
        var fromActive = ReferenceEquals(sender, ActiveStatus);

        if (fromActive)
        {
            OnPropertyChanged(args.PropertyName);
            if (args.PropertyName == nameof(IsBusy))
            {
                RefreshCommand.NotifyCanExecuteChanged();
            }
        }
    }

    private void NotifyProxied()
    {
        OnPropertyChanged(nameof(PageDescription));
        OnPropertyChanged(nameof(RefreshTooltip));
        OnPropertyChanged(nameof(StatusCaption));
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(IsIndeterminate));
        OnPropertyChanged(nameof(ProgressValue));
        OnPropertyChanged(nameof(ProgressMax));
        OnPropertyChanged(nameof(CancelCommand));
        RefreshCommand.NotifyCanExecuteChanged();
    }

    private bool CanRefresh()
    {
        return !IsBusy;
    }

    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private Task RefreshAsync()
    {
        return ((IAsyncRelayCommand)ActiveRefresh.RefreshCommand).ExecuteAsync(null);
    }
}
