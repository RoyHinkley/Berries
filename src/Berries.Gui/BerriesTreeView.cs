using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace Berries.Gui;

/// <summary>
/// TreeView whose selection state is controlled by Berries rather than by native
/// TreeView pointer or keyboard selection. SelectedItems is used only to portray
/// the persistent semantic file selection.
/// </summary>
public sealed class BerriesTreeView : TreeView
{
    protected override bool ShouldTriggerSelection(Visual selectable, PointerEventArgs eventArgs) => false;

    protected override bool ShouldTriggerSelection(Visual selectable, KeyEventArgs eventArgs) => false;
}
