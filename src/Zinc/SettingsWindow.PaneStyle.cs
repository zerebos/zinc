using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Zinc.Settings;

namespace Zinc;

/// <summary>
/// Layout > Panes' "Pane style". Choosing Compact or Cards sets the five pane settings at once (Core's
/// <see cref="PaneStyle"/>), and the fold under the style holds the settings themselves. When they match neither
/// style, the style reads Custom. Settings the app doesn't honor yet (<see cref="SettingDef.Unfinished"/>) stay out of
/// the window, and the style with them.
/// </summary>
internal sealed partial class SettingsWindow
{
    /// <summary>The catalog as this window shows it, which is without unfinished settings.</summary>
    private static IEnumerable<SettingDef> ShownSettings => SettingsCatalog.All.Where(Shown);

    private static bool Shown(SettingDef setting) => !setting.Unfinished || TestHooks.UnfinishedSettings;

    /// <summary>The fold <paramref name="key"/> is shown in, or null while any of that fold's settings is hidden.</summary>
    private static SettingFold? FoldFor(string key) =>
        SettingsCatalog.FoldOf(key) is { } fold && fold.Children.Append(fold.Lead ?? fold.Children[0]).All(k => Shown(SettingsCatalog.Find(k)!))
            ? fold
            : null;

    private static readonly (string Label, PaneStyleKind Style, string Detail)[] PaneStyles =
    [
        ("Compact", PaneStyleKind.Compact, "Panes edge to edge, with a header only on panes with several tabs."),
        ("Cards", PaneStyleKind.Cards, "Each pane a rounded card with a header, a gap between them, and a ring around the focused one."),
    ];

    /// <summary>What a style is, in one sentence (the welcome page's cards say the same).</summary>
    internal static string PaneStyleDetail(PaneStyleKind style) => PaneStyles.First(s => s.Style == style).Detail;

    /// <summary>The Pane style fold's header: the style in force, a dropdown that sets all five, and an arrow back to Compact.</summary>
    private FrameworkElement PaneStyleHeader()
    {
        var reset = ResetArrow("Pane style", "Back to Compact (removes these settings from config.toml)");
        var description = DescriptionText();
        var box = new ComboBox { Width = ControlWidth };
        AutomationProperties.SetName(box, "Pane style");
        foreach (var (label, _, _) in PaneStyles)
        {
            box.Items.Add(label);
        }

        void Apply(PaneStyleKind style)
        {
            if (_updating || _config.HasErrors)
            {
                return;
            }

            var values = PaneStyle.Values(style);
            Guard("saving the pane style", () =>
            {
                _writer.Set(PaneStyle.Keys.Select(k => (SettingsCatalog.Find(k)!, (object?)values[k])));
                Wrote?.Invoke();
                UpdateChanges();
            });
        }

        box.SelectionChanged += (_, _) =>
        {
            if (box.SelectedIndex >= 0)
            {
                Apply(PaneStyles[box.SelectedIndex].Style);
            }
        };
        reset.Click += (_, _) =>
        {
            FocusAfterReset(reset, box);
            Apply(PaneStyleKind.Compact);
        };

        void Refresh()
        {
            var style = PaneStyle.Of(_config.ValueOf);
            var index = Array.FindIndex(PaneStyles, s => s.Style == style);
            Quietly(() => box.SelectedIndex = index);
            box.PlaceholderText = index < 0 ? "Custom" : "";
            reset.Visibility = style == PaneStyleKind.Compact ? Visibility.Collapsed : Visibility.Visible;
            SetText(description, index < 0 ? "Your own mix of the settings below." : PaneStyles[index].Detail);
        }

        _refreshers.Add(Refresh);
        Refresh();
        var card = CardLayout(new TextBlock { Text = "Pane style" }, reset, description, null, null, box);
        var header = (FrameworkElement)card.Child;
        card.Child = null;
        return header;
    }
}
