namespace SpaceSnoop.Core.Cleanup;

public sealed class WorkloadCleanupPlan
{
    internal WorkloadCleanupPlan(Guid owner, string executable, string workingDirectory, string sdkVersion,
        string installedWorkloads, DateTime createdUtc, string workloadListLogPath)
    {
        Owner = owner;
        Executable = executable;
        WorkingDirectory = workingDirectory;
        SdkVersion = sdkVersion;
        InstalledWorkloads = installedWorkloads;
        CreatedUtc = createdUtc;
        WorkloadListLogPath = workloadListLogPath;
    }

    internal Guid Owner { get; }
    internal DateTime CreatedUtc { get; }
    internal int Used;
    public DateTime ExpiresUtc => CreatedUtc.AddMinutes(15);
    public string Executable { get; }
    public string WorkingDirectory { get; }
    public string SdkVersion { get; }
    public string InstalledWorkloads { get; }
    public string Command => "dotnet workload clean";
    public string WorkloadListLogPath { get; }
}

public sealed class WorkloadInspectionException : InvalidOperationException
{
    internal WorkloadInspectionException(string message, DotNetCliResult result) : base(message)
    {
        Outcome = result.Outcome;
        ExitCode = result.ExitCode;
        OutputTail = result.OutputTail;
        LogPath = result.LogPath;
        OutputTruncated = result.OutputTruncated;
    }

    public DotNetCliOutcome Outcome { get; }
    public int? ExitCode { get; }
    public string OutputTail { get; }
    public string LogPath { get; }
    public bool OutputTruncated { get; }
}

public sealed record WorkloadCleanupReport(DotNetCliOutcome Outcome, int? ExitCode, string OutputTail,
    string LogPath, bool OutputTruncated, string? Error)
{
    public bool Cancelled => Outcome == DotNetCliOutcome.Cancelled;
    public bool TimedOut => Outcome == DotNetCliOutcome.TimedOut;
    public bool Succeeded => Outcome == DotNetCliOutcome.Succeeded;
}
