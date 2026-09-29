using KeepShell.Diagnostics;
using KeepShell.Services;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using SpaceSnoop.Wpf.Views;
using System.Diagnostics;
using System.IO;

namespace SpaceSnoop.Wpf;

public partial class App : Application
{
    private ServiceProvider? _services;
    private KeepShellLogging? _logging;
    private ISettingsStore? _settings;

    protected override void OnStartup(StartupEventArgs e)
    {
        var startedAt = Stopwatch.GetTimestamp();

        base.OnStartup(e);

        var syncIndex = Array.FindIndex(e.Args, static arg => string.Equals(arg, AppInfo.SyncArgument, StringComparison.OrdinalIgnoreCase));
        var galleryIndex = Array.FindIndex(e.Args, static arg => string.Equals(arg, AppInfo.GalleryArgument, StringComparison.OrdinalIgnoreCase));

        string logsDirectory;

        try
        {
            AppResources.InstallInto(this);

            var candidate = Path.Combine(AppStorage.DataDirectory, AppStorage.LogsFolderName);

            _logging = KeepShellLogging.Bootstrap(new()
            {
                LogsDirectory = candidate,
                FileNamePrefix = AppInfo.LogFilePrefix,
                MinimumLevelOverrides = AppDefaults.LogLevelOverrides,
            });

            logsDirectory = candidate;
        }
        catch (Exception ex) when (syncIndex < 0 && galleryIndex < 0)
        {
            ReportStartupFailure(ex, null);
            return;
        }

        if (syncIndex >= 0)
        {
            var profileId = syncIndex + 1 < e.Args.Length ? e.Args[syncIndex + 1] : null;
            RunHeadlessSync(profileId);
            return;
        }

        if (galleryIndex >= 0)
        {
            RunGallery(e.Args.Skip(galleryIndex + 1));
            return;
        }

        AttachExceptionHandlers(logsDirectory);

        StyledMessageBox.DefaultTitle = AppInfo.Name;

        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        StartupSplash? splash = null;

        try
        {
            var settingsPath = Path.Combine(AppStorage.DataDirectory, TomlSettingsFile.PrimaryFileName);
            var freshProfile = !AppStorage.HasSettings(AppStorage.DataDirectory);
            ISettingsStore settings = new SettingsStore(settingsPath);
            _settings = settings;

            if (freshProfile)
            {
                settings.SetBool(SettingsKeys.WelcomePending, true);
                settings.SetInt(SettingsKeys.ScanTipsShown, 0);
            }

            AppThemes.Register();
            ThemeManager.Apply(AppThemes.StartupKey(settings, freshProfile));
            FontScaleManager.Initialize(settings.GetDouble(SettingsKeys.FontScale, FontScaleManager.DefaultScale));

            ViewLocator.InstallIntoApplication();

            Log.Information("{Marker}...", AppInfo.SessionStartMarker);

            splash = new(AppInfo.Name, AppInfo.Version, $"Запуск {AppInfo.Name}", 2, _logging.CreateLogger<StartupSplash>());

            using (splash.StartSpan("Подготовка сервисов..."))
            {
                _services = ConfigureServices(settings, _logging);
            }

            ReportSettingsWriteFailures(settings, _services);

            using (splash.StartSpan("Открытие главного окна..."))
            {
                var window = _services.GetRequiredService<MainWindow>();
                MainWindow = window;
                window.Show();

                ShutdownMode = ShutdownMode.OnMainWindowClose;
            }

            var monitor = _services.GetRequiredService<PerformanceMonitor>();
            monitor.ReportStartup(Stopwatch.GetElapsedTime(startedAt));

            var diagnostics = _services.GetRequiredService<DiagnosticsCollector>();
            diagnostics.CaptureMachine(MainWindow!);
            _logging.CreateLogger<MachineProfile>().MachineProfileCaptured(string.Join(" · ", diagnostics.Machine.Describe()));

            _services.GetRequiredService<McpServerHost>().Apply();
            _services.GetRequiredService<SystemTheme>().Watch();
            monitor.Start();

            _ = Task.Run(() => ScheduleReconciler.Reconcile(settings, _logging.CreateLogger<ScheduleViewModel>()));

            splash.Dispose();
            splash = null;

            _ = AskAboutAdministratorAsync(_services);
        }
        catch (Exception ex)
        {
            splash?.Dispose();
            ReportStartupFailure(ex, logsDirectory);
        }
    }

