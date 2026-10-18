using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Zinc.Input;
using Zinc.Settings;
using Windows.UI;

namespace Zinc;

// Search: settings, a profile's own settings ("font size ubuntu") and key bindings, each group
// saying where it lives. Every result is a working card; "Show in …" (or Enter, for the first
// result) goes to it in its own page, scrolls it into view, focuses it and flashes it.
internal sealed partial class SettingsWindow
{
    /// <summary>The cards on the page, by (profile, setting key), for going to one.</summary>
    private readonly Dictionary<(string? Profile, string Key), (Border Card, FrameworkElement Control)> _cards = [];

    /// <summary>Where each search result goes, in order (Enter takes the first).</summary>
    private readonly List<Action> _jumps = [];

    /// <summary>Set while the window changes section itself (not a selection by the user).</summary>
    private bool _navigating;

    /// <summary>Shows section <paramref name="id"/> (the ▾ menu's "About Zinc").</summary>
    public void ShowSection(string id) => GoTo(id, null, null);

    private static string SectionTitle(string id) => SettingsCatalog.Sections.First(s => s.Id == id).Title;

    private void SearchResults(string query)
    {
        var words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        static bool All(string text, IEnumerable<string> words) => words.All(w => text.Contains(w, StringComparison.OrdinalIgnoreCase));
        static string Text(SettingDef s) => $"{s.Label} {s.Description} {s.Key} {s.Group} {SectionTitle(s.Section)}";

        // Matching folds with a title of their own (like "Pane style") show whole, and matching settings show on their
        // own, each under the section it lives in. A setting inside a matching fold shows only in that fold.
        static string SectionOf(SettingFold fold) => SettingsCatalog.Find(fold.Children[0])!.Section;
        var folds = SettingsCatalog.Folds
            .Where(f => f.Title != null && FoldFor(f.Children[0]) == f && All($"{f.Title} {f.Description} {SectionTitle(SectionOf(f))}", words))
            .ToList();
        var inFolds = folds.SelectMany(f => f.Children).ToHashSet();
        var matching = ShownSettings.Where(s => !inFolds.Contains(s.Key) && All(Text(s), words)).ToList();
        foreach (var section in SettingsCatalog.Sections)
        {
            var id = section.Id;
            var (sectionFolds, sectionSettings) = (folds.Where(f => SectionOf(f) == id).ToList(), matching.Where(s => s.Section == id).ToList());
            if (sectionFolds.Count == 0 && sectionSettings.Count == 0)
            {
                continue;
            }

            var where = section.Title;
            _page.Children.Add(GroupTitle(where));
            foreach (var fold in sectionFolds)
            {
                var card = SettingFoldCard(fold);
                _page.Children.Add(card);
                AddGoTo(card, $"Show in {where}", () => GoTo(id, null, FoldKey(fold)));
            }

            foreach (var setting in sectionSettings)
            {
                var card = AddSettingCards(setting, null);
                AddGoTo(card, $"Show in {where}", () => GoTo(id, null, setting.Key));
            }
        }

        // A profile named in the query: its page, or its own value of the rest ("font size ubuntu").
        foreach (var profile in _config.Profiles)
        {
            var named = words.Where(w => w.Length >= 2 &&
                (profile.Name.Contains(w, StringComparison.OrdinalIgnoreCase) || profile.Id.Contains(w, StringComparison.OrdinalIgnoreCase))).ToList();
            if (named.Count == 0)
            {
                continue;
            }

            var rest = words.Except(named, StringComparer.OrdinalIgnoreCase).ToList();
            var where = $"Profiles › {profile.Name}";
            if (rest.Count == 0)
            {
                _page.Children.Add(GroupTitle("Profiles"));
                _page.Children.Add(ProfileRow(profile, [.. _config.Profiles.Select(p => p.Id)]));
                var id = profile.Id;
                _jumps.Add(() => GoTo("profiles", id, null));
                continue;
            }

            var settings = ShownSettings.Where(s => s.ProfileOverridable && All(Text(s), rest)).ToList();
            if (settings.Count == 0)
            {
                continue;
            }

            _page.Children.Add(GroupTitle(where));
            foreach (var setting in settings)
            {
                var card = AddSettingCards(setting, profile.Id);
                var id = profile.Id;
                AddGoTo(card, $"Show in {profile.Name}", () => GoTo("profiles", id, setting.Key));
            }
        }

        // Key bindings.
        var lines = KeyBindingEditor.Lines(_config);
        var keyHits = KeyActions()
            .Select(a => (Action: a, Title: Actions.Title(a, PaneTabs),
                Chords: _config.Keybindings.All.Where(b => b.Action == a).Select(b => b.Chord).ToList(),
                Sequences: _config.Keybindings.Sequences.Where(s => s.Action == a).Select(s => s.Keys).ToList()))
            .Where(k => All($"{k.Title} {Actions.Format(k.Action)} {Actions.Category(k.Action)} {string.Join(' ', k.Chords)}", words))
            .Take(8)
            .ToList();
        if (keyHits.Count > 0)
        {
            _page.Children.Add(GroupTitle("Keyboard"));
            foreach (var hit in keyHits)
            {
                var row = KeyRow(hit.Action, hit.Title, hit.Chords, hit.Sequences, lines);
                _page.Children.Add(row);
                var title = hit.Title;
                AddGoTo(row, "Show in Keyboard", () => GoTo("keyboard", null, null, keyFilter: title));
            }
        }

        if (_page.Children.Count <= 2)
        {
            _page.Children.Add(Note($"Nothing matches \"{query}\"."));
        }
    }

