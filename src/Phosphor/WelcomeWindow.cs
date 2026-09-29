using Microsoft.Graphics.Canvas.Text;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Phosphor.Ghostty;
using Phosphor.Terminal;
using Windows.UI;

namespace Phosphor;

/// <summary>
/// The welcome page: shown instead of a terminal on first launch (no config file yet), and later
/// from the palette (Welcome…). Asks how you like to work (Simple: tabs on top; Workspaces: in a
/// sidebar, or on top), colors and font (previewed), and the default shell, then raises
/// <see cref="Finished"/> with the answers (null: skipped or closed) for App to write into
/// config.toml. A window of its own: its layout choice decides how the main window is built.
/// </summary>
internal sealed partial class WelcomeWindow : Window
{
    private const string DefaultSchemeLabel = "Phosphor's own";
    private const string BundledFontLabel = BundledFonts.Family + " (included)";

    private readonly ConfigFile _config;
    private WorkStyle _style;
    private WorkspaceTabs _workspacesOn = WorkspaceTabs.Left;
    private string? _scheme;
    private string? _font;
    private string? _profile;
    private bool _finished;

    private readonly Button _simpleCard, _workspacesCard;
    private readonly StackPanel _workspacesOnRow;
    private readonly Border _preview = new() { CornerRadius = new CornerRadius(8), Padding = new Thickness(14, 12, 14, 12) };
    private readonly RichTextBlock _previewText = new() { FontSize = 13, IsTextSelectionEnabled = false };

    /// <summary>The answers (null if the page was skipped or closed).</summary>
    public event Action<WelcomeChoices?>? Finished;

    public WelcomeWindow(ConfigFile config, bool firstRun)
    {
        _config = config;
        Title = "Welcome to Phosphor";
        ExtendsContentIntoTitleBar = true;
        SystemBackdrop = TestHooks.Deterministic ? null : new MicaBackdrop();
        WindowIcon.Apply(this, Path.Combine(AppContext.BaseDirectory, "Assets", "Phosphor.ico"));
        AppWindow.Resize(new Windows.Graphics.SizeInt32(980, 820));

        // Start from what's in force; on first run, workspaces in the sidebar (Phosphor at its most
        // itself; Simple is one click away).
        var global = config.Global;
        _style = firstRun || global.TabGroups || global.WorkspaceTabs == WorkspaceTabs.Left ? WorkStyle.Workspaces : WorkStyle.Simple;
        _workspacesOn = firstRun || global.WorkspaceTabs == WorkspaceTabs.Left ? WorkspaceTabs.Left : WorkspaceTabs.Top;
        _font = global.FontFamily == BundledFonts.Family ? null : global.FontFamily;
        _scheme = config.SchemeName;
        _profile = config.DefaultProfile.Id;

        var page = new StackPanel { Spacing = 28, MaxWidth = 860, Padding = new Thickness(32, 44, 32, 32) };
        page.Children.Add(Header());

        (_simpleCard, _workspacesCard, _workspacesOnRow) = StyleSection(page);
        LookSection(page);
        ShellSection(page);
        page.Children.Add(Footer(firstRun));

        var root = new Grid { RequestedTheme = ElementTheme.Dark };
        root.Children.Add(new ScrollViewer { Content = page, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        Content = root;
        ShowStyle();
        UpdatePreview();

        // Closing it is skipping it.
        Closed += (_, _) =>
        {
            if (!_finished)
            {
                _finished = true;
                Finished?.Invoke(null);
            }
        };
    }

    private static FrameworkElement Header() => new StackPanel
    {
        Spacing = 6,
        Children =
        {
            new TextBlock { Text = "Welcome to Phosphor", FontSize = 30, FontWeight = FontWeights.SemiBold },
            new TextBlock
            {
                Text = "A few choices to start with. They all end up in config.toml, and you can change them any time, " +
                       "or come back here from the command palette (Ctrl+Shift+P): Welcome…",
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.8,
            },
        },
    };

    private static TextBlock SectionTitle(string text) => new() { Text = text, FontSize = 18, FontWeight = FontWeights.SemiBold };

    // ------------------------------------------------------------ how you work

    private (Button Simple, Button Workspaces, StackPanel OnRow) StyleSection(Panel page)
    {
        var simple = Card("Simple", "Tabs along the top, one terminal each. Like Windows Terminal.", Mockup(sidebar: false, panes: false));
        var workspaces = Card("Workspaces", "Workspaces in a sidebar, each with its own tabs and splits. Good for juggling projects.",
            Mockup(sidebar: true, panes: true));
        simple.Click += (_, _) => { _style = WorkStyle.Simple; ShowStyle(); };
        workspaces.Click += (_, _) => { _style = WorkStyle.Workspaces; ShowStyle(); };

        var cards = new Grid { ColumnSpacing = 16 };
        cards.ColumnDefinitions.Add(new ColumnDefinition());
        cards.ColumnDefinitions.Add(new ColumnDefinition());
        Grid.SetColumn(workspaces, 1);
        cards.Children.Add(simple);
        cards.Children.Add(workspaces);

        // Where the workspaces go: the sidebar by default, or tabs along the top.
        var left = new RadioButton { Content = "In a sidebar", GroupName = "on", IsChecked = _workspacesOn == WorkspaceTabs.Left };
        var top = new RadioButton { Content = "Along the top", GroupName = "on", IsChecked = _workspacesOn == WorkspaceTabs.Top };
        left.Checked += (_, _) => { _workspacesOn = WorkspaceTabs.Left; ShowStyle(); };
        top.Checked += (_, _) => { _workspacesOn = WorkspaceTabs.Top; ShowStyle(); };
        var onRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 16,
            Children = { new TextBlock { Text = "Workspaces:", VerticalAlignment = VerticalAlignment.Center, Opacity = 0.8 }, left, top },
        };

        page.Children.Add(new StackPanel { Spacing = 12, Children = { SectionTitle("How do you like to work?"), cards, onRow } });
        return (simple, workspaces, onRow);
    }

    /// <summary>A layout choice: a button (keyboard, screen readers), drawn as a card.</summary>
    private static Button Card(string title, string text, FrameworkElement picture)
    {
        var card = new Button
        {
            Padding = new Thickness(16),
            CornerRadius = new CornerRadius(8),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Top,
            Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
            Content = new StackPanel
            {
                Spacing = 10,
                Children =
                {
                    picture,
                    new TextBlock { Text = title, FontSize = 16, FontWeight = FontWeights.SemiBold },
                    new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Opacity = 0.8 },
                },
            },
        };
        AutomationProperties.SetName(card, title);
        return card;
    }

