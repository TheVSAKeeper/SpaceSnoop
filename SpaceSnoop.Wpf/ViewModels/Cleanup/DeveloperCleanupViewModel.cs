using KeepShell.Services;
using MahApps.Metro.IconPacks;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;

namespace SpaceSnoop.Wpf.ViewModels.Cleanup;

public sealed partial class DeveloperCleanupViewModel : ObservableObject, IPageHeader, IPageRefresh, IPageStatus
{
    private readonly NuGetCleanupService _service;
    private readonly IDialogService _dialogs;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly ILogger<DeveloperCleanupViewModel> _logger;
    private NuGetCleanupPlan? _plan;
    private CancellationTokenSource? _cancellation;
    private bool _loaded;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    [NotifyPropertyChangedFor(nameof(CanClean))]
    [NotifyPropertyChangedFor(nameof(CancelCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    [NotifyCanExecuteChangedFor(nameof(CleanCommand))]
    [NotifyCanExecuteChangedFor(nameof(SelectRecommendedCommand))]
    [NotifyCanExecuteChangedFor(nameof(ClearSelectionCommand))]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusCaption))]
    private string? _statusText;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasResult))]
    private string? _resultText;

    public DeveloperCleanupViewModel(NuGetCleanupService service, IDialogService dialogs, IUiDispatcher uiDispatcher,
        ILogger<DeveloperCleanupViewModel> logger)
    {
        _service = service;
        _dialogs = dialogs;
        _uiDispatcher = uiDispatcher;
        _logger = logger;
    }

    public ObservableCollection<NuGetCacheViewModel> Caches { get; } = [];
    public string PageTitle => "Разработка";
    public string PageDescription => "Кэши .NET текущего пользователя. SDK, Runtime, инструменты и проекты сохраняются.";
    public string? RefreshTooltip => "Определить пути NuGet и измерить кэши заново";
    public string? StatusCaption => StatusText;
    public bool IsIdle => !IsBusy;
    public bool IsIndeterminate => true;
    public double ProgressValue => 0;
    public double ProgressMax => 1;
    public ICommand? CancelCommand => IsBusy ? CancelOperationCommand : null;
    ICommand IPageRefresh.RefreshCommand => RefreshCommand;
    public bool HasResult => !string.IsNullOrEmpty(ResultText);
    public bool HasCaches => Caches.Count > 0;
    public string TotalText => SizeFormatter.Format(Caches.Sum(x => x.Bytes));
    public string SelectedText => SizeFormatter.Format(Caches.Where(x => x.IsSelected && x.CanSelect).Sum(x => x.Bytes));
    public string SelectionText => $"Категорий: {Caches.Count(x => x.IsSelected && x.CanSelect)} из {Caches.Count}";
    public bool HasSelectedBytes => Caches.Any(x => x.IsSelected && x.CanSelect && x.Bytes > 0);
    public bool CanClean => !IsBusy && _plan is not null && Caches.Any(x => x.IsSelected && x.CanSelect);

    public Task EnsureLoadedAsync()
    {
        if (_loaded || IsBusy)
        {
            return Task.CompletedTask;
        }

        _loaded = true;
        return RefreshAsync();
    }

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private async Task RefreshAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        _cancellation = new();
        ResultText = null;
        try
        {
            await ScanAsync(_cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            StatusText = "Замер отменён. Для очистки повторите сканирование.";
        }
        catch (Exception exception)
        {
            _logger.DeveloperCleanupFailed(exception.Unwrap());
            StatusText = exception.Unwrap().Message;
        }
        finally
        {
            _cancellation.Dispose();
            _cancellation = null;
            IsBusy = false;
        }
    }

    private async Task ScanAsync(CancellationToken token)
    {
        _plan = null;
        ClearRows();
        StatusText = "Определяю пути и измеряю NuGet…";
        var plan = await _service.ScanAsync(token);
        _uiDispatcher.Invoke(() =>
        {
            _plan = plan;
            foreach (var scan in plan.Caches)
            {
                var row = new NuGetCacheViewModel(scan);
                row.PropertyChanged += OnSelectionChanged;
                Caches.Add(row);
            }

            StatusText = plan.Caches.Any(x => x.Errors.Count > 0)
                ? "Замер завершён. Категории с ошибками недоступны для очистки."
                : "Замер завершён. Выберите кэши для очистки.";
            NotifySelection();
        });
    }

    [RelayCommand(CanExecute = nameof(CanClean))]
    private async Task CleanAsync()
    {
        if (!CanClean || _plan is null)
        {
            return;
        }

        var plan = _plan;
        var selected = Caches.Where(x => x.IsSelected && x.CanSelect).ToArray();
        var lines = new List<ConfirmLine>
        {
            new ConfirmTextLine("Перед очисткой остановите restore, сборки и операции NuGet в IDE."),
            new ConfirmTextLine("Будут удалены только файлы из показанного замера. Новые файлы останутся."),
        };
        foreach (var row in selected)
        {
            lines.Add(new ConfirmMetricLine(row.Name, row.FilesText, row.SizeText));
            lines.Add(new ConfirmTextLine(row.Path));
            if (row.Kind == NuGetCacheKind.GlobalPackages)
            {
                lines.Add(new ConfirmTextLine(row.Description, ConfirmTextTone.Strong));
            }
        }

        var confirmation = new ConfirmDialogViewModel("Очистка NuGet", PackIconLucideKind.Trash2, lines,
            [new("Отмена", ConfirmChoiceKind.Dismissive), new("Удалить безвозвратно", ConfirmChoiceKind.Destructive)])
        {
            Warning = "Файлы удаляются безвозвратно, мимо корзины. Восстановить их можно только повторной загрузкой из доступного источника.",
        };

        IsBusy = true;
        try
        {
            if (!await _dialogs.ShowAsync(confirmation))
            {
                return;
            }

            _cancellation = new();
            _plan = null;
            StatusText = "Удаляю выбранные файлы NuGet…";
            var result = await _service.CleanAsync(plan, selected.Select(x => x.Kind).ToArray(), null, _cancellation.Token);
            ResultText = $"{(result.Cancelled ? "Отменено" : "Завершено")}. Удалено файлов: {result.Deleted:N0}, данных: {SizeFormatter.Format(result.FreedBytes)}. Пропущено: {result.Skipped:N0}."
                + (result.Errors.Count == 0 ? string.Empty : Environment.NewLine + string.Join(Environment.NewLine, result.Errors));
            _logger.DeveloperCleanupFinished(result.Deleted, result.FreedBytes, result.Skipped, result.Cancelled);
            _cancellation.Dispose();
            _cancellation = new();
            try
            {
                await ScanAsync(_cancellation.Token);
            }
            catch (OperationCanceledException)
            {
                StatusText = "Повторный замер отменён. Результат удаления сохранён.";
            }
            catch (Exception exception)
            {
                _logger.DeveloperCleanupFailed(exception.Unwrap());
                StatusText = $"Повторный замер не выполнен: {exception.Unwrap().Message}";
            }
        }
        catch (Exception exception)
        {
            _plan = null;
            _logger.DeveloperCleanupFailed(exception.Unwrap());
            StatusText = exception.Unwrap().Message;
        }
        finally
        {
            _cancellation?.Dispose();
            _cancellation = null;
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void CancelOperation() => _cancellation?.Cancel();

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private void SelectRecommended()
    {
        if (IsBusy)
        {
            return;
        }

        foreach (var row in Caches)
        {
            row.IsSelected = row.CanSelect && row.IsRecommended;
        }
    }

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private void ClearSelection()
    {
        if (IsBusy)
        {
            return;
        }

        foreach (var row in Caches)
        {
            row.IsSelected = false;
        }
    }

    private void ClearRows()
    {
        _uiDispatcher.Invoke(() =>
        {
            foreach (var row in Caches)
            {
                row.PropertyChanged -= OnSelectionChanged;
            }

            Caches.Clear();
            NotifySelection();
        });
    }

    internal void ShowForAutomation()
    {
        _uiDispatcher.Invoke(() =>
        {
            _loaded = true;
            _plan = null;
            ClearRows();
            var rows = new[]
            {
            new NuGetCacheViewModel(NuGetCacheKind.HttpCache, @"C:\Users\Developer\AppData\Local\NuGet\v3-cache", 320_000_000, 1_240, true, string.Empty),
            new NuGetCacheViewModel(NuGetCacheKind.Temp, @"C:\Users\Developer\AppData\Local\Temp\NuGetScratch", 48_000_000, 36, true, string.Empty),
            new NuGetCacheViewModel(NuGetCacheKind.PluginsCache, @"C:\Users\Developer\AppData\Local\NuGet\plugins-cache", 12_000_000, 90, true, string.Empty),
            new NuGetCacheViewModel(NuGetCacheKind.GlobalPackages, @"C:\Users\Developer\.nuget\packages", 4_020_000_000, 18_540, true, string.Empty),
        };
            foreach (var row in rows)
            {
                row.PropertyChanged += OnSelectionChanged;
                Caches.Add(row);
            }

            StatusText = "Пример результатов замера. Очистка в демонстрации недоступна.";
            NotifySelection();
        });
    }

    private void OnSelectionChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(NuGetCacheViewModel.IsSelected))
        {
            NotifySelection();
        }
    }

    private void NotifySelection()
    {
        OnPropertyChanged(nameof(HasCaches));
        OnPropertyChanged(nameof(TotalText));
        OnPropertyChanged(nameof(SelectedText));
        OnPropertyChanged(nameof(SelectionText));
        OnPropertyChanged(nameof(HasSelectedBytes));
        OnPropertyChanged(nameof(CanClean));
        CleanCommand.NotifyCanExecuteChanged();
    }
}