    /// <summary>Adds <paramref name="setting"/>'s card(s) to the page; returns its main card (the scheme's: the scheme itself).</summary>
    private Border AddSettingCards(SettingDef setting, string? profile)
    {
        var card = setting.Kind == SettingKind.Scheme ? SchemeCard(setting, profile)
            : setting.Kind == SettingKind.Files ? ShaderFilesCard(setting, profile)
            : profile == null ? Card(setting) : OverrideCard(setting, profile);
        _page.Children.Add(card);
        return card;
    }

    /// <summary>A "Show in …" link under a result card's text, and the same jump for Enter.</summary>
    private void AddGoTo(Border card, string label, Action go)
    {
        _jumps.Add(go);
        var text = card.Child switch
        {
            Grid { Children: [StackPanel t, ..] } => t, // a setting's card
            StackPanel { Children: [Grid { Children: [StackPanel t, ..] }, ..] } => t, // a fold with a title of its own
            StackPanel { Children: [Grid { Children: [Grid { Children: [StackPanel t, ..] }, ..] }, ..] } => t, // a fold headed by a card (Pane style)
            _ => null,
        };
        if (text != null)
        {
            var link = new HyperlinkButton { Content = label, FontSize = 12, Padding = new Thickness(0, 2, 0, 0) };
            link.Click += (_, _) => go();
            text.Children.Add(link);
        }
    }

    /// <summary>Marks <paramref name="section"/> in the sidebar (null: none), without navigating.</summary>
    private void SelectSection(string? section)
    {
        _navigating = true;
        try
        {
            _nav.SelectedItem = section == null ? null
                : _nav.MenuItems.Concat(_nav.FooterMenuItems).OfType<NavigationViewItem>().FirstOrDefault(i => (string)i.Tag == section);
        }
        finally
        {
            _navigating = false;
        }
    }

