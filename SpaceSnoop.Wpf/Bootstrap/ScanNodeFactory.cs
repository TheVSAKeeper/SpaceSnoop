namespace SpaceSnoop.Wpf.Bootstrap;

public sealed class ScanNodeFactory(ScanPreferences preferences, AgentPreferences agent, IShellLauncher shell)
{
    public event Action<ScanNodeViewModel>? ArchiveRequested;
    public event Action<ScanNodeViewModel>? AskAgentRequested;
    internal event Action<ScanMarkSnapshot>? MarksChanged;

    internal Func<SpaceBase, SpaceBase, ScanMarkSnapshot>? MarkSnapshot { get; set; }

    public bool MarksPresent { get; set; }

    public bool RevealFiles => preferences.RevealFiles;

    public bool ChatEnabled => agent.Enabled;

    public IShellLauncher Shell => shell;

    public ScanNodeViewModel Create(SpaceBase space, double siblingMax, double parentTotal, SpaceBase root, ScanSortState sort)
    {
        return new(space, siblingMax, parentTotal, root, null, sort, this);
    }

    public ScanNodeViewModel CreateRoot(SpaceBase space, ScanSortState sort)
    {
        return new(space, space.TotalSize, space.TotalSize, space, DriveCapacity.TryRead(space.AbsolutePath), sort, this);
    }

    internal ScanMarkSnapshot CaptureMarks(SpaceBase space, SpaceBase root)
    {
        return MarkSnapshot?.Invoke(root, space) ?? ScanTreeEditor.CaptureMarks(root, space, MarksPresent);
    }

    internal void RaiseMarksChanged(ScanMarkSnapshot before)
    {
        MarksChanged?.Invoke(before);
    }

    public void RequestArchive(ScanNodeViewModel node)
    {
        ArchiveRequested?.Invoke(node);
    }

    public void RequestAskAgent(ScanNodeViewModel node)
    {
        AskAgentRequested?.Invoke(node);
    }
}
