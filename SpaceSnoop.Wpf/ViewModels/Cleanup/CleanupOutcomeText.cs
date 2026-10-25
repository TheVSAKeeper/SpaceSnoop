namespace SpaceSnoop.Wpf.ViewModels.Cleanup;

internal static class CleanupOutcomeText
{
    public static string For(CleanupReport report)
    {
        return report.Cancelled ? "Отменено"
            : report.Errors.Count > 0 ? "Завершено с ошибками"
            : report.Skipped > 0 ? "Завершено с пропусками" : "Завершено";
    }

    public static string For(DotNetCliOutcome outcome)
    {
        return outcome switch
        {
            DotNetCliOutcome.Cancelled => "Отменено",
            DotNetCliOutcome.TimedOut => "Истекло время ожидания",
            DotNetCliOutcome.Succeeded => "Завершено",
            DotNetCliOutcome.LaunchFailed or DotNetCliOutcome.LoggingFailed => "Не удалось выполнить",
            _ => "Завершено с ошибкой",
        };
    }
}
