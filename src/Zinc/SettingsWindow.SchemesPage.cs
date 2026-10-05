using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Zinc.Settings;
using Zinc.Terminal;

namespace Zinc;

// The color schemes page: a sub-page of Appearance (or of a profile's page), opened from the Color scheme card. The scheme in
// force (or the light/dark pair) with Edit and New scheme…; then the library, as the shaders page's (Store-style: pills for
// dark, light or yours, a search, a card per scheme, in a grid that only builds what's in view: there are ~650). A click
// on a scheme shows it in the preview above; a click again takes it back. Use puts it in place (with a pair: one side).
internal sealed partial class SettingsWindow
{
    /// <summary>The color schemes page is shown (for <see cref="_schemesProfile"/>: null, the global scheme).</summary>
    private bool _editingSchemes;

    private string? _schemesProfile;

    // The library scheme in the preview, the library as last read, its filters, and which side of a light/dark pair Use sets.
    private string? _previewedScheme;
    private IReadOnlyList<SchemeEntry> _schemeList = [];
    private SchemeKind _schemeKind;
    private string _schemeQuery = "";
    private bool _schemeForLight;

    /// <summary>Zinc's own scheme: in force where none is chosen.</summary>
    private const string ZincScheme = "Lit Cell";

    private void OpenSchemes(string? profile)
    {
        // From anywhere, search results included: a sub-page of Appearance, or of the profile's page.
        _navigating = true;
        try
        {
            _search.Text = "";
        }
        finally
        {
            _navigating = false;
        }

        _section = profile == null ? "appearance" : "profiles";
        _editing = profile;
        SelectSection(_section);
        _editingShaders = false;
        _editingSchemes = true;
        _schemesProfile = profile;
        _previewedScheme = null;
        _schemeKind = SchemeKind.All;
        _schemeQuery = "";
        _schemeForLight = ConfigFile.SystemScheme == Zinc.Ghostty.ColorScheme.Light; // the side in force now
        PreviewForLibrary(open: true);
        Render();
        _scroller.ChangeView(null, 0, null, disableAnimation: true);
    }

    private void LeaveSchemes()
    {
        _editingSchemes = false;
        _previewedScheme = null;
        PreviewForLibrary(open: false);
        Render();
    }

    // The scheme of the global settings (null) or of a profile, as config.toml has it: where it's written, its own (null:
    // none, i.e. Zinc's own globally, inherited in a profile), what it inherits, and what's in force.
    private static IReadOnlyList<string> SchemePath(string? profile) => profile == null ? ["colors", "theme"] : ["profile", profile, "colors", "theme"];

    private string? OwnScheme(string? profile) => (profile == null ? _config.ValueOf("colors.theme") : _config.ValueOf(profile, "colors.theme")) as string;

    private string? InheritedScheme(string? profile) => profile == null ? null : _config.ValueOf("colors.theme") as string;

    private string? ShownScheme(string? profile) => OwnScheme(profile) ?? InheritedScheme(profile);

    /// <summary>A scheme value's sides: the scheme (or the dark one of a pair; null: Zinc's own) and the light one (null: no pair).</summary>
    private static (string? Dark, string? Light) SchemeSides(string? value) =>
        value != null && ThemeLibrary.TryParsePair(value, out var light, out var dark) ? (dark, light) : (value, null);

    private void SchemesPage(string? profile)
    {
        var back = new HyperlinkButton { Content = "‹ Back", Padding = new Thickness(0, 2, 0, 2) };
        AutomationProperties.SetName(back, "Back");
        back.Click += (_, _) => LeaveSchemes();
        _page.Children.Insert(0, back);
        var setting = SettingsCatalog.Find("colors.theme")!;
        var path = SchemePath(profile);
        _page.Children.Add(Note(setting.Description + " Click a scheme in the library to preview it; click again to stop."));

        // ---- In use: the scheme, or the pair; following Windows' light/dark; which side Use sets.
        var reset = ResetArrow(setting.Label, profile == null ? "Back to Zinc's own: Lit Cell" : "Back to the scheme every profile inherits");
        reset.Click += (_, _) => Write(path, null);
        var description = DescriptionText();
        var inUse = PartTitle("In use");
        var title = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Margin = inUse.Margin, Children = { inUse, reset } };
        inUse.Margin = new Thickness(0);
        inUse.VerticalAlignment = VerticalAlignment.Center;
        _page.Children.Add(title);
        if (profile != null)
        {
            _page.Children.Add(description);
        }

