using CommunityToolkit.Mvvm.Input;
using SpaceSnoop.Wpf.Views;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls.Primitives;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
[Apartment(ApartmentState.STA)]
public class CommandButtonAutomationTests
{
    private static IEnumerable<TestCaseData> Patterns()
    {
        yield return new TestCaseData(
                new Func<ButtonBase>(static () => new CommandRadioButton()),
                new Action<AutomationPeer>(static peer => ((ISelectionItemProvider)peer.GetPattern(PatternInterface.SelectionItem)).Select()))
            .SetArgDisplayNames("плитка цели, SelectionItem.Select");

        yield return new TestCaseData(
                new Func<ButtonBase>(static () => new CommandToggleButton()),
                new Action<AutomationPeer>(static peer => ((IToggleProvider)peer.GetPattern(PatternInterface.Toggle)).Toggle()))
            .SetArgDisplayNames("пометка дубликата, Toggle.Toggle");
    }

    [TestCaseSource(nameof(Patterns))]
    public void Действие_через_UI_Automation_выполняет_команду_как_щелчок_мышью(Func<ButtonBase> create, Action<AutomationPeer> invoke)
    {
        var runs = 0;
        var button = create();
        button.Command = new RelayCommand(() => runs++);

        invoke(UIElementAutomationPeer.CreatePeerForElement(button));

        Assert.That(runs, Is.EqualTo(1));
    }
}
