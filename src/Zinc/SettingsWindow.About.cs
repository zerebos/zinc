using System.Diagnostics;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Zinc;

/// <summary>About's list of the open-source software Zinc ships (<see cref="OpenSourceNotices"/>), each with its license.</summary>
internal sealed partial class SettingsWindow
{
    private void OpenSourceList()
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "Licenses");
        var header = new Grid { Margin = new Thickness(0, 18, 0, 0) };
        header.Children.Add(new TextBlock { Text = "Open-source software", FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        var open = new Button { Content = "Open folder", HorizontalAlignment = HorizontalAlignment.Right };
        AutomationProperties.SetName(open, "Open the licenses folder");
        open.Click += (_, _) => Reveal(Path.Combine(folder, "Ghostty.txt"));
        header.Children.Add(open);
        _page.Children.Add(header);
        _page.Children.Add(Note("Zinc is built with these projects and ships them. Select a license to read it."));

        var list = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
        var components = OpenSourceNotices.Components;
        for (var i = 0; i < components.Count; i++)
        {
            list.Children.Add(ComponentRow(components[i], first: i == 0));
        }

        _page.Children.Add(CardBorder(list));
    }

    private FrameworkElement ComponentRow(OpenSourceComponent component, bool first)
    {
        var secondary = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];
        var name = new TextBlock { TextWrapping = TextWrapping.Wrap };
        name.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = component.Name });
        if (component.Version.Length > 0)
        {
            name.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = "  " + component.Version, Foreground = secondary, FontSize = 12 });
        }

        var text = new StackPanel
        {
            Spacing = 2,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { name, new TextBlock { Text = component.Use, FontSize = 12, Foreground = secondary, TextWrapping = TextWrapping.Wrap } },
        };

        var license = new HyperlinkButton { Content = component.License, VerticalAlignment = VerticalAlignment.Center, Padding = new Thickness(6, 2, 6, 2) };
        AutomationProperties.SetName(license, $"{component.Name} license: {component.License}");
        license.Click += (_, _) => ShowLicense(component);

        var row = new Grid
        {
            ColumnSpacing = 12,
            Padding = new Thickness(0, 8, 0, 8),
            ColumnDefinitions = { new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }, new ColumnDefinition { Width = GridLength.Auto } },
        };
        row.Children.Add(text);
        Grid.SetColumn(license, 1);
        row.Children.Add(license);

        return first ? row : new Border
        {
            Child = row,
            BorderThickness = new Thickness(0, 1, 0, 0),
            BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
        };
    }

    private async void ShowLicense(OpenSourceComponent component)
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, component.LicenseFile);
            var text = File.Exists(path) ? await File.ReadAllTextAsync(path) : $"The license file is missing: {path}";
            var dialog = new ContentDialog
            {
                XamlRoot = Content.XamlRoot,
                Title = $"{component.Name}: {component.License}",
                Content = new ScrollViewer
                {
                    MaxHeight = 480,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    Content = new TextBlock
                    {
                        Text = text.TrimEnd(),
                        TextWrapping = TextWrapping.Wrap,
                        IsTextSelectionEnabled = true,
                        FontFamily = new FontFamily("Cascadia Mono, Consolas"),
                        FontSize = 12,
                    },
                },
                PrimaryButtonText = "Website",
                CloseButtonText = "Close",
                DefaultButton = ContentDialogButton.Close,
            };
            dialog.Resources["ContentDialogMaxWidth"] = 720.0;
            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                Process.Start(new ProcessStartInfo(component.Url) { UseShellExecute = true });
            }
        }
        catch (Exception ex)
        {
            Log.Crash("settings: license", ex);
        }
    }
}