        var quiet = false; // showing values, not changing them
        var (followPanel, follow) = Switch("Follow Windows light/dark");
        _page.Children.Add(CardLayout(new TextBlock { Text = "Follow Windows light/dark" }, null,
            DescriptionText("Two schemes: one while Windows is light, one while it's dark."), null, null, followPanel));
        var slots = new StackPanel { Spacing = 4, Margin = new Thickness(0, 4, 0, 4) };
        _page.Children.Add(slots);
        var sides = new RadioButtons { MaxColumns = 2, Items = { "Dark scheme", "Light scheme" } };
        AutomationProperties.SetName(sides, "Use sets");
        var sidesCard = CardLayout(new TextBlock { Text = "Use sets" }, null,
            DescriptionText("Which of the two the library's Use button replaces."), null, null, sides);
        _page.Children.Add(sidesCard);

        follow.Toggled += (_, _) =>
        {
            if (quiet || _updating)
            {
                return;
            }

            var (dark, _) = SchemeSides(ShownScheme(profile));
            if (follow.IsOn)
            {
                // The scheme in use becomes the dark side, and the light side starts from its counterpart.
                var light = ThemeLibrary.LightCounterpart(dark, _config.Themes.Names()) ?? ZincScheme + " Light";
                Write(path, $"light:{light},dark:{dark ?? ZincScheme}");
            }
            else
            {
                Write(path, dark);
            }
        };

