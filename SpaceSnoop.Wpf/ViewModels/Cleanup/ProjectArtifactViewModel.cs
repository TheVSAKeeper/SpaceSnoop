namespace SpaceSnoop.Wpf.ViewModels.Cleanup;

public sealed partial class ProjectArtifactViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isSelected;

    public ProjectArtifactViewModel(ProjectArtifactScan scan)
        : this(scan.Kind, scan.Path, scan.Bytes, scan.Files, scan.CanClean, string.Join(Environment.NewLine, scan.Errors))
    {
    }

    internal ProjectArtifactViewModel(ProjectArtifactKind kind, string path, long bytes, int files, bool canSelect, string error)
    {
        Kind = kind;
        Path = path;
        Bytes = bytes;
        Files = files;
        CanSelect = canSelect;
        ErrorText = error;
    }

    public ProjectArtifactKind Kind { get; }
    public string Path { get; }
    public long Bytes { get; }
    public int Files { get; }
    public bool CanSelect { get; }
    public string ErrorText { get; }
    public bool HasError => ErrorText.Length > 0;
    public string SizeText => SizeFormatter.Format(Bytes);
    public string FilesText => $"Файлов: {Files:N0}";
    public string Name => Kind switch
    {
        ProjectArtifactKind.Bin => "bin · результаты сборки",
        ProjectArtifactKind.Obj => "obj · промежуточные файлы",
        ProjectArtifactKind.TestResults => "TestResults · результаты тестов",
        ProjectArtifactKind.Benchmarks => "BenchmarkDotNet · результаты замеров",
        _ => string.Empty,
    };
    public string Hint => Kind is ProjectArtifactKind.TestResults or ProjectArtifactKind.Benchmarks
        ? "Отчёты могут быть нужны для сравнения и диагностики. Выбирайте отдельно."
        : "Каталог может содержать ваши файлы. Проверьте содержимое перед выбором.";
}
