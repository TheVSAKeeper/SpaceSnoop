using KeepShell.Services;
using MahApps.Metro.IconPacks;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text.Json;
using System.Windows.Data;

namespace SpaceSnoop.Wpf.ViewModels.Cleanup;

public sealed partial class ProjectCleanupViewModel : ObservableObject
{
    private readonly ProjectCleanupService _service;
    private readonly IDialogService _dialogs;
    private readonly IFilePicker _picker;
    private readonly IShellLauncher _shell;
    private readonly IUiDispatcher _dispatcher;
    private readonly ISettingsStore _settings;
    private readonly ILogger<ProjectCleanupViewModel> _logger;
    private ProjectCleanupPlan? _plan;
    private CancellationTokenSource? _cancellation;
    private long _inputVersion;
    private bool _restoring;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    [NotifyPropertyChangedFor(nameof(CanClean))]
    [NotifyCanExecuteChangedFor(nameof(FindCommand))]
    [NotifyCanExecuteChangedFor(nameof(BrowseCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddRootCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddCustomOutputCommand))]
    [NotifyCanExecuteChangedFor(nameof(RemoveRootCommand))]
    [NotifyCanExecuteChangedFor(nameof(RemoveCustomOutputCommand))]
    [NotifyCanExecuteChangedFor(nameof(ClearRootsCommand))]
    [NotifyCanExecuteChangedFor(nameof(ClearCustomOutputsCommand))]
    [NotifyCanExecuteChangedFor(nameof(CleanCommand))]
    [NotifyCanExecuteChangedFor(nameof(ClearSelectionCommand))]
    [NotifyCanExecuteChangedFor(nameof(SelectBuildArtifactsCommand))]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    [NotifyPropertyChangedFor(nameof(CanClean))]
    [NotifyCanExecuteChangedFor(nameof(FindCommand))]
    [NotifyCanExecuteChangedFor(nameof(BrowseCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddRootCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddCustomOutputCommand))]
    [NotifyCanExecuteChangedFor(nameof(RemoveRootCommand))]
    [NotifyCanExecuteChangedFor(nameof(RemoveCustomOutputCommand))]
    [NotifyCanExecuteChangedFor(nameof(ClearRootsCommand))]
    [NotifyCanExecuteChangedFor(nameof(ClearCustomOutputsCommand))]
    [NotifyCanExecuteChangedFor(nameof(CleanCommand))]
    [NotifyCanExecuteChangedFor(nameof(ClearSelectionCommand))]
    [NotifyCanExecuteChangedFor(nameof(SelectBuildArtifactsCommand))]
    private bool _isHostBusy;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(FindCommand))]
    private string _rootPath = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HiddenSelectionText))]
    [NotifyPropertyChangedFor(nameof(HasHiddenSelection))]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _largestFirst = true;

    [ObservableProperty]
    private string? _statusText;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasResult))]
    private string? _resultText;

    public ProjectCleanupViewModel(ProjectCleanupService service, IDialogService dialogs, IFilePicker picker,
        IShellLauncher shell, IUiDispatcher dispatcher, ISettingsStore settings, ILogger<ProjectCleanupViewModel> logger)
    {
        _service = service;
        _dialogs = dialogs;
        _picker = picker;
        _shell = shell;
        _dispatcher = dispatcher;
        _settings = settings;
        _logger = logger;
        RowsView = CollectionViewSource.GetDefaultView(Artifacts);
        RowsView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ProjectArtifactViewModel.ProjectDirectory)));
        RowsView.SortDescriptions.Add(new(nameof(ProjectArtifactViewModel.ProjectBytes), ListSortDirection.Descending));
        RowsView.SortDescriptions.Add(new(nameof(ProjectArtifactViewModel.ProjectDirectory), ListSortDirection.Ascending));
        RowsView.SortDescriptions.Add(new(nameof(ProjectArtifactViewModel.Bytes), ListSortDirection.Descending));
        RowsView.SortDescriptions.Add(new(nameof(ProjectArtifactViewModel.Path), ListSortDirection.Ascending));
        RowsView.Filter = FilterArtifact;
        AdditionalRoots.CollectionChanged += (_, _) => InputChanged();
        CustomOutputs.CollectionChanged += (_, _) => InputChanged();
        RestoreInputs();
    }

    public ObservableCollection<string> AdditionalRoots { get; } = [];
    public ObservableCollection<string> CustomOutputs { get; } = [];
    public ObservableCollection<ProjectArtifactViewModel> Artifacts { get; } = [];
    public ICollectionView RowsView { get; }
    public bool IsIdle => !IsBusy && !IsHostBusy;
    public bool HasArtifacts => Artifacts.Count > 0;
    public bool HasResult => !string.IsNullOrEmpty(ResultText);
    public bool CanClean => IsIdle && _plan is not null && Artifacts.Any(x => x.IsSelected && x.CanSelect);
    public string TotalText => SizeFormatter.Format(Artifacts.Sum(x => x.Bytes));
    public string SelectedText => SizeFormatter.Format(Artifacts.Where(x => x.IsSelected && x.CanSelect).Sum(x => x.Bytes));
    public string SelectionText => $"Выбрано строк: {Artifacts.Count(x => x.IsSelected && x.CanSelect)} из {Artifacts.Count}";
    public string HiddenSelectionText => Artifacts.Any(x => x.IsSelected && x.CanSelect && !FilterArtifact(x))
        ? "Есть выбранные скрытые строки – они тоже попадут в подтверждение."
        : string.Empty;
    public bool HasHiddenSelection => HiddenSelectionText.Length > 0;

    partial void OnRootPathChanged(string value)
    {
        if (!_restoring) { InputChanged(); }
    }

    partial void OnSearchTextChanged(string value)
    {
        RowsView.Refresh();
        OnPropertyChanged(nameof(HiddenSelectionText));
        OnPropertyChanged(nameof(HasHiddenSelection));
    }

    partial void OnLargestFirstChanged(bool value)
    {
        RowsView.SortDescriptions.Clear();
        RowsView.SortDescriptions.Add(new(nameof(ProjectArtifactViewModel.ProjectBytes), value ? ListSortDirection.Descending : ListSortDirection.Ascending));
        RowsView.SortDescriptions.Add(new(nameof(ProjectArtifactViewModel.ProjectDirectory), ListSortDirection.Ascending));
        RowsView.SortDescriptions.Add(new(nameof(ProjectArtifactViewModel.Bytes), value ? ListSortDirection.Descending : ListSortDirection.Ascending));
        RowsView.SortDescriptions.Add(new(nameof(ProjectArtifactViewModel.Path), ListSortDirection.Ascending));
    }

    private bool FilterArtifact(object item)
    {
        if (item is not ProjectArtifactViewModel row) { return false; }
        var query = SearchText.Trim();
        return query.Length == 0 || row.Path.Contains(query, StringComparison.OrdinalIgnoreCase)
            || row.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
            || row.ProjectDirectory.Contains(query, StringComparison.OrdinalIgnoreCase)
            || row.Contents.Any(file => file.Path.Contains(query, StringComparison.OrdinalIgnoreCase));
    }

    private void InputChanged()
    {
        if (_restoring) { return; }
        _inputVersion++;
        _plan = null;
        ResultText = null;
        ClearRows();
        PersistInputs();
        StatusText = "Пути изменились. Повторите поиск.";
        FindCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanClean));
    }

    private bool CanFind() => IsIdle && ScopeRoots().Length + CustomOutputs.Count > 0;

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private async Task BrowseAsync()
    {
        if (!IsIdle) { return; }
        var path = _picker.PickFolder("Каталог проектов для поиска артефактов", RootPath);
        if (path is null) { return; }
        RootPath = path;
        await FindAsync();
    }

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private void AddRoot()
    {
        if (!IsIdle) { return; }
        var path = _picker.PickFolder("Дополнительный корень проектов", RootPath);
        if (path is not null) { AdditionalRoots.Add(path); }
    }

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private void AddCustomOutput()
    {
        if (!IsIdle) { return; }
        var path = _picker.PickFolder("Каталог нестандартных результатов сборки", RootPath);
        if (path is not null) { CustomOutputs.Add(path); }
    }

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private void RemoveRoot(string? path)
    {
        if (IsIdle && path is not null) { AdditionalRoots.Remove(path); }
    }

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private void RemoveCustomOutput(string? path)
    {
        if (IsIdle && path is not null) { CustomOutputs.Remove(path); }
    }

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private void ClearRoots()
    {
        if (!IsIdle) { return; }
        RootPath = string.Empty;
        AdditionalRoots.Clear();
    }

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private void ClearCustomOutputs()
    {
        if (IsIdle) { CustomOutputs.Clear(); }
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
        var roots = ScopeRoots();
        var outputs = CustomOutputs.ToArray();
        var version = _inputVersion;
        StatusText = "Ищу проекты и измеряю артефакты…";
        var plan = await _service.ScanAsync(roots, outputs, token);
        _dispatcher.Invoke(() =>
        {
            if (version != _inputVersion || !SameInputs(roots, outputs))
            {
                StatusText = "Пути изменились во время поиска. Повторите поиск.";
                return;
            }
            _plan = plan;
            var totals = plan.Artifacts.GroupBy(x => x.ProjectDirectory, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(x => x.Key, x => x.Sum(row => row.Bytes), StringComparer.OrdinalIgnoreCase);
            foreach (var scan in plan.Artifacts)
            {
                var row = new ProjectArtifactViewModel(scan, totals.GetValueOrDefault(scan.ProjectDirectory));
                row.PropertyChanged += OnSelectionChanged;
                Artifacts.Add(row);
            }
            RowsView.Refresh();
            StatusText = plan.Artifacts.Count == 0 ? "В выбранных путях проектные артефакты не найдены."
                : "Поиск завершён. Проверьте содержимое и выберите каталоги. Отчёты и нестандартные результаты выбираются отдельно.";
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
        var version = _inputVersion;
        var roots = ScopeRoots();
        var outputs = CustomOutputs.ToArray();
        var selected = Artifacts.Where(x => x.IsSelected && x.CanSelect).ToArray();
        var selectedIds = selected.Select(x => x.Id).ToArray();
        var lines = new List<ConfirmLine>
        {
            new ConfirmTextLine("Остановите сборки, тесты и бенчмарки. В найденных каталогах могут лежать ваши файлы; автоматическое восстановление не гарантируется.", ConfirmTextTone.Strong),
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
            var currentIds = Artifacts.Where(x => x.IsSelected && x.CanSelect).Select(x => x.Id).ToArray();
            if (!ReferenceEquals(_plan, plan) || version != _inputVersion || !SameInputs(roots, outputs)
                || !selectedIds.SequenceEqual(currentIds, StringComparer.Ordinal))
            {
                StatusText = "Пути, выбор или план изменились. Повторите поиск.";
                return;
            }
            _plan = null;
            _cancellation = new();
            StatusText = "Отправляю выбранные файлы в корзину…";
            var report = await _service.CleanAsync(plan, selected.Select(x => x.Id).ToArray(), null, _cancellation.Token);
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
            OnPropertyChanged(nameof(CanClean));
            CleanCommand.NotifyCanExecuteChanged();
        }
    }

    [RelayCommand]
    private void Cancel() => _cancellation?.Cancel();

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private void SelectBuildArtifacts()
    {
        if (!IsIdle) { return; }
        foreach (var row in Artifacts.Where(row => row.CanSelect && FilterArtifact(row)
            && row.Kind is ProjectArtifactKind.Bin or ProjectArtifactKind.Obj))
        {
            row.IsSelected = true;
        }
    }

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

    private string[] ScopeRoots() => new[] { RootPath }.Concat(AdditionalRoots)
        .Where(path => !string.IsNullOrWhiteSpace(path)).ToArray();

    private bool SameInputs(IReadOnlyCollection<string> roots, IReadOnlyCollection<string> outputs) =>
        roots.SequenceEqual(ScopeRoots(), StringComparer.Ordinal)
        && outputs.SequenceEqual(CustomOutputs, StringComparer.Ordinal);

    private void RestoreInputs()
    {
        _restoring = true;
        try
        {
            var roots = ReadPaths(_settings.GetStringValue(SettingsKeys.DeveloperProjectRoots));
            RootPath = roots.FirstOrDefault() ?? string.Empty;
            foreach (var path in roots.Skip(1)) { AdditionalRoots.Add(path); }
            foreach (var path in ReadPaths(_settings.GetStringValue(SettingsKeys.DeveloperCustomOutputs))) { CustomOutputs.Add(path); }
        }
        finally { _restoring = false; }
    }

    private void PersistInputs()
    {
        _settings.SetValue(SettingsKeys.DeveloperProjectRoots, JsonSerializer.Serialize(ScopeRoots()));
        _settings.SetValue(SettingsKeys.DeveloperCustomOutputs, JsonSerializer.Serialize(CustomOutputs));
    }

    private static string[] ReadPaths(string? json)
    {
        try
        {
            return (JsonSerializer.Deserialize<string[]>(json ?? "[]") ?? [])
                .Where(path => !string.IsNullOrWhiteSpace(path)).Select(path => path.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }
        catch (JsonException) { return []; }
    }

    private void ClearRows() => _dispatcher.Invoke(() =>
    {
        foreach (var row in Artifacts) { row.PropertyChanged -= OnSelectionChanged; }
        Artifacts.Clear();
        RowsView.Refresh();
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
        OnPropertyChanged(nameof(HiddenSelectionText));
        OnPropertyChanged(nameof(HasHiddenSelection));
        OnPropertyChanged(nameof(CanClean));
        CleanCommand.NotifyCanExecuteChanged();
    }

    internal void ShowForAutomation()
    {
        ResetForAutomation();
        _restoring = true;
        try
        {
            RootPath = @"C:\Sources\Projects";
            AdditionalRoots.Add(@"C:\Sources\Samples");
            CustomOutputs.Add(@"C:\Sources\Projects\Legacy\artifacts");
        }
        finally { _restoring = false; }
        _dispatcher.Invoke(() =>
        {
            foreach (var row in new[]
            {
                new ProjectArtifactViewModel(ProjectArtifactKind.Bin, @"C:\Sources\Projects\App\bin", 840_000_000, 320, true, string.Empty, @"C:\Sources\Projects\App", [new(@"C:\Sources\Projects\App\bin\Debug\app.dll", 840_000_000)], projectBytes: 960_000_000),
                new ProjectArtifactViewModel(ProjectArtifactKind.Obj, @"C:\Sources\Projects\App\obj", 120_000_000, 640, true, string.Empty, @"C:\Sources\Projects\App", [new(@"C:\Sources\Projects\App\obj\Debug\data.bin", 120_000_000)], projectBytes: 960_000_000),
                new ProjectArtifactViewModel(ProjectArtifactKind.Coverage, @"C:\Sources\Projects\App.Tests\TestResults\coverage.cobertura.xml", 64_000_000, 1, true, string.Empty, @"C:\Sources\Projects\App.Tests", [new(@"C:\Sources\Projects\App.Tests\TestResults\coverage.cobertura.xml", 64_000_000)]),
                new ProjectArtifactViewModel(ProjectArtifactKind.CustomOutput, @"C:\Sources\Projects\Legacy\artifacts", 8_000_000, 12, true, string.Empty, @"C:\Sources\Projects\Legacy", [new(@"C:\Sources\Projects\Legacy\artifacts\report.json", 8_000_000)]),
            })
            {
                row.PropertyChanged += OnSelectionChanged;
                Artifacts.Add(row);
            }
            RowsView.Refresh();
            StatusText = "Пример результатов поиска. Очистка в демонстрации недоступна.";
            NotifySelection();
        });
    }

    internal void ResetForAutomation()
    {
        _plan = null;
        _inputVersion++;
        _restoring = true;
        try
        {
            RootPath = string.Empty;
            AdditionalRoots.Clear();
            CustomOutputs.Clear();
        }
        finally { _restoring = false; }
        ResultText = null;
        StatusText = null;
        ClearRows();
    }
}
