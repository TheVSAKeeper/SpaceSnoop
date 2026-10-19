using KeepShell.Services;
using KeepShell.Services.Platform;
using MahApps.Metro.IconPacks;
using SpaceSnoop.Core.Cleanup;
using System.Windows.Input;

namespace SpaceSnoop.Wpf.ViewModels.Cleanup;

public sealed partial class DotNetCleanViewModel : ObservableObject
{
    private readonly DotNetCleanService _service;
    private readonly IDialogService _dialogs;
    private readonly IFilePicker _picker;
    private readonly IShellLauncher _shell;
    private readonly IUiDispatcher _dispatcher;
    private readonly ILogger<DotNetCleanViewModel> _logger;
    private DotNetCleanPlan? _plan;
    private CancellationTokenSource? _cancellation;
    private long _inputVersion;
    private bool _isHostBusy;
    private bool _isBusy;
    private bool _isTrusted;
    private string _projectPath = string.Empty;
    private string _configuration = "Debug";
    private string _framework = string.Empty;
    private string _runtime = string.Empty;

    public DotNetCleanViewModel(DotNetCleanService service, IDialogService dialogs, IFilePicker picker,
        IShellLauncher shell, IUiDispatcher dispatcher, ILogger<DotNetCleanViewModel> logger)
    {
        _service = service;
        _dialogs = dialogs;
        _picker = picker;
        _shell = shell;
        _dispatcher = dispatcher;
        _logger = logger;
    }

