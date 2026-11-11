using KeepShell.Bootstrap;
using KeepShell.Services.Platform;
using KeepShell.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using SpaceSnoop.Wpf.Bootstrap;
using SpaceSnoop.Wpf.ViewModels.Docker;
using SpaceSnoop.Wpf.Views;
using SpaceSnoop.Wpf.Views.Docker;
using System.Runtime.CompilerServices;
using System.Windows;

namespace SpaceSnoop.Wpf.Tests;

public partial class BindingSmokeTests
{
    [TestCase(AppThemes.LightKey)]
    [TestCase(AppThemes.DarkKey)]
    public void Холодный_опрос_Docker_и_его_отмена_не_показывают_ошибку_доступности(string theme)
    {
        ThemeManager.Apply(theme);
        var runner = new DockerViewModelTests.PausedRunner();
        var vm = new DockerViewModel(
            DockerViewModelTests.IsolatedService(runner), new NoopDialogs(), new FakeUiDispatcher(), NullLogger<DockerViewModel>.Instance);
        var view = new DockerView { DataContext = vm, Margin = new(20) };
        var window = new Window { Content = view };
        window.SetResourceReference(Window.BackgroundProperty, "Bg.Base");
        using var sink = BindingErrorSink.Attach();
        using var host = VisualTestHost.Show(window, 900, 600);
        var unavailable = ((System.Windows.Controls.StackPanel)((System.Windows.Controls.ScrollViewer)view.Content).Content)
            .Children.OfType<DockerUnavailableView>().Single();
        try
        {
            VisualTestHost.Run(async () =>
            {
                var run = vm.EnsureLoadedAsync();
                try
                {
                    await runner.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
                    host.Settle();
                    Assert.That(vm.IsBusy, Is.True);
                    Assert.That(vm.CanRun, Is.False);
                    Assert.That(unavailable.IsVisible, Is.False);
                    SaveDockerFrame(window, $"cold-poll-{theme}");
                }
                finally
                {
                    vm.CancelCommand?.Execute(null);
                    await run;
                }
                host.Settle();
                Assert.That(vm.IsBusy, Is.False);
                Assert.That(vm.CanRun, Is.False);
                Assert.That(unavailable.IsVisible, Is.False);
                Assert.That(vm.StatusText, Does.Contain("остановлен"));
                SaveDockerFrame(window, $"cold-canceled-{theme}");
                runner.Completion.TrySetResult(new(-1, string.Empty, "Docker offline"));
                await vm.EnsureLoadedAsync();
                host.Settle();
                Assert.That(unavailable.IsVisible, Is.True);
                Assert.That(vm.CanRun, Is.False);
                SaveDockerFrame(window, $"failed-poll-{theme}");
            });
            Assert.That(sink.Errors, Is.Empty, () => string.Join(Environment.NewLine, sink.Errors));
        }
        finally
        {
            ThemeManager.Apply(AppThemes.LightKey);
        }
    }

    private static void SaveDockerFrame(FrameworkElement element, string name, [CallerFilePath] string caller = "")
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(caller)!, ".."));
        var directory = Path.Combine(root, ".kc", "scratch", "cleanup-second-review", "cold-docker");
        Directory.CreateDirectory(directory);
        ViewCapture.Save(element, Path.Combine(directory, name + ".png"));
    }
}
