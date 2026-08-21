using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace Berries.Gui;

internal sealed class SprinkledDuplicateDialog : Window
{
    private readonly List<CheckBox> checkBoxes = [];

    public SprinkledDuplicateDialog(IReadOnlyList<SprinkledDuplicateCandidate> candidates)
    {
        Title = "Potentially intentional distributed duplicates";
        Width = 700;
        Height = 650;
        MinWidth = 500;
        MinHeight = 400;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var root = new Grid
        {
            Margin = new Thickness(20),
            RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto")
        };

        var heading = new TextBlock
        {
            Text = "These identical files occur once in each of several directories.",
            FontSize = 18,
            FontWeight = Avalonia.Media.FontWeight.SemiBold
        };
        root.Children.Add(heading);

        var explanation = new TextBlock
        {
            Margin = new Thickness(0, 8, 0, 12),
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            Text = "Check each file whose copies are intentionally distributed and should all be retained. " +
                   "Checked duplicate sets will be treated as settled before directory and scope analysis."
        };
        Grid.SetRow(explanation, 1);
        root.Children.Add(explanation);

        var list = new StackPanel { Spacing = 6 };
        foreach (var candidate in candidates)
        {
            var checkBox = new CheckBox
            {
                Content = $"{candidate.FileName}    ({candidate.DirectoryCount:N0} folders)",
                Tag = candidate
            };
            checkBoxes.Add(checkBox);
            list.Children.Add(checkBox);
        }

        var scroll = new ScrollViewer
        {
            Content = list,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
        };
        Grid.SetRow(scroll, 2);
        root.Children.Add(scroll);

        var buttons = new StackPanel
        {
            Margin = new Thickness(0, 16, 0, 0),
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8
        };
        Grid.SetRow(buttons, 3);

        var continueButton = new Button { Content = "Continue", IsDefault = true };
        continueButton.Click += (_, _) => Close(
            checkBoxes
                .Where(checkBox => checkBox.IsChecked == true)
                .Select(checkBox => (SprinkledDuplicateCandidate)checkBox.Tag!)
                .ToArray());

        var cancelButton = new Button { Content = "Cancel", IsCancel = true };
        cancelButton.Click += (_, _) => Close(null);

        buttons.Children.Add(cancelButton);
        buttons.Children.Add(continueButton);
        root.Children.Add(buttons);

        Content = root;
    }
}