    /// <summary>A little drawing of the layout: tab strip or sidebar, and a terminal (split, for workspaces).</summary>
    private static FrameworkElement Mockup(bool sidebar, bool panes)
    {
        Brush Fill(string key) => (Brush)Application.Current.Resources[key];
        Border Block(double w, double h, string key = "ControlFillColorSecondaryBrush") =>
            new() { Width = w, Height = h, CornerRadius = new CornerRadius(3), Background = Fill(key) };

        var grid = new Grid { Height = 96, ColumnSpacing = 6, RowSpacing = 6 };
        var terminal = new Grid { ColumnSpacing = 4 };
        terminal.ColumnDefinitions.Add(new ColumnDefinition());
        if (panes)
        {
            terminal.ColumnDefinitions.Add(new ColumnDefinition());
        }

        for (var i = 0; i < (panes ? 2 : 1); i++)
        {
            var pane = new Border { CornerRadius = new CornerRadius(3), Background = Fill("SolidBackgroundFillColorBaseBrush") };
            Grid.SetColumn(pane, i);
            terminal.Children.Add(pane);
        }

        if (sidebar)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(52) });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            var list = new StackPanel { Spacing = 4, Children = { Block(52, 12, "AccentFillColorDefaultBrush"), Block(52, 12), Block(52, 12) } };
            grid.Children.Add(list);
            Grid.SetColumn(terminal, 1);
        }
        else
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition());
            var tabs = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 4,
                Children = { Block(44, 12, "AccentFillColorDefaultBrush"), Block(44, 12), Block(44, 12) },
            };
            grid.Children.Add(tabs);
            Grid.SetRow(terminal, 1);
        }

        grid.Children.Add(terminal);
        return grid;
    }

    private void ShowStyle()
    {
        foreach (var (card, chosen) in new[] { (_simpleCard, _style == WorkStyle.Simple), (_workspacesCard, _style == WorkStyle.Workspaces) })
        {
            card.BorderThickness = new Thickness(chosen ? 2 : 1);
            card.BorderBrush = (Brush)Application.Current.Resources[chosen ? "AccentFillColorDefaultBrush" : "CardStrokeColorDefaultBrush"];
        }

        _workspacesOnRow.Visibility = _style == WorkStyle.Workspaces ? Visibility.Visible : Visibility.Collapsed;
    }

    // ------------------------------------------------------------ colors and font

    private void LookSection(Panel page)
    {
        var schemes = new List<string> { DefaultSchemeLabel };
        schemes.AddRange(_config.Themes.Names());
        var list = new ListView { Height = 260, SelectionMode = ListViewSelectionMode.Single };
        AutomationProperties.SetName(list, "Color schemes");
        var search = new TextBox { PlaceholderText = "Search 650 color schemes" };
        AutomationProperties.SetName(search, "Search color schemes");
        void Fill()
        {
            var query = search.Text.Trim();
            list.Items.Clear();
            foreach (var name in schemes.Where(n => query.Length == 0 || n.Contains(query, StringComparison.OrdinalIgnoreCase)))
            {
                list.Items.Add(name);
            }

            list.SelectedItem = list.Items.FirstOrDefault(i => (string)i == (_scheme ?? DefaultSchemeLabel)) ?? list.Items.FirstOrDefault();
        }

        search.TextChanged += (_, _) => Fill();
        list.SelectionChanged += (_, _) =>
        {
            if (list.SelectedItem is string name)
            {
                _scheme = name == DefaultSchemeLabel ? null : name;
                UpdatePreview();
            }
        };
        Fill();

        var fonts = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, Header = "Font" };
        fonts.Items.Add(BundledFontLabel);
        foreach (var family in MonospaceFamilies())
        {
            fonts.Items.Add(family);
        }

        fonts.SelectedItem = _font ?? BundledFontLabel;
        fonts.SelectionChanged += (_, _) =>
        {
            _font = fonts.SelectedItem is string f && f != BundledFontLabel ? f : null;
            UpdatePreview();
        };

        _preview.Child = _previewText;
        var right = new StackPanel { Spacing = 12, Children = { fonts, _preview } };
        var left = new StackPanel { Spacing = 8, Children = { search, list } };
        var columns = new Grid { ColumnSpacing = 20 };
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(280) });
        columns.ColumnDefinitions.Add(new ColumnDefinition());
        Grid.SetColumn(right, 1);
        columns.Children.Add(left);
        columns.Children.Add(right);
        page.Children.Add(new StackPanel { Spacing = 12, Children = { SectionTitle("Colors and font"), columns } });
    }

    /// <summary>Installed fixed-width families, sorted.</summary>
    private static IEnumerable<string> MonospaceFamilies()
    {
        var set = CanvasFontSet.GetSystemFontSet();
        foreach (var family in CanvasTextFormat.GetSystemFontFamilies().Order(StringComparer.OrdinalIgnoreCase))
        {
            bool mono;
            try
            {
                mono = set.GetMatchingFonts(family, FontWeights.Normal, Windows.UI.Text.FontStretch.Normal, Windows.UI.Text.FontStyle.Normal)
                    .Fonts.FirstOrDefault() is { IsMonospaced: true };
            }
            catch (Exception)
            {
                mono = false;
            }

            if (mono && !family.Equals(BundledFonts.Family, StringComparison.OrdinalIgnoreCase))
            {
                yield return family;
            }
        }
    }

    /// <summary>A little shell session in the chosen colors and font (a Nerd Font icon included).</summary>
    private void UpdatePreview()
    {
        var theme = (_scheme is { } name ? _config.Themes.Apply(new Config(), name)?.Theme : null) ?? TerminalTheme.Default;
        Color Ansi(int index) => theme.Palette.TryGetValue(index, out var c) ? ToColor(c) : AnsiDefaults[index];
        _preview.Background = new SolidColorBrush(ToColor(theme.Background));
        _previewText.Foreground = new SolidColorBrush(ToColor(theme.Foreground));
        _previewText.FontFamily = _font is { } family ? new FontFamily(family) : BundledFontFamily();

        var lines = new (string Text, int Color)[][]
        {
            [("~/src/phosphor ", 4), (" main ", 5), ("❯ ", 2), ("git status", -1)],
            [("On branch main", -1)],
            [("  modified:   ", 1), ("README.md", 1)],
            [("  new file:   ", 2), ("src/Welcome.cs", 2)],
            [("~/src/phosphor ", 4), (" main ", 5), ("❯ ", 2), ("ls", -1)],
            [("assets/  ", 4), ("src/  ", 4), ("tests/  ", 4), ("README.md  ", -1), ("build.sh", 2)],
            [("~/src/phosphor ", 4), (" main ", 5), ("❯ ", 2), ("█", -2)],
        };
        _previewText.Blocks.Clear();
        var paragraph = new Paragraph();
        foreach (var line in lines)
        {
            foreach (var (text, color) in line)
            {
                var run = new Run { Text = text };
                if (color >= 0)
                {
                    run.Foreground = new SolidColorBrush(Ansi(color));
                }
                else if (color == -2)
                {
                    run.Foreground = new SolidColorBrush(ToColor(theme.Cursor));
                }

                paragraph.Inlines.Add(run);
            }

            paragraph.Inlines.Add(new LineBreak());
        }

        _previewText.Blocks.Add(paragraph);
    }

    /// <summary>The bundled font for XAML text (the terminal draws it through Win2D instead).</summary>
    private static FontFamily BundledFontFamily() =>
        new($"ms-appx:///Fonts/CascadiaMonoNF.ttf#{BundledFonts.Family}, Cascadia Mono, Consolas");

    private static Color ToColor(GhosttyColorRgb c) => Color.FromArgb(0xFF, c.R, c.G, c.B);

    // Standard ANSI colors, for schemes (and Phosphor's own) that don't set them.
    private static readonly Color[] AnsiDefaults =
    [
        Color.FromArgb(0xFF, 0x00, 0x00, 0x00), Color.FromArgb(0xFF, 0xCD, 0x31, 0x31), Color.FromArgb(0xFF, 0x0D, 0xBC, 0x79),
        Color.FromArgb(0xFF, 0xE5, 0xE5, 0x10), Color.FromArgb(0xFF, 0x24, 0x72, 0xC8), Color.FromArgb(0xFF, 0xBC, 0x3F, 0xBC),
        Color.FromArgb(0xFF, 0x11, 0xA8, 0xCD), Color.FromArgb(0xFF, 0xE5, 0xE5, 0xE5),
    ];

    // ------------------------------------------------------------ shell

    private void ShellSection(Panel page)
    {
        var shells = new ListView { SelectionMode = ListViewSelectionMode.Single, MaxHeight = 280 };
        AutomationProperties.SetName(shells, "Default shell");
        foreach (var profile in _config.VisibleProfiles)
        {
            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 10,
                Tag = profile.Id,
                Children =
                {
                    new IconSourceElement { IconSource = ProfileIcons.For(profile), Width = 16, Height = 16 },
                    new TextBlock { Text = profile.Name, VerticalAlignment = VerticalAlignment.Center },
                },
            };
            AutomationProperties.SetName(row, profile.Name);
            shells.Items.Add(row);
        }

        shells.SelectedItem = shells.Items.OfType<StackPanel>().FirstOrDefault(r => (string)r.Tag == _profile) ?? shells.Items.FirstOrDefault();
        shells.SelectionChanged += (_, _) => _profile = (shells.SelectedItem as StackPanel)?.Tag as string;
        page.Children.Add(new StackPanel
        {
            Spacing = 12,
            Children =
            {
                SectionTitle("Default shell"),
                new TextBlock { Text = "New tabs open with it. Found on this PC; every one of them stays a click away in the ▾ menu.", Opacity = 0.8, TextWrapping = TextWrapping.Wrap },
                shells,
            },
        });
    }

    // ------------------------------------------------------------ finish

    private FrameworkElement Footer(bool firstRun)
    {
        var start = new Button
        {
            Content = firstRun ? "Get started" : "Save",
            Style = (Style)Application.Current.Resources["AccentButtonStyle"],
            MinWidth = 140,
        };
        var skip = new Button { Content = firstRun ? "Skip: use the defaults" : "Cancel" };
        start.Click += (_, _) => Finish(new WelcomeChoices(_style, _workspacesOn, _scheme, _font, _profile));
        // Enter accepts what's shown (and nothing above wears a focus outline at first).
        start.Loaded += (_, _) => start.Focus(FocusState.Programmatic);
        skip.Click += (_, _) => Finish(null);
        var note = new TextBlock
        {
            Text = firstRun ? "" : "How you work applies to windows opened from now on; colors, font and shell apply right away.",
            Opacity = 0.7,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var row = new Grid { ColumnSpacing = 12 };
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(skip, 1);
        Grid.SetColumn(start, 2);
        row.Children.Add(note);
        row.Children.Add(skip);
        row.Children.Add(start);
        return row;
    }

    private void Finish(WelcomeChoices? choices)
    {
        if (_finished)
        {
            return;
        }

        _finished = true;
        Finished?.Invoke(choices);
        Close();
    }
}
