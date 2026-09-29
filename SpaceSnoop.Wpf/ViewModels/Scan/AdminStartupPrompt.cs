using KeepShell.Services;
using MahApps.Metro.IconPacks;

namespace SpaceSnoop.Wpf.ViewModels.Scan;

public enum StartupAdminAction
{
    None = 0,
    WelcomeCard = 1,
    Question = 2,
}

public sealed class AdminStartupPrompt(IDialogService dialogs, ShellPreferences shell, Func<Task> restart)
{
    internal static readonly ConfirmChoice StopAsking = new("Больше не спрашивать", ConfirmChoiceKind.Secondary);

    internal static readonly ConfirmChoice Restart = new("Перезапустить", ConfirmChoiceKind.Primary);

    public static StartupAdminAction Decide(bool isElevated, bool welcomePending, bool warnIfNotAdmin)
    {
        if (welcomePending)
        {
            return StartupAdminAction.WelcomeCard;
        }

        return !isElevated && warnIfNotAdmin ? StartupAdminAction.Question : StartupAdminAction.None;
    }

    public async Task RunAsync(bool isElevated, bool welcomePending)
    {
        if (Decide(isElevated, welcomePending, shell.WarnIfNotAdministrator) != StartupAdminAction.Question)
        {
            return;
        }

        var dialog = BuildConfirm();
        await dialogs.ShowAsync(dialog);

        if (ReferenceEquals(dialog.Chosen, StopAsking))
        {
            shell.WarnIfNotAdministrator = false;
        }
        else if (ReferenceEquals(dialog.Chosen, Restart))
        {
            await restart();
        }
    }

    internal static ConfirmDialogViewModel BuildConfirm()
    {
        return new(
            "Запустить с правами администратора?",
            PackIconLucideKind.ShieldAlert,
            [
                "Программа запущена без прав администратора, поэтому часть системных папок не прочитается и размеры выйдут заниженными.",
                "Программа закроется и откроется снова, Windows спросит разрешение.",
                "Если больше не спрашивать, вопрос можно вернуть в «Настройки → Запуск».",
            ],
            [
                new("Не перезапускать", ConfirmChoiceKind.Dismissive),
                StopAsking,
                Restart,
            ]);
    }
}
