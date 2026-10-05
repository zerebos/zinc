using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;

namespace Zinc;

/// <summary>An item built from a panel (a sidebar workspace, a pane's tab) that screen readers should see and pick.</summary>
internal interface ISelectableItem
{
    bool IsSelectedItem { get; }

    void SelectItem();
}

/// <summary>
/// Makes a panel-based item a list or tab item to UI Automation: panels have no automation peer, so screen readers
/// skipped them, and the name (title and what its dot means) set on them went nowhere. Selecting it does what a click
/// does. The name is the element's AutomationProperties.Name.
/// </summary>
internal sealed partial class SelectableItemPeer(FrameworkElement owner, AutomationControlType type)
    : FrameworkElementAutomationPeer(owner), ISelectionItemProvider
{
    protected override AutomationControlType GetAutomationControlTypeCore() => type;

    protected override string GetClassNameCore() => Owner.GetType().Name;

    protected override bool IsControlElementCore() => true;

    protected override bool IsContentElementCore() => true;

    protected override object? GetPatternCore(PatternInterface pattern) =>
        pattern == PatternInterface.SelectionItem ? this : base.GetPatternCore(pattern);

    public bool IsSelected => ((ISelectableItem)Owner).IsSelectedItem;

    public IRawElementProviderSimple? SelectionContainer => null;

    public void Select() => ((ISelectableItem)Owner).SelectItem();

    public void AddToSelection() => Select();

    public void RemoveFromSelection()
    {
    }
}