    private void ReportStartupFailure(Exception exception, string? logsDirectory)
    {
        Log.Fatal(exception, $"{AppInfo.Name}.Wpf не смог запуститься");

        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        var text = StartupFailureNote.Describe(exception, logsDirectory);
        var caption = $"{AppInfo.Name} – ошибка запуска";

        try
        {
            StyledMessageBox.Show(text, caption, MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch (Exception)
        {
            MessageBox.Show(text, caption, MessageBoxButton.OK, MessageBoxImage.Error);
        }

        Shutdown(1);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _settings?.Close();

        _services?.Dispose();
        _logging?.Dispose();
        base.OnExit(e);
    }

    private void ReportSettingsWriteFailures(ISettingsStore settings, ServiceProvider services)
    {
        var logger = _logging!.CreateLogger<App>();
        var dispatcher = services.GetRequiredService<IUiDispatcher>();
        var notifier = services.GetRequiredService<ToastNotifier>();

        settings.WriteFailed += (_, failure) =>
        {
            logger.SettingsWriteFailed(failure.Exception, failure.FilePath);

            dispatcher.Invoke(() => notifier.Notify(
                $"Настройка не сохранена: файл «{failure.FilePath}» не записан. Значение осталось только в окне и пропадёт при следующем запуске.",
                StatusSeverity.Error));
        };
    }

    private static async Task AskAboutAdministratorAsync(ServiceProvider services)
    {
        var isElevated = AdminElevation.IsElevated;
        var scan = services.GetRequiredService<ScanViewModel>();
        var shell = services.GetRequiredService<ShellPreferences>();
        var welcomePending = scan.FirstRun.IsVisible;

        Log.Information(isElevated ? "Приложение запущено от имени администратора" : "Приложение запущено без прав администратора");

        switch (AdminStartupPrompt.Decide(isElevated, welcomePending, shell.WarnIfNotAdministrator))
        {
            case StartupAdminAction.WelcomeCard:
                Log.Information("Первый запуск: вместо вопроса о правах администратора показана карточка первого запуска");
                break;

            case StartupAdminAction.None when !isElevated:
                Log.Information("Предупреждение о запуске без прав администратора отключено в настройках");
                break;

            default:
                break;
        }

        try
        {
            var prompt = new AdminStartupPrompt(services.GetRequiredService<IDialogService>(), shell, scan.RestartPrompt.RunAsync);
            await prompt.RunAsync(isElevated, welcomePending);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Вопрос о правах администратора не показан");
        }
    }

    internal static ServiceProvider ConfigureServices(ISettingsStore settings, KeepShellLogging logging)
    {
        var services = new ServiceCollection();

        services.AddSingleton(settings);
        services.AddKeepShellLogging(logging);

        services.AddSingleton<DiskSpaceCalculator>();
        services.AddSingleton<MftScanner>();
        services.AddSingleton<ScanRunner>();

        services.AddSingleton<DuplicateFinder>();
        services.AddSingleton<DockerService>();
        services.AddSingleton<ArchiveService>();
        services.AddSingleton<CleanupService>();
        services.AddSingleton<CompareDirectoriesUseCase>();
        services.AddSingleton<ExecuteSyncUseCase>();

        services.AddKeepShell();
        services.AddKeepShellToasts();
        services.AddSingleton<ToastNotifier>();
        services.AddSingleton<ShellPreferences>();
        services.AddSingleton<OperationPreferences>();
        services.AddSingleton<ScanPreferences>();
        services.AddSingleton<UpdatePreferences>();
        services.AddSingleton<ThemeViewModel>();
        services.AddSingleton<SystemTheme>();

        services.AddSingleton(new ErrorReportOptions
        {
            IssueRepo = () => AppInfo.RepoSlug,
            LogFileGlobs = [AppInfo.LogFileGlob],
            SessionStartMarker = AppInfo.SessionStartMarker,
        });

        services.AddSingleton<ErrorReportService>();

        services.AddKeepShellDiagnostics(new()
        {
            AppName = AppInfo.Name,
            AppVersion = AppInfo.Version,
            DataDirectory = AppStorage.DataDirectory,
            PortableStorage = !AppStorage.UseAppData,
            SampleInterval = TimeSpan.FromMilliseconds(AppDefaults.PerformanceSampleIntervalMs),
            WindowSamples = AppDefaults.PerformanceWindowSamples,
            HistorySamples = AppDefaults.PerformanceHistorySamples,
            HistoryPointsMax = AppDefaults.PerformanceHistoryPointsMax,
            HitchThresholdMs = AppDefaults.PerformanceHitchMs,
            HitchLogInterval = TimeSpan.FromSeconds(AppDefaults.PerformanceHitchLogIntervalSeconds),
            HitchRowsMax = AppDefaults.PerformanceHitchRowsMax,
            BundleHitchRows = AppDefaults.DiagnosticsHitchRows,
            FrameSlowMs = AppDefaults.PerformanceFrameSlowMs,
            FrameGapMs = AppDefaults.PerformanceFrameGapMs,
            ChartRefresh = TimeSpan.FromMilliseconds(AppDefaults.PerformanceChartRefreshMs),
            ChartWindow = AppDefaults.PerformanceChartWindowDefault,
            ChartHeight = AppDefaults.PerformanceChartPageHeight,
            ChartDotLimit = AppDefaults.PerformanceChartDotLimit,
            BundleLogFiles = AppDefaults.DiagnosticsLogFiles,
            BundleLogTailLines = AppDefaults.DiagnosticsLogTailLines,
            BundleNameAttempts = AppDefaults.DiagnosticsNameAttempts,
        });

        services.AddSingleton<PerformanceOperations>();
        services.AddSingleton<PerformanceHudViewModel>();
        services.AddSingleton<ScanOperationsCard>();
        services.AddSingleton<IDiagnosticsCard>(static provider => provider.GetRequiredService<ScanOperationsCard>());
        services.AddSingleton<IDiagnosticsBundleSource>(static provider => new SpaceSnoopBundleSource(
            provider.GetRequiredService<ISettingsStore>(),
            provider.GetRequiredService<PerformanceOperations>(),
            Path.Combine(AppStorage.DataDirectory, TomlSettingsFile.PrimaryFileName)));
        services.AddSingleton<IDiagnosticsSecretSource, SpaceSnoopSecretSource>();

        services.AddSingleton<ScanInspectorViewModel>();
        services.AddSingleton<ScanNodeFactory>();
        services.AddSingleton<DeleteProgressDialogFactory>();
        services.AddSingleton<ArchiveProgressDialogFactory>();
        services.AddSingleton<CleanupProgressDialogFactory>();

        services.AddSingleton<DuplicateProgressDialogFactory>();

        services.AddSingleton<AppNavigator>();
        services.AddSingleton<IAppNavigator>(static provider => provider.GetRequiredService<AppNavigator>());

        services.AddSingleton<ScanViewModel>();
        services.AddSingleton<IScanAutomation>(static provider => provider.GetRequiredService<ScanViewModel>());
        services.AddSingleton<SyncViewModel>();
        services.AddSingleton<ISyncAutomation>(static provider => provider.GetRequiredService<SyncViewModel>());
        services.AddSingleton<OverviewViewModel>();
        services.AddSingleton<IScheduleRunner, ScheduleRunner>();
        services.AddSingleton<ScheduleViewModel>();
        services.AddSingleton<DockerViewModel>();
        services.AddSingleton<CleanupViewModel>();
        services.AddSingleton<ICleanupAutomation>(static provider => provider.GetRequiredService<CleanupViewModel>());
        services.AddSingleton<CleanupPageViewModel>();
        services.AddSingleton<ChatViewModel>();
        services.AddSingleton<LogsViewModel>();
        services.AddSingleton<AboutViewModel>();
        services.AddSingleton<SettingsViewModel>();

        services.AddSingleton<AppUpdateViewModel>();
        services.AddSingleton<ShellViewModel>();
        services.AddSingleton<MainWindow>();

        services.AddSingleton<AgentPreferences>();
        services.AddSingleton<AgentModelSelector>();
        services.AddSingleton(provider => new ChatHistoryStore(provider.GetRequiredService<ILogger<ChatHistoryStore>>()));
        services.AddSingleton(provider => new AgentTranscriptStore(
            provider.GetRequiredService<AgentPreferences>(),
            provider.GetRequiredService<ILogger<AgentTranscriptStore>>()));
        services.AddSingleton<ClaudeAgentBackend>();
        services.AddSingleton<CodexAgentBackend>();
        services.AddSingleton<OpenCodeAgentBackend>();
        services.AddSingleton<AgentBackends>();

        services.AddSingleton<McpPreferences>();
        services.AddSingleton<McpBridge>();
        services.AddSingleton<McpServerHost>();

        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });
    }

