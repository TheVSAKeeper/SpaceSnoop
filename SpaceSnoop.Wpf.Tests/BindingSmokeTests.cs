using KeepShell.Bootstrap;
using KeepShell.Services.Platform;
using KeepShell.ViewModels;
using KeepShell.Testing;
using KeepShell.Views.Controls;
using Microsoft.Extensions.DependencyInjection;
using SpaceSnoop.Core;
using SpaceSnoop.Core.Duplicates;
using SpaceSnoop.Wpf.Bootstrap;
using SpaceSnoop.Wpf.Bootstrap.Gallery;
using SpaceSnoop.Wpf.Bootstrap.Platform;
using SpaceSnoop.Wpf.Bootstrap.Schedule;
using SpaceSnoop.Wpf.Bootstrap.Storage;
using SpaceSnoop.Wpf.ViewModels;
using SpaceSnoop.Wpf.ViewModels.Cleanup;
using SpaceSnoop.Wpf.ViewModels.Dialogs;
using SpaceSnoop.Wpf.ViewModels.Scan;
using SpaceSnoop.Wpf.ViewModels.Settings;
using SpaceSnoop.Wpf.ViewModels.Sync;
using SpaceSnoop.Wpf.Views;
using SpaceSnoop.Wpf.Views.Dialogs;
using SpaceSnoop.Wpf.Views.Scan;
using SpaceSnoop.Wpf.Views.Settings;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
[Apartment(ApartmentState.STA)]
[NonParallelizable]
public class BindingSmokeTests
{
    private const double SettingsCardsProbeHeight = 240;
    private const double SmallWindowWidth = 880;
    private const double SmallWindowHeight = 584;
    private const double FirstScreenWidth = 900;
    private const double FirstScreenHeight = 600;
    private const int RecentProbeCount = 6;
    private const int NarrowTextMinLength = 12;
    private const double NarrowTextWidth = 120;

    private BindingErrorSink _sink = null!;
    private ServiceProvider _services = null!;
    private KeepShellLogging _logging = null!;
    private VisualTestHost _host = null!;
    private MainWindow _window = null!;
    private ShellViewModel _shell = null!;
    private GalleryFixture _fixture = null!;

    public static IReadOnlyList<string> Pages => SectionKey.All;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        // Класс App брать нельзя: его OnStartup висит в очереди диспетчера и на первой же прокачке
        // запускает настоящее приложение – разбор в docs/ui-tests-window-focus.md.
        _ = TestApplication.Ensure(AppResources.Sources);

        _fixture = GalleryFixtures.Create();

        ISettingsStore settings = new MemorySettings();
        settings.SetBool(SettingsKeys.UpdateCheckOnStartup, false);
        settings.SetBool(SettingsKeys.ScanDuplicatesEnabled, true);
        SyncProfileStore.Save(settings, GalleryFixtures.Profiles(_fixture));

        AppThemes.Register();
        ThemeManager.Apply(AppThemes.LightKey);
        ViewLocator.InstallIntoApplication();

        _logging = KeepShellLogging.Bootstrap(new()
        {
            LogsDirectory = Path.Combine(_fixture.Root, "logs"),
            FileNamePrefix = AppInfo.LogFilePrefix,
        });

        _services = App.ConfigureServices(settings, _logging);
        _shell = _services.GetRequiredService<ShellViewModel>();
        _window = _services.GetRequiredService<MainWindow>();

        _host = VisualTestHost.Show(_window);

        VisualTestHost.Run(() => GalleryHost.ArrangeAsync(_services, _fixture));

        _sink = BindingErrorSink.Attach();
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        _sink?.Dispose();
        _host?.Dispose();
        _services?.Dispose();
        _logging?.Dispose();

