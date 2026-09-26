using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace Berries.Gui;

/// <summary>
/// TreeView whose semantic selection is controlled by Berries rather than by
/// Avalonia's native TreeView selection model.
/// </summary>
public sealed class BerriesTreeView : TreeView
{
    protected override Type StyleKeyOverride => typeof(TreeView);

    protected override bool ShouldTriggerSelection(Visual selectable, PointerEventArgs eventArgs) => false;

    protected override bool ShouldTriggerSelection(Visual selectable, KeyEventArgs eventArgs) => false;
}
