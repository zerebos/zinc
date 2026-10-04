using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Zinc.Settings;
using Zinc.Terminal;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.UI;

namespace Zinc;

/// <summary>
/// The settings window: every setting in <see cref="SettingsCatalog"/>, grouped by section, each
/// written to config.toml as soon as it changes (one key at a time, spliced in place; a default is
/// written as nothing) and applied by the config watcher's live reload, so your terminals show it
/// right away. "Undo changes" puts the file back as it was when the window opened. While
/// config.toml has an error, nothing is written. Its own window, so fonts and colors are judged
/// on the real terminals beside it.
/// </summary>
internal sealed partial class SettingsWindow : Window
{
    private const string DefaultSchemeLabel = "Default (Lit Cell)";
    private const string BundledFontLabel = BundledFonts.Family + " (included)";

    private readonly SettingsWriter _writer;
    private ConfigFile _config;
    private readonly NavigationView _nav;
    private readonly AutoSuggestBox _search;

    // Bottom padding: room for the undo bar floating over the last card.
    private readonly StackPanel _page = new() { Spacing = 4, MaxWidth = 960, Padding = new Thickness(40, 12, 40, 96) };
    private readonly ScrollViewer _scroller = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    private readonly Border _snackbar;
    private readonly Button _undo = new() { Content = "Undo changes", IsEnabled = false };
    private bool _snackbarShown;
    private readonly InfoBar _problems = new() { Severity = InfoBarSeverity.Error, IsClosable = false, Title = "config.toml has an error" };
    private readonly List<Action> _refreshers = [];
    private string _section;
    private string? _editing; // the profile open in the Profiles section's editor
    private List<(string Id, string Name, bool Hidden)> _profilesShown = [];
    private bool _updating;

    /// <summary>A change was written to config.toml.</summary>
    public event Action? Wrote;

    /// <summary>The section shown (reopening the window during this run starts there).</summary>
    public string Section => _section;