        try
        {
            if (Directory.Exists(_fixture.Root))
            {
                Directory.Delete(_fixture.Root, true);
            }
        }
        catch (IOException exception)
        {
            TestContext.Out.WriteLine($"Не удалось удалить каталог галереи {_fixture.Root}: {exception.Message}");
        }
    }

    [SetUp]
    public void SetUp()
    {
        _sink.Clear();
    }

    [TestCaseSource(nameof(Pages))]
    public void Разметка_страницы_не_теряет_биндинги(string page)
    {
        Assert.That(_shell.TryNavigate(page), Is.True, $"Страница «{page}» не открылась.");

        Settle();

        Assert.That(_sink.Errors, Is.Empty, () => string.Join(Environment.NewLine, _sink.Errors));
    }

    [Test]
    public void Алиас_docker_открывает_Очистку_на_секции_Docker()
    {
        Assert.That(_shell.TryNavigate(SectionKey.Cleanup), Is.True, "Страница «Очистка» не открылась.");
        _services.GetRequiredService<CleanupPageViewModel>().IsDockerActive = false;

        Assert.That(_shell.TryNavigate(SectionKey.Docker), Is.True, "Алиас «docker» не открыл страницу.");

        Settle();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(_shell.CurrentSectionKey, Is.EqualTo(SectionKey.Cleanup));
            Assert.That(_services.GetRequiredService<CleanupPageViewModel>().IsDockerActive, Is.True);
            Assert.That(_sink.Errors, Is.Empty, () => string.Join(Environment.NewLine, _sink.Errors));
        }
    }

    [Test]
    public void Всплывающая_настройка_подсветки_не_теряет_биндинги()
    {
        Assert.That(_shell.TryNavigate(SectionKey.Scan), Is.True, "Страница «Сканирование» не открылась.");

        Settle();

        var toggle = ViewCapture.Find(_window, "HeatToggle") as ToggleButton;
        Assert.That(toggle, Is.Not.Null, "В разметке сканирования нет кнопки подсветки HeatToggle.");

        _sink.Clear();
        toggle.IsChecked = true;
        Settle();

        var popup = toggle.FindName("HeatPopup") as Popup;
        Assert.That(popup?.Child, Is.Not.Null, "Всплывающая панель подсветки не построила содержимое.");

        var content = (FrameworkElement)popup.Child;
        content.UpdateLayout();
        Settle();

        var slider = (Slider)toggle.FindName("HeatSlider");
        var intensity = BindingOperations.GetBindingExpression(slider, RangeBase.ValueProperty);

        toggle.IsChecked = false;

        Assert.Multiple(() =>
        {
            Assert.That(content.DataContext, Is.InstanceOf<ScanViewModel>(), "Всплывающая панель не унаследовала контекст страницы.");
            Assert.That(intensity?.Status, Is.EqualTo(BindingStatus.Active), "Ползунок подсветки не привязался к странице.");
            Assert.That(_sink.Errors, Is.Empty, () => string.Join(Environment.NewLine, _sink.Errors));
        });
    }

    [Test]
    public void Всплывающая_панель_синхронизации_не_теряет_биндинги()
    {
        Assert.That(_shell.TryNavigate(SectionKey.Sync), Is.True, "Страница «Синхронизация» не открылась.");

        Settle();

        var toggle = ViewCapture.Find(_window, "MoreToggle") as ToggleButton;
        Assert.That(toggle, Is.Not.Null, "В тулбаре синхронизации нет кнопки «Ещё».");

        _sink.Clear();
        toggle.IsChecked = true;
        Settle();

        var popup = toggle.FindName("MorePopup") as Popup;
        Assert.That(popup?.Child, Is.Not.Null, "Всплывающая панель «Ещё» не построила содержимое.");

        var content = (FrameworkElement)popup.Child;
        content.UpdateLayout();
        Settle();

        var mirror = (ToggleButton)toggle.FindName("MirrorButton");
        var checkedBinding = BindingOperations.GetBindingExpression(mirror, ToggleButton.IsCheckedProperty);
        var enabledBinding = BindingOperations.GetBindingExpression(mirror, UIElement.IsEnabledProperty);

        toggle.IsChecked = false;

        Assert.Multiple(() =>
        {
            Assert.That(content.DataContext, Is.InstanceOf<SyncViewModel>(), "Всплывающая панель не унаследовала контекст страницы.");
            Assert.That(checkedBinding?.Status, Is.EqualTo(BindingStatus.Active), "Тумблер зеркала не привязался к настройке.");
            Assert.That(enabledBinding?.Status, Is.EqualTo(BindingStatus.Active), "Доступность тумблера зеркала не привязалась к странице.");
            Assert.That(_sink.Errors, Is.Empty, () => string.Join(Environment.NewLine, _sink.Errors));
        });
    }

    [Test]
    public void Выбор_цели_сканирования_не_теряет_биндинги()
    {
        var page = _services.GetRequiredService<ScanViewModel>();
        var recent = _fixture.Root;

        Assert.That(_shell.TryNavigate(SectionKey.Scan), Is.True, "Страница «Сканирование» не открылась.");
        Settle();

        _sink.Clear();

        if (!page.Drives.HasDrive(recent))
        {
            page.Drives.AddDrive(recent);
        }

        page.IsPickerOpen = true;

        Settle();

        var targets = ViewCapture.Find(_window, "Targets") as ItemsControl;

        try
        {
            Assert.Multiple(() =>
            {
                Assert.That(page.ShowTargetPicker, Is.True, "Открытый чипом выбор цели не показан.");
                Assert.That(targets?.IsVisible, Is.True, "Плитки дисков не показаны.");
                Assert.That(targets?.Items.OfType<DriveItem>(), Is.Not.Empty, "Ни один том не попал в плитки.");
                Assert.That(page.Drives.RecentDirectories, Is.Not.Empty, "Каталог не попал в недавние.");
                Assert.That(_sink.Errors, Is.Empty, () => string.Join(Environment.NewLine, _sink.Errors));
            });

            SaveFrame(ViewCapture.Find(_window, "PickerBody"), "scan-target-picker");
        }
        finally
        {
            page.IsPickerOpen = false;
            page.Drives.RemoveDrive(recent);
            Settle();
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Кнопка_скана_в_окне_900_на_600_видна_без_прокрутки(bool firstRun)
    {
        var page = _services.GetRequiredService<ScanViewModel>();
        var recents = Directory.GetDirectories(_fixture.Root, "*", SearchOption.AllDirectories)
            .Prepend(_fixture.Root)
            .Where(path => !page.Drives.HasDrive(path))
            .Take(RecentProbeCount)
            .ToArray();
        var (width, height, welcome, elevated) = (_window.Width, _window.Height, page.FirstRun.IsVisible, page.FirstRun.IsElevated);

        try
        {
            _window.Width = FirstScreenWidth;
            _window.Height = FirstScreenHeight;

            Assert.That(_shell.TryNavigate(SectionKey.Scan), Is.True, "Страница «Сканирование» не открылась.");

            foreach (var path in recents)
            {
                page.Drives.AddDrive(path);
            }

            if (firstRun)
            {
                page.FirstRun.ShowForAutomation(isElevated: false);
            }

            page.IsPickerOpen = true;
            Settle();

            var view = Descendant<ScanView>(_window) ?? throw new InvalidOperationException("Страница скана не найдена.");
            var start = (Button)(ViewCapture.Find(_window, "StartButton") ?? throw new InvalidOperationException("Кнопка «Сканировать» не найдена."));
            var scroll = (ScrollViewer)(ViewCapture.Find(_window, "PickerScroll") ?? throw new InvalidOperationException("Прокрутка выбора цели не найдена."));
            var button = start.TransformToAncestor(_window).TransformBounds(new(start.RenderSize));
            var area = view.TransformToAncestor(_window).TransformBounds(new(view.RenderSize));

            using (Assert.EnterMultipleScope())
            {
                Assert.That(scroll.ScrollableHeight, Is.GreaterThan(0), "Выбор цели влез целиком – кейс ничего не проверяет.");
                Assert.That(area.Bottom, Is.LessThanOrEqualTo(_window.ActualHeight + 0.5), "Страница скана выше окна – край страницы не край видимой области.");
                Assert.That(start.IsVisible, Is.True, "Кнопка «Сканировать» не показана.");
                Assert.That(button.Top, Is.GreaterThanOrEqualTo(area.Top), "Кнопка «Сканировать» выше страницы.");
                Assert.That(button.Bottom, Is.LessThanOrEqualTo(area.Bottom + 0.5), $"Кнопка «Сканировать» уходит за нижний край страницы на {button.Bottom - area.Bottom:F0} px.");
            }
        }
        finally
        {
            page.IsPickerOpen = false;
            page.FirstRun.ShowForAutomation(elevated);
            page.FirstRun.IsVisible = welcome;

            foreach (var path in recents)
            {
                page.Drives.RemoveDrive(path);
            }

            _window.Width = width;
            _window.Height = height;
            Settle();
        }
    }

    [TestCase(false, TestName = "Выбор плитки тома через UI Automation меняет цель скана")]
    [TestCase(true, TestName = "Выбор недавнего каталога через UI Automation меняет цель скана")]
    public void Выбор_плитки_через_UI_Automation_меняет_цель_скана(bool recentTarget)
    {
        var page = _services.GetRequiredService<ScanViewModel>();
        var volume = page.Drives.Volumes[0].Path;
        var recent = _fixture.Root;
        var selected = page.SelectedDrive;

        Assume.That(page.Drives.HasDrive(recent), Is.False, "Каталог стенда уже в списке – убирать его после кейса нельзя.");

        try
        {
            Assert.That(_shell.TryNavigate(SectionKey.Scan), Is.True, "Страница «Сканирование» не открылась.");

            page.Drives.AddDrive(recent);
            var (from, to) = recentTarget ? (volume, recent) : (recent, volume);
            page.SelectedDrive = from;
            page.IsPickerOpen = true;
            Settle();

            var tile = Descendants<CommandRadioButton>(_window)
                           .FirstOrDefault(button => button.DataContext is DriveItem item && string.Equals(item.Path, to, StringComparison.OrdinalIgnoreCase))
                       ?? throw new InvalidOperationException($"Плитка {to} не найдена.");
            var peer = UIElementAutomationPeer.CreatePeerForElement(tile);

            ((ISelectionItemProvider)peer.GetPattern(PatternInterface.SelectionItem)).Select();
            Settle();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(page.SelectedDrive, Is.EqualTo(to).IgnoreCase);
                Assert.That(tile.IsChecked, Is.True);
            }
        }
        finally
        {
            page.IsPickerOpen = false;
            page.Drives.RemoveDrive(recent);
            page.SelectedDrive = selected;
            Settle();
        }
    }

    [Test]
    public void Панель_дубликатов_не_теряет_биндинги()
    {
        var report = FindDuplicates();
        var page = _services.GetRequiredService<ScanViewModel>();

        Assert.That(_shell.TryNavigate(SectionKey.Scan), Is.True, "Страница «Сканирование» не открылась.");
        Settle();

        _sink.Clear();
        page.Duplicates.Apply(report);
        page.ViewMode = ScanViewMode.Duplicates;
        Settle();

        var panel = ViewCapture.Find(_window, "Duplicates");

        try
        {
            Assert.Multiple(() =>
            {
                Assert.That(report.Groups, Has.Count.EqualTo(2), "Дерево-образец не дало двух групп дубликатов.");
                Assert.That(panel?.IsVisible, Is.True, "Панель дубликатов не показана в третьем режиме.");
                Assert.That(_sink.Errors, Is.Empty, () => string.Join(Environment.NewLine, _sink.Errors));
            });

            SaveFrame(panel, "scan-duplicates");
        }
        finally
        {
            page.ViewMode = ScanViewMode.Tree;
            page.Duplicates.Clear();
            Settle();
        }
    }

    private static DuplicateReport FindDuplicates()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "spacesnoop-duplicates", Guid.NewGuid().ToString("N")));
        var nested = root.CreateSubdirectory("Загрузки");

        var photo = new byte[96 * 1024];
        var archive = new byte[640 * 1024];
        Random.Shared.NextBytes(photo);
        Random.Shared.NextBytes(archive);

        File.WriteAllBytes(Path.Combine(root.FullName, "отпуск.jpg"), photo);
        File.WriteAllBytes(Path.Combine(root.FullName, "отпуск (1).jpg"), photo);
        File.WriteAllBytes(Path.Combine(nested.FullName, "отпуск - копия.jpg"), photo);
        File.WriteAllBytes(Path.Combine(root.FullName, "дистрибутив.zip"), archive);
        File.WriteAllBytes(Path.Combine(nested.FullName, "дистрибутив.zip"), archive);

        var tree = new DiskSpaceCalculator().Calculate(root);
        tree.FixAbsolutePath(root);

        return new DuplicateFinder().Find(tree, DuplicateOptions.Default, null, CancellationToken.None);
    }

    private static void SaveFrame(FrameworkElement? panel, string name)
    {
        if (panel is null)
        {
            return;
        }

        var directory = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "spacesnoop-frames"));
        var file = Path.Combine(directory.FullName, $"{name}.png");
        ViewCapture.Save(panel, file, AppDefaults.ViewCaptureScaleDefault);
        TestContext.Out.WriteLine($"Кадр «{name}»: {file}");
    }

    [Test]
    public void Возврат_на_настройки_сохраняет_фильтр_разделов()
    {
        Assert.That(_shell.TryNavigate(SectionKey.Settings), Is.True, "Страница «Настройки» не открылась.");
        Settle();

        var rail = Descendant<SettingsRail>(_window) ?? throw new InvalidOperationException("На странице настроек нет рейла разделов.");
        var sections = _services.GetRequiredService<SettingsViewModel>().Sections;

        try
        {
            rail.SetCurrentValue(SettingsRail.SearchTextProperty, "шнырь");
            Settle();

            Assert.That(_shell.TryNavigate(SectionKey.Scan), Is.True, "Страница «Сканирование» не открылась.");
            Settle();

            Assert.That(_shell.TryNavigate(SectionKey.Settings), Is.True, "Страница «Настройки» не открылась второй раз.");
            Settle();

            var reopened = Descendant<SettingsRail>(_window) ?? throw new InvalidOperationException("Вернувшаяся страница настроек осталась без рейла.");

            using (Assert.EnterMultipleScope())
            {
                Assert.That(reopened, Is.Not.SameAs(rail), "Страница вернулась тем же рейлом – регрессию проверять не на чем.");
                Assert.That(reopened.Sections, Is.SameAs(sections), "Разделы у страницы новые – терять фильтр было негде.");
                Assert.That(reopened.SearchText, Is.EqualTo("шнырь"), "Новый рейл затёр запрос, который держал список разделов.");
                Assert.That(sections.Items.Where(section => section.IsVisible).Select(section => section.Key), Is.EqualTo(new[] { "agent" }));
                Assert.That(_sink.Errors, Is.Empty, () => string.Join(Environment.NewLine, _sink.Errors));
            }
        }
        finally
        {
            (Descendant<SettingsRail>(_window))?.SetCurrentValue(SettingsRail.SearchTextProperty, string.Empty);
            Settle();
        }
    }

    [Test]
    public void Потерянный_путь_биндинга_виден_тесту()
    {
        var probe = new TextBlock { DataContext = _shell };
        probe.SetBinding(TextBlock.TextProperty, new Binding("НетТакогоСвойства"));

        Settle();

        Assert.That(_sink.Errors, Is.Not.Empty, "Сенсор ошибок биндинга молчит – остальные проверки этого набора ничего не значат.");
    }

    [Test]
    public void Кнопка_сброса_строки_настроек_видна_только_у_изменённого_значения()
    {
        Assert.That(_shell.TryNavigate(SectionKey.Settings), Is.True, "Страница «Настройки» не открылась.");
        Settle();

        var settings = _services.GetRequiredService<SettingsViewModel>();
        settings.Sections.SelectCommand.Execute(settings.Sections["scan"]);
        Settle();

        var scan = _services.GetRequiredService<ScanPreferences>();
        var button = RowResetButton("Сбросить: число параллельных потоков");

        Assert.That(button.Visibility, Is.EqualTo(Visibility.Hidden), "Нетронутая строка показывает кнопку сброса.");

        scan.MaxParallelism = 1;
        Settle();

        Assert.That(button.Visibility, Is.EqualTo(Visibility.Visible), "Изменённая строка осталась без кнопки сброса: биндинг ResetFields[…] не дошёл до команды.");

        button.Command.Execute(button.CommandParameter);
        Settle();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(scan.MaxParallelism, Is.EqualTo(scan.ParallelismCeiling), "Нажатие не вернуло настройку к заводскому значению.");
            Assert.That(button.Visibility, Is.EqualTo(Visibility.Hidden), "Вернувшаяся к умолчанию строка держит кнопку видимой.");
            Assert.That(button.ActualWidth, Is.GreaterThan(0), "Спрятанная кнопка отдала своё место – строка прыгает при возврате к умолчанию.");
            Assert.That(_sink.Errors, Is.Empty, () => string.Join(Environment.NewLine, _sink.Errors));
        }
    }

    [Test]
    public void Каждая_сбрасываемая_настройка_имеет_кнопку_в_своей_строке()
    {
        Assert.That(_shell.TryNavigate(SectionKey.Settings), Is.True, "Страница «Настройки» не открылась.");
        Settle();

        var names = Descendants<Button>(_window)
            .Select(AutomationProperties.GetName)
            .ToHashSet(StringComparer.Ordinal);

        var missing = _services.GetRequiredService<SettingsViewModel>().ResetFields.Values
            .Where(field => field.Key != SettingsKeys.AgentHistoryVisible)
            .Where(field => !names.Contains(field.AutomationName))
            .Select(field => field.Label)
            .ToArray();

        Assert.That(missing, Is.Empty, $"Настройка сбрасывается разделом, но кнопки в своей строке не получила: {string.Join(", ", missing)}. Заведите её в карточке или объявите исключением здесь.");
    }

    [Test]
    public void Каждый_подпункт_настроек_находит_свой_блок_в_карточке()
    {
        Assert.That(_shell.TryNavigate(SectionKey.Settings), Is.True, "Страница «Настройки» не открылась.");
        Settle();

        var sections = _services.GetRequiredService<SettingsViewModel>().Sections;
        var restore = sections.SelectedPath;
        var missing = new List<string>();

        foreach (var section in sections.Items.Where(section => section.Children.Count > 0))
        {
            sections.SelectCommand.Execute(section);
            Settle();

            var anchors = Descendants<FrameworkElement>(_window)
                .Where(element => element.IsVisible)
                .Select(SettingsBlock.GetKey)
                .Where(key => key is { Length: > 0 })
                .ToHashSet(StringComparer.Ordinal);

            missing.AddRange(section.Children
                .Where(child => !anchors.Contains(child.Key))
                .Select(child => $"{section.Title} – {child.Title}"));
        }

        sections.Restore(restore);
        Settle();

        Assert.That(missing, Is.Empty, $"Подпункт рейла не нашёл своего блока в карточке, поэтому нажатие на него никуда не прокрутит: {string.Join(", ", missing)}. Пометьте блок через SettingsBlock.Key или уберите подпункт.");
    }

    [Test]
    public void Прокрутка_настроек_держит_подсветку_на_краях_карточки()
    {
        Assert.That(_shell.TryNavigate(SectionKey.Settings), Is.True, "Страница «Настройки» не открылась.");
        Settle();

        var sections = _services.GetRequiredService<SettingsViewModel>().Sections;
        var restore = sections.SelectedPath;
        var cards = Descendants<ScrollViewer>(_window).FirstOrDefault(viewer => viewer.Name == "Cards");

        Assert.That(cards, Is.Not.Null, "Прокручиваемая карточка настроек не найдена.");

        try
        {
            cards.MaxHeight = SettingsCardsProbeHeight;

            var section = sections.Items.FirstOrDefault(item => item.Children.Count > 1 && Scrollable(sections, item, cards));

            Assert.That(section, Is.Not.Null, "Ни одна карточка с подпунктами не прокручивается на этом окне – сценарий краёв невыразим.");

            var last = section.Children[^1];
            sections.SelectChildCommand.Execute(last);
            Settle();

            Assert.That(sections.SelectedChild, Is.SameAs(last),
                $"Клик по последнему подпункту «{last.Title}» подсветил «{sections.SelectedChild?.Title}»: прокрутку зажало по ScrollableHeight.");

            cards.ScrollToTop();
            Settle();

            Assert.That(sections.SelectedChild, Is.SameAs(section.Children[0]),
                $"Прокрутка в самый верх оставила подсвеченным «{sections.SelectedChild?.Title}» вместо первого подпункта.");
        }
        finally
        {
            cards.MaxHeight = double.PositiveInfinity;
            sections.Restore(restore);
            Settle();
        }
    }

    [Test]
    public void Кнопка_строки_настроек_сбрасывает_свою_настройку_а_не_соседнюю()
    {
        Assert.That(_shell.TryNavigate(SectionKey.Settings), Is.True, "Страница «Настройки» не открылась.");
        Settle();

        var buttons = RowResetButtons();

        Assert.That(buttons, Is.Not.Empty, "На странице настроек не нашлось ни одной кнопки сброса строки.");

        foreach (var button in buttons)
        {
            var box = RowCheckBox(button);

            if (box is null)
            {
                continue;
            }

            var before = buttons.ToDictionary(candidate => candidate, CanReset);

            box.SetCurrentValue(ToggleButton.IsCheckedProperty, !(box.IsChecked ?? false));
            Settle();

            var flipped = buttons.Where(candidate => CanReset(candidate) != before[candidate]).ToArray();

            box.SetCurrentValue(ToggleButton.IsCheckedProperty, !(box.IsChecked ?? false));
            Settle();

            Assert.That(flipped, Is.EqualTo(new[] { button }),
                $"Правка строки «{box.Content}» отозвалась не на своей кнопке: {string.Join(", ", flipped.Select(AutomationProperties.GetName))}. Кнопка привязана к чужой настройке.");
        }
    }

    [Test]
    public void Правка_настройки_мимо_страницы_поднимает_кнопку_сброса_при_возврате()
    {
        var store = _services.GetRequiredService<ISettingsStore>();

        Assert.That(_shell.TryNavigate(SectionKey.Settings), Is.True, "Страница «Настройки» не открылась.");
        Settle();

        var settings = _services.GetRequiredService<SettingsViewModel>();
        settings.Sections.SelectCommand.Execute(settings.Sections["sync"]);
        Settle();

        Assert.That(RowResetButton("Сбросить: git-папки").Visibility, Is.EqualTo(Visibility.Hidden), "Политика git-папок не на умолчании – проверять нечего.");

        try
        {
            store.SetEnum(SettingsKeys.SyncGitFolders, GitFolderPromptChoice.Skip);

            Assert.That(_shell.TryNavigate(SectionKey.Scan), Is.True, "Страница «Сканирование» не открылась.");
            Settle();

            Assert.That(_shell.TryNavigate(SectionKey.Settings), Is.True, "Страница «Настройки» не открылась второй раз.");
            Settle();

            Assert.That(RowResetButton("Сбросить: git-папки").Visibility, Is.EqualTo(Visibility.Visible),
                "Настройку правит и страница синхронизации, мимо холдеров; вернувшаяся страница настроек не пересчитала доступность сброса.");
        }
        finally
        {
            store.SetEnum(SettingsKeys.SyncGitFolders, AppDefaults.SyncGitFoldersDefault);
            (_window.DataContext as ShellViewModel)?.ToString();
        }
    }

    [Test]
    public void Настройки_при_крупном_шрифте_в_маленьком_окне_читаются_и_не_уходят_за_край()
    {
        var shell = _services.GetRequiredService<ShellPreferences>();
        var sections = _services.GetRequiredService<SettingsViewModel>().Sections;
        var (width, height, scale, restore) = (_window.Width, _window.Height, shell.FontScale, sections.SelectedPath);

        try
        {
            _window.Width = SmallWindowWidth;
            _window.Height = SmallWindowHeight;
            shell.FontScale = FontScaleManager.MaxScale;

            Assert.That(_shell.TryNavigate(SectionKey.Settings), Is.True, "Страница «Настройки» не открылась.");
            Settle();

            var cards = Descendants<ScrollViewer>(_window).First(viewer => viewer.Name == "Cards");
            var host = (FrameworkElement)cards.Content;
            var problems = new List<string>();

            foreach (var section in sections.Items)
            {
                sections.SelectCommand.Execute(section);
                Settle();

                foreach (var element in Descendants<FrameworkElement>(host).Where(element => element.IsVisible && element is TextBlock or Control))
                {
                    var bounds = element.TransformToAncestor(host).TransformBounds(new(element.RenderSize));

                    if (element is TextBlock { Text.Length: >= NarrowTextMinLength } text && text.ActualWidth < NarrowTextWidth)
                    {
                        problems.Add($"{section.Title}: текст «{text.Text}» сжат до {text.ActualWidth:F0} px");
                    }
                    else if (element is Control && bounds.Right > host.ActualWidth + 1)
                    {
                        problems.Add($"{section.Title}: {element.GetType().Name} «{AutomationProperties.GetName(element)}» уходит за край на {bounds.Right - host.ActualWidth:F0} px");
                    }
                }
            }

            sections.SelectCommand.Execute(sections["appearance"]);
            Settle();

            var slider = Descendants<Slider>(cards).Single(candidate => candidate.IsVisible);
            var reset = RowResetButton("Сбросить: масштаб шрифта");
            reset.Command.Execute(reset.CommandParameter);
            Settle();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(problems, Is.Empty, () => string.Join(Environment.NewLine, problems));
                Assert.That(slider.ActualWidth, Is.GreaterThan(0), "Ползунок масштаба шрифта не отрисован.");
                Assert.That(shell.FontScale, Is.EqualTo(FontScaleManager.DefaultScale), "Кнопка строки не вернула масштаб к 100 %.");
            }
        }
        finally
        {
            shell.FontScale = scale;
            _window.Width = width;
            _window.Height = height;
            sections.Restore(restore);
            Settle();
        }
    }

    [Test]
    public void Плашка_прав_в_тесном_заголовке_видна_целиком_или_иконкой_и_заголовок_не_обрезан()
    {
        var shell = _services.GetRequiredService<ShellPreferences>();
        var (width, height, scale) = (_window.Width, _window.Height, shell.FontScale);
        var title = Descendants<TitleBarView>(_window).Single();
        var pill = (FrameworkElement)title.FindName("ElevationPill")!;
        var icon = (FrameworkElement)title.FindName("ElevationIcon")!;
        var slot = (FrameworkElement)title.FindName("ElevationSlot")!;

        try
        {
            _window.Width = SmallWindowWidth;
            _window.Height = SmallWindowHeight;
            shell.FontScale = FontScaleManager.MaxScale;
            Settle();

            var room = ((Grid)slot.Parent).ColumnDefinitions[Grid.GetColumn(slot)].ActualWidth - slot.Margin.Left;
            var fits = pill.DesiredSize.Width <= room;
            var shown = fits ? pill : icon;
            var right = shown.TransformToAncestor(title).Transform(new(shown.ActualWidth, 0)).X;
            var named = Descendants<FrameworkElement>(icon).Where(element => element.IsVisible && AutomationProperties.GetName(element).Length > 0).ToList();
            var titleSlot = (FrameworkElement)VisualTreeHelper.GetParent(title);

            title.Measure(new(title.ActualWidth, double.PositiveInfinity));

            using (Assert.EnterMultipleScope())
            {
                Assert.That(pill.IsVisible, Is.EqualTo(fits), $"Плашка шириной {pill.DesiredSize.Width:F0} при месте {room:F0}: показан не тот вариант.");
                Assert.That(icon.IsVisible, Is.EqualTo(!fits), "Иконка и полная плашка видны не взаимоисключающе.");
                Assert.That(right, Is.LessThanOrEqualTo(room + slot.TransformToAncestor(title).Transform(default).X + 0.5), "Видимый вариант плашки прав обрезан справа.");
                Assert.That(fits || named.Count > 0, Is.True, "У иконки режима запуска нет имени для диктора.");
                Assert.That(titleSlot.ActualHeight, Is.GreaterThanOrEqualTo(title.DesiredSize.Height - 0.5), "Заголовок окна при крупном шрифте режется снизу.");
            }
        }
        finally
        {
            shell.FontScale = scale;
            _window.Width = width;
            _window.Height = height;
            Settle();
        }
    }

    [TestCase(FirstScreenWidth, FirstScreenHeight, true, TestName = "Запрос прав при крупном шрифте в узком окне ставит исходы столбиком и не режет кнопки")]
    [TestCase(1280d, 800d, false, TestName = "Запрос прав в обычном окне держит исходы одним рядом справа")]
    public void Кнопки_запроса_прав(double width, double height, bool bigFont)
    {
        var shell = _services.GetRequiredService<ShellPreferences>();
        var modals = _services.GetRequiredService<ModalHostViewModel>();
        var (oldWidth, oldHeight, scale) = (_window.Width, _window.Height, shell.FontScale);

        try
        {
            _window.Width = width;
            _window.Height = height;
            shell.FontScale = bigFont ? FontScaleManager.MaxScale : FontScaleManager.DefaultScale;
            _ = modals.ShowAsync(AdminStartupPrompt.BuildConfirm());
            Settle();

            var view = Descendant<ConfirmDialogView>(_window) ?? throw new InvalidOperationException("Диалог подтверждения не показан.");
            var row = (FrameworkElement)view.FindName("ChoiceRow");
            var column = (FrameworkElement)view.FindName("ChoiceColumn");
            var buttons = Descendants<Button>(view).Where(button => button.IsVisible).ToList();
            var bounds = buttons.Select(button => button.TransformToAncestor(view).TransformBounds(new(button.RenderSize))).ToList();

            SaveFrame(view, bigFont ? "confirm-admin-column" : "confirm-admin-row");

            using (Assert.EnterMultipleScope())
            {
                Assert.That(buttons.Select(static button => button.Content), Is.EqualTo(new[] { "Не перезапускать", "Больше не спрашивать", "Перезапустить" }), "Порядок фокуса исходов изменился: первым должен идти отказ.");
                Assert.That(column.IsVisible, Is.EqualTo(bigFont), "Показан не тот вариант ряда исходов.");
                Assert.That(row.IsVisible, Is.EqualTo(!bigFont), "Ряд и столбик исходов видны не взаимоисключающе.");
                Assert.That(buttons.Where(static button => button.ActualWidth < button.DesiredSize.Width - 0.5).Select(static button => button.Content), Is.Empty, "Подпись кнопки режется.");
                Assert.That(bounds.Where(rect => rect.Left < -0.5 || rect.Right > view.ActualWidth + 0.5), Is.Empty, "Кнопка выходит за край диалога.");

                if (bigFont)
                {
                    Assert.That(bounds[0].Top, Is.GreaterThan(bounds[2].Top), "В столбике отказ должен стоять внизу, действие – сверху.");
                    Assert.That(bounds.Select(static rect => rect.Width).Distinct().Count(), Is.EqualTo(1), "Кнопки столбика не на всю ширину.");
                }
                else
                {
                    Assert.That(bounds.Select(static rect => Math.Round(rect.Top)).Distinct().Count(), Is.EqualTo(1), "Исходы в обычном окне не в один ряд.");
                }
            }
        }
        finally
        {
            modals.RequestCancel();
            shell.FontScale = scale;
            _window.Width = oldWidth;
            _window.Height = oldHeight;
            Settle();
        }
    }

    [TestCase(FirstScreenWidth, FirstScreenHeight, true, TestName = "Вопрос про git-папки при крупном шрифте в узком окне ставит кнопки столбиком на всю ширину")]
    [TestCase(1280d, 800d, false, TestName = "Вопрос про git-папки в обычном окне держит кнопки одним рядом справа")]
    public void Кнопки_вопроса_про_git_папки(double width, double height, bool bigFont)
    {
        var shell = _services.GetRequiredService<ShellPreferences>();
        var modals = _services.GetRequiredService<ModalHostViewModel>();
        var (oldWidth, oldHeight, scale) = (_window.Width, _window.Height, shell.FontScale);

        try
        {
            _window.Width = width;
            _window.Height = height;
            shell.FontScale = bigFont ? FontScaleManager.MaxScale : FontScaleManager.DefaultScale;
            _ = modals.ShowAsync(new GitFolderPromptViewModel(3));
            Settle();

            var view = Descendant<GitFolderPromptView>(_window) ?? throw new InvalidOperationException("Вопрос про git-папки не показан.");
            var column = (FrameworkElement)view.FindName("ChoiceColumn");
            var row = (FrameworkElement)view.FindName("ChoiceRow");
            var buttons = Descendants<Button>(view).Where(static button => button.IsVisible).ToList();
            var bounds = buttons.Select(button => button.TransformToAncestor(view).TransformBounds(new(button.RenderSize))).ToList();

            SaveFrame(view, bigFont ? "git-column" : "git-row");

            using (Assert.EnterMultipleScope())
            {
                Assert.That(buttons.Select(static button => button.Content), Is.EqualTo(new[] { "Синхронизировать .git", "Пропустить .git" }), "Порядок фокуса исходов изменился: первым должен идти отказ.");
                Assert.That(column.IsVisible, Is.EqualTo(bigFont), "Показан не тот вариант ряда кнопок.");
                Assert.That(row.IsVisible, Is.EqualTo(!bigFont), "Ряд и столбик кнопок видны не взаимоисключающе.");
                Assert.That(buttons.Where(static button => button.ActualWidth < button.DesiredSize.Width - button.Margin.Left - button.Margin.Right - 0.5).Select(static button => button.Content), Is.Empty, "Подпись кнопки режется.");
                Assert.That(bounds.Where(rect => rect.Left < -0.5 || rect.Right > view.ActualWidth + 0.5), Is.Empty, "Кнопка выходит за край диалога.");
                Assert.That(buttons.Count(static button => button.IsDefault), Is.EqualTo(1), "Enter подтверждает не одну кнопку.");
                Assert.That(buttons.Count(static button => button.IsCancel), Is.EqualTo(1), "Escape отвечает не одной кнопкой.");

                if (bigFont)
                {
                    Assert.That(bounds[0].Top, Is.GreaterThan(bounds[1].Top), "В столбике отказ должен стоять внизу, действие – сверху.");
                    Assert.That(bounds.Select(static rect => Math.Round(rect.Width)).Distinct().Count(), Is.EqualTo(1), "Кнопки столбика не на всю ширину.");
                }
                else
                {
                    Assert.That(bounds.Select(static rect => Math.Round(rect.Top)).Distinct().Count(), Is.EqualTo(1), "Кнопки в обычном окне не в один ряд.");
                    Assert.That(buttons[1].TransformToAncestor(row).TransformBounds(new(buttons[1].RenderSize)).Right, Is.EqualTo(row.ActualWidth).Within(1), "Ряд кнопок не прижат вправо.");
                }
            }
        }
        finally
        {
            modals.RequestCancel();
            shell.FontScale = scale;
            _window.Width = oldWidth;
            _window.Height = oldHeight;
            Settle();
        }
    }

    [TestCase(FirstScreenWidth, FirstScreenHeight, true, TestName = "Страница скана при крупном шрифте в окне 900 на 600 ужимает верхние блоки и показывает шесть строк дерева")]
    [TestCase(1280d, 800d, false, TestName = "Страница скана в обычном окне не ужимает верхние блоки")]
    public void Дерево_скана_держит_минимум_строк(double width, double height, bool bigFont)
    {
        var shell = _services.GetRequiredService<ShellPreferences>();
        var page = _services.GetRequiredService<ScanViewModel>();
        var (oldWidth, oldHeight, scale, collapsed) = (_window.Width, _window.Height, shell.FontScale, page.Inspector.IsInspectorCollapsed);
        var selected = page.SelectedNode;

        try
        {
            _window.Width = width;
            _window.Height = height;
            shell.FontScale = bigFont ? FontScaleManager.MaxScale : FontScaleManager.DefaultScale;

            Assert.That(_shell.TryNavigate(SectionKey.Scan), Is.True, "Страница «Сканирование» не открылась.");

            page.Tips.ShowForAutomation(0);
            page.Inspector.IsInspectorCollapsed = true;
            Settle();
            page.SelectedNode = page.Roots[0];
            page.Inspector.IsInspectorCollapsed = true;
            Settle();

            var view = Descendant<ScanView>(_window) ?? throw new InvalidOperationException("Страница скана не найдена.");
            var content = (FrameworkElement)view.FindName("ContentArea");
            var rail = Descendants<TextBlock>(view).Single(static text => text.Text == "Детали" && text.LayoutTransform is RotateTransform);
            var railBottom = rail.TransformToAncestor(content).TransformBounds(new(rail.RenderSize)).Bottom;
            var tipText = Descendants<TextBlock>(view).Single(text => text.Text == page.Tips.Text);
            var tree = Descendants<TreeView>(view).Single(static tree => tree.IsVisible);
            var viewport = Descendant<ScrollContentPresenter>(tree) ?? throw new InvalidOperationException("У дерева нет области прокрутки.");
            var row = Descendants<TreeViewItem>(tree).Select(static item => item.Template.FindName("SelectionBd", item)).OfType<FrameworkElement>().First(static bd => bd.ActualHeight > 0);
            var title = Descendants<TextBlock>(view).Single(static text => text.Text == "Структура каталога");
            var structure = Descendant<ScanStructurePanel>(view) ?? throw new InvalidOperationException("Панель структуры не найдена.");
            var railPanel = Descendant<ScanInspectorRail>(view) ?? throw new InvalidOperationException("Свёрнутая колонка деталей не найдена.");
            var railActions = (FrameworkElement)railPanel.FindName("RailActions");
            var moreButton = (Button)railPanel.FindName("MoreButton");
            var moreMenu = (ContextMenu)railPanel.FindName("MoreMenu");
            var railButtons = Descendants<Button>(railPanel).Where(static button => button.IsVisible).ToList();
            var railBounds = railButtons.Select(button => button.TransformToAncestor(content).TransformBounds(new(button.RenderSize))).ToList();
            var actionCount = Descendants<Button>(railActions).Count(static button => button.Visibility == Visibility.Visible);
            SaveFrame(view, bigFont ? "scan-compact" : "scan-regular");

            using (Assert.EnterMultipleScope())
            {
                Assert.That(page.ShowSummary && page.Tips.IsVisible, Is.True, "Над деревом нет итогов или совета – кейс ничего не проверяет.");
                Assert.That(view.IsCompact, Is.EqualTo(bigFont), "Верхние блоки ужаты не по месту.");
                Assert.That(Math.Floor(viewport.ActualHeight / row.ActualHeight), Is.GreaterThanOrEqualTo(ScanView.MinTreeRows), "В дереве видно меньше шести строк.");
                Assert.That(title.IsVisible, Is.EqualTo(!bigFont), "Заголовок панели в сжатом виде должен уходить.");
                Assert.That(tipText.IsVisible, Is.EqualTo(!bigFont), "Текст совета в сжатом виде должен прятаться до щелчка.");
                Assert.That(railBottom, Is.LessThanOrEqualTo(content.ActualHeight + 0.5), "Подпись свёрнутых деталей режется снизу.");
                Assert.That(tree.GetValue(AutomationProperties.NameProperty), Is.EqualTo("Структура каталога"), "Дерево без имени для экранного диктора.");
                Assert.That(structure.TreeRowCapacity() * row.ActualHeight, Is.EqualTo(viewport.ActualHeight).Within(1), "Запас строк дерева посчитан не по его настоящей высоте.");
                Assert.That(railBounds.Where(rect => rect.Bottom > content.ActualHeight + 0.5), Is.Empty, "Кнопка свёрнутых деталей режется снизу.");
                Assert.That(railActions.IsVisible || moreButton.IsVisible, Is.True, "Действия выбранного узла недоступны из свёрнутых деталей.");

                if (moreButton.IsVisible)
                {
                    moreButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                    Settle();
                    Assert.That(moreMenu.IsOpen, Is.True, "Кнопка «Ещё» не открыла меню действий.");
                    Assert.That(moreMenu.Items.OfType<MenuItem>().Count(static item => item.Visibility == Visibility.Visible), Is.EqualTo(actionCount), "В меню «Ещё» не все действия свёрнутых деталей.");
                    moreMenu.IsOpen = false;
                }
            }
        }
        finally
        {
            page.Tips.DismissCommand.Execute(null);
            page.SelectedNode = selected;
            page.Inspector.IsInspectorCollapsed = collapsed;
            shell.FontScale = scale;
            _window.Width = oldWidth;
            _window.Height = oldHeight;
            Settle();
        }
    }

    [TestCase("scan", "Сколько ставить")]
    [TestCase("archive", "Когда сжатие не помогает")]
    [TestCase("sync", "Что настройка не меняет")]
    public void Раскрытие_подробностей_не_сдвигает_контрол_строки(string section, string header)
    {
        var sections = _services.GetRequiredService<SettingsViewModel>().Sections;
        var restore = sections.SelectedPath;
        Expander? details = null;

        try
        {
            Assert.That(_shell.TryNavigate(SectionKey.Settings), Is.True, "Страница «Настройки» не открылась.");
            sections.SelectCommand.Execute(sections[section]);
            Settle();

            details = Descendants<Expander>(_window).Single(expander => expander.IsVisible && Equals(expander.Header, header));
            var row = Ancestor<SettingRowPanel>(details) ?? throw new InvalidOperationException($"Раскрытие «{header}» стоит не в строке формы.");
            var before = Tops(row);

            details.IsExpanded = true;
            Settle();

            Assert.That(Tops(row), Is.EqualTo(before), $"Раскрытие «{header}» сдвинуло контрол строки.");
        }
        finally
        {
            details?.SetCurrentValue(Expander.IsExpandedProperty, false);
            sections.Restore(restore);
            Settle();
        }

        static double[] Tops(SettingRowPanel row)
        {
            return [.. row.Children.Cast<FrameworkElement>().Skip(1).Select(child => Math.Round(child.TransformToAncestor(row).Transform(default).Y, 1))];
        }
    }

    private static T? Ancestor<T>(DependencyObject element)
        where T : DependencyObject
    {
        for (var current = VisualTreeHelper.GetParent(element); current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is T found)
            {
                return found;
            }
        }

        return null;
    }

    private bool Scrollable(SettingsSectionList sections, SettingsSection section, ScrollViewer cards)
    {
        sections.SelectCommand.Execute(section);
        Settle();

        return cards.ScrollableHeight > 0;
    }

    private static bool CanReset(Button button)
    {
        return button.Command?.CanExecute(null) ?? false;
    }

    private static CheckBox? RowCheckBox(Button button)
    {
        return VisualTreeHelper.GetParent(button) is DependencyObject row
            ? Descendants<CheckBox>(row).FirstOrDefault()
            : null;
    }

    private Button[] RowResetButtons()
    {
        return [.. Descendants<Button>(_window).Where(button => AutomationProperties.GetName(button).StartsWith("Сбросить: ", StringComparison.Ordinal))];
    }

    private Button RowResetButton(string automationName)
    {
        return Descendants<Button>(_window).FirstOrDefault(button => AutomationProperties.GetName(button) == automationName)
               ?? throw new InvalidOperationException($"Кнопка сброса строки «{automationName}» не найдена на странице настроек.");
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root)
        where T : DependencyObject
    {
        if (root is T match)
        {
            yield return match;
        }

        var count = VisualTreeHelper.GetChildrenCount(root);

        for (var index = 0; index < count; index++)
        {
            foreach (var found in Descendants<T>(VisualTreeHelper.GetChild(root, index)))
            {
                yield return found;
            }
        }
    }

    private static T? Descendant<T>(DependencyObject root)
        where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(root);

        for (var index = 0; index < count; index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);

            if (child is T found)
            {
                return found;
            }

            if (Descendant<T>(child) is { } nested)
            {
                return nested;
            }
        }

        return null;
    }

    private void Settle()
    {
        _host.Settle();
    }
}