        // A slot: one scheme in force, its tile, Edit and New scheme….
        Border Slot(string label, string about, string? name)
        {
            var shown = name ?? ZincScheme;
            var tile = SchemeTiles.Tile(SchemeTiles.ThemeOf(_config.Themes, name) ?? TerminalTheme.Default, shown);
            tile.Width = ControlWidth;
            var edit = new Button { Content = $"Edit {shown}" };
            edit.Click += (_, _) => CustomizeScheme(name, path);
            var newScheme = new Button { Content = "New scheme…", Flyout = NewSchemeFlyout(() => shown, () => name, path) };
            var text = new StackPanel
            {
                Spacing = 1,
                VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    new TextBlock { Text = label },
                    DescriptionText(about),
                    new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 8, 0, 0), Children = { edit, newScheme } },
                },
            };
            return CardBorder(TextAndControl(text, tile));
        }

        // ---- Library: a heading with the count; one toolbar row (dark, light, yours as pills; a search; a menu); a card per
        // scheme in a grid. The heading and the toolbar stay in view once scrolled to (StickLibraryHeader).
        var libraryTitle = PartTitle("Library");
        var pills = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        AutomationProperties.SetName(pills, "Kinds of scheme");
        var filter = new TextBox { PlaceholderText = "Search", Text = _schemeQuery, Width = 240, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(filter, "Search schemes");
        var more = new Button
        {
            Content = new FontIcon { Glyph = "", FontSize = 14 }, // more (…)
            Padding = new Thickness(10, 8, 10, 8),
            VerticalAlignment = VerticalAlignment.Center,
        };
        ToolTipService.SetToolTip(more, "More");
        AutomationProperties.SetName(more, "More");
        var menu = new MenuFlyout { Placement = FlyoutPlacementMode.BottomEdgeAlignedRight };
        var openFolder = new MenuFlyoutItem { Text = "Open themes folder", Icon = new FontIcon { Glyph = "" } };
        openFolder.Click += (_, _) => OpenThemesFolder();
        var source = new MenuFlyoutItem { Text = "Where Zinc's schemes come from", Icon = new FontIcon { Glyph = "" } };
        source.Click += (_, _) => OpenLink("https://github.com/mbadolato/iTerm2-Color-Schemes");
        menu.Items.Add(openFolder);
        menu.Items.Add(source);
        more.Flyout = menu;
        var pillScroller = new ScrollViewer
        {
            Content = pills,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var toolbar = new Grid
        {
            ColumnSpacing = 8,
            Margin = new Thickness(0, 0, 0, 8),
            ColumnDefinitions = { new() { Width = new GridLength(1, GridUnitType.Star) }, new() { Width = GridLength.Auto }, new() { Width = GridLength.Auto } },
            Children = { pillScroller, filter, more },
        };
        Grid.SetColumn(filter, 1);
        Grid.SetColumn(more, 2);
        var libraryHeader = new StackPanel { Children = { libraryTitle, toolbar } };
        _page.Children.Add(libraryHeader);
        _stickLibrary = () => StickLibraryHeader(libraryHeader);
        var status = DescriptionText("Reading the schemes…");
        _page.Children.Add(status);

        // Cards are built as they come into view (and dropped when scrolled away); each knows how to show the state.
        var shownCards = new Dictionary<UIElement, Action>();
        var grid = new ItemsRepeater
        {
            Layout = new UniformGridLayout
            {
                MinItemWidth = 250,
                MinItemHeight = 104,
                MinRowSpacing = 8,
                MinColumnSpacing = 8,
                ItemsStretch = UniformGridLayoutItemsStretch.Fill,
            },
            XYFocusKeyboardNavigation = XYFocusKeyboardNavigationMode.Enabled,
            TabFocusNavigation = KeyboardNavigationMode.Once,
        };
        AutomationProperties.SetName(grid, "Schemes");
        _page.Children.Add(grid);

        void UpdateCards()
        {
            foreach (var update in shownCards.Values)
            {
                update();
            }
        }

        bool Following() => SchemeSides(ShownScheme(profile)).Light != null;

        // The side Use replaces: with a pair, the one chosen; else the scheme.
        string Target()
        {
            var (dark, light) = SchemeSides(ShownScheme(profile));
            return light != null && _schemeForLight ? light : dark ?? ZincScheme;
        }

        bool InForce(string name)
        {
            var (dark, light) = SchemeSides(ShownScheme(profile));
            return name.Equals(dark ?? ZincScheme, StringComparison.OrdinalIgnoreCase) || name.Equals(light, StringComparison.OrdinalIgnoreCase);
        }

        void Use(string name)
        {
            var (dark, light) = SchemeSides(ShownScheme(profile));
            _previewedScheme = null; // in force now: the preview shows it anyway
            Write(path, light == null ? name : _schemeForLight ? $"light:{name},dark:{dark}" : $"light:{light},dark:{name}");
        }

        void Preview(string name)
        {
            _previewedScheme = name.Equals(_previewedScheme, StringComparison.OrdinalIgnoreCase) ? null : name; // the same again: stop
            if (_previewedScheme is { } shown)
            {
                PreviewScheme(path, shown);
            }
            else
            {
                RefreshPreview();
            }

            UpdateCards();
        }

        void MenuFor(MenuFlyout options, SchemeEntry entry)
        {
            options.Items.Clear();
            var edit = new MenuFlyoutItem { Text = entry.Yours ? "Edit colors" : "Edit colors (makes your copy)" };
            edit.Click += (_, _) => CustomizeScheme(entry.Name, path);
            options.Items.Add(edit);
            if (entry.Yours && _config.Themes.Find(entry.Name) is { } file)
            {
                var open = new MenuFlyoutItem { Text = "Open in editor" };
                open.Click += (_, _) => OpenInEditor(file);
                options.Items.Add(open);
            }

            if (entry.ReplacesBundled)
            {
                var original = new MenuFlyoutItem { Text = "Restore the original" };
                ToolTipService.SetToolTip(original, $"Deletes your copy: {entry.Name} as Zinc comes with it is used again.");
                original.Click += (_, _) =>
                {
                    try
                    {
                        _config.Themes.BackToOriginal(entry.Name);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        Log.Write($"settings: couldn't remove scheme {entry.Name}: {ex.Message}");
                    }
                };
                options.Items.Add(original);
            }
            else if (entry.Yours)
            {
                var delete = new MenuFlyoutItem { Text = "Delete…" };
                delete.Click += (_, _) => DeleteScheme(entry.Name);
                options.Items.Add(delete);
            }
        }

        // A card: the scheme as itself on top (its name and 16 colors on its background); under it "In use", whose it is,
        // Use and a menu. The card is a button: a click previews (Space too), Enter uses it.
        Button SchemeCard(SchemeEntry entry)
        {
            var theme = SchemeTiles.ThemeOf(_config.Themes, entry.Name) ?? TerminalTheme.Default;
            var inUseBadge = new TextBlock
            {
                Text = "✓ In use",
                FontSize = 12,
                Foreground = (Brush)Application.Current.Resources["AccentTextFillColorPrimaryBrush"],
                VerticalAlignment = VerticalAlignment.Center,
            };
            var badges = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center, Children = { inUseBadge } };
            if (entry.Yours)
            {
                var whose = new TextBlock { Text = entry.ReplacesBundled ? "Your copy" : "Yours", FontSize = 12, Opacity = 0.7, VerticalAlignment = VerticalAlignment.Center };
                if (entry.ReplacesBundled)
                {
                    ToolTipService.SetToolTip(whose, "Your edited copy, used instead of the one Zinc comes with.");
                }

                badges.Children.Add(whose);
            }

            var use = new Button { MinWidth = 72, VerticalAlignment = VerticalAlignment.Center };
            use.Click += (_, _) => Use(entry.Name);
            var options = new MenuFlyout();
            options.Opening += (_, _) => MenuFor(options, entry);
            var optionsButton = new Button
            {
                Content = new FontIcon { Glyph = "", FontSize = 14 },
                Flyout = options,
                Padding = new Thickness(10, 8, 10, 8),
                VerticalAlignment = VerticalAlignment.Center,
                Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                BorderThickness = new Thickness(0),
            };
            ToolTipService.SetToolTip(optionsButton, "More");
            AutomationProperties.SetName(optionsButton, $"More for {entry.Name}");
            var footer = new Grid
            {
                ColumnSpacing = 4,
                Padding = new Thickness(12, 6, 4, 6),
                ColumnDefinitions = { new() { Width = new GridLength(1, GridUnitType.Star) }, new() { Width = GridLength.Auto }, new() { Width = GridLength.Auto } },
                Children = { badges, use, optionsButton },
            };
            Grid.SetColumn(use, 1);
            Grid.SetColumn(optionsButton, 2);
            Grid.SetRow(footer, 1);
            var card = new Button
            {
                Content = new Grid
                {
                    RowDefinitions = { new() { Height = GridLength.Auto }, new() { Height = GridLength.Auto } },
                    Children = { SchemeTiles.Sample(theme, entry.Name), footer },
                },
                Padding = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Top,
                CornerRadius = new CornerRadius(6),
                Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
                BorderThickness = new Thickness(1),
            };
            AutomationProperties.SetName(card, entry.Name);
            AutomationProperties.SetHelpText(card, $"{(entry.IsDark ? "Dark" : "Light")} scheme. Space previews it; Enter uses it.");
            card.Click += (_, _) => Preview(entry.Name);
            card.AddHandler(UIElement.PreviewKeyDownEvent, new KeyEventHandler((_, e) =>
            {
                if (e.Key == Windows.System.VirtualKey.Enter && ReferenceEquals(e.OriginalSource, card) && use.IsEnabled)
                {
                    Use(entry.Name);
                    e.Handled = true;
                }
            }), handledEventsToo: true);

            void Update()
            {
                inUseBadge.Visibility = InForce(entry.Name) ? Visibility.Visible : Visibility.Collapsed;
                var label = Following() ? _schemeForLight ? "Use for light" : "Use for dark" : "Use";
                use.Content = label;
                use.IsEnabled = !entry.Name.Equals(Target(), StringComparison.OrdinalIgnoreCase);
                AutomationProperties.SetName(use, $"{label}: {entry.Name}");
                var previewed = entry.Name.Equals(_previewedScheme, StringComparison.OrdinalIgnoreCase);
                card.BorderBrush = (Brush)Application.Current.Resources[previewed ? "AccentFillColorDefaultBrush" : "CardStrokeColorDefaultBrush"];
            }

            Update();
            shownCards[card] = Update;
            return card;
        }

        grid.ItemTemplate = new SchemeCardFactory(data => SchemeCard((SchemeEntry)data), element => shownCards.Remove(element));

        void FillLibrary()
        {
            var query = _schemeQuery.Trim();
            var ofKind = SchemeCatalog.Filter(_schemeList, _schemeKind, "");
            var shown = SchemeCatalog.Filter(ofKind, SchemeKind.All, query);
            shownCards.Clear();
            grid.ItemsSource = shown.ToList();
            libraryTitle.Text = query.Length > 0 ? $"Library · {shown.Count} of {ofKind.Count} schemes"
                : ofKind.Count == 1 ? "Library · 1 scheme" : $"Library · {ofKind.Count} schemes";
            SetText(status, shown.Count > 0 ? ""
                : query.Length > 0 ? $"Nothing matches “{query}”."
                : _schemeKind == SchemeKind.Yours ? "None yet. Edit colors on any scheme, or New scheme…, makes one of yours."
                : "No schemes here.");
        }

        foreach (var (kind, label) in new[] { (SchemeKind.All, "All"), (SchemeKind.Dark, "Dark"), (SchemeKind.Light, "Light"), (SchemeKind.Yours, "Yours") })
        {
            // Pills, as the Store's: the one shown is checked (accent). Dark or Light while following also picks the side Use sets.
            var pill = new ToggleButton { Content = label, IsChecked = kind == _schemeKind, CornerRadius = new CornerRadius(16), Padding = new Thickness(16, 5, 16, 6) };
            pill.Click += (_, _) =>
            {
                _schemeKind = kind;
                foreach (var other in pills.Children.OfType<ToggleButton>())
                {
                    other.IsChecked = ReferenceEquals(other, pill);
                }

                if (kind is SchemeKind.Dark or SchemeKind.Light && Following())
                {
                    _schemeForLight = kind == SchemeKind.Light;
                    quiet = true;
                    sides.SelectedIndex = _schemeForLight ? 1 : 0;
                    quiet = false;
                }

                FillLibrary();
            };
            pills.Children.Add(pill);
        }

        sides.SelectionChanged += (_, _) =>
        {
            if (!quiet && sides.SelectedIndex >= 0)
            {
                _schemeForLight = sides.SelectedIndex == 1;
                UpdateCards();
            }
        };
        filter.TextChanged += (_, _) =>
        {
            _schemeQuery = filter.Text;
            FillLibrary();
        };
        filter.KeyDown += (_, e) =>
        {
            if (e.Key == Windows.System.VirtualKey.Down && grid.TryGetElement(0) is Control first)
            {
                first.Focus(FocusState.Keyboard);
                e.Handled = true;
            }
        };

        void RefreshInUse()
        {
            var own = OwnScheme(profile);
            var (dark, light) = SchemeSides(ShownScheme(profile));
            reset.Visibility = own != null ? Visibility.Visible : Visibility.Collapsed;
            if (profile != null)
            {
                InheritedText(description, setting, Describe(setting, InheritedScheme(profile)), own != null);
            }

            quiet = true;
            follow.IsOn = light != null;
            sides.SelectedIndex = _schemeForLight ? 1 : 0;
            quiet = false;
            sidesCard.Visibility = light != null ? Visibility.Visible : Visibility.Collapsed;
            slots.Children.Clear();
            if (light == null)
            {
                slots.Children.Add(Slot(setting.Label, dark == null && profile == null ? "Zinc's own." : "", dark));
            }
            else
            {
                slots.Children.Add(Slot("Dark scheme", "While Windows is in dark mode.", dark));
                slots.Children.Add(Slot("Light scheme", "While Windows is in light mode.", light));
            }
        }

        async void LoadLibrary()
        {
            try
            {
                var themes = _config.Themes;
                var list = await Task.Run(() => SchemeCatalog.List(themes));
                if (!_editingSchemes || !_page.Children.Contains(grid))
                {
                    return; // left the page meanwhile
                }

                if (!list.SequenceEqual(_schemeList) || grid.ItemsSource == null)
                {
                    _schemeList = list;
                    FillLibrary();
                }
            }
            catch (Exception ex)
            {
                Log.Crash("listing color schemes", ex);
                SetText(status, $"Couldn't list the schemes: {ex.Message}");
            }
        }

        _refreshers.Add(() =>
        {
            RefreshInUse();
            UpdateCards();
            LoadLibrary(); // a scheme made, deleted or edited lighter shows up
            if (_previewedScheme is { } previewed)
            {
                PreviewScheme(path, previewed); // a reload shows the config in force: the previewed one goes back on top
            }
        });
        HookLibrarySticking();
        Quietly(RefreshInUse);
        if (_schemeList.Count > 0)
        {
            FillLibrary(); // as last read, at once; read again below
        }

        LoadLibrary();
    }

    /// <summary>Opens the themes folder next to config.toml (made if it isn't there yet).</summary>
    private void OpenThemesFolder()
    {
        try
        {
            Directory.CreateDirectory(_config.Themes.UserFolder);
            Process.Start(new ProcessStartInfo(_config.Themes.UserFolder) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Write($"settings: couldn't open the themes folder: {ex.Message}");
        }
    }

    private static void OpenLink(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Write($"settings: couldn't open {url}: {ex.Message}");
        }
    }
}

/// <summary>Builds the color schemes page's cards for its grid as they come into view, and hears when one is dropped.</summary>
internal sealed partial class SchemeCardFactory(Func<object, UIElement> build, Action<UIElement> dropped) : IElementFactory
{
    public UIElement GetElement(ElementFactoryGetArgs args) => build(args.Data);

    public void RecycleElement(ElementFactoryRecycleArgs args) => dropped(args.Element);
}