    public SettingsWindow(ConfigFile config, string? startSection = null)
    {
        _config = config;
        _writer = new SettingsWriter(config.Path);
        _section = SettingsCatalog.Sections.Any(s => s.Id == startSection) ? startSection! : SettingsCatalog.Sections[0].Id;
        Title = "Zinc Settings";
        ExtendsContentIntoTitleBar = true;
        Brand.StyleCaptionButtons(AppWindow.TitleBar);
        SystemBackdrop = TestHooks.Deterministic ? null : new MicaBackdrop();
        WindowIcon.Apply(this, Path.Combine(AppContext.BaseDirectory, "Assets", "Zinc.ico"));
        WindowCursor.LeaveToXaml(this);
        Updater.Changed += ShowUpdateStatus;
        Closed += (_, _) => Updater.Changed -= ShowUpdateStatus;

        // Title bar: the app's mark and the window's name, in the caption area.
        var titleBar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            Height = 40,
            Padding = new Thickness(16, 0, 0, 0),
            Children =
            {
                new Image { Width = 16, Height = 16, Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(Path.Combine(AppContext.BaseDirectory, "Assets", "Zinc.ico"))) },
                new TextBlock { Text = "Zinc Settings", VerticalAlignment = VerticalAlignment.Center, FontSize = 12 },
            },
        };

        _search = new AutoSuggestBox { PlaceholderText = "Find a setting", QueryIcon = new SymbolIcon(Symbol.Find) };
        AutomationProperties.SetName(_search, "Find a setting");
        // Typing searches; the window clearing the box itself (going to a result) doesn't.
        _search.TextChanged += (_, e) =>
        {
            if (e.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
            {
                Render();
            }
        };
        _search.QuerySubmitted += (_, _) => _jumps.FirstOrDefault()?.Invoke();

        // The undo bar: floats at the bottom while there are changes to undo.
        ToolTipService.SetToolTip(_undo, "Puts config.toml back as it was when Settings opened");
        _undo.Click += (_, _) => Guard("undoing changes", () =>
        {
            _writer.Undo();
            Wrote?.Invoke();
            UpdateChanges();
        });
        _snackbar = new Border
        {
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 12,
                Children =
                {
                    new FontIcon
                    {
                        Glyph = "",
                        FontSize = 14,
                        VerticalAlignment = VerticalAlignment.Center,
                        Foreground = (Brush)Application.Current.Resources["AccentTextFillColorPrimaryBrush"],
                    },
                    new TextBlock { Text = "Saved to config.toml and applied", VerticalAlignment = VerticalAlignment.Center },
                    _undo,
                },
            },
            Padding = new Thickness(16, 8, 8, 8),
            Margin = new Thickness(0, 0, 0, 20),
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            Background = (Brush)Application.Current.Resources["SolidBackgroundFillColorTertiaryBrush"],
            BorderBrush = (Brush)Application.Current.Resources["SurfaceStrokeColorFlyoutBrush"],
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            Opacity = 0,
            IsHitTestVisible = false,
            Translation = new Vector3(0, 16, 32),
            OpacityTransition = new ScalarTransition { Duration = TimeSpan.FromMilliseconds(160) },
            TranslationTransition = new Vector3Transition { Duration = TimeSpan.FromMilliseconds(160) },
        };
        var shadow = new ThemeShadow();
        shadow.Receivers.Add(_scroller);
        _snackbar.Shadow = shadow;

        _nav = new NavigationView
        {
            PaneDisplayMode = NavigationViewPaneDisplayMode.Left,
            IsBackButtonVisible = NavigationViewBackButtonVisible.Collapsed,
            IsSettingsVisible = false,
            IsPaneToggleButtonVisible = false,
            OpenPaneLength = 250,
            AutoSuggestBox = _search,
            Content = PageArea(),
        };
        foreach (var section in SettingsCatalog.Sections)
        {
            var item = new NavigationViewItem { Content = section.Title, Tag = section.Id, Icon = new FontIcon { Glyph = section.Glyph } };
            (section.Id == "about" ? _nav.FooterMenuItems : _nav.MenuItems).Add(item);
        }

        _nav.SelectedItem = _nav.MenuItems.Concat(_nav.FooterMenuItems).OfType<NavigationViewItem>().First(i => (string)i.Tag == _section);
        _nav.SelectionChanged += (_, e) =>
        {
            if (!_navigating && e.SelectedItem is NavigationViewItem { Tag: string id })
            {
                _section = id;
                _editing = null;
                _editingScheme = null;
                _search.Text = "";
                Render();
                _scroller.ChangeView(null, 0, null, disableAnimation: true);
            }
        };

        var openFile = new Button { Content = "Open config.toml" };
        openFile.Click += (_, _) => OpenConfigFile();
        _problems.ActionButton = openFile;

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition());
        Grid.SetRow(_nav, 1);
        root.Children.Add(titleBar);
        root.Children.Add(_nav);
        SetUpIndicator(root);
        Content = root;
        SetTitleBar(titleBar);
        Render();
    }

    /// <summary>The page: the pinned area (heading and preview, on some pages) over the scrolling settings, and the undo bar.</summary>
    private Grid PageArea()
    {
        var area = new Grid();
        area.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        area.RowDefinitions.Add(new RowDefinition());
        Grid.SetRow(_scroller, 1);
        Grid.SetRowSpan(_snackbar, 2);
        area.Children.Add(_pinned);
        area.Children.Add(_scroller);
        area.Children.Add(_snackbar);
        area.SizeChanged += (_, _) => FitPreview();
        return area;
    }

    /// <summary>The config was read again (after our write, or an edit elsewhere): show what's in force.</summary>
    public void Refresh(ConfigFile config)
    {
        // The profile being edited, as the page shows it: a change in whether it can open (its
        // search turned back on), its name or where it came from rebuilds the page.
        static object? Shape(Profile? p) => p is null ? null : (p.Name, p.Missing, p.Found, p.Source);
        var editedBefore = _editing is { } id ? Shape(_config.Profiles.FirstOrDefault(p => p.Id == id)) : null;
        _config = config;
        var editedNow = _editing is { } id2 ? Shape(_config.Profiles.FirstOrDefault(p => p.Id == id2)) : null;
        if (_section == "profiles" && _search.Text.Trim().Length == 0 &&
            (_editing == null ? !_profilesShown.SequenceEqual(Listed(config)) : !Equals(editedBefore, editedNow)))
        {
            Render(); // the list changed (or its order), or the profile being edited appeared or changed state
            return;
        }

        _updating = true;
        try
        {
            UpdateBanners();
            RefreshPreview();
            foreach (var refresh in _refreshers)
            {
                refresh();
            }
        }
        finally
        {
            _updating = false;
        }
    }

    // ------------------------------------------------------------ pages

    private void Render()
    {
        _page.Children.Clear();
        _refreshers.Clear();
        foreach (var timer in _paletteTimers)
        {
            timer.Stop();
        }

        _paletteTimers.Clear();
        _cards.Clear();
        _foldOf.Clear();
        _jumps.Clear();
        var query = _search.Text.Trim();
        var heading = new TextBlock
        {
            Text = query.Length > 0 ? "Results"
                : _editingScheme is { } scheme ? scheme
                : _section == "profiles" && _editing is { } editing ? _config.FindProfile(editing)?.Name ?? editing
                : SettingsCatalog.Sections.First(s => s.Id == _section).Title,
            FontSize = 28,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 8, 0, 12),
        };
        _page.Children.Add(heading);
        if (query.Length == 0 && _editingScheme == null && _section == "about")
        {
            heading.Visibility = Visibility.Collapsed; // About's lockup is its heading
        }

        _page.Children.Add(_problems);
        UpdateBanners();

        if (query.Length > 0)
        {
            SearchResults(query);
        }
        else if (_editingScheme is { } editingScheme)
        {
            SchemeEditor(editingScheme);
        }
        else if (_section == "about")
        {
            AboutPage();
        }
        else if (_section == "profiles")
        {
            if (_editing is { } id)
            {
                ProfileEditor(id);
            }
            else
            {
                ProfilesPage();
            }
        }
        else if (_section == "keyboard")
        {
            KeyboardPage();
        }
        else
        {
            foreach (var group in SettingsCatalog.All.Where(s => s.Section == _section).GroupBy(s => s.Group))
            {
                AddGroup(group.Key, group);
                if (group.Key == "When you come back")
                {
                    DefaultTerminalCard(); // Windows' setting, not one of config.toml's
                }

                if (group.Key == "Colors")
                {
                    _page.Children.Add(Note(SettingsCatalog.FileOnly["colors.palette"]));
                }
            }
        }

        PlacePreview(heading);
        _scroller.Content = _page;
    }

    private void UpdateBanners()
    {
        _problems.IsOpen = _config.HasErrors;
        _problems.Message = (_config.Warnings.FirstOrDefault() ?? "") + " Nothing is saved from here until it's fixed.";
        UpdateChanges();
    }

    /// <summary>Slides the undo bar in (with how many settings Undo would put back) or out.</summary>
    private void UpdateChanges()
    {
        var count = 0;
        Guard("counting changes", () => count = _config.HasErrors ? 0 : _writer.ChangeCount());
        var show = count > 0;
        if (show)
        {
            _undo.Content = count == 1 ? "Undo 1 change" : $"Undo {count} changes";
            AutomationProperties.SetName(_undo, "Undo changes");
        }

        if (show == _snackbarShown)
        {
            return;
        }

        _snackbarShown = show;
        _snackbar.Opacity = show ? 1 : 0;
        _snackbar.Translation = new Vector3(0, show ? 0 : 16, 32);
        _snackbar.IsHitTestVisible = show;
        _undo.IsEnabled = show; // out of the tab order while hidden
    }

    private void AddGroup(string title, IEnumerable<SettingDef> settings)
    {
        _page.Children.Add(GroupTitle(title));
        foreach (var setting in settings)
        {
            if (SettingsCatalog.FoldOf(setting.Key) is { } fold)
            {
                // The fold goes where its first setting is; the rest are in it.
                if (setting.Key == (fold.Lead ?? fold.Children[0]))
                {
                    _page.Children.Add(SettingFoldCard(fold));
                }
            }
            else if (setting.Kind == SettingKind.Scheme)
            {
                foreach (var card in SchemeCards(setting, null))
                {
                    _page.Children.Add(card);
                }
            }
            else if (setting.Kind == SettingKind.Files)
            {
                _page.Children.Add(ShaderFilesCard(setting, null));
            }
            else
            {
                _page.Children.Add(Card(setting));
            }
        }
    }

    private static TextBlock GroupTitle(string title) =>
        new() { Text = title, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 18, 0, 6) };

    private static TextBlock Note(string text) => new()
    {
        Text = text,
        TextWrapping = TextWrapping.Wrap,
        Opacity = 0.7,
        Margin = new Thickness(2, 8, 0, 0),
    };

    private void AboutPage()
    {
        // The lockup (wordmark, then mark; docs/brand.md section 10) stands in for the page's heading, centered.
        var lockup = Path.Combine(AppContext.BaseDirectory, "Brand",
            Brand.IsDark(_page) ? "quadrant-zinc-dark.svg" : "quadrant-zinc-light.svg");
        var image = new Image
        {
            Source = new Microsoft.UI.Xaml.Media.Imaging.SvgImageSource(new Uri(lockup)), // vector: the cells stay crisp at any scale
            // The mark's cells show from 64 px up (docs/brand.md section 7; smaller, they blur together).
            Height = 100,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 12, 0, 18),
        };
        AutomationProperties.SetName(image, "Zinc");
        _page.Children.Add(image);
        _page.Children.Add(Row(TerminalSession.Version.Replace("zinc", "Zinc"),
            "Zinc: what makes ghosts glow in the dark. Built on libghostty, the terminal engine that powers Ghostty; the open-source software Zinc ships is listed below.",
            null));
        _page.Children.Add(Row("Config file", _config.Path, ButtonRow(("Open", OpenConfigFile), ("Open folder", () => Reveal(_config.Path)))));
        _page.Children.Add(Row("Welcome page", "The first-run questions: how you work, colors and font, default shell.",
            ButtonRow(("Open", () => App.Instance.ShowWelcome(firstRun: false)))));
        UpdatesGroup();
        AddGroup("Troubleshooting", SettingsCatalog.All.Where(s => s.Section == "about" && s.Group == "Troubleshooting"));
        if (Log.FromEnvironment)
        {
            _page.Children.Add(Note("ZINC_LOG is set, so the log is always on, in the file it names."));
        }

        _page.Children.Add(LogFileRow());
        _page.Children.Add(Row("Crash log", Log.CrashPath, ButtonRow(("Open folder", () => Reveal(Log.CrashPath)))));
        MemoryRow();
        BenchmarkRows();
        OpenSourceList();
    }

    // The Updates card's live parts (replaced on every render; ShowUpdateStatus updates them in place).
    private TextBlock? _updateStatus;
    private Button? _updateCheck, _updateRestart;
    private HyperlinkButton? _updateNotes;

    /// <summary>Updates: where things stand (live), Check for updates / Restart now, and the automatic-check switch.</summary>
    private void UpdatesGroup()
    {
        var at = _page.Children.Count;
        AddGroup("Updates", SettingsCatalog.All.Where(s => s.Section == "about" && s.Group == "Updates"));

        _updateStatus = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            IsTextSelectionEnabled = true,
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
        };
        _updateCheck = new Button { Content = "Check for updates" };
        _updateCheck.Click += (_, _) => _ = Updater.CheckAsync();
        _updateRestart = new Button { Content = "Restart now", Style = (Style)Application.Current.Resources["AccentButtonStyle"] };
        AutomationProperties.SetName(_updateRestart, "Restart to update");
        _updateRestart.Click += (_, _) => Updater.RestartNow();
        _updateNotes = new HyperlinkButton { Padding = new Thickness(0, 2, 0, 0), FontSize = 12 };
        _updateNotes.Click += (_, _) => ShowReleaseNotes();
        var text = new StackPanel { Spacing = 2, Children = { new TextBlock { Text = "Status", TextWrapping = TextWrapping.Wrap }, _updateStatus, _updateNotes } };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { _updateRestart, _updateCheck } };
        _page.Children.Insert(at + 1, CardBorder(TextAndControl(text, buttons))); // under the group's title
        ShowUpdateStatus();
    }

    private void ShowUpdateStatus()
    {
        if (_updateStatus == null || _updateCheck == null || _updateRestart == null)
        {
            return;
        }

        var status = Updater.Status;
        _updateStatus.Text = Updater.Describe();
        _updateCheck.Visibility = status == UpdateStatus.NotInstalled ? Visibility.Collapsed : Visibility.Visible;
        _updateCheck.IsEnabled = status is not (UpdateStatus.Checking or UpdateStatus.Downloading or UpdateStatus.Ready);
        _updateRestart.Visibility = status == UpdateStatus.Ready ? Visibility.Visible : Visibility.Collapsed;
        if (_updateNotes != null)
        {
            _updateNotes.Content = $"What's new in {Updater.NewVersion}";
            _updateNotes.Visibility = status == UpdateStatus.Ready && Updater.NewNotes.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    /// <summary>The downloaded update's release notes (CHANGELOG.md's entry), as headings and bullets.</summary>
    private void ShowReleaseNotes()
    {
        var body = new StackPanel { Spacing = 6 };
        foreach (var (kind, text) in ReleaseNotes.Pieces(Updater.NewNotes))
        {
            body.Children.Add(kind switch
            {
                NoteKind.Heading => new TextBlock { Text = text, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 6, 0, 0) },
                NoteKind.Bullet => new Grid
                {
                    ColumnSpacing = 8,
                    ColumnDefinitions = { new() { Width = GridLength.Auto }, new() { Width = new GridLength(1, GridUnitType.Star) } },
                    Children = { new TextBlock { Text = "•" }, Column(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap }, 1) },
                },
                _ => new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap },
            });
        }

        static FrameworkElement Column(FrameworkElement element, int column)
        {
            Grid.SetColumn(element, column);
            return element;
        }

        var dialog = new ContentDialog
        {
            Title = $"What's new in {Updater.NewVersion}",
            Content = new ScrollViewer { Content = body, MaxHeight = 420 },
            CloseButtonText = "Close",
            XamlRoot = _page.XamlRoot,
        };
        _ = Guarded(dialog);

        static async Task Guarded(ContentDialog dialog)
        {
            try
            {
                await dialog.ShowAsync();
            }
            catch (Exception ex)
            {
                Log.Crash("release notes", ex); // after an await: an escape would end the process
            }
        }
    }

    /// <summary>The debug log's file: show it, copy it (with the version and Windows on top) for a bug report, or start it afresh.</summary>
    private Border LogFileRow()
    {
        var copy = new Button { Content = "Copy" };
        AutomationProperties.SetName(copy, "Copy the debug log");
        copy.Click += (_, _) => Guard("copying the log", () =>
        {
            var text = Log.ReadText();
            ClipboardText.Set($"Zinc debug log: {Log.About}{Environment.NewLine}{Environment.NewLine}" +
                (text.Length > 0 ? text : "(empty: turn the debug log on, make the problem happen, then copy it)"));
            copy.Content = "Copied";
        });

        var clear = new Button { Content = "Clear" };
        AutomationProperties.SetName(clear, "Clear the debug log");
        clear.Click += (_, _) => Guard("clearing the log", () =>
        {
            Log.Clear();
            copy.Content = "Copy";
        });

        var open = new Button { Content = "Open folder" };
        AutomationProperties.SetName(open, "Open the debug log's folder");
        open.Click += (_, _) => Reveal(Log.FilePath);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { open, copy, clear } };
        return Row("Debug log file", Log.FilePath, buttons);
    }

    /// <summary>
    /// Where memory goes (<see cref="Zinc.Pty.MemoryReport"/>): Zinc's own, the .NET heap's share, and the programs it
    /// started, which Task Manager counts in Zinc's row. Measured when About opens and on Refresh.
    /// </summary>
    private void MemoryRow()
    {
        var (windows, panes) = App.Instance.Counts();
        var report = Zinc.Pty.MemoryReport.Measure(windows, panes);
        var copy = new Button { Content = "Copy" };
        AutomationProperties.SetName(copy, "Copy the memory details");
        copy.Click += (_, _) =>
        {
            ClipboardText.Set($"{report.Text()}{Environment.NewLine}{Log.About}");
            copy.Content = "Copied";
        };

        var refresh = new Button { Content = "Refresh" };
        AutomationProperties.SetName(refresh, "Measure memory again");
        refresh.Click += (_, _) => Render();
        _page.Children.Add(Row("Memory", report.Summary + ". Most of it is native: Windows' UI framework, the graphics card's driver and the terminals' screens and scrollback.",
            new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { refresh, copy } }));
    }

    private static StackPanel ButtonRow(params (string Label, Action Run)[] buttons)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        foreach (var (label, run) in buttons)
        {
            var button = new Button { Content = label };
            button.Click += (_, _) => run();
            row.Children.Add(button);
        }

        return row;
    }

    // ------------------------------------------------------------ profiles

    /// <summary>The default profile, every profile (found and yours, in your order), and where Zinc looks for shells.</summary>
    private void ProfilesPage()
    {
        _page.Children.Add(GroupTitle("Default"));
        var visible = _config.VisibleProfiles.ToList();
        var defaults = new ComboBox { Width = ControlWidth };
        AutomationProperties.SetName(defaults, "Default profile");
        foreach (var profile in visible)
        {
            defaults.Items.Add(profile.Name);
        }

        defaults.SelectedIndex = visible.FindIndex(p => p.Id == _config.DefaultProfile.Id);
        defaults.SelectionChanged += (_, _) =>
        {
            if (defaults.SelectedIndex >= 0)
            {
                Write(["default-profile"], visible[defaults.SelectedIndex].Id);
            }
        };
        _page.Children.Add(Row("Default profile", "New windows, workspaces and tabs open with it.", defaults));

        var add = new Button { Content = "New profile", HorizontalAlignment = HorizontalAlignment.Right };
        add.Click += (_, _) => NewProfile();
        var header = new Grid { Margin = new Thickness(0, 18, 0, 0) };
        header.Children.Add(new TextBlock { Text = "Profiles", FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        header.Children.Add(add);
        _page.Children.Add(header);
        _page.Children.Add(Note("Drag to reorder: the new-tab menu lists them in this order."));

        // The rows are their own ListViewItems, so a drag moves them as they are.
        var list = new ListView
        {
            SelectionMode = ListViewSelectionMode.None,
            CanReorderItems = true,
            CanDragItems = true,
            AllowDrop = true,
            Margin = new Thickness(0, 6, 0, 0),
        };
        AutomationProperties.SetName(list, "Profiles");
        var ids = _config.Profiles.Select(p => p.Id).ToList();
        _profilesShown = Listed(_config);
        foreach (var profile in _config.Profiles)
        {
            var item = new ListViewItem
            {
                Content = ProfileRow(profile, ids),
                Tag = profile.Id,
                Padding = new Thickness(0),
                Margin = new Thickness(0, 0, 0, 4),
                MinHeight = 0,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
            };
            AutomationProperties.SetName(item, profile.Name);
            list.Items.Add(item);
        }

        list.DragItemsCompleted += (_, e) =>
        {
            if (e.DropResult == DataPackageOperation.Move)
            {
                // Shown as dropped already: the reload that follows has nothing to re-render.
                var order = list.Items.OfType<ListViewItem>().Select(i => (string)i.Tag).ToList();
                _profilesShown = [.. order.Select(id => _profilesShown.First(p => p.Id == id))];
                WriteProfileOrder(order);
            }
        };
        _page.Children.Add(list);

        _page.Children.Add(GroupTitle("Find shells on this PC"));
        foreach (var search in SettingsCatalog.Discovery)
        {
            var card = Card(search);
            _page.Children.Add(card);
            if (card.Child is Grid { Children: [StackPanel text, ..] })
            {
                text.Children.Add(FoundBy(search.Path[1]));
            }
        }
    }

    private static List<(string Id, string Name, bool Hidden)> Listed(ConfigFile config) =>
        config.Profiles.Select(p => (p.Id, p.Name, p.Hidden)).ToList();

    /// <summary>The name of search <paramref name="source"/> ("SSH hosts"), as its switch shows it.</summary>
    private static string SearchLabel(string? source) =>
        SettingsCatalog.Discovery.FirstOrDefault(d => d.Path[1] == source)?.Label ?? source ?? "";

    /// <summary>What turns a profile that can't open back on: its search, or (gone for good) Delete, confirmed.</summary>
    private Button? MissingFix(Profile profile)
    {
        if (profile.Missing == ProfileMissing.SearchOff && profile.Source is { } source)
        {
            var on = new Button { Content = $"Turn on “{SearchLabel(source)}”" };
            on.Click += (_, _) => Write(["discover", source], null); // on is the default: the line goes
            return on;
        }

        if (profile.Missing != ProfileMissing.NotFound)
        {
            return null;
        }

        var delete = new Button { Content = "Delete" };
        AutomationProperties.SetName(delete, $"Delete {profile.Name}");
        var confirm = new Button { Content = "Delete it", Style = (Style)Application.Current.Resources["AccentButtonStyle"] };
        AutomationProperties.SetName(confirm, "Delete it");
        var flyout = new Flyout
        {
            Content = new StackPanel
            {
                Spacing = 10,
                MaxWidth = 300,
                Children = { new TextBlock { Text = "Removes its section from config.toml, with your changes to it.", TextWrapping = TextWrapping.Wrap }, confirm },
            },
        };
        delete.Flyout = flyout;
        confirm.Click += (_, _) =>
        {
            flyout.Hide();
            if (_config.HasErrors)
            {
                return;
            }

            Guard($"removing profile {profile.Id}", () =>
            {
                _writer.RemoveTable(["profile", profile.Id]);
                Wrote?.Invoke();
                UpdateChanges();
            });
            _editing = null;
            Render();
        };
        return delete;
    }

    /// <summary>Under a search's switch: what it found (links to each profile), or what it keeps while it's off.</summary>
    private FrameworkElement FoundBy(string source)
    {
        var mine = _config.Profiles.Where(p => p.Source == source).ToList();
        var found = mine.Where(p => p.Found).ToList();
        var kept = mine.Count(p => p.Missing == ProfileMissing.SearchOff);
        var line = new WrapPanel { HorizontalSpacing = 10, VerticalSpacing = 2, Margin = new Thickness(0, 4, 0, 0) };
        var label = new TextBlock
        {
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
        };
        line.Children.Add(label);
        if (found.Count > 0)
        {
            label.Text = "Found:";
            foreach (var profile in found)
            {
                var link = new HyperlinkButton { Content = profile.Name, FontSize = 12, Padding = new Thickness(0) };
                link.Click += (_, _) => GoTo("profiles", profile.Id, null);
                line.Children.Add(link);
            }
        }
        else
        {
            label.Text = kept > 0
                ? $"Off: {kept} profile{(kept == 1 ? "" : "s")} you changed {(kept == 1 ? "is" : "are")} kept, but can't open."
                : _config.ValueOf($"discover.{source}") is not false ? "Nothing found on this PC." : "Off.";
        }

        return line;
    }

    /// <summary>
    /// The profiles' order, as <c>profile-order</c> (every id, so profiles found later go after
    /// them). Without a default-profile the default is the first one, so moving profiles would
    /// change it: the default in force is written first.
    /// </summary>
    private void WriteProfileOrder(List<string> ids)
    {
        if (_config.ValueOf("default-profile") == null)
        {
            Write(["default-profile"], _config.DefaultProfile.Id);
        }

        Write(["profile-order"], ids.Cast<object>().ToList());
    }

    private Border ProfileRow(Profile profile, List<string> order)
    {
        var what = new List<string>();
        if (profile.Id == _config.DefaultProfile.Id)
        {
            what.Add("default");
        }

        what.Add(profile.Missing switch
        {
            ProfileMissing.NotFound => "not found on this PC any more",
            ProfileMissing.SearchOff => $"“{SearchLabel(profile.Source)}” is turned off",
            _ => profile.Found ? profile.InFile ? "found on this PC, with your changes" : "found on this PC" : "yours",
        });
        if (profile.Hidden && profile.Missing == ProfileMissing.None)
        {
            what.Add("hidden");
        }

        var edit = new Button { Content = "Edit", VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(edit, $"Edit {profile.Name}");
        edit.Click += (_, _) =>
        {
            _editing = profile.Id;
            Render();
            _scroller.ChangeView(null, 0, null, disableAnimation: true);
        };

        // Moving without a mouse (and for automation): the same order a drag writes.
        var at = order.IndexOf(profile.Id);
        void Move(int to)
        {
            var ids = order.ToList();
            ids.Remove(profile.Id);
            ids.Insert(Math.Clamp(to, 0, ids.Count), profile.Id);
            WriteProfileOrder(ids);
        }

        MenuFlyoutItem Duplicate()
        {
            var item = new MenuFlyoutItem { Text = "Duplicate" };
            item.Click += (_, _) => DuplicateProfile(profile, order);
            return item;
        }

        MenuFlyoutItem MoveItem(string text, int to)
        {
            var item = new MenuFlyoutItem { Text = text, IsEnabled = to != at && to >= 0 && to < order.Count };
            item.Click += (_, _) => Move(to);
            return item;
        }

        var more = new Button
        {
            Content = new FontIcon { Glyph = "", FontSize = 14 },
            Padding = new Thickness(8, 8, 8, 8),
            VerticalAlignment = VerticalAlignment.Center,
            Flyout = new MenuFlyout
            {
                Items =
                {
                    Duplicate(),
                    new MenuFlyoutSeparator(),
                    MoveItem("Move up", at - 1),
                    MoveItem("Move down", at + 1),
                    MoveItem("Move to the top", 0),
                },
            },
        };
        ToolTipService.SetToolTip(more, "More");
        AutomationProperties.SetName(more, $"More for {profile.Name}");

        var grid = new Grid { ColumnSpacing = 14 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        // A profile that can't open gets a warning, not the icon of the shell it would fall back to.
        var icon = new IconSourceElement
        {
            IconSource = profile.Missing == ProfileMissing.None
                ? ProfileIcons.For(profile)
                : new FontIconSource { Glyph = ((char)0xE7BA).ToString(), FontSize = 18 },
            Width = 20,
            Height = 20,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var text = new StackPanel
        {
            Spacing = 1,
            Opacity = profile.Hidden ? 0.6 : 1,
            Children =
            {
                new TextBlock { Text = profile.Name },
                new TextBlock
                {
                    Text = $"{string.Join(" · ", what)} · {profile.Id}",
                    FontSize = 12,
                    Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                },
            },
        };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { edit, more } };
        if (MissingFix(profile) is { } fix)
        {
            fix.VerticalAlignment = VerticalAlignment.Center;
            buttons.Children.Insert(0, fix);
        }
        Grid.SetColumn(text, 1);
        Grid.SetColumn(buttons, 2);
        grid.Children.Add(icon);
        grid.Children.Add(text);
        grid.Children.Add(buttons);
        return CardBorder(grid);
    }

    /// <summary>
    /// A copy of <paramref name="profile"/> as a profile of your own ("Ubuntu (copy)"): its own
    /// values, plus what it runs, where it starts and its icon when those came from discovery.
    /// Listed right after the original, and opened in the editor.
    /// </summary>
    private void DuplicateProfile(Profile profile, List<string> order)
    {
        if (_config.HasErrors)
        {
            return;
        }

        var n = 1;
        while (_config.FindProfile($"profile-{n}") != null || _config.ProfileValues.ContainsKey($"profile-{n}"))
        {
            n++;
        }

        var id = $"profile-{n}";
        var own = _config.ProfileValues.GetValueOrDefault(profile.Id) ?? new Dictionary<string, object>();
        Write(["profile", id, "name"], $"{profile.Name} (copy)");
        foreach (var (key, value) in own.Where(v => v.Key is not ("name" or "hidden")))
        {
            Write(["profile", id, .. key.Split('.')], value);
        }

        if (!own.ContainsKey("command"))
        {
            Write(["profile", id, "command"], profile.Config.Command ?? profile.FoundCommand ?? "pwsh.exe");
        }

        if (!own.ContainsKey("working-directory") && profile.Config.WorkingDirectory is { } folder)
        {
            Write(["profile", id, "working-directory"], folder);
        }

        if (!own.ContainsKey("icon") && profile.Config.Icon is { } icon)
        {
            Write(["profile", id, "icon"], icon);
        }

        var ids = order.ToList();
        ids.Insert(ids.IndexOf(profile.Id) + 1, id);
        WriteProfileOrder(ids);
        _editing = id;
        Render();
        _scroller.ChangeView(null, 0, null, disableAnimation: true);
    }

    /// <summary>A profile of your own, named "New profile", running what the default profile runs; opened in the editor.</summary>
    private void NewProfile()
    {
        var n = 1;
        while (_config.FindProfile($"profile-{n}") != null || _config.ProfileValues.ContainsKey($"profile-{n}"))
        {
            n++;
        }

        var id = $"profile-{n}";
        Write(["profile", id, "name"], "New profile");
        Write(["profile", id, "command"], _config.DefaultProfile.Config.Command ?? "pwsh.exe");
        _editing = id;
        Render();
    }

    /// <summary>One profile: its own fields, then every font, color and terminal setting it inherits or overrides.</summary>
    private void ProfileEditor(string id)
    {
        var profile = _config.FindProfile(id);
        var back = new HyperlinkButton { Content = "‹ All profiles", Padding = new Thickness(0, 2, 0, 2) };
        back.Click += (_, _) =>
        {
            _editing = null;
            Render();
        };
        _page.Children.Insert(0, back);
        if (profile is { Missing: not ProfileMissing.None })
        {
            var why = new InfoBar
            {
                IsOpen = true,
                IsClosable = false,
                Severity = InfoBarSeverity.Warning,
                Title = "This profile can't open",
                Message = profile.Missing == ProfileMissing.SearchOff
                    ? $"It was found by “{SearchLabel(profile.Source)}”, which is turned off. Your changes to it are kept in config.toml."
                    : "It was found on this PC, but isn't here any more. Your changes to it are kept in config.toml; delete it if it's gone for good.",
                ActionButton = MissingFix(profile),
                Margin = new Thickness(0, 0, 0, 8),
            };
            _page.Children.Add(why);
        }
        else
        {
            _page.Children.Add(Note(profile is { Found: true }
                ? $"Found on this PC ({profile.FoundCommand}). What you change here is kept in its own section in config.toml."
                : "Yours, in its own section in config.toml."));
        }

        _page.Children.Add(GroupTitle("General"));
        foreach (var field in SettingsCatalog.ProfileFields)
        {
            var hint = field.Key switch
            {
                "name" => profile?.FoundName ?? (profile is { Missing: not ProfileMissing.None } ? profile.Name : id),
                "command" => profile?.FoundCommand ?? "The default shell",
                _ => field.Placeholder,
            };
            _page.Children.Add(Card(field with { Placeholder = hint }, id));
        }

        _page.Children.Add(Note("The settings below come from the defaults until you change them here; the reset arrow gives one back."));
        foreach (var group in SettingsCatalog.All.Where(s => s.ProfileOverridable).GroupBy(s => s.Group))
        {
            var rows = new List<FrameworkElement>();
            foreach (var setting in group)
            {
                if (setting.Kind == SettingKind.Scheme)
                {
                    rows.AddRange(SchemeCards(setting, id));
                }
                else if (setting.Kind == SettingKind.Files)
                {
                    rows.Add(ShaderFilesCard(setting, id));
                }
                else
                {
                    rows.Add(OverrideCard(setting, id));
                }
            }

            _page.Children.Add(ProfileGroupFold(group.Key, id, [.. group], rows));
        }

        // Found profiles go back to what was found; your own can be deleted.
        var found = profile is { Found: true };
        var remove = new Button { Content = found ? "Undo all changes to this profile" : "Delete this profile", Margin = new Thickness(0, 24, 0, 0) };
        var confirm = new Button { Content = found ? "Undo them" : "Delete it", Style = (Style)Application.Current.Resources["AccentButtonStyle"] };
        var flyout = new Flyout
        {
            Content = new StackPanel
            {
                Spacing = 10,
                MaxWidth = 300,
                Children =
                {
                    new TextBlock
                    {
                        Text = found
                            ? "Removes its section from config.toml: back to what was found on this PC."
                            : "Removes its section from config.toml.",
                        TextWrapping = TextWrapping.Wrap,
                    },
                    confirm,
                },
            },
        };
        remove.Flyout = flyout;
        AutomationProperties.SetName(remove, found ? "Undo all changes to this profile" : "Delete this profile");
        AutomationProperties.SetName(confirm, found ? "Undo them" : "Delete it");
        confirm.Click += (_, _) =>
        {
            flyout.Hide();
            if (_config.HasErrors)
            {
                return;
            }

            Guard($"removing profile {id}", () =>
            {
                _writer.RemoveTable(["profile", id]);
                Wrote?.Invoke();
                UpdateChanges();
            });
            _editing = null;
            Render();
        };
        _page.Children.Add(remove);
    }

    /// <summary>
    /// A setting in a profile: shows the value in force for it, inherited ("Inherited: 11 pt")
    /// until changed here, which writes it into the profile's section; the reset arrow removes it
    /// again.
    /// </summary>
    private Border OverrideCard(SettingDef setting, string id)
    {
        IReadOnlyList<string> path = ["profile", id, .. setting.Path];
        var error = ErrorText();
        var reset = ResetArrow(setting.Label, "Back to the value every profile inherits");
        var description = DescriptionText();
        object? Inherited() => _config.ValueOf(setting.Key) ?? setting.Default;

        void State(object? own)
        {
            reset.Visibility = own != null ? Visibility.Visible : Visibility.Collapsed;
            InheritedText(description, setting, Describe(setting, Inherited()), own != null);
        }

        void Commit(object? value)
        {
            if (_updating)
            {
                return;
            }

            if (_config.HasErrors)
            {
                Show(error, "config.toml has an error; fix it first (see the top of the page).");
                return;
            }

            // The control's "default" (the included font, a number's default) is a value of the
            // profile's own here: without one, it inherits whatever the defaults say.
            value ??= setting.Kind == SettingKind.Font ? BundledFonts.Family : setting.Default;
            if (value != null && SettingsCatalog.Problem(setting, value) is { } problem)
            {
                Show(error, problem);
                return;
            }

            error.Visibility = Visibility.Collapsed;
            Write(path, value);
            State(value);
        }

        var (control, show) = Control(setting, Commit, id);

        void Refresh()
        {
            var own = _config.ValueOf(id, setting.Key);
            show(own ?? Inherited());
            State(own);
        }

        reset.Click += (_, _) =>
        {
            FocusAfterReset(reset, control);
            Write(path, null);
            error.Visibility = Visibility.Collapsed;
            Quietly(() =>
            {
                show(Inherited());
                State(null);
            });
        };
        _refreshers.Add(Refresh);
        Quietly(Refresh);
        var card = CardLayout(new TextBlock { Text = setting.Label }, reset, description, $"profile.{id}.{setting.Key}", error, control);
        _cards[(id, setting.Key)] = (card, control);
        return card;
    }

    /// <summary>Writes one key (null removes it), unless the file has an error.</summary>
    private void Write(IReadOnlyList<string> path, object? value)
    {
        if (_updating || _config.HasErrors)
        {
            return;
        }

        Guard($"saving {string.Join('.', path)}", () =>
        {
            _writer.Set(path, value);
            Wrote?.Invoke();
            UpdateChanges();
        });
    }

    /// <summary>Runs <paramref name="work"/> as showing values, not changing them.</summary>
    private void Quietly(Action work)
    {
        var was = _updating;
        _updating = true;
        try
        {
            work();
        }
        finally
        {
            _updating = was;
        }
    }

    // ------------------------------------------------------------ cards

    /// <summary>A setting's card: its name (and the reset arrow while it's changed), what it does, its config key, and the control.</summary>
    private Border Card(SettingDef setting, string? profile = null)
    {
        // A profile's own field ([profile.<id>] name, command, ...), or a global setting.
        IReadOnlyList<string> path = profile == null ? setting.Path : ["profile", profile, .. setting.Path];
        var error = ErrorText();
        var reset = ResetArrow(setting.Label, "Back to the default (removes it from config.toml)");
        var description = DescriptionText();
        bool Changed(object? value) => value != null && !SettingsCatalog.Same(value, setting.Default);

        void State(object? value)
        {
            reset.Visibility = Changed(value) ? Visibility.Visible : Visibility.Collapsed;
            SetText(description, Explain(setting, value));
        }

        void Commit(object? value)
        {
            if (_updating)
            {
                return;
            }

            if (_config.HasErrors)
            {
                Show(error, "config.toml has an error; fix it first (see the top of the page).");
                return;
            }

            if (value != null && profile == null && SettingsCatalog.Problem(setting, value) is { } problem)
            {
                Show(error, problem);
                return;
            }

            error.Visibility = Visibility.Collapsed;
            Guard($"saving {setting.Key}", () =>
            {
                _writer.Set(path, Changed(value) ? value : null);
                Wrote?.Invoke();
                UpdateChanges();
                State(value);
            });
        }

        var (control, show) = Control(setting, Commit, profile);
        reset.Click += (_, _) =>
        {
            FocusAfterReset(reset, control);
            Commit(null);
            Quietly(() => show(null));
        };

        void Refresh()
        {
            var value = profile == null ? _config.ValueOf(setting.Key) : _config.ValueOf(profile, setting.Key);
            State(value);
            show(value);
        }

        _refreshers.Add(Refresh);
        Quietly(Refresh);
        var card = CardLayout(new TextBlock { Text = setting.Label }, reset, description,
            profile == null ? setting.Key : $"profile.{profile}.{setting.Key}", error, control);
        _cards[(profile, setting.Key)] = (card, control);
        return card;
    }

    /// <summary>
    /// The color scheme as cards: "Follow Windows light/dark", then the scheme ("Dark scheme"
    /// while following) and, while following, "Light scheme". Written as one key, a name or
    /// <c>"light:X,dark:Y"</c>. In a profile (<paramref name="profile"/>), inherited until changed.
    /// </summary>
    private IEnumerable<Border> SchemeCards(SettingDef setting, string? profile)
    {
        IReadOnlyList<string> path = profile == null ? setting.Path : ["profile", profile, .. setting.Path];
        var names = _config.Themes.Names();

        // Pickers showing each scheme as a tile. A profile has no "Default (Lit Cell)" to pick:
        // without a scheme of its own it inherits.
        var scheme = new SchemePicker(_config.Themes, names, profile == null ? DefaultSchemeLabel : null, "Color scheme");
        var light = new SchemePicker(_config.Themes, names, null, "Light scheme");
        var (followSwitch, follow) = Switch("Follow Windows light/dark");
        var label = new TextBlock { Text = setting.Label };
        var description = DescriptionText();
        var reset = ResetArrow(setting.Label, profile == null
            ? "Back to the default (removes it from config.toml)"
            : "Back to the scheme every profile inherits");
        var lightCard = CardLayout(new TextBlock { Text = "Light scheme" }, null, DescriptionText("While Windows is in light mode."), null, null, light);
        var local = false; // showing a value: not a change

        object? Own() => profile == null ? _config.ValueOf(setting.Key) : _config.ValueOf(profile, setting.Key);
        object? Inherited() => profile == null ? null : _config.ValueOf(setting.Key);

        void Layout(bool pair, object? own)
        {
            label.Text = pair ? "Dark scheme" : setting.Label;
            lightCard.Visibility = pair ? Visibility.Visible : Visibility.Collapsed;
            reset.Visibility = own != null ? Visibility.Visible : Visibility.Collapsed;
            if (pair || profile == null)
            {
                SetText(description, pair ? "While Windows is in dark mode." : setting.Description);
            }
            else
            {
                InheritedText(description, setting, Describe(setting, Inherited()), own != null);
            }
        }

        void Display(object? value, object? own)
        {
            local = true;
            var text = value as string;
            var pair = text != null && ThemeLibrary.TryParsePair(text, out _, out _);
            follow.IsOn = pair;
            if (pair && ThemeLibrary.TryParsePair(text!, out var l, out var d))
            {
                scheme.Show(d, scheme.Contains(d));
                light.Show(l, light.Contains(l));
            }
            else if (text != null)
            {
                scheme.Show(text, scheme.Contains(text));
            }
            else
            {
                scheme.Show(null, selected: profile == null); // Zinc's own; a profile: placeholder
            }

            scheme.Placeholder = Describe(setting, value);
            light.Placeholder = "Choose a scheme";
            Layout(pair, own);
            local = false;
        }

        void Commit(string? value)
        {
            Write(path, value);
            Layout(follow.IsOn, value);
        }

        void Changed()
        {
            if (local || _updating)
            {
                return;
            }

            if (follow.IsOn)
            {
                if (light.Selected is { } l && scheme.Selected is { } d)
                {
                    Commit($"light:{l},dark:{d}");
                }
            }
            else if (scheme.HasSelection)
            {
                Commit(scheme.Selected); // null: Zinc's own
            }
        }

        scheme.Picked += _ => Changed();
        light.Picked += _ => Changed();
        // Moving through a list previews each scheme (by itself, even while following light/dark).
        scheme.Highlighted += name => PreviewScheme(path, name);
        light.Highlighted += name => PreviewScheme(path, name);
        scheme.HighlightEnded += RefreshPreview;
        light.HighlightEnded += RefreshPreview;
        follow.Toggled += (_, _) =>
        {
            if (local || _updating)
            {
                return;
            }

            if (follow.IsOn)
            {
                // The scheme in use becomes the dark side (Zinc's own: Lit Cell), and the light
                // side starts from its counterpart.
                local = true;
                var dark = scheme.Selected ?? names.FirstOrDefault(n => n == "Lit Cell") ?? names.FirstOrDefault();
                scheme.Show(dark);
                light.Show(ThemeLibrary.LightCounterpart(dark, names));
                local = false;
            }

            Changed();
        };
        reset.Click += (_, _) =>
        {
            FocusAfterReset(reset, scheme);
            Write(path, null);
            Display(Inherited(), null);
        };

        void Refresh()
        {
            var own = Own();
            Display(own ?? Inherited(), own);
        }

        _refreshers.Add(Refresh);
        Quietly(Refresh);
        var followCard = CardLayout(new TextBlock { Text = "Follow Windows light/dark" }, null,
            DescriptionText("Two schemes: one while Windows is light, one while it's dark."), null, null, followSwitch);
        var main = CardLayout(label, reset, description, profile == null ? setting.Key : $"profile.{profile}.{setting.Key}", null, scheme);
        _cards[(profile, setting.Key)] = (main, scheme);

        // Customize: the scheme(s) in force, edited on a page of their own.
        var customizeDark = new Button();
        var customizeLight = new Button();
        string? darkName = null, lightName = null;
        customizeDark.Click += (_, _) => CustomizeScheme(darkName, path);
        customizeLight.Click += (_, _) => CustomizeScheme(lightName, path);
        var customizeText = DescriptionText();
        void ShowCustomize()
        {
            var value = (Own() ?? Inherited()) as string;
            if (value != null && ThemeLibrary.TryParsePair(value, out var l, out var d))
            {
                (darkName, lightName) = (d, l);
            }
            else
            {
                (darkName, lightName) = (value, null);
            }

            customizeDark.Content = $"Customize {darkName ?? "Lit Cell"}";
            customizeLight.Content = $"Customize {lightName}";
            customizeLight.Visibility = lightName != null ? Visibility.Visible : Visibility.Collapsed;
            SetText(customizeText, darkName == null
                ? "Change its colors, live. The default colors are Lit Cell's: editing them makes a copy you can change."
                : "Change its colors, live: every pane using it follows.");
        }

        _refreshers.Add(ShowCustomize);
        ShowCustomize();
        var newScheme = new Button { Content = "New scheme…", Flyout = NewSchemeFlyout(() => darkName ?? "Lit Cell", () => darkName, path) };
        var customizeCard = CardLayout(new TextBlock { Text = "Edit colors" }, null, customizeText, null, null,
            new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { customizeDark, customizeLight, newScheme } });
        return [followCard, main, lightCard, customizeCard];
    }

    /// <summary>A setting's description, and what its chosen option means.</summary>
    private static string Explain(SettingDef setting, object? value)
    {
        var detail = setting.Kind == SettingKind.Choice
            ? setting.Choices.FirstOrDefault(c => SettingsCatalog.Same(c.Value, value ?? setting.Default))?.Detail
            : null;
        return string.Join(" ", new[] { setting.Description, detail }.Where(t => !string.IsNullOrEmpty(t)));
    }

    /// <summary>A value as words, for "Inherited: …".</summary>
    private static string Describe(SettingDef setting, object? value) => value switch
    {
        null => setting.Kind switch
        {
            SettingKind.Color => "the scheme's",
            SettingKind.Font => BundledFonts.Family,
            SettingKind.Scheme => DefaultSchemeLabel,
            _ => "the default",
        },
        _ when setting.Kind == SettingKind.Choice && setting.Choices.FirstOrDefault(c => SettingsCatalog.Same(c.Value, value)) is { } choice =>
            choice.Label,
        bool b => b ? "on" : "off",
        string t when setting.Kind == SettingKind.Scheme && ThemeLibrary.TryParsePair(t, out var l, out var d) => $"{d} / {l}",
        _ when setting.Kind == SettingKind.Choice => Format(value),
        _ => setting.Unit is { } unit ? $"{Format(value)} {unit}" : Format(value),
    };

    /// <summary>The small arrow beside a setting's name that puts it back; shown while it's changed.</summary>
    private static Button ResetArrow(string name, string tip)
    {
        var button = new Button
        {
            Content = UTurnArrow(),
            Padding = new Thickness(5),
            Margin = new Thickness(0, -4, 0, -4),
            Background = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Collapsed,
        };
        ToolTipService.SetToolTip(button, tip);
        AutomationProperties.SetName(button, $"Reset {name}");
        return button;
    }

    /// <summary>
    /// An arrow that goes left and turns back down (the shape of Heroicons' arrow-uturn-left, MIT):
    /// "put it back". Drawn in a 24-unit box with a 1.5-unit stroke, like the original.
    /// </summary>
    private static Viewbox UTurnArrow()
    {
        static PathFigure Figure(Point start, params PathSegment[] segments)
        {
            var figure = new PathFigure { StartPoint = start, IsClosed = false, IsFilled = false };
            foreach (var segment in segments)
            {
                figure.Segments.Add(segment);
            }

            return figure;
        }

        var geometry = new PathGeometry();
        // The arrowhead, then the shaft: left along the top, round the bend, back along the bottom.
        geometry.Figures.Add(Figure(new Point(9, 15), new LineSegment { Point = new Point(3, 9) }, new LineSegment { Point = new Point(9, 3) }));
        geometry.Figures.Add(Figure(new Point(3, 9),
            new LineSegment { Point = new Point(15, 9) },
            new ArcSegment { Point = new Point(15, 21), Size = new Size(6, 6), SweepDirection = SweepDirection.Clockwise },
            new LineSegment { Point = new Point(12, 21) }));
        var path = new Microsoft.UI.Xaml.Shapes.Path
        {
            Data = geometry,
            Stroke = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            StrokeThickness = 1.5,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
        };
        return new Viewbox { Width = 14, Height = 14, Child = new Canvas { Width = 24, Height = 24, Children = { path } } };
    }

    private static TextBlock DescriptionText(string text = "")
    {
        var block = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
        };
        SetText(block, text);
        return block;
    }

    private static void SetText(TextBlock block, string text)
    {
        block.Text = text;
        block.Visibility = text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static TextBlock KeyText(string key) => new()
    {
        Text = key,
        FontFamily = new FontFamily("Cascadia Mono, Consolas"),
        FontSize = 11,
        Foreground = (Brush)Application.Current.Resources["TextFillColorTertiaryBrush"],
    };

    private static TextBlock ErrorText() => new()
    {
        Foreground = (Brush)Application.Current.Resources["SystemFillColorCriticalBrush"], // red that reads on light and dark
        TextWrapping = TextWrapping.Wrap,
        Visibility = Visibility.Collapsed,
        Margin = new Thickness(0, 6, 0, 0),
    };

    /// <summary>A card: name (+ reset arrow), description, config key and error on the left; the control on the right.</summary>
    private static Border CardLayout(TextBlock label, Button? reset, TextBlock? description, string? key, TextBlock? error, FrameworkElement control)
    {
        label.TextWrapping = TextWrapping.Wrap;
        label.VerticalAlignment = VerticalAlignment.Center;
        var title = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children = { label } };
        if (reset != null)
        {
            title.Children.Add(reset);
        }

        var text = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center, Children = { title } };
        if (description != null)
        {
            text.Children.Add(description);
        }

        if (key != null)
        {
            text.Children.Add(KeyText(key));
        }

        if (error != null)
        {
            text.Children.Add(error);
        }

        return CardBorder(TextAndControl(text, control));
    }

    /// <summary>Below this width a card's control goes under its text (Windows Terminal's threshold for its cards, less their padding).</summary>
    private const double NarrowCard = 440;

    /// <summary>One width for dropdowns, text boxes and pickers, so their left edges line up down the page.</summary>
    private const double ControlWidth = 280;

    /// <summary>Text on the left, the control on the right; on a narrow card, the control under the text.</summary>
    private static Grid TextAndControl(FrameworkElement text, FrameworkElement control)
    {
        control.VerticalAlignment = VerticalAlignment.Center;
        var grid = new Grid { ColumnSpacing = 24 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetColumn(control, 1);
        grid.Children.Add(text);
        grid.Children.Add(control);
        var align = control.HorizontalAlignment;
        grid.SizeChanged += (_, e) =>
        {
            var narrow = e.NewSize.Width < NarrowCard;
            Grid.SetColumn(control, narrow ? 0 : 1);
            Grid.SetRow(control, narrow ? 1 : 0);
            grid.ColumnSpacing = narrow ? 0 : 24;
            grid.RowSpacing = narrow ? 8 : 0; // (spacing applies even to an empty row)
            control.HorizontalAlignment = narrow ? HorizontalAlignment.Left : align;
        };
        return grid;
    }

    /// <summary>
    /// A switch with its On/Off label on its left, so switches line up at the cards' right edge
    /// (WinUI puts the label on the right, where it pushes the switch in by its width).
    /// </summary>
    private static (FrameworkElement Panel, ToggleSwitch Toggle) Switch(string name)
    {
        var toggle = new ToggleSwitch { OnContent = "", OffContent = "", MinWidth = 0, Margin = new Thickness(0, 0, -12, 0) };
        AutomationProperties.SetName(toggle, name);
        var state = new TextBlock { Text = "Off", VerticalAlignment = VerticalAlignment.Center };
        toggle.Toggled += (_, _) => state.Text = toggle.IsOn ? "On" : "Off";
        return (new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Children = { state, toggle } }, toggle);
    }

    /// <summary>
    /// After a reset its arrow disappears, taking keyboard focus with it: hand focus to the
    /// setting's control instead (as Windows Terminal does).
    /// </summary>
    private static void FocusAfterReset(Button reset, FrameworkElement control)
    {
        if (reset.FocusState == FocusState.Unfocused)
        {
            return;
        }

        var how = reset.FocusState == FocusState.Keyboard ? FocusState.Keyboard : FocusState.Programmatic;
        control.DispatcherQueue.SafeEnqueue(() =>
        {
            var target = control as Control ?? Microsoft.UI.Xaml.Input.FocusManager.FindFirstFocusableElement(control) as Control;
            target?.Focus(how);
        });
    }

    private static Border Row(string title, string detail, FrameworkElement? control)
    {
        var text = new StackPanel
        {
            Spacing = 2,
            Children =
            {
                new TextBlock { Text = title, TextWrapping = TextWrapping.Wrap },
                new TextBlock
                {
                    Text = detail,
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 12,
                    IsTextSelectionEnabled = true,
                    Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                },
            },
        };
        return CardBorder(control != null ? TextAndControl(text, control) : text);
    }

    private static Border CardBorder(UIElement child) => new()
    {
        Child = child,
        Padding = new Thickness(16, 12, 16, 12),
        CornerRadius = new CornerRadius(6),
        BorderThickness = new Thickness(1),
        Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
        BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
    };

    private static void Show(TextBlock error, string message)
    {
        error.Text = message;
        error.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// The control for <paramref name="setting"/>, calling <paramref name="commit"/> with each new
    /// value (null: the default), and how to show a value from the file in it.
    /// </summary>
    private (FrameworkElement Control, Action<object?> Show) Control(SettingDef setting, Action<object?> commit, string? profile = null) => setting.Kind switch
    {
        SettingKind.Toggle => ToggleControl(setting, commit),
        SettingKind.Choice => ChoiceControl(setting, commit),
        SettingKind.Number => NumberControl(setting, commit),
        SettingKind.Color => ColorControl(setting, commit, profile),
        SettingKind.Font => FontControl(commit),
        _ when profile != null && setting.Section == "profiles" && setting.Key is "command" or "working-directory" or "icon" =>
            BrowseControl(setting, commit),
        _ => TextControl(setting, commit), // SettingKind.Scheme has cards of its own (SchemeCards)
    };

    private static (FrameworkElement, Action<object?>) ToggleControl(SettingDef setting, Action<object?> commit)
    {
        var (panel, toggle) = Switch(setting.Label);
        toggle.Toggled += (_, _) => commit(toggle.IsOn);
        return (panel, value => toggle.IsOn = value as bool? ?? setting.Default as bool? ?? false);
    }

    private static (FrameworkElement, Action<object?>) ChoiceControl(SettingDef setting, Action<object?> commit)
    {
        var box = new ComboBox { Width = ControlWidth };
        AutomationProperties.SetName(box, setting.Label);
        foreach (var choice in setting.Choices)
        {
            box.Items.Add(choice.Label);
        }

        box.SelectionChanged += (_, _) =>
        {
            if (box.SelectedIndex >= 0 && box.SelectedIndex < setting.Choices.Count)
            {
                commit(setting.Choices[box.SelectedIndex].Value);
            }
        };
        return (box, value =>
        {
            var wanted = value ?? setting.Default;
            var index = setting.Choices.ToList().FindIndex(c => SettingsCatalog.Same(c.Value, wanted));
            box.SelectedIndex = index;
            box.PlaceholderText = index < 0 && value != null ? $"{value} (set in config.toml)" : "";
        });
    }

    private static (FrameworkElement, Action<object?>) NumberControl(SettingDef setting, Action<object?> commit)
    {
        var box = new NumberBox
        {
            Minimum = setting.Minimum,
            Maximum = setting.Maximum,
            SmallChange = setting.Step,
            LargeChange = setting.Step * 4,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
            Width = 130,
        };
        AutomationProperties.SetName(box, setting.Label);
        var special = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Opacity = 0.7, Visibility = Visibility.Collapsed };
        box.ValueChanged += (_, e) =>
            commit(double.IsNaN(e.NewValue) ? null : SettingsCatalog.FromText(setting, e.NewValue.ToString(CultureInfo.InvariantCulture)));
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { special, box } };
        if (setting.Unit != null)
        {
            panel.Children.Add(new TextBlock { Text = setting.Unit, VerticalAlignment = VerticalAlignment.Center, Opacity = 0.7 });
        }

        return (panel, value =>
        {
            // Padding written as [left, right]: shown, edited in the file.
            var list = value is List<object>;
            special.Text = list ? $"{ConfigEditor.FormatValue(value!)} in config.toml" : "";
            special.Visibility = list ? Visibility.Visible : Visibility.Collapsed;
            box.Visibility = list ? Visibility.Collapsed : Visibility.Visible;
            box.Value = value is long or double ? Convert.ToDouble(value, CultureInfo.InvariantCulture)
                : setting.Default is { } d ? Convert.ToDouble(d, CultureInfo.InvariantCulture) : double.NaN;
        });
    }

    private static (FrameworkElement, Action<object?>) TextControl(SettingDef setting, Action<object?> commit)
    {
        var box = new TextBox { Width = ControlWidth, PlaceholderText = setting.Placeholder ?? Format(setting.Default) };
        AutomationProperties.SetName(box, setting.Label);
        string? shown = null;
        void Apply()
        {
            if (box.Text != shown)
            {
                shown = box.Text;
                commit(SettingsCatalog.FromText(setting, box.Text));
            }
        }

        box.LostFocus += (_, _) => Apply();
        box.KeyDown += (_, e) =>
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                Apply();
                e.Handled = true;
            }
        };
        return (box, value =>
        {
            if (box.FocusState == FocusState.Unfocused)
            {
                box.Text = shown = Format(value);
            }
        });
    }

    /// <summary>A text box with a folder button beside it: a profile's command (a program), starting folder or icon (an image).</summary>
    private (FrameworkElement, Action<object?>) BrowseControl(SettingDef setting, Action<object?> commit)
    {
        var (text, show) = TextControl(setting, commit);
        var box = (TextBox)text;
        box.Width = ControlWidth - 40 - 8;
        var browse = new Button { Content = new FontIcon { Glyph = ((char)0xE838).ToString(), FontSize = 14 }, VerticalAlignment = VerticalAlignment.Center };
        var what = setting.Key switch { "command" => "Browse for a program", "icon" => "Browse for an image", _ => "Browse for a folder" };
        ToolTipService.SetToolTip(browse, what);
        AutomationProperties.SetName(browse, what);
        browse.Click += async (_, _) =>
        {
            // Code after an await resumes as queued work, where an exception would end the app.
            try
            {
                if (await PickPath(setting.Key) is { } path)
                {
                    // A program's path with spaces is quoted, so arguments can follow it.
                    box.Text = setting.Key == "command" && path.Contains(' ') ? '"' + path + '"' : path;
                    commit(SettingsCatalog.FromText(setting, box.Text));
                }
            }
            catch (Exception ex)
            {
                Log.Crash("settings: browsing", ex);
            }
        };
        return (new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { box, browse } }, show);
    }

    private async Task<string?> PickPath(string key)
    {
        var window = WinRT.Interop.WindowNative.GetWindowHandle(this);
        if (key == "working-directory")
        {
            var folders = new Windows.Storage.Pickers.FolderPicker();
            folders.FileTypeFilter.Add("*");
            WinRT.Interop.InitializeWithWindow.Initialize(folders, window);
            return (await folders.PickSingleFolderAsync())?.Path;
        }

        var files = new Windows.Storage.Pickers.FileOpenPicker();
        foreach (var type in key == "command" ? [".exe", ".cmd", ".bat", ".com"] : new[] { ".png", ".ico", ".jpg", ".jpeg", ".bmp" })
        {
            files.FileTypeFilter.Add(type);
        }

        WinRT.Interop.InitializeWithWindow.Initialize(files, window);
        return (await files.PickSingleFileAsync())?.Path;
    }

    /// <summary>
    /// A color: typed as #rrggbb, or picked from the scheme's 16 colors (named on hover), "use the
    /// scheme's" (a visible choice, with a swatch of what that is), or any color with the full
    /// picker, applied live while you drag (a few times a second).
    /// </summary>
    private (FrameworkElement, Action<object?>) ColorControl(SettingDef setting, Action<object?> commit, string? profile)
    {
        // What "none" means for this setting, and its color under the scheme in force.
        var nullLabel = setting.NullLabel ?? (setting.Key == "window.unfocused-split-fill" ? "Use each pane's background" : "Use the scheme's color");
        Color NullColor()
        {
            // The tab dots default to the brand's colors, not the scheme's.
            var dark = Brand.IsDark(_page);
            switch (setting.Key)
            {
                case "notify.finished-color": return Brand.Attention(dark);
                case "notify.bell-color": return Brand.CautionFor(dark);
                case "notify.failed-color": return Brand.ErrorFor(dark);
            }

            var theme = SchemeTheme(profile);
            return SchemeTiles.ToColor(setting.Key switch
            {
                "colors.foreground" => theme.Foreground,
                "colors.cursor" => theme.Cursor,
                _ => theme.Background,
            });
        }

        var swatch = new Border { Width = 22, Height = 22, CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1) };
        swatch.BorderBrush = (Brush)Application.Current.Resources["ControlStrongStrokeColorDefaultBrush"];
        var box = new TextBox { Width = ControlWidth - 22 - 8 - 40 - 8, PlaceholderText = setting.Placeholder ?? "The scheme's" };
        AutomationProperties.SetName(box, setting.Label);

        // The flyout: the scheme's colors as chips, "use the scheme's", and the full picker below.
        var chips = new Grid { ColumnSpacing = 4, RowSpacing = 4 };
        var chipButtons = new Button[16];
        var chipFills = new SolidColorBrush[16];
        for (var i = 0; i < 4; i++)
        {
            chips.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            chips.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }

        for (var i = 0; i < 16; i++)
        {
            var chip = new Button
            {
                Width = 30,
                Height = 30,
                Padding = new Thickness(0),
                CornerRadius = new CornerRadius(4),
                BorderThickness = new Thickness(2),
            };

            // The chip keeps its color in every state (a button's hover and press fills would hide
            // it); hovering shows a ring instead. One brush per chip, recolored for each scheme.
            var fill = chipFills[i] = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            chip.Background = fill;
            foreach (var key in new[] { "ButtonBackground", "ButtonBackgroundPointerOver", "ButtonBackgroundPressed" })
            {
                chip.Resources[key] = fill;
            }

            chip.Resources["ButtonBorderBrushPointerOver"] = Application.Current.Resources["TextFillColorPrimaryBrush"];
            chip.Resources["ButtonBorderBrushPressed"] = Application.Current.Resources["TextFillColorSecondaryBrush"];
            Grid.SetColumn(chip, i % 4);
            Grid.SetRow(chip, i / 4);
            chips.Children.Add(chip);
            chipButtons[i] = chip;
        }

        var nullSwatch = new Border { Width = 16, Height = 16, CornerRadius = new CornerRadius(3), BorderThickness = new Thickness(1) };
        nullSwatch.BorderBrush = (Brush)Application.Current.Resources["ControlStrongStrokeColorDefaultBrush"];
        var useScheme = new ToggleButton
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { nullSwatch, new TextBlock { Text = nullLabel } } },
        };
        AutomationProperties.SetName(useScheme, nullLabel);
        var more = new Button { Content = "More colors…", HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left };
        var picker = new ColorPicker
        {
            IsAlphaEnabled = false,
            IsMoreButtonVisible = true,
            ColorSpectrumShape = ColorSpectrumShape.Ring,
            Visibility = Visibility.Collapsed,
        };
        var top = new Grid { ColumnSpacing = 16 };
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var side = new StackPanel { Spacing = 8, MinWidth = 200, Children = { useScheme, more } };
        Grid.SetColumn(side, 1);
        top.Children.Add(chips);
        top.Children.Add(side);
        var flyout = new Flyout { Content = new StackPanel { Spacing = 16, Children = { top, picker } } };
        var pick = new Button { Content = new FontIcon { Glyph = ((char)0xE790).ToString(), FontSize = 14 }, Flyout = flyout };
        AutomationProperties.SetName(pick, $"Pick a color for {setting.Label}");
        ToolTipService.SetToolTip(pick, "Pick a color");

        string? shown = null;
        var open = false;      // the flyout is open
        var dragging = false;  // the full picker is out: its changes are written as they settle
        void Paint(string? hex) => swatch.Background = new SolidColorBrush(hex != null && TryHex(hex, out var c) ? c : NullColor());
        void Apply(string text)
        {
            if (!string.Equals(text, shown, StringComparison.OrdinalIgnoreCase))
            {
                shown = text;
                Paint(text);
                commit(text.Trim().Length == 0 ? null : text.Trim());
            }

            Mark();
        }

        // Which choice is current: a chip with the same color, or "use the scheme's".
        void Mark()
        {
            var has = TryHex(box.Text, out var current);
            var colors = SchemeTheme(profile).BaseColors;
            for (var i = 0; i < 16; i++)
            {
                var chip = SchemeTiles.ToColor(colors[i]);
                var selected = has && chip == current;
                chipButtons[i].BorderBrush = selected
                    ? (Brush)Application.Current.Resources["TextFillColorPrimaryBrush"]
                    : new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            }

            useScheme.IsChecked = box.Text.Trim().Length == 0;
        }

        var settle = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread().CreateTimer();
        settle.Interval = TimeSpan.FromMilliseconds(150);
        settle.IsRepeating = false;

        void PutPickerAway()
        {
            dragging = false;
            settle.Stop();
            picker.Visibility = Visibility.Collapsed;
            more.Visibility = Visibility.Visible;
        }

        for (var i = 0; i < 16; i++)
        {
            var index = i;
            chipButtons[i].Click += (_, _) =>
            {
                PutPickerAway();
                box.Text = SchemeTiles.Hex(SchemeTiles.ToColor(SchemeTheme(profile).BaseColors[index]));
                Apply(box.Text);
            };
        }

        useScheme.Click += (_, _) =>
        {
            PutPickerAway();
            box.Text = "";
            Apply("");
        };
        more.Click += (_, _) =>
        {
            picker.Color = TryHex(box.Text, out var c) ? c : NullColor();
            picker.Visibility = Visibility.Visible;
            more.Visibility = Visibility.Collapsed;
            dragging = true;
        };

        // While the full picker is out, each change is written once it settles for a moment:
        // live, without a config reload per pixel of a drag.
        void Picked()
        {
            box.Text = SchemeTiles.Hex(picker.Color);
            Apply(box.Text);
        }

        settle.OnTick(_ =>
        {
            if (open && dragging)
            {
                Picked();
            }
        });
        picker.ColorChanged += (_, _) =>
        {
            if (open && dragging)
            {
                settle.Stop();
                settle.Start();
            }
        };

        box.LostFocus += (_, _) => Apply(box.Text);
        box.KeyDown += (_, e) =>
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                Apply(box.Text);
                e.Handled = true;
            }
        };
        flyout.Opening += (_, _) =>
        {
            // The chips show the scheme in force now (it may have changed since the page opened).
            var colors = SchemeTheme(profile).BaseColors;
            for (var i = 0; i < 16; i++)
            {
                var color = SchemeTiles.ToColor(colors[i]);
                chipFills[i].Color = color;
                var name = $"{SchemeTiles.ColorNames[i]} {SchemeTiles.Hex(color)}";
                ToolTipService.SetToolTip(chipButtons[i], name);
                AutomationProperties.SetName(chipButtons[i], name);
            }

            nullSwatch.Background = new SolidColorBrush(NullColor());
            picker.Visibility = Visibility.Collapsed;
            more.Visibility = Visibility.Visible;
            dragging = false;
            Mark();
        };
        flyout.Opened += (_, _) => open = true;
        flyout.Closed += (_, _) =>
        {
            open = false;
            settle.Stop();
            if (dragging)
            {
                Picked(); // the last movement, if it hadn't settled yet
            }

            dragging = false;
        };
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { swatch, box, pick } };
        return (panel, value =>
        {
            if (box.FocusState == FocusState.Unfocused && !open)
            {
                box.Text = shown = value as string ?? "";
                Paint(shown.Length == 0 ? null : shown);
            }
        });
    }

    /// <summary>The colors of the scheme in force for <paramref name="profile"/> (null: the defaults), on today's Windows mode.</summary>
    private TerminalTheme SchemeTheme(string? profile)
    {
        var name = (profile != null ? _config.ValueOf(profile, "colors.theme") : null) ?? _config.ValueOf("colors.theme");
        return name is string n
            ? SchemeTiles.ThemeOf(_config.Themes, ThemeLibrary.Resolve(n, ConfigFile.SystemScheme)) ?? TerminalTheme.Default
            : TerminalTheme.Default;
    }

    private static (FrameworkElement, Action<object?>) FontControl(Action<object?> commit)
    {
        var box = new ComboBox { Width = ControlWidth };
        AutomationProperties.SetName(box, "Font");
        box.Items.Add(BundledFontLabel);
        foreach (var family in WelcomeWindow.MonospaceFamilies())
        {
            box.Items.Add(family);
        }

        box.SelectionChanged += (_, _) =>
        {
            if (box.SelectedItem is string family)
            {
                commit(family == BundledFontLabel ? null : family);
            }
        };
        return (box, value =>
        {
            var family = value as string;
            if (family != null && !family.Equals(BundledFonts.Family, StringComparison.OrdinalIgnoreCase) && !box.Items.Contains(family))
            {
                box.Items.Add(family); // not installed, or not monospace: still shown as written
            }

            box.SelectedItem = family is null || family.Equals(BundledFonts.Family, StringComparison.OrdinalIgnoreCase) ? BundledFontLabel : family;
        });
    }

    // ------------------------------------------------------------ helpers

    private static string Format(object? value) => value switch
    {
        null => "",
        string s => s,
        bool b => b ? "true" : "false",
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => ConfigEditor.FormatValue(value),
    };

    private static bool TryHex(string text, out Color color)
    {
        color = default;
        var s = text.Trim().TrimStart('#');
        if (s.Length != 6 || !uint.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
        {
            return false;
        }

        color = Color.FromArgb(0xFF, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
        return true;
    }

    private void OpenConfigFile()
    {
        try
        {
            if (!File.Exists(_config.Path))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_config.Path)!);
                File.WriteAllText(_config.Path, FirstRun.Template);
            }

            Process.Start(new ProcessStartInfo(_config.Path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Write($"settings: open with the default app failed ({ex.Message}); using notepad");
            Process.Start("notepad.exe", $"\"{_config.Path}\"");
        }
    }

    private static void Reveal(string file)
    {
        try
        {
            Process.Start("explorer.exe", File.Exists(file) ? $"/select,\"{file}\"" : $"\"{Path.GetDirectoryName(file)}\"");
        }
        catch (Exception ex)
        {
            Log.Write($"settings: couldn't show {file}: {ex.Message}");
        }
    }

    private static void Guard(string what, Action work)
    {
        try
        {
            work();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Crash(what, ex);
        }
    }
}
