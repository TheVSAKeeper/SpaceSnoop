using KeepShell.Services;
using MahApps.Metro.IconPacks;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace SpaceSnoop.Wpf.ViewModels.Cleanup;

public sealed partial class ProjectCleanupViewModel : ObservableObject
{
    private readonly ProjectCleanupService _service;
    private readonly IDialogService _dialogs;
    private readonly IFilePicker _picker;
    private readonly IShellLauncher _shell;
    private readonly IUiDispatcher _dispatcher;
    private readonly ILogger<ProjectCleanupViewModel> _logger;
    private ProjectCleanupPlan? _plan;
    private CancellationTokenSource? _cancellation;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    [NotifyPropertyChangedFor(nameof(CanClean))]
    [NotifyCanExecuteChangedFor(nameof(FindCommand))]
    [NotifyCanExecuteChangedFor(nameof(BrowseCommand))]
    [NotifyCanExecuteChangedFor(nameof(CleanCommand))]
    [NotifyCanExecuteChangedFor(nameof(ClearSelectionCommand))]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    [NotifyPropertyChangedFor(nameof(CanClean))]
    [NotifyCanExecuteChangedFor(nameof(FindCommand))]
    [NotifyCanExecuteChangedFor(nameof(BrowseCommand))]
    [NotifyCanExecuteChangedFor(nameof(CleanCommand))]
    [NotifyCanExecuteChangedFor(nameof(ClearSelectionCommand))]
    private bool _isHostBusy;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(FindCommand))]
    private string _rootPath = string.Empty;

    [ObservableProperty]
    private string? _statusText;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasResult))]
    private string? _resultText;

    public ProjectCleanupViewModel(ProjectCleanupService service, IDialogService dialogs, IFilePicker picker,
        IShellLauncher shell, IUiDispatcher dispatcher, ILogger<ProjectCleanupViewModel> logger)
    {
        _service = service;
        _dialogs = dialogs;
        _picker = picker;
        _shell = shell;
        _dispatcher = dispatcher;
        _logger = logger;
    }

    public ObservableCollection<ProjectArtifactViewModel> Artifacts { get; } = [];
    public bool IsIdle => !IsBusy && !IsHostBusy;
    public bool HasArtifacts => Artifacts.Count > 0;
    public bool HasResult => !string.IsNullOrEmpty(ResultText);
    public bool CanClean => IsIdle && _plan is not null && Artifacts.Any(x => x.IsSelected && x.CanSelect);
    public string TotalText => SizeFormatter.Format(Artifacts.Sum(x => x.Bytes));
    public string SelectedText => SizeFormatter.Format(Artifacts.Where(x => x.IsSelected && x.CanSelect).Sum(x => x.Bytes));
    public string SelectionText => $"Выбрано каталогов: {Artifacts.Count(x => x.IsSelected && x.CanSelect)} из {Artifacts.Count}";

    partial void OnRootPathChanged(string value)
    {
        _plan = null;
        ResultText = null;
        ClearRows();
        StatusText = "Повторите поиск для выбранного каталога.";
    }

    private bool CanFind() => IsIdle && !string.IsNullOrWhiteSpace(RootPath);

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private async Task BrowseAsync()
    {
        if (!IsIdle) { return; }
        var path = _picker.PickFolder("Каталог проектов для поиска bin/obj", RootPath);
        if (path is null) { return; }
        RootPath = path;
        await FindAsync();
    }

    [RelayCommand(CanExecute = nameof(CanFind))]
    private async Task FindAsync()
    {
        if (!CanFind()) { return; }
        IsBusy = true;
        ResultText = null;
        _cancellation = new();
        try { await ScanAsync(_cancellation.Token); }
        catch (OperationCanceledException) { StatusText = "Поиск отменён. Для очистки повторите поиск."; }
        catch (Exception exception)
        {
            _logger.ProjectCleanupFailed(exception.Unwrap());
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
        var root = RootPath;
        StatusText = "Ищу проекты и измеряю артефакты…";
        var plan = await _service.ScanAsync(root, token);
        _dispatcher.Invoke(() =>
        {
            if (!string.Equals(root, RootPath, StringComparison.Ordinal))
            {
                StatusText = "Каталог изменился во время поиска. Повторите поиск.";
                return;
            }
            RootPath = plan.Root;
            _plan = plan;
            foreach (var scan in plan.Artifacts)
            {
                var row = new ProjectArtifactViewModel(scan);
                row.PropertyChanged += OnSelectionChanged;
                Artifacts.Add(row);
            }
            StatusText = plan.Artifacts.Count == 0 ? "В выбранном каталоге проектные артефакты не найдены."
                : "Поиск завершён. Проверьте пути и выберите каталоги. Все категории требуют ручного выбора.";
            if (plan.Errors.Count > 0)
            {
                StatusText += Environment.NewLine + "Часть каталогов не прочитана:" + Environment.NewLine + string.Join(Environment.NewLine, plan.Errors);
            }
            NotifySelection();
        });
    }

    [RelayCommand(CanExecute = nameof(CanClean))]
    private async Task CleanAsync()
    {
        if (!CanClean || _plan is null) { return; }
        var plan = _plan;
        var selected = Artifacts.Where(x => x.IsSelected && x.CanSelect).ToArray();
        var lines = new List<ConfirmLine>
        {
            new ConfirmTextLine("Остановите сборки, тесты и бенчмарки. В bin/obj могут лежать ваши файлы; автоматическое восстановление не гарантируется.", ConfirmTextTone.Strong),
            new ConfirmTextLine("В корзину попадут только просмотренные неизменившиеся файлы. Новые файлы и пустые каталоги останутся."),
            new ConfirmTextLine("Файлы группируются по каталогу в папку SpaceSnoop. Для восстановления верните её из корзины; исходный путь указан в SpaceSnoop-restore.json."),
        };
        foreach (var row in selected)
        {
            lines.Add(new ConfirmMetricLine(row.Name, row.FilesText, row.SizeText));
            lines.Add(new ConfirmTextLine(row.Path));
        }
        var confirmation = new ConfirmDialogViewModel("Очистка проектов", PackIconLucideKind.Trash2, lines,
            [new("Отмена", ConfirmChoiceKind.Dismissive), new("В корзину", ConfirmChoiceKind.Destructive)])
        {
            Warning = "Результаты тестов и бенчмарков могут быть нужными свидетельствами. Восстановление файлов сборки может потребовать restore и доступа к источникам пакетов. Корзина продолжает занимать место на диске.",
        };
        IsBusy = true;
        try
        {
            if (!await _dialogs.ShowAsync(confirmation)) { return; }
            if (!ReferenceEquals(_plan, plan) || !string.Equals(RootPath, plan.Root, StringComparison.Ordinal))
            {
                StatusText = "Каталог или план изменились. Повторите поиск.";
                return;
            }
            _plan = null;
            _cancellation = new();
            StatusText = "Отправляю выбранные файлы в корзину…";
            var report = await _service.CleanAsync(plan, selected.Select(x => x.Path).ToArray(), null, _cancellation.Token);
            var outcome = report.Cancelled ? "Отменено" : report.Errors.Count > 0 ? "Завершено с ошибками"
                : report.Skipped > 0 ? "Завершено с пропусками" : "Завершено";
            ResultText = $"{outcome}. В корзине файлов: {report.Deleted:N0}, данных: {SizeFormatter.Format(report.FreedBytes)}. Пропущено: {report.Skipped:N0}."
                + (report.Errors.Count == 0 ? string.Empty : Environment.NewLine + string.Join(Environment.NewLine, report.Errors));
            _logger.ProjectCleanupFinished(report.Deleted, report.FreedBytes, report.Skipped, report.Cancelled);
            _cancellation.Dispose();
            _cancellation = new();
            try { await ScanAsync(_cancellation.Token); }
            catch (OperationCanceledException) { StatusText = "Повторный поиск отменён. Результат очистки сохранён."; }
            catch (Exception exception)
            {
                _logger.ProjectCleanupFailed(exception.Unwrap());
                StatusText = $"Повторный поиск не выполнен: {exception.Unwrap().Message}";
            }
        }
        catch (Exception exception)
        {
            _plan = null;
            _logger.ProjectCleanupFailed(exception.Unwrap());
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
    private void Cancel() => _cancellation?.Cancel();

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private void ClearSelection()
    {
        if (!IsIdle) { return; }
        foreach (var row in Artifacts) { row.IsSelected = false; }
    }

    [RelayCommand]
    private void OpenFolder(ProjectArtifactViewModel? row)
    {
        if (row is not null) { _shell.Open(row.Path); }
    }

    private void ClearRows() => _dispatcher.Invoke(() =>
    {
        foreach (var row in Artifacts) { row.PropertyChanged -= OnSelectionChanged; }
        Artifacts.Clear();
        NotifySelection();
    });

    private void OnSelectionChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(ProjectArtifactViewModel.IsSelected)) { NotifySelection(); }
    }

    private void NotifySelection()
    {
        OnPropertyChanged(nameof(HasArtifacts));
        OnPropertyChanged(nameof(TotalText));
        OnPropertyChanged(nameof(SelectedText));
        OnPropertyChanged(nameof(SelectionText));
        OnPropertyChanged(nameof(CanClean));
        CleanCommand.NotifyCanExecuteChanged();
    }

    internal void ShowForAutomation()
    {
        ResetForAutomation();
        RootPath = @"C:\Sources\Projects";
        _dispatcher.Invoke(() =>
        {
            foreach (var row in new[]
            {
                new ProjectArtifactViewModel(ProjectArtifactKind.Bin, @"C:\Sources\Projects\App\bin", 840_000_000, 320, true, string.Empty),
                new ProjectArtifactViewModel(ProjectArtifactKind.Obj, @"C:\Sources\Projects\App\obj", 120_000_000, 640, true, string.Empty),
                new ProjectArtifactViewModel(ProjectArtifactKind.TestResults, @"C:\Sources\Projects\App.Tests\TestResults", 64_000_000, 24, true, string.Empty),
                new ProjectArtifactViewModel(ProjectArtifactKind.Benchmarks, @"C:\Sources\Projects\BenchmarkDotNet.Artifacts", 8_000_000, 12, true, string.Empty),
            })
            {
                row.PropertyChanged += OnSelectionChanged;
                Artifacts.Add(row);
            }
            StatusText = "Пример результатов поиска. Очистка в демонстрации недоступна.";
            NotifySelection();
        });
    }

    internal void ResetForAutomation()
    {
        _plan = null;
        RootPath = string.Empty;
        ResultText = null;
        StatusText = null;
        ClearRows();
    }
}
