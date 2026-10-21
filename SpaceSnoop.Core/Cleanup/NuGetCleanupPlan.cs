namespace SpaceSnoop.Core.Cleanup;

public sealed class NuGetCleanupPlan
{
    internal NuGetCleanupPlan(Guid owner, string context, IReadOnlyList<NuGetCacheScan> caches)
    {
        Owner = owner;
        Context = context;
        Caches = caches;
    }

    internal Guid Owner { get; }
    internal string Context { get; }
    internal int Used;
    public DateTime CreatedUtc { get; } = DateTime.UtcNow;
    public IReadOnlyList<NuGetCacheScan> Caches { get; }
}

public sealed class NuGetCacheScan
{
    internal NuGetCacheScan(NuGetCacheKind kind, string path, CacheFileStamp root,
        IReadOnlyList<CacheFileStamp> files, IReadOnlyList<string> errors, bool exists = true)
    {
        Kind = kind;
        Path = path;
        Root = root;
        Entries = files;
        Errors = errors;
        Exists = exists;
    }

    public NuGetCacheKind Kind { get; }
    public string Path { get; }
    public bool Exists { get; }
    public long Bytes => Entries.Sum(file => file.Length);
    public int Files => Entries.Count;
    public IReadOnlyList<string> Errors { get; }
    public bool CanClean => Errors.Count == 0 && Files > 0;
    internal CacheFileStamp Root { get; }
    internal IReadOnlyList<CacheFileStamp> Entries { get; }
}

internal sealed record NuGetCacheLocations(string Context, IReadOnlyDictionary<NuGetCacheKind, string> Paths);

internal readonly record struct CacheFileStamp(string Path, uint Volume, ulong Id, long Length,
    ulong Written, ulong Created, uint Attributes);
