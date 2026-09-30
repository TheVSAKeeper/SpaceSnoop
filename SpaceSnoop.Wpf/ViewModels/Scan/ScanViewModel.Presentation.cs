using System.IO;

namespace SpaceSnoop.Wpf.ViewModels.Scan;

public sealed partial class ScanViewModel
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CancelledCaption))]
    private string _cancelledPath = string.Empty;

    [ObservableProperty]
    private bool _cancelledReleasedPrevious;

    public bool TreeVisible => HasResult && ViewMode == ScanViewMode.Tree;

    public bool TreemapVisible => HasResult && ViewMode == ScanViewMode.Treemap;

    public bool DuplicatesVisible => HasResult && ViewMode == ScanViewMode.Duplicates;

    public bool ShowTargetPicker => !IsScanning && (!HasResult || IsPickerOpen);

    public bool ShowScanResult => !ShowTargetPicker;

    public bool ShowScanningState => !HasResult && IsScanning;

    public bool ShowSummary => HasResult && !IsScanning && !IsPickerOpen;

    public bool ShowStaleResult => IsScanning
        && HasResult
        && CurrentRoot is { } root
        && !string.IsNullOrWhiteSpace(ScanTargetPath)
        && !ScanTreeEditor.SamePath(root.AbsolutePath, ScanTargetPath);

    public string StaleResultCaption => CurrentRoot is { } root
        ? $"Прежний результат: {root.AbsolutePath} – новый появится после обхода {ScanTargetPath}"
        : string.Empty;

    public string CancelledCaption => $"Сканирование {CancelledPath} остановлено";

    public bool HasTarget => !string.IsNullOrWhiteSpace(SelectedDrive);

    public string StartCaption => HasTarget
        ? $"Сканировать {DriveItem.ShortName(SelectedDrive)}"
        : "Выберите диск или каталог";

    private bool CanRescanCancelled()
    {
        return !IsScanning && ScanWasCancelled && !string.IsNullOrWhiteSpace(CancelledPath);
    }

    [RelayCommand(CanExecute = nameof(CanRescanCancelled))]
    private async Task RescanCancelledAsync()
    {
        var path = CancelledPath;

        if (!Directory.Exists(path))
        {
            ScanWasCancelled = false;
            _dialogs.Warning("Сканирование", $"Папка {path} не найдена: её удалили, переименовали или диск отключён. Выберите для сканирования другую папку.");
            return;
        }

        SelectPathForAutomation(path);
        await ScanAfterRescanPromptAsync(path);
    }

    [RelayCommand]
    private void DismissCancelled()
    {
        ScanWasCancelled = false;
    }
}
