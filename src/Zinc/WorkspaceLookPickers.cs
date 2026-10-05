using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Zinc.Panes;

namespace Zinc;

/// <summary>
/// The workspace icon and color choosers, shared by the new-workspace page and the workspace
/// menu: a grid of the curated icons (<see cref="WorkspaceIcons"/>) and a row of the colors.
/// </summary>
internal static class WorkspaceLookPickers
{
    public static Windows.UI.Color ToColor(uint rgb) => Windows.UI.Color.FromArgb(0xFF, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);

    /// <summary>"Profile's icon" (none of its own) and every icon; <paramref name="picked"/> gets the name (null: none).</summary>
    public static FrameworkElement Icons(string? current, Action<string?> picked)
    {
        var grid = new GridView
        {
            SelectionMode = ListViewSelectionMode.Single,
            IsItemClickEnabled = true,
            MaxWidth = 7 * 46,
        };
        AutomationProperties.SetName(grid, "Workspace icon");
        grid.Items.Add(Cell(null, new TextBlock { Text = "None", FontSize = 11, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center }, "Profile's icon"));
        foreach (var (name, glyph) in WorkspaceIcons.All)
        {
            grid.Items.Add(Cell(name, new FontIcon { Glyph = glyph.ToString(), FontSize = 18 }, name));
        }

        grid.SelectedItem = grid.Items.OfType<GridViewItem>().FirstOrDefault(i => (string?)i.Tag == current);
        // A click (also on the icon already chosen), or a selection by keyboard or automation.
        grid.ItemClick += (_, e) => picked((string?)((FrameworkElement)e.ClickedItem).Tag); // (the items are their own containers)
        grid.SelectionChanged += (_, _) =>
        {
            if (grid.SelectedItem is GridViewItem { } item)
            {
                picked((string?)item.Tag);
            }
        };
        return new StackPanel
        {
            Spacing = 8,
            Children = { new TextBlock { Text = "Icon", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold }, grid },
        };
    }

    private static GridViewItem Cell(string? name, UIElement content, string label)
    {
        var item = new GridViewItem { Tag = name, Width = 40, Height = 40, Content = content };
        AutomationProperties.SetName(item, label);
        ToolTipService.SetToolTip(item, label);
        return item;
    }

    /// <summary>
    /// A swatch per color and "No color", as a GridView like the icons (arrow keys move, screen readers hear the
    /// selection); <paramref name="picked"/> gets the color (null: none).
    /// </summary>
    public static FrameworkElement Colors(Windows.UI.Color? current, Action<Windows.UI.Color?> picked)
    {
        var grid = new GridView
        {
            SelectionMode = ListViewSelectionMode.Single,
            IsItemClickEnabled = true,
            MaxWidth = (WorkspaceColors.All.Count + 1) * 40,
        };
        AutomationProperties.SetName(grid, "Workspace color");
        grid.Items.Add(Swatch(null, "No color"));
        foreach (var (name, rgb) in WorkspaceColors.All)
        {
            grid.Items.Add(Swatch(ToColor(rgb), name));
        }

        grid.SelectedItem = grid.Items.OfType<GridViewItem>().FirstOrDefault(i => (Windows.UI.Color?)i.Tag == current);
        // A click (also on the color already chosen), or a selection by keyboard or automation.
        grid.ItemClick += (_, e) => picked((Windows.UI.Color?)((FrameworkElement)e.ClickedItem).Tag);
        grid.SelectionChanged += (_, _) =>
        {
            if (grid.SelectedItem is GridViewItem { } item)
            {
                picked((Windows.UI.Color?)item.Tag);
            }
        };
        return new StackPanel
        {
            Spacing = 8,
            Children = { new TextBlock { Text = "Color", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold }, grid },
        };
    }

    // A round swatch in a GridView cell (the cell's own selected border marks the chosen one).
    private static GridViewItem Swatch(Windows.UI.Color? color, string label)
    {
        var dot = new Border
        {
            Width = 22,
            Height = 22,
            CornerRadius = new CornerRadius(11),
            BorderThickness = new Thickness(1),
            BorderBrush = (Brush)Application.Current.Resources["ControlStrongStrokeColorDefaultBrush"],
            Background = color is { } c ? new SolidColorBrush(c) : new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            Child = color == null ? new FontIcon { Glyph = ((char)0xE711).ToString(), FontSize = 10 } : null,
        };
        var item = new GridViewItem { Tag = color, Width = 34, Height = 34, MinWidth = 0, MinHeight = 0, Content = dot };
        AutomationProperties.SetName(item, label);
        ToolTipService.SetToolTip(item, label);
        return item;
    }
}
