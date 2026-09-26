using System.Reflection;
using Avalonia;
using Avalonia.Controls;

namespace Berries.Gui;

/// <summary>
/// Diagnostic virtualizing panel that prevents Avalonia's ScrollContentPresenter
/// from choosing realized rows as scroll anchors.
/// </summary>
public sealed class BerriesVirtualizingStackPanel : VirtualizingStackPanel
{
    private static readonly FieldInfo ScrollAnchorProviderField =
        typeof(VirtualizingStackPanel).GetField(
            "_scrollAnchorProvider",
            BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingFieldException(
            typeof(VirtualizingStackPanel).FullName,
            "_scrollAnchorProvider");

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        ScrollAnchorProviderField.SetValue(this, null);
    }
}