    private void RunGallery(IEnumerable<string> args)
    {
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        AppThemes.Register();

        var directory = Path.Combine(AppStorage.DataDirectory, GalleryRunner.FolderName);
        var options = GalleryOptions.Parse(args, directory);

        _ = Dispatcher.InvokeAsync(async () =>
        {
            var exitCode = 1;

            try
            {
                var fixture = GalleryFixtures.Create();
                var settingsPath = Path.Combine(fixture.Root, TomlSettingsFile.PrimaryFileName);
                ISettingsStore settings = new SettingsStore(settingsPath);
                _settings = settings;
                SyncProfileStore.Save(settings, GalleryFixtures.Profiles(fixture));

                ThemeManager.Apply(AppThemes.LightKey);
                FontScaleManager.Initialize(options.Arguments.FontScale);
                ViewLocator.InstallIntoApplication();

                _services = ConfigureServices(settings, _logging!);

                var logger = _logging!.CreateLogger<App>();
                var host = GalleryHost.Create(_services, fixture, options, logger);

                exitCode = await GalleryRunner.RunAsync(host, options.Arguments, new GalleryJournal(logger));
            }
            catch (Exception ex)
            {
                Log.Fatal(ex.Unwrap(), "Галерея не отрисована");
            }

            Shutdown(exitCode);
        });
    }

    private void RunHeadlessSync(string? profileId)
    {
        var exitCode = 1;

        try
        {
            var settingsPath = Path.Combine(AppStorage.DataDirectory, TomlSettingsFile.PrimaryFileName);
            ISettingsStore settings = new SettingsStore(settingsPath);
            _settings = settings;
            exitCode = HeadlessSync.Run(settings, _logging!, profileId);
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Автосинхронизация не смогла запуститься");
        }

        Shutdown(exitCode);
    }

    private void AttachExceptionHandlers(string logsDirectory)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Log.Fatal(args.ExceptionObject as Exception, "Необработанное исключение домена");

        DispatcherUnhandledException += (_, args) =>
        {
            Log.Error(args.Exception, "Необработанное исключение UI-потока");
            StyledMessageBox.Show(UnexpectedErrorNote.Describe(logsDirectory),
                "Ошибка",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            args.Handled = true;
        };
    }
}
