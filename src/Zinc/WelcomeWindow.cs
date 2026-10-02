using System.Numerics;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Zinc.Ghostty;
using Zinc.Terminal;
using Windows.UI;

namespace Zinc;

/// <summary>
/// The welcome page: shown instead of a terminal on first launch (no config file yet), and later
/// from the palette (Welcome…). Asks how you like to work (Simple: tabs on top; Workspaces: in a
/// sidebar, or on top), colors and font (previewed), and the default shell, then raises
/// <see cref="Finished"/> with the answers (null: skipped or closed) for App to write into
/// config.toml. A window of its own: its layout choice decides how the main window is built.
/// </summary>
internal sealed partial class WelcomeWindow : Window
{
    private const string DefaultSchemeLabel = "Default (Lit Cell)";
    private const string BundledFontLabel = BundledFonts.Family + " (included)";

    private readonly ConfigFile _config;
    private WorkStyle _style;
    private WorkspaceTabs _workspacesOn = WorkspaceTabs.Left;
    private string? _scheme;   // the scheme, or with _follow the dark side
    private bool _follow;      // follow Windows light/dark: _light by day, _scheme by night
    private string? _light;
    private bool _toast;       // a Windows notification when a long command finishes out of view
    private string? _font;
    private string? _profile;
    private bool _finished;

    private readonly Button _simpleCard, _workspacesCard;
    private readonly StackPanel _workspacesOnRow;
    private readonly Border _preview = new() { CornerRadius = new CornerRadius(8), Padding = new Thickness(14, 12, 14, 12) };
    private readonly RichTextBlock _previewText = new() { FontSize = 13, IsTextSelectionEnabled = false };

    // One question per page, Back/Next and page dots below.
    private readonly bool _firstRun;
    private readonly FrameworkElement[] _pages;
    private readonly ScrollViewer _scroller = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    private readonly StackPanel _dots = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly Button _back = new() { Content = "Back", MinWidth = 96 };
    private readonly Button _next = new() { MinWidth = 120 };
    private Button? _save;
    private int _page = -1;

    /// <summary>The answers (null if the page was skipped or closed).</summary>
    public event Action<WelcomeChoices?>? Finished;

    public WelcomeWindow(ConfigFile config, bool firstRun)
    {
        _config = config;
        Title = "Welcome to Zinc";
        ExtendsContentIntoTitleBar = true;
        Brand.StyleCaptionButtons(AppWindow.TitleBar);
        SystemBackdrop = TestHooks.Deterministic ? null : new MicaBackdrop();
        WindowIcon.Apply(this, Path.Combine(AppContext.BaseDirectory, "Assets", "Zinc.ico"));
        _firstRun = firstRun;
        if (TestHooks.Deterministic)
        {
            // Each page focuses Next; whether its focus outline shows depends on how Windows took the last input
            // (UI Automation's invoke, while the window may or may not be in front), so captures differed run to run.
            _next.UseSystemFocusVisuals = false;
            _back.UseSystemFocusVisuals = false;
        }

        // Start from what's in force; on first run, workspaces in the sidebar (Zinc at its most
        // itself; Simple is one click away).
        var global = config.Global;
        _style = firstRun || global.TabGroups || global.WorkspaceTabs == WorkspaceTabs.Left ? WorkStyle.Workspaces : WorkStyle.Simple;
        _workspacesOn = firstRun || global.WorkspaceTabs == WorkspaceTabs.Left ? WorkspaceTabs.Left : WorkspaceTabs.Top;
        _font = global.FontFamily == BundledFonts.Family ? null : global.FontFamily;
        _scheme = config.SchemeName;
        if (_scheme != null && ThemeLibrary.TryParsePair(_scheme, out var light, out var dark))
        {
            (_follow, _light, _scheme) = (true, light, dark);
        }

        _toast = !firstRun && global.NotifyCommandFinished is NotifyMode.Toast or NotifyMode.ToastAlways;
        _profile = config.DefaultProfile.Id;

        FrameworkElement style;
        (style, _simpleCard, _workspacesCard, _workspacesOnRow) = StylePage();
        _pages = [style, LookPage(), ShellPage()];

        // Header, the page (scrolls if the window is small), footer pinned at the bottom.
        var root = new Grid { Padding = new Thickness(40, 44, 40, 24), RowSpacing = 24 };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition());
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var pages = new Grid();
        foreach (var page in _pages)
        {
            page.Visibility = Visibility.Collapsed;
            pages.Children.Add(page);
        }

