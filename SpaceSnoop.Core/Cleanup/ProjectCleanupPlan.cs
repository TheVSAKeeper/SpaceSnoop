namespace SpaceSnoop.Core.Cleanup;

public sealed class ProjectCleanupPlan
{
    internal ProjectCleanupPlan(Guid owner, string root, CacheFileStamp stamp,
        IReadOnlyList<ProjectArtifactScan> artifacts, IReadOnlyList<string> errors)
    {
        Owner = owner;
        Root = root;
        RootStamp = stamp;
        Artifacts = artifacts;
        Errors = errors;
    }

    internal Guid Owner { get; }
    internal CacheFileStamp RootStamp { get; }
    internal int Used;
    public string Root { get; }
    public DateTime CreatedUtc { get; } = DateTime.UtcNow;
    public IReadOnlyList<ProjectArtifactScan> Artifacts { get; }
    public IReadOnlyList<string> Errors { get; }
}

public sealed class ProjectArtifactScan
{
    internal ProjectArtifactScan(ProjectArtifactKind kind, string path, CacheFileStamp root,
        IReadOnlyList<CacheFileStamp> files, IReadOnlyList<string> errors)
    {
        Kind = kind;
        Path = path;
        Root = root;
        Entries = files;
        Errors = errors;
    }

    public ProjectArtifactKind Kind { get; }
    public string Path { get; }
    public long Bytes => Entries.Sum(file => file.Length);
    public int Files => Entries.Count;
    public IReadOnlyList<string> Errors { get; }
    public bool CanClean => Errors.Count == 0 && Files > 0;
    internal CacheFileStamp Root { get; }
    internal IReadOnlyList<CacheFileStamp> Entries { get; }
}
