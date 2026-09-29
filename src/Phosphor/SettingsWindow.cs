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
using Phosphor.Settings;
using Phosphor.Terminal;
using Windows.ApplicationModel.DataTransfer;
using Windows.UI;

namespace Phosphor;

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
    private const string DefaultSchemeLabel = "Phosphor's own";
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
        Title = "Phosphor Settings";
        ExtendsContentIntoTitleBar = true;
        SystemBackdrop = TestHooks.Deterministic ? null : new MicaBackdrop();
        WindowIcon.Apply(this, Path.Combine(AppContext.BaseDirectory, "Assets", "Phosphor.ico"));

        // Title bar: the app's mark and the window's name, in the caption area.
        var titleBar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            Height = 40,
            Padding = new Thickness(16, 0, 0, 0),
            Children =
            {
                new Image { Width = 16, Height = 16, Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(Path.Combine(AppContext.BaseDirectory, "Assets", "Phosphor.ico"))) },
                new TextBlock { Text = "Phosphor Settings", VerticalAlignment = VerticalAlignment.Center, FontSize = 12 },
            },
        };

        _search = new AutoSuggestBox { PlaceholderText = "Find a setting", QueryIcon = new SymbolIcon(Symbol.Find) };
        AutomationProperties.SetName(_search, "Find a setting");
        _search.TextChanged += (_, _) => Render();

        // The undo bar: floats at the bottom while there are changes to undo.
        ToolTipService.SetToolTip(_undo, "Puts config.toml back as it was when Settings opened");
        _undo.Click += (_, _) => Guard("undoing changes", () =>
        {
            _writer.Undo();
            Wrote?.Invoke();
            ShowChanges(false);
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
            Content = new Grid { Children = { _scroller, _snackbar } },
        };
        foreach (var section in SettingsCatalog.Sections)
        {
            var item = new NavigationViewItem { Content = section.Title, Tag = section.Id, Icon = new FontIcon { Glyph = section.Glyph } };
            (section.Id == "about" ? _nav.FooterMenuItems : _nav.MenuItems).Add(item);
        }

        _nav.SelectedItem = _nav.MenuItems.Concat(_nav.FooterMenuItems).OfType<NavigationViewItem>().First(i => (string)i.Tag == _section);
        _nav.SelectionChanged += (_, e) =>
        {
            if (e.SelectedItem is NavigationViewItem { Tag: string id })
            {
                _section = id;
                _editing = null;
                _search.Text = "";
                Render();
                _scroller.ChangeView(null, 0, null, disableAnimation: true);
            }
        };

        var openFile = new Button { Content = "Open config.toml" };
        openFile.Click += (_, _) => OpenConfigFile();
        _problems.ActionButton = openFile;

        var root = new Grid { RequestedTheme = ElementTheme.Dark };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition());
        Grid.SetRow(_nav, 1);
        root.Children.Add(titleBar);
        root.Children.Add(_nav);
        Content = root;
        SetTitleBar(titleBar);
        Render();
    }

    /// <summary>The config was read again (after our write, or an edit elsewhere): show what's in force.</summary>
    public void Refresh(ConfigFile config)
    {
        var listed = _config.Profiles.Select(p => (p.Id, p.Name, p.Hidden)).ToList();
        _config = config;
        if (_section == "profiles" && _search.Text.Trim().Length == 0 &&
            (_editing == null ? !listed.SequenceEqual(config.Profiles.Select(p => (p.Id, p.Name, p.Hidden))) : listed.All(p => p.Id != _editing)))
        {
            Render(); // the list changed (or its order, or the profile being edited just appeared)
            return;
        }

        _updating = true;
        try
        {
            UpdateBanners();
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
        var query = _search.Text.Trim();
        _page.Children.Add(new TextBlock
        {
            Text = query.Length > 0 ? "Results"
                : _section == "profiles" && _editing is { } editing ? _config.FindProfile(editing)?.Name ?? editing
                : SettingsCatalog.Sections.First(s => s.Id == _section).Title,
            FontSize = 28,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 8, 0, 12),
        });
        _page.Children.Add(_problems);
        UpdateBanners();

        if (query.Length > 0)
        {
            var hits = SettingsCatalog.All.Where(s => Matches(s, query)).ToList();
            if (hits.Count == 0)
            {
                _page.Children.Add(Note($"No setting matches “{query}”."));
            }

            foreach (var section in hits.GroupBy(s => s.Section))
            {
                AddGroup(SettingsCatalog.Sections.First(s => s.Id == section.Key).Title, section);
            }
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
                if (group.Key == "Colors")
                {
                    _page.Children.Add(Note(SettingsCatalog.FileOnly["colors.palette"]));
                }
            }
        }

        _scroller.Content = _page;
    }

    private static bool Matches(SettingDef s, string query) =>
        $"{s.Label} {s.Description} {s.Key} {s.Group}".Contains(query, StringComparison.OrdinalIgnoreCase);

    private void UpdateBanners()
    {
        _problems.IsOpen = _config.HasErrors;
        _problems.Message = (_config.Warnings.FirstOrDefault() ?? "") + " Nothing is saved from here until it's fixed.";
        ShowChanges(!_config.HasErrors && _writer.Changed);
    }

    /// <summary>Slides the undo bar in (there are changes) or out.</summary>
    private void ShowChanges(bool show)
    {
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
            if (setting.Kind == SettingKind.Scheme)
            {
                foreach (var card in SchemeCards(setting, null))
                {
                    _page.Children.Add(card);
                }
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
        _page.Children.Add(Row(TerminalSession.Version.Replace("phosphor", "Phosphor"),
            "Terminal emulation and the built-in glyphs by Ghostty (MIT). Cascadia Mono NF by Microsoft (OFL). Color schemes from iTerm2-Color-Schemes.",
            null));
        _page.Children.Add(Row("Config file", _config.Path, ButtonRow(("Open", OpenConfigFile), ("Open folder", () => Reveal(_config.Path)))));
        _page.Children.Add(Row("Crash log", Log.CrashPath, ButtonRow(("Open folder", () => Reveal(Log.CrashPath)))));
        _page.Children.Add(Row("Welcome page", "The first-run questions: how you work, colors and font, default shell.",
            ButtonRow(("Open", () => App.Instance.ShowWelcome(firstRun: false)))));
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

    /// <summary>The default profile, every profile (found and yours, in your order), and where Phosphor looks for shells.</summary>
    private void ProfilesPage()
    {
        _page.Children.Add(GroupTitle("Default"));
        var visible = _config.VisibleProfiles.ToList();
        var defaults = new ComboBox { MinWidth = 260 };
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
                WriteProfileOrder(list.Items.OfType<ListViewItem>().Select(i => (string)i.Tag).ToList());
            }
        };
        _page.Children.Add(list);

        AddGroup("Find shells on this PC", SettingsCatalog.Discovery);
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

        what.Add(profile.Found ? profile.InFile ? "found on this PC, with your changes" : "found on this PC" : "yours");
        if (profile.Hidden)
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
            Flyout = new MenuFlyout { Items = { MoveItem("Move up", at - 1), MoveItem("Move down", at + 1), MoveItem("Move to the top", 0) } },
        };
        ToolTipService.SetToolTip(more, "More");
        AutomationProperties.SetName(more, $"More for {profile.Name}");

        var grid = new Grid { ColumnSpacing = 14 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var icon = new IconSourceElement { IconSource = ProfileIcons.For(profile), Width = 20, Height = 20, VerticalAlignment = VerticalAlignment.Center };
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
        Grid.SetColumn(text, 1);
        Grid.SetColumn(buttons, 2);
        grid.Children.Add(icon);
        grid.Children.Add(text);
        grid.Children.Add(buttons);
        return CardBorder(grid);
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
        _page.Children.Add(Note(profile is { Found: true }
            ? $"Found on this PC ({profile.FoundCommand}). What you change here is kept in its own section in config.toml."
            : "Yours, in its own section in config.toml."));

        _page.Children.Add(GroupTitle("General"));
        foreach (var field in SettingsCatalog.ProfileFields)
        {
            var hint = field.Key switch
            {
                "name" => profile?.FoundName ?? id,
                "command" => profile?.FoundCommand ?? "The default shell",
                _ => field.Placeholder,
            };
            _page.Children.Add(Card(field with { Placeholder = hint }, id));
        }

        _page.Children.Add(Note("The settings below come from the defaults until you change them here; the reset arrow gives one back."));
        foreach (var group in SettingsCatalog.All.Where(s => s.ProfileOverridable).GroupBy(s => s.Group))
        {
            _page.Children.Add(GroupTitle(group.Key));
            foreach (var setting in group)
            {
                if (setting.Kind == SettingKind.Scheme)
                {
                    foreach (var card in SchemeCards(setting, id))
                    {
                        _page.Children.Add(card);
                    }
                }
                else
                {
                    _page.Children.Add(OverrideCard(setting, id));
                }
            }
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
                ShowChanges(true);
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
            var inherited = Describe(setting, Inherited());
            SetText(description, own != null ? $"Its own. Everywhere else: {inherited}." : $"Inherited: {inherited}.");
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

        var (control, show) = Control(setting, Commit);

        void Refresh()
        {
            var own = _config.ValueOf(id, setting.Key);
            show(own ?? Inherited());
            State(own);
        }

        reset.Click += (_, _) =>
        {
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
        return CardLayout(new TextBlock { Text = setting.Label }, reset, description, $"profile.{id}.{setting.Key}", error, control);
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
            ShowChanges(true);
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
                ShowChanges(true);
                State(value);
            });
        }

        var (control, show) = Control(setting, Commit);
        reset.Click += (_, _) =>
        {
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
        return CardLayout(new TextBlock { Text = setting.Label }, reset, description,
            profile == null ? setting.Key : $"profile.{profile}.{setting.Key}", error, control);
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
        ComboBox Box(string name, bool withDefault)
        {
            var box = new ComboBox { MinWidth = 240 };
            AutomationProperties.SetName(box, name);
            if (withDefault)
            {
                box.Items.Add(DefaultSchemeLabel);
            }

            foreach (var n in names)
            {
                box.Items.Add(n);
            }

            return box;
        }

        // A profile has no "Phosphor's own" to pick: without a scheme of its own it inherits.
        var scheme = Box("Color scheme", withDefault: profile == null);
        var light = Box("Light scheme", withDefault: false);
        var follow = new ToggleSwitch { OnContent = "On", OffContent = "Off", MinWidth = 0 };
        AutomationProperties.SetName(follow, "Follow Windows light/dark");
        var label = new TextBlock { Text = setting.Label };
        var description = DescriptionText();
        var reset = ResetArrow(setting.Label, profile == null
            ? "Back to Phosphor's own (removes it from config.toml)"
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
            SetText(description,
                pair ? "While Windows is in dark mode."
                : profile == null ? setting.Description
                : own != null ? $"Its own. Everywhere else: {Describe(setting, Inherited())}."
                : $"Inherited: {Describe(setting, Inherited())}.");
        }

        void Display(object? value, object? own)
        {
            local = true;
            var text = value as string;
            var pair = text != null && ThemeLibrary.TryParsePair(text, out _, out _);
            follow.IsOn = pair;
            if (pair && ThemeLibrary.TryParsePair(text!, out var l, out var d))
            {
                scheme.SelectedItem = scheme.Items.Contains(d) ? d : null;
                light.SelectedItem = light.Items.Contains(l) ? l : null;
            }
            else
            {
                scheme.SelectedItem = text != null && scheme.Items.Contains(text) ? text : profile == null ? DefaultSchemeLabel : null;
            }

            scheme.PlaceholderText = scheme.SelectedItem == null ? Describe(setting, value) : "";
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
                if (light.SelectedItem is string l && scheme.SelectedItem is string d && d != DefaultSchemeLabel)
                {
                    Commit($"light:{l},dark:{d}");
                }
            }
            else if (scheme.SelectedItem is string s)
            {
                Commit(s == DefaultSchemeLabel ? null : s);
            }
        }

        scheme.SelectionChanged += (_, _) => Changed();
        light.SelectionChanged += (_, _) => Changed();
        follow.Toggled += (_, _) =>
        {
            if (local || _updating)
            {
                return;
            }

            if (follow.IsOn)
            {
                // The scheme in use becomes the dark side (Phosphor's own: Catppuccin Mocha), and
                // the light side starts from its counterpart.
                local = true;
                var current = scheme.SelectedItem as string;
                var dark = current != null && current != DefaultSchemeLabel
                    ? current
                    : names.FirstOrDefault(n => n == "Catppuccin Mocha") ?? names.FirstOrDefault();
                scheme.SelectedItem = dark;
                light.SelectedItem = ThemeLibrary.LightCounterpart(dark, names);
                local = false;
            }

            Changed();
        };
        reset.Click += (_, _) =>
        {
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
            DescriptionText("Two schemes: one while Windows is light, one while it's dark."), null, null, follow);
        var main = CardLayout(label, reset, description, profile == null ? setting.Key : $"profile.{profile}.{setting.Key}", null, scheme);
        return [followCard, main, lightCard];
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
        bool b => b ? "on" : "off",
        string t when setting.Kind == SettingKind.Scheme && ThemeLibrary.TryParsePair(t, out var l, out var d) => $"{d} / {l}",
        _ when setting.Kind == SettingKind.Choice =>
            setting.Choices.FirstOrDefault(c => SettingsCatalog.Same(c.Value, value))?.Label ?? Format(value),
        _ => setting.Unit is { } unit ? $"{Format(value)} {unit}" : Format(value),
    };

    /// <summary>The small arrow beside a setting's name that puts it back; shown while it's changed.</summary>
    private static Button ResetArrow(string name, string tip)
    {
        var button = new Button
        {
            Content = new FontIcon { Glyph = "", FontSize = 12 },
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
        Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0xFF, 0x99, 0xA4)),
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

        control.VerticalAlignment = VerticalAlignment.Center;
        var grid = new Grid { ColumnSpacing = 24 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(control, 1);
        grid.Children.Add(text);
        grid.Children.Add(control);
        return CardBorder(grid);
    }

    private static Border Row(string title, string detail, FrameworkElement? control)
    {
        var grid = new Grid { ColumnSpacing = 24 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(new StackPanel
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
        });
        if (control != null)
        {
            control.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(control, 1);
            grid.Children.Add(control);
        }

        return CardBorder(grid);
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
    private (FrameworkElement Control, Action<object?> Show) Control(SettingDef setting, Action<object?> commit) => setting.Kind switch
    {
        SettingKind.Toggle => ToggleControl(setting, commit),
        SettingKind.Choice => ChoiceControl(setting, commit),
        SettingKind.Number => NumberControl(setting, commit),
        SettingKind.Color => ColorControl(commit),
        SettingKind.Font => FontControl(commit),
        _ => TextControl(setting, commit), // SettingKind.Scheme has cards of its own (SchemeCards)
    };

    private static (FrameworkElement, Action<object?>) ToggleControl(SettingDef setting, Action<object?> commit)
    {
        var toggle = new ToggleSwitch { OnContent = "On", OffContent = "Off", MinWidth = 0 };
        AutomationProperties.SetName(toggle, setting.Label);
        toggle.Toggled += (_, _) => commit(toggle.IsOn);
        return (toggle, value => toggle.IsOn = value as bool? ?? setting.Default as bool? ?? false);
    }

    private static (FrameworkElement, Action<object?>) ChoiceControl(SettingDef setting, Action<object?> commit)
    {
        var box = new ComboBox { MinWidth = 200 };
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
        var box = new TextBox { Width = 220, PlaceholderText = setting.Placeholder ?? Format(setting.Default) };
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

    /// <summary>A color: typed as #rrggbb, or picked (applied live while you drag, a few times a second).</summary>
    private static (FrameworkElement, Action<object?>) ColorControl(Action<object?> commit)
    {
        var swatch = new Border { Width = 22, Height = 22, CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1) };
        swatch.BorderBrush = (Brush)Application.Current.Resources["ControlStrongStrokeColorDefaultBrush"];
        var box = new TextBox { Width = 120, PlaceholderText = "The scheme's" };
        var picker = new ColorPicker { IsAlphaEnabled = false, IsMoreButtonVisible = true, ColorSpectrumShape = ColorSpectrumShape.Ring };
        var flyout = new Flyout { Content = picker };
        var pick = new Button { Content = new FontIcon { Glyph = "", FontSize = 14 }, Flyout = flyout };
        AutomationProperties.SetName(pick, "Pick a color");
        string? shown = null;
        var picking = false;
        void Paint(string? hex) => swatch.Background = hex != null && TryHex(hex, out var c)
            ? new SolidColorBrush(c)
            : (Brush)Application.Current.Resources["SubtleFillColorTransparentBrush"];
        void Apply(string text)
        {
            if (!string.Equals(text, shown, StringComparison.OrdinalIgnoreCase))
            {
                shown = text;
                Paint(text);
                commit(text.Trim().Length == 0 ? null : text.Trim());
            }
        }

        void Picked()
        {
            var c = picker.Color;
            box.Text = $"#{c.R:x2}{c.G:x2}{c.B:x2}";
            Apply(box.Text);
        }

        // While the picker is open, each change is written once it settles for a moment: live,
        // without a config reload per pixel of a drag.
        var settle = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread().CreateTimer();
        settle.Interval = TimeSpan.FromMilliseconds(150);
        settle.IsRepeating = false;
        settle.OnTick(_ =>
        {
            if (picking)
            {
                Picked();
            }
        });
        picker.ColorChanged += (_, _) =>
        {
            if (picking)
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
            if (TryHex(box.Text, out var c))
            {
                picker.Color = c;
            }
        };
        flyout.Opened += (_, _) => picking = true;
        flyout.Closed += (_, _) =>
        {
            picking = false;
            settle.Stop();
            Picked();
        };
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { swatch, box, pick } };
        return (panel, value =>
        {
            if (box.FocusState == FocusState.Unfocused && !picking)
            {
                box.Text = shown = value as string ?? "";
                Paint(shown);
            }
        });
    }

    private static (FrameworkElement, Action<object?>) FontControl(Action<object?> commit)
    {
        var box = new ComboBox { MinWidth = 260 };
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