        _scroller.Content = pages;
        var footer = Footer();
        Grid.SetRow(_scroller, 1);
        Grid.SetRow(footer, 2);
        root.Children.Add(Header());
        root.Children.Add(_scroller);
        root.Children.Add(footer);
        Content = root;
        ShowStyle();
        UpdatePreview();
        ShowPage(0);

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

    /// <summary>
    /// The page's title after the app icon (the tile you just clicked; docs/brand.md section 9 has the mark
    /// large here: the user chose the icon over the mark, 2026-10-01). One tile works on light and dark.
    /// </summary>
    private static FrameworkElement Header() => new StackPanel
    {
        Orientation = Orientation.Horizontal,
        Spacing = 16,
        Children =
        {
            new Image
            {
                Width = 48,
                Height = 48,
                Source = new Microsoft.UI.Xaml.Media.Imaging.SvgImageSource(new Uri(Path.Combine(AppContext.BaseDirectory, "Brand", "quadrant-app-tile.svg"))),
                VerticalAlignment = VerticalAlignment.Center,
            },
            new TextBlock { Text = "Welcome to Zinc", FontSize = 30, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center },
        },
    };

    private static TextBlock SectionTitle(string text) => new() { Text = text, FontSize = 18, FontWeight = FontWeights.SemiBold };

    private static TextBlock Note(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, Opacity = 0.8 };

    // ------------------------------------------------------------ pages

    private void ShowPage(int index)
    {
        index = Math.Clamp(index, 0, _pages.Length - 1);
        if (index == _page)
        {
            return;
        }

        var forward = index > _page;
        if (_page >= 0)
        {
            _pages[_page].Visibility = Visibility.Collapsed;
        }

        SlideIn(_pages[index], forward);
        _pages[index].Visibility = Visibility.Visible;
        _page = index;
        _scroller.ChangeView(null, 0, null, disableAnimation: true);
        ShowDots();

        var last = index == _pages.Length - 1;
        _back.Visibility = index > 0 ? Visibility.Visible : Visibility.Collapsed;
        _next.Content = !last ? "Next" : _firstRun ? "Get started" : "Save";
        AutomationProperties.SetName(_next, (string)_next.Content);
        if (_save != null)
        {
            _save.Visibility = last ? Visibility.Collapsed : Visibility.Visible;
        }

        // Enter moves on (and nothing on the page wears a focus outline at first).
        _next.Focus(FocusState.Programmatic);
    }

    /// <summary>The page slides in from the side it comes from (and fades in).</summary>
    private static void SlideIn(FrameworkElement page, bool forward)
    {
        if (TestHooks.Deterministic)
        {
            return;
        }

        ElementCompositionPreview.SetIsTranslationEnabled(page, true);
        var compositor = ElementCompositionPreview.GetElementVisual(page).Compositor;
        var ease = compositor.CreateCubicBezierEasingFunction(new Vector2(0.1f, 0.9f), new Vector2(0.2f, 1f));
        var slide = compositor.CreateVector3KeyFrameAnimation();
        slide.Target = "Translation";
        slide.InsertKeyFrame(0, new Vector3(forward ? 48 : -48, 0, 0));
        slide.InsertKeyFrame(1, Vector3.Zero, ease);
        slide.Duration = TimeSpan.FromMilliseconds(300);
        var fade = compositor.CreateScalarKeyFrameAnimation();
        fade.Target = "Opacity";
        fade.InsertKeyFrame(0, 0);
        fade.InsertKeyFrame(1, 1, ease);
        fade.Duration = TimeSpan.FromMilliseconds(200);
        var show = compositor.CreateAnimationGroup();
        show.Add(slide);
        show.Add(fade);
        ElementCompositionPreview.SetImplicitShowAnimation(page, show);
    }

