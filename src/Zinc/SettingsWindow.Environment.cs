using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Zinc;

// Environment variables (`[environment]` for every program, `[profile.x.environment]` for one profile's) get a card
// that lists them, each with a value editable in place and a remove button, plus a row to add one. In a profile, the
// card also lists the ones every profile gets, as inherited, until the profile sets its own.
internal sealed partial class SettingsWindow
{
    private Border EnvironmentCard(string? profile)
    {
        IReadOnlyList<string> Path(string name) => profile == null ? ["environment", name] : ["profile", profile, "environment", name];

        // The variables config.toml sets at one level, which is every profile's (null) or one profile's own.
        Dictionary<string, string> Own(string? of)
        {
            IReadOnlyDictionary<string, object> values = of == null ? _config.GlobalValues
                : _config.ProfileValues.TryGetValue(of, out var profileValues) ? profileValues : new Dictionary<string, object>();
            return values.Where(v => v.Key.StartsWith("environment.", StringComparison.Ordinal) && v.Value is string)
                .ToDictionary(v => v.Key["environment.".Length..], v => (string)v.Value, StringComparer.OrdinalIgnoreCase);
        }

        var rows = new StackPanel { Spacing = 6, Margin = new Thickness(0, 8, 0, 0) };
        var error = ErrorText();

        Grid Row(FrameworkElement name, FrameworkElement value, FrameworkElement? action)
        {
            var row = new Grid
            {
                ColumnSpacing = 8,
                ColumnDefinitions = { new() { Width = new GridLength(200) }, new() { Width = new GridLength(1, GridUnitType.Star) }, new() { Width = new GridLength(40) } },
                Children = { name, value },
            };
            Grid.SetColumn(value, 1);
            if (action != null)
            {
                Grid.SetColumn(action, 2);
                row.Children.Add(action);
            }

            return row;
        }

        TextBlock NameText(string name, bool inherited) => new()
        {
            Text = name,
            FontFamily = new FontFamily("Cascadia Mono, Consolas"),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Opacity = inherited ? 0.6 : 1,
        };

        Button RemoveButton(string name)
        {
            var remove = new Button
            {
                Content = new FontIcon { Glyph = "\uE711", FontSize = 12 }, // ×
                Padding = new Thickness(8),
                VerticalAlignment = VerticalAlignment.Center,
                Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                BorderThickness = new Thickness(0),
            };
            ToolTipService.SetToolTip(remove, "Remove");
            AutomationProperties.SetName(remove, $"Remove {name}");
            remove.Click += (_, _) => Write(Path(name), null);
            return remove;
        }

        void Refresh()
        {
            rows.Children.Clear();
            var own = Own(profile);
            var inherited = profile == null ? [] : Own(null);
            foreach (var (name, value) in own.OrderBy(v => v.Key, StringComparer.OrdinalIgnoreCase))
            {
                var box = new TextBox { Text = value, PlaceholderText = "Empty removes the variable for these programs", FontFamily = new FontFamily("Cascadia Mono, Consolas") };
                AutomationProperties.SetName(box, $"Value of {name}");
                void Commit()
                {
                    if (box.Text != value)
                    {
                        Write(Path(name), box.Text);
                    }
                }

                box.LostFocus += (_, _) => Commit();
                box.KeyDown += (_, e) =>
                {
                    if (e.Key == Windows.System.VirtualKey.Enter)
                    {
                        Commit();
                        e.Handled = true;
                    }
                };
                rows.Children.Add(Row(NameText(name, false), box, RemoveButton(name)));
            }

            foreach (var (name, value) in inherited.Where(v => !own.ContainsKey(v.Key)).OrderBy(v => v.Key, StringComparer.OrdinalIgnoreCase))
            {
                var shown = new TextBlock
                {
                    Text = value.Length == 0 ? "(removed)" : value,
                    FontFamily = new FontFamily("Cascadia Mono, Consolas"),
                    Opacity = 0.6,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                };
                ToolTipService.SetToolTip(shown, "Every profile gets this variable from Terminal > Shell. Add the variable here to change it for this profile.");
                rows.Children.Add(Row(NameText(name, true), shown, null));
            }
        }

        // The row that adds a variable writes its name and value together.
        var newName = new TextBox { PlaceholderText = "Name", FontFamily = new FontFamily("Cascadia Mono, Consolas") };
        var newValue = new TextBox { PlaceholderText = "Value (%PATH% and the like work)", FontFamily = new FontFamily("Cascadia Mono, Consolas") };
        AutomationProperties.SetName(newName, "New variable's name");
        AutomationProperties.SetName(newValue, "New variable's value");
        var add = new Button { Content = new FontIcon { Glyph = "\uE710", FontSize = 12 }, Padding = new Thickness(8), VerticalAlignment = VerticalAlignment.Center };
        ToolTipService.SetToolTip(add, "Add");
        AutomationProperties.SetName(add, "Add variable");

        void Add()
        {
            var name = newName.Text.Trim();
            if (name.Length == 0 || name.Contains('=') || name.Any(char.IsWhiteSpace))
            {
                Show(error, "A variable's name can't be empty or have spaces or =.");
                return;
            }

            error.Visibility = Visibility.Collapsed;
            Write(Path(name), newValue.Text);
            newName.Text = newValue.Text = "";
        }

        add.Click += (_, _) => Add();
        foreach (var box in new[] { newName, newValue })
        {
            box.KeyDown += (_, e) =>
            {
                if (e.Key == Windows.System.VirtualKey.Enter)
                {
                    Add();
                    e.Handled = true;
                }
            };
        }

        var description = DescriptionText(profile == null
            ? "Zinc sets these for every program it starts. %NAME% uses a variable's value, as in PATH = %PATH%;C:\\tools. An empty value removes a variable."
            : "Zinc sets these for this profile's programs, on top of every profile's. %NAME% uses a variable's value. An empty value removes a variable.");
        var text = new StackPanel
        {
            Spacing = 1,
            Children =
            {
                new TextBlock { Text = "Environment variables" },
                description,
                KeyText(profile == null ? "[environment]" : $"[profile.{profile}.environment]"),
                rows,
                Row(newName, newValue, add),
                error,
            },
        };
        ((Grid)text.Children[^2]).Margin = new Thickness(0, 6, 0, 0);
        _refreshers.Add(Refresh);
        Refresh();
        return CardBorder(text);
    }
}