    /// <summary>
    /// Shows <paramref name="section"/> (a profile's page, or the keyboard page filtered), then
    /// brings setting <paramref name="key"/>'s card into view, focuses it and flashes it.
    /// </summary>
    private void GoTo(string section, string? profile, string? key, string? keyFilter = null)
    {
        _navigating = true;
        try
        {
            _search.Text = "";
            _section = section;
            _editing = profile;
            if (_editingShaders || _editingSchemes)
            {
                _editingShaders = _editingSchemes = false;
                PreviewForLibrary(open: false);
            }

            if (keyFilter != null)
            {
                _keyFilter = keyFilter;
            }

            _nav.SelectedItem = _nav.MenuItems.Concat(_nav.FooterMenuItems).OfType<NavigationViewItem>().First(i => (string)i.Tag == section);
        }
        finally
        {
            _navigating = false;
        }

        Render();
        _scroller.ChangeView(null, 0, null, disableAnimation: true);
        DispatcherQueue.SafeEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            if (key != null && _cards.TryGetValue((profile, key), out var found))
            {
                Reveal(found.Card, found.Control);
            }
            else if (keyFilter != null && _keyRows.Children.OfType<Border>().FirstOrDefault() is { } row)
            {
                Reveal(row, row);
            }
        });
    }

    private void Reveal(Border card, FrameworkElement control)
    {
        if (_foldOf.TryGetValue(card, out var open))
        {
            open();
            card.UpdateLayout();
        }

        card.StartBringIntoView(new BringIntoViewOptions { VerticalAlignmentRatio = 0.3, AnimationDesired = true });
        var target = control as Control ?? Microsoft.UI.Xaml.Input.FocusManager.FindFirstFocusableElement(control) as Control;
        target?.Focus(FocusState.Keyboard);
        Flash(card);
    }

    /// <summary>An accent border that holds for a moment and fades back (padding shrinks as the border grows, so nothing moves).</summary>
    private static void Flash(Border card)
    {
        var accent = ((SolidColorBrush)Application.Current.Resources["AccentFillColorDefaultBrush"]).Color;
        var (border, padding, thickness) = (card.BorderBrush, card.Padding, card.BorderThickness);
        var end = border is SolidColorBrush b ? b.Color : Color.FromArgb(0, accent.R, accent.G, accent.B);
        var brush = new SolidColorBrush(accent);
        card.BorderBrush = brush;
        card.BorderThickness = new Thickness(2);
        card.Padding = new Thickness(
            Math.Max(0, padding.Left - (2 - thickness.Left)), Math.Max(0, padding.Top - (2 - thickness.Top)),
            Math.Max(0, padding.Right - (2 - thickness.Right)), Math.Max(0, padding.Bottom - (2 - thickness.Bottom)));
        var fade = new ColorAnimation
        {
            From = accent,
            To = end,
            BeginTime = TimeSpan.FromMilliseconds(700),
            Duration = TimeSpan.FromMilliseconds(900),
            EnableDependentAnimation = true,
        };
        Storyboard.SetTarget(fade, brush);
        Storyboard.SetTargetProperty(fade, "Color");
        var story = new Storyboard { Children = { fade } };
        story.Completed += (_, _) =>
        {
            card.BorderBrush = border;
            card.BorderThickness = thickness;
            card.Padding = padding;
        };
        story.Begin();
    }

    /// <summary>
    /// A profile setting's description: where its inherited value is set, as a link there
    /// ("Inherited from Appearance: 11 pt."), or that the profile has its own.
    /// </summary>
    private void InheritedText(TextBlock block, SettingDef setting, string inherited, bool own)
    {
        var section = setting.Section;
        var link = new Hyperlink();
        link.Inlines.Add(new Run { Text = SectionTitle(section) });
        link.Click += (_, _) => GoTo(section, null, setting.Key);
        block.Inlines.Clear();
        if (own)
        {
            block.Inlines.Add(new Run { Text = $"Its own. Everywhere else: {inherited}, set in " });
            block.Inlines.Add(link);
            block.Inlines.Add(new Run { Text = "." });
        }
        else
        {
            block.Inlines.Add(new Run { Text = "Inherited from " });
            block.Inlines.Add(link);
            block.Inlines.Add(new Run { Text = $": {inherited}." });
        }

        block.Visibility = Visibility.Visible;
    }
}