    // ------------------------------------------------------------ how you work

    private (FrameworkElement Page, Button Simple, Button Workspaces, StackPanel OnRow) StylePage()
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

        var page = new StackPanel
        {
            Spacing = 12,
            Children =
            {
                Note("A few choices to start with. They all end up in config.toml, and you can change them any time, " +
                     "or come back here from the command palette (Ctrl+Shift+P): Welcome…"),
                new Border { Height = 8 },
                SectionTitle("How do you like to work?"),
                cards,
                onRow,
            },
        };
        if (!_firstRun)
        {
            page.Children.Add(Note("This applies to windows opened from now on; colors, font and shell apply right away."));
        }

        return (page, simple, workspaces, onRow);
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
        // On a light card the usual control fill is near white: a grey that shows instead.
        var blockKey = Application.Current.RequestedTheme == ApplicationTheme.Light ? "ControlStrongStrokeColorDisabledBrush" : "ControlFillColorSecondaryBrush";
        Border Block(double w, double h, string? key = null) =>
            new() { Width = w, Height = h, CornerRadius = new CornerRadius(3), Background = Fill(key ?? blockKey) };

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

    private FrameworkElement LookPage()
    {
        var names = _config.Themes.Names();
        var schemes = new List<string> { DefaultSchemeLabel };
        schemes.AddRange(names);
        var list = new ListView { Height = 390, SelectionMode = ListViewSelectionMode.Single };
        AutomationProperties.SetName(list, "Color schemes");
        SchemeTiles.ShowAsTiles(list, _config.Themes, DefaultSchemeLabel); // each scheme drawn in its own colors
        var search = new TextBox { PlaceholderText = $"Search {names.Count} color schemes" };
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
        // The page is laid out only once it's shown: bring the chosen scheme into view then.
        void FirstLayout(object sender, SizeChangedEventArgs e)
        {
            list.SizeChanged -= FirstLayout;
            if (list.SelectedItem is { } chosen)
            {
                list.ScrollIntoView(chosen, ScrollIntoViewAlignment.Leading);
            }
        }

        list.SizeChanged += FirstLayout;
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

        // Follow Windows light/dark: the list above picks the dark side, this one the light side.
        var listLabel = new TextBlock { Text = _follow ? "When Windows is dark" : "Scheme", Opacity = 0.8 };
        var lightPicker = new SchemePicker(_config.Themes, names, null, "When Windows is light")
        {
            Width = double.NaN,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        lightPicker.Show(_light, _light != null);
        lightPicker.Picked += name =>
        {
            _light = name;
            UpdatePreview();
        };
        var lightBox = new StackPanel
        {
            Spacing = 8,
            Visibility = _follow ? Visibility.Visible : Visibility.Collapsed,
            Children = { new TextBlock { Text = "When Windows is light" }, lightPicker },
        };
        var follow = new ToggleSwitch { Header = "Follow Windows light/dark", IsOn = _follow, OnContent = "Two schemes", OffContent = "One scheme" };
        AutomationProperties.SetName(follow, "Follow Windows light/dark");
        follow.Toggled += (_, _) =>
        {
            _follow = follow.IsOn;
            if (_follow)
            {
                // A pair names two schemes: Zinc's own becomes Catppuccin Mocha, and the light
                // side starts from the dark one's counterpart.
                if (_scheme == null && names.FirstOrDefault(n => n == "Catppuccin Mocha") is { } mocha)
                {
                    _scheme = mocha;
                    search.Text = "";
                    list.SelectedItem = mocha;
                    list.ScrollIntoView(mocha);
                }

                _light ??= ThemeLibrary.LightCounterpart(_scheme, names);
                lightPicker.Show(_light, _light != null);
            }

            listLabel.Text = _follow ? "When Windows is dark" : "Scheme";
            lightBox.Visibility = _follow ? Visibility.Visible : Visibility.Collapsed;
            UpdatePreview();
        };

        _preview.Child = _previewText;
        var right = new StackPanel { Spacing = 12, Children = { follow, lightBox, fonts, _preview } };
        var left = new StackPanel { Spacing = 8, Children = { listLabel, search, list } };
        var columns = new Grid { ColumnSpacing = 20 };
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(280) });
        columns.ColumnDefinitions.Add(new ColumnDefinition());
        Grid.SetColumn(right, 1);
        columns.Children.Add(left);
        columns.Children.Add(right);
        return new StackPanel { Spacing = 12, Children = { SectionTitle("Colors and font"), columns } };
    }

    /// <summary>Installed fixed-width families, sorted.</summary>
    internal static IEnumerable<string> MonospaceFamilies()
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
        // Following Windows: the side for its current mode.
        var shownScheme = _follow && ConfigFile.SystemScheme == Zinc.Ghostty.ColorScheme.Light ? _light : _scheme;
        var theme = (shownScheme is { } name ? _config.Themes.Apply(new Config(), name)?.Theme : null) ?? TerminalTheme.Default;
        Color Ansi(int index) => theme.Palette.TryGetValue(index, out var c) ? ToColor(c) : AnsiDefaults[index];
        _preview.Background = new SolidColorBrush(ToColor(theme.Background));
        _previewText.Foreground = new SolidColorBrush(ToColor(theme.Foreground));
        _previewText.FontFamily = _font is { } family ? new FontFamily(family) : BundledFontFamily();

        var lines = new (string Text, int Color)[][]
        {
            [("~/src/zinc ", 4), (" main ", 5), ("❯ ", 2), ("git status", -1)],
            [("On branch main", -1)],
            [("  modified:   ", 1), ("README.md", 1)],
            [("  new file:   ", 2), ("src/Welcome.cs", 2)],
            [("~/src/zinc ", 4), (" main ", 5), ("❯ ", 2), ("ls", -1)],
            [("assets/  ", 4), ("src/  ", 4), ("tests/  ", 4), ("README.md  ", -1), ("build.sh", 2)],
            [("~/src/zinc ", 4), (" main ", 5), ("❯ ", 2), ("█", -2)],
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

    // Standard ANSI colors, for schemes (and Zinc's own) that don't set them.
    private static readonly Color[] AnsiDefaults =
    [
        Color.FromArgb(0xFF, 0x00, 0x00, 0x00), Color.FromArgb(0xFF, 0xCD, 0x31, 0x31), Color.FromArgb(0xFF, 0x0D, 0xBC, 0x79),
        Color.FromArgb(0xFF, 0xE5, 0xE5, 0x10), Color.FromArgb(0xFF, 0x24, 0x72, 0xC8), Color.FromArgb(0xFF, 0xBC, 0x3F, 0xBC),
        Color.FromArgb(0xFF, 0x11, 0xA8, 0xCD), Color.FromArgb(0xFF, 0xE5, 0xE5, 0xE5),
    ];

    // ------------------------------------------------------------ shell

    private FrameworkElement ShellPage()
    {
        var shells = new ListView { SelectionMode = ListViewSelectionMode.Single, MaxHeight = 440 };
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

        // A long command finishing out of view: a dot on its tab either way; in the background,
        // the taskbar button flashes or (toast) a Windows notification shows.
        var flash = new RadioButton { Content = "Flash the taskbar button", GroupName = "done", IsChecked = !_toast };
        var toast = new RadioButton { Content = "Show a Windows notification", GroupName = "done", IsChecked = _toast };
        AutomationProperties.SetName(toast, "Show a Windows notification");
        flash.Checked += (_, _) => _toast = false;
        toast.Checked += (_, _) => _toast = true;
        var left = new StackPanel
        {
            Spacing = 12,
            Children =
            {
                SectionTitle("Default shell"),
                Note("New tabs open with it. Found on this PC; each one stays a click away in the ▾ menu."),
                shells,
            },
        };
        var right = new StackPanel
        {
            Spacing = 12,
            Children =
            {
                SectionTitle("When a long command finishes"),
                Note("A command that ran 10 seconds or more puts a dot on its tab if you weren't looking. " +
                     "If Zinc is in the background, also:"),
                flash,
                toast,
            },
        };
        var columns = new Grid { ColumnSpacing = 32 };
        columns.ColumnDefinitions.Add(new ColumnDefinition());
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });
        Grid.SetColumn(right, 1);
        columns.Children.Add(left);
        columns.Children.Add(right);
        return columns;
    }

    // ------------------------------------------------------------ finish

    /// <summary>Skip (or Cancel) on the left, page dots in the middle, Back / Next on the right.</summary>
    private FrameworkElement Footer()
    {
        var skip = new Button { Content = _firstRun ? "Skip: use the defaults" : "Cancel" };
        skip.Click += (_, _) => Finish(null);
        _next.Style = (Style)Application.Current.Resources["AccentButtonStyle"];
        _next.Click += (_, _) =>
        {
            if (_page < _pages.Length - 1)
            {
                ShowPage(_page + 1);
            }
            else
            {
                Save();
            }
        };
        _back.Click += (_, _) => ShowPage(_page - 1);
        for (var i = 0; i < _pages.Length; i++)
        {
            _dots.Children.Add(Dot(i));
        }
        _next.Loaded += (_, _) => _next.Focus(FocusState.Programmatic);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { _back, _next } };
        // Coming back later to change one thing: save from any page.
        if (!_firstRun)
        {
            _save = new Button { Content = "Save", MinWidth = 96 };
            _save.Click += (_, _) => Save();
            buttons.Children.Insert(0, _save);
        }

        var row = new Grid { ColumnSpacing = 12 };
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        skip.HorizontalAlignment = HorizontalAlignment.Left;
        buttons.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetColumn(_dots, 1);
        Grid.SetColumn(buttons, 2);
        row.Children.Add(skip);
        row.Children.Add(_dots);
        row.Children.Add(buttons);
        return row;
    }

    /// <summary>A page dot: a button (click, keyboard, screen readers) with no chrome of its own.</summary>
    private Button Dot(int page)
    {
        var dot = new Button
        {
            Padding = new Thickness(5, 8, 5, 8),
            BorderThickness = new Thickness(0),
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            Content = new Border { Height = 8, CornerRadius = new CornerRadius(4) },
        };
        foreach (var key in new[] { "ButtonBackgroundPointerOver", "ButtonBackgroundPressed" })
        {
            dot.Resources[key] = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        }

        AutomationProperties.SetName(dot, $"Page {page + 1} of {_pages.Length}");
        dot.Click += (_, _) => ShowPage(page);
        return dot;
    }

    /// <summary>The current page's dot is a wider accent pill; the others, small grey dots.</summary>
    private void ShowDots()
    {
        for (var i = 0; i < _dots.Children.Count; i++)
        {
            var pill = (Border)((Button)_dots.Children[i]).Content;
            var current = i == _page;
            pill.Width = current ? 24 : 8;
            pill.Background = (Brush)Application.Current.Resources[current ? "AccentFillColorDefaultBrush" : "ControlStrongFillColorDefaultBrush"];
        }
    }

    private void Save() => Finish(new WelcomeChoices(_style, _workspacesOn,
        _follow && _scheme != null && _light != null ? $"light:{_light},dark:{_scheme}" : _scheme, _font, _profile, _toast));

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