    public string ProjectPath { get => _projectPath; set { if (SetProperty(ref _projectPath, value ?? string.Empty)) { IsTrusted = false; InputChanged(); } } }
    public string Configuration { get => _configuration; set { if (SetProperty(ref _configuration, value ?? string.Empty)) InputChanged(); } }
    public string Framework { get => _framework; set { if (SetProperty(ref _framework, value ?? string.Empty)) InputChanged(); } }
    public string Runtime { get => _runtime; set { if (SetProperty(ref _runtime, value ?? string.Empty)) InputChanged(); } }
    public bool IsTrusted { get => _isTrusted; set { if (SetProperty(ref _isTrusted, value)) InputChanged(); } }
    public bool IsBusy { get => _isBusy; private set { if (SetProperty(ref _isBusy, value)) { OnPropertyChanged(nameof(IsIdle)); NotifyCommands(); } } }
    public bool IsHostBusy { get => _isHostBusy; set { if (SetProperty(ref _isHostBusy, value)) { OnPropertyChanged(nameof(IsIdle)); NotifyCommands(); } } }
    public bool IsIdle => !IsBusy && !IsHostBusy;
    public bool HasPlan => DisplayPlan is not null;
    public bool HasResult => !string.IsNullOrEmpty(ResultText);
    public bool CanClean => IsIdle && IsTrusted && _plan is not null;
    public bool CanCancel => _cancellation is not null;
    public ICommand? CancelCommand => CanCancel ? CancelOperationCommand : null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPlan))]
    private DotNetCleanDisplay? _displayPlan;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasResult))]
    private string? _resultText;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenLogCommand))]
    private string? _logPath;

    [ObservableProperty]
    private string? _statusText;

    partial void OnDisplayPlanChanged(DotNetCleanDisplay? value) => OnPropertyChanged(nameof(CanClean));

    private void InputChanged()
    {
        _inputVersion++;
        _plan = null;
        DisplayPlan = null;
        ResultText = null;
        LogPath = null;
        StatusText = "Параметры изменились. Повторите проверку.";
        OnPropertyChanged(nameof(CanClean));
        NotifyCommands();
    }

    [RelayCommand(CanExecute = nameof(CanChooseProject))]
    private void PickProject()
    {
        if (!CanChooseProject()) return;
        var path = _picker.PickFile(new("Выберите проект .NET", "Проекты .NET (*.csproj;*.fsproj;*.vbproj)|*.csproj;*.fsproj;*.vbproj|Все файлы (*.*)|*.*"));
        if (path is not null) ProjectPath = path;
    }

    private bool CanChooseProject() => IsIdle;

    [RelayCommand(CanExecute = nameof(CanInspect))]
    private async Task InspectAsync()
    {
        if (!CanInspect()) return;
        var version = _inputVersion;
        var project = ProjectPath;
        var options = GetOptions();
        IsBusy = true;
        _cancellation = new();
        OnPropertyChanged(nameof(CanCancel));
        OnPropertyChanged(nameof(CancelCommand));
        _plan = null;
        DisplayPlan = null;
        ResultText = null;
        LogPath = null;
        StatusText = "Проверяю SDK в каталоге проекта…";
        try
        {
            var plan = await _service.InspectAsync(project, options, IsTrusted, _cancellation.Token);
            _dispatcher.Invoke(() =>
            {
                if (version != _inputVersion || !IsTrusted || project != ProjectPath || options != GetOptions()) return;
                _plan = plan;
                DisplayPlan = new(plan.ProjectPath, plan.Executable, plan.WorkingDirectory, plan.SdkVersion,
                    plan.Options.Configuration, plan.Options.Framework ?? "", plan.Options.Runtime ?? "", plan.Command);
                LogPath = plan.InspectionLogPath;
                StatusText = "Проверка завершена. Очистка может выполнять импортированные задачи MSBuild.";
                OnPropertyChanged(nameof(CanClean));
            });
        }
        catch (OperationCanceledException)
        {
            StatusText = "Проверка отменена.";
        }
        catch (DotNetCleanInspectionException exception)
        {
            LogPath = exception.LogPath;
            ResultText = FormatOutput(exception.OutputTail, exception.OutputTruncated, exception.Message);
            StatusText = exception.Outcome == DotNetCliOutcome.Cancelled ? "Проверка отменена." : exception.Message;
            if (exception.Outcome != DotNetCliOutcome.Cancelled) _logger.DotNetCleanFailed(exception);
        }
        catch (Exception exception)
        {
            _logger.DotNetCleanFailed(exception);
            StatusText = exception.Message;
            ResultText = exception.Message;
        }
        finally
        {
            _cancellation.Dispose();
            _cancellation = null;
            OnPropertyChanged(nameof(CanCancel));
            OnPropertyChanged(nameof(CancelCommand));
            IsBusy = false;
        }
    }

    private bool CanInspect() => IsIdle && IsTrusted && !string.IsNullOrWhiteSpace(ProjectPath);

    [RelayCommand(CanExecute = nameof(CanClean))]
    private async Task CleanAsync()
    {
        var plan = _plan;
        if (!CanClean || plan is null) return;
        var version = _inputVersion;
        var project = ProjectPath;
        var confirmation = new ConfirmDialogViewModel("Очистка проекта .NET", PackIconLucideKind.Trash2,
        [
            new ConfirmTextLine("Будет выполнена команда: " + plan.Command, ConfirmTextTone.Strong),
            new ConfirmTextLine("Проект: " + plan.ProjectPath),
            new ConfirmTextLine("Исполняемый файл: " + plan.Executable),
            new ConfirmTextLine("Рабочий каталог: " + plan.WorkingDirectory),
            new ConfirmTextLine("Активный SDK: " + plan.SdkVersion),
            new ConfirmTextLine($"Параметры: configuration={plan.Options.Configuration}, framework={plan.Options.Framework ?? "(нет)"}, runtime={plan.Options.Runtime ?? "(нет)"}"),
            new ConfirmTextLine("Импортированные цели MSBuild могут выполнять произвольный код и затрагивать файлы за пределами проекта. Очистка необратима, может завершиться частично и не имеет отката или пробного запуска.", ConfirmTextTone.Strong),
            new ConfirmTextLine("Отмена останавливает только процесс dotnet и его дочерние процессы. Уже удалённые файлы не восстанавливаются."),
        ], [new("Отмена", ConfirmChoiceKind.Dismissive), new("Выполнить dotnet clean", ConfirmChoiceKind.Destructive)])
        { Warning = "Проверьте проект, SDK и точную команду. Импортированные задачи могут менять данные вне каталога проекта." };
        IsBusy = true;
        try
        {
            if (!await _dialogs.ShowAsync(confirmation)) return;
            if (!ReferenceEquals(_plan, plan) || version != _inputVersion || !IsTrusted || ProjectPath != project
                || GetOptions() != plan.Options || DateTime.UtcNow > plan.ExpiresUtc)
            {
                _plan = null;
                DisplayPlan = null;
                StatusText = "План изменился или устарел. Повторите проверку.";
                return;
            }
            _plan = null;
            OnPropertyChanged(nameof(CanClean));
            _cancellation = new();
            OnPropertyChanged(nameof(CanCancel));
            OnPropertyChanged(nameof(CancelCommand));
            StatusText = "Выполняю dotnet clean…";
            var report = await _service.CleanAsync(plan, IsTrusted, _cancellation.Token);
            _dispatcher.Invoke(() => ApplyReport(report));
            _logger.DotNetCleanFinished(report.Outcome.ToString(), report.ExitCode, report.LogPath);
        }
        catch (Exception exception)
        {
            _logger.DotNetCleanFailed(exception);
            StatusText = exception.Message;
            ResultText = exception.Message;
        }
        finally
        {
            _cancellation?.Dispose();
            _cancellation = null;
            OnPropertyChanged(nameof(CanCancel));
            OnPropertyChanged(nameof(CancelCommand));
            IsBusy = false;
        }
    }

    private void ApplyReport(DotNetCleanReport report)
    {
        var outcome = report.Cancelled ? "Отменено" : report.TimedOut ? "Истекло время ожидания"
            : report.Succeeded ? "Завершено" : "Завершено с ошибкой";
        StatusText = outcome + ".";
        ResultText = outcome + (report.ExitCode is { } code ? $". Код завершения: {code}." : ".")
            + (report.Error is null ? string.Empty : Environment.NewLine + report.Error)
            + (report.OutputTail.Length == 0 ? string.Empty : Environment.NewLine + report.OutputTail)
            + (report.OutputTruncated ? Environment.NewLine + "В окне показан только конец вывода." : string.Empty)
            + (report.Cancelled || !report.Succeeded ? Environment.NewLine + "Результат может быть частичным; уже удалённые файлы не восстановлены." : string.Empty);
        if (!string.IsNullOrEmpty(report.LogPath)) LogPath = report.LogPath;
    }

    private static string FormatOutput(string tail, bool truncated, string message) => message
        + (tail.Length == 0 ? string.Empty : Environment.NewLine + tail)
        + (truncated ? Environment.NewLine + "В окне показан только конец вывода." : string.Empty);

    private DotNetCleanOptions GetOptions() => new(Configuration.Trim(), EmptyToNull(Framework), EmptyToNull(Runtime));
    private static string? EmptyToNull(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    [RelayCommand(CanExecute = nameof(CanOpenLog))]
    private void OpenLog() { if (CanOpenLog()) _shell.Reveal(LogPath!); }
    private bool CanOpenLog() => !string.IsNullOrWhiteSpace(LogPath);

    [RelayCommand]
    private void CancelOperation() => _cancellation?.Cancel();

    private void NotifyCommands()
    {
        PickProjectCommand.NotifyCanExecuteChanged();
        InspectCommand.NotifyCanExecuteChanged();
        CleanCommand.NotifyCanExecuteChanged();
        OpenLogCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanClean));
        OnPropertyChanged(nameof(CancelCommand));
    }

    internal void ResetForAutomation()
    {
        _plan = null;
        _inputVersion++;
        IsTrusted = false;
        DisplayPlan = null;
        ResultText = null;
        LogPath = null;
        StatusText = "Демонстрация dotnet clean отключена.";
    }

    internal void ShowForAutomation()
    {
        ResetForAutomation();
        ProjectPath = @"C:\workspace\sample\Sample.csproj";
        DisplayPlan = new(@"C:\workspace\sample\Sample.csproj", @"C:\Program Files\dotnet\dotnet.exe",
            @"C:\workspace\sample", "10.0.303", "Debug", "", "",
            "\"C:\\Program Files\\dotnet\\dotnet.exe\" \"clean\" \"C:\\workspace\\sample\\Sample.csproj\" \"--configuration\" \"Debug\" \"--disable-build-servers\"");
        StatusText = "Демонстрационный контекст. Запуск команды отключён.";
    }
}

public sealed record DotNetCleanDisplay(string ProjectPath, string Executable, string WorkingDirectory,
    string SdkVersion, string Configuration, string Framework, string Runtime, string Command);
