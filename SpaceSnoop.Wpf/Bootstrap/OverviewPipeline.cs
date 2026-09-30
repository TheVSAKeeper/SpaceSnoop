namespace SpaceSnoop.Wpf.Bootstrap;

public readonly record struct OverviewPreflight(OverviewRunStatus Status, string? Reason = null);

public static class OverviewPipeline
{
    public static OverviewPreflight? Classify(SyncProfile profile)
    {
        var left = profile.Left.Trim();
        var right = profile.Right.Trim();

        if (left.Length == 0 || right.Length == 0)
        {
            return new(OverviewRunStatus.Unavailable);
        }

        if (SyncProfile.SourceMissing(left, right, HeadlessSync.MapMode(profile.Mode)))
        {
            return new(OverviewRunStatus.Unavailable);
        }

        if (SyncRoots.Refusal(left, right) is { } refusal)
        {
            return new(OverviewRunStatus.Overlap, refusal);
        }

        return null;
    }
}
