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

    // The library scheme in the preview, the library as last read, and its filters.
    private string? _previewedScheme;
    private IReadOnlyList<SchemeEntry> _schemeList = [];
    private SchemeKind _schemeKind;
    private string _schemeQuery = "";

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

        // ---- In use: the scheme, or the pair; following Windows' light/dark.
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

        // A slot: one scheme in force, its tile and Edit beside it (the tile names the scheme).
        Border Slot(string label, string about, string? name)
        {
            var shown = name ?? ZincScheme;
            var tile = SchemeTiles.Tile(SchemeTiles.ThemeOf(_config.Themes, name) ?? TerminalTheme.Default, shown);
            tile.Width = ControlWidth;
            var edit = new Button { Content = "Edit", VerticalAlignment = VerticalAlignment.Stretch };
            AutomationProperties.SetName(edit, $"Edit {shown}");
            ToolTipService.SetToolTip(edit, $"Edit {shown}'s colors");
            edit.Click += (_, _) => CustomizeScheme(name, path);
            return CardLayout(new TextBlock { Text = label }, null, DescriptionText(about), null, null,
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { tile, edit } });
        }

        // ---- Library: a heading with the count; one toolbar row (dark, light, yours as pills; a search; New scheme; a menu); a card per
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
        // New scheme: a copy of the one previewed, else the one in use (or any: "Start from"). With a pair, "Use it now" puts it
        // on the side its starting scheme suits.
        var add = new Button
        {
            Content = new FontIcon { Glyph = "\uE710", FontSize = 14 }, // add (+)
            Padding = new Thickness(10, 8, 10, 8),
            VerticalAlignment = VerticalAlignment.Center,
        };
        ToolTipService.SetToolTip(add, "New scheme…");
        AutomationProperties.SetName(add, "New scheme");
        add.Flyout = NewSchemeFlyout(
            () => _previewedScheme ?? SchemeSides(ShownScheme(profile)).Dark ?? ZincScheme,
            start =>
            {
                var (dark, light) = SchemeSides(ShownScheme(profile));
                return light != null && !(_schemeList.FirstOrDefault(e => e.Name.Equals(start, StringComparison.OrdinalIgnoreCase))?.IsDark ?? true) ? light : dark;
            },
            path,
            () => [.. _schemeList.Select(e => e.Name)]);
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
            ColumnDefinitions = { new() { Width = new GridLength(1, GridUnitType.Star) }, new() { Width = GridLength.Auto }, new() { Width = GridLength.Auto }, new() { Width = GridLength.Auto } },
            Children = { pillScroller, filter, add, more },
        };
        Grid.SetColumn(filter, 1);
        Grid.SetColumn(add, 2);
        Grid.SetColumn(more, 3);
        var libraryHeader = new StackPanel { Children = { libraryTitle, toolbar } };
        _page.Children.Add(libraryHeader);
        _stickLibrary = () => StickLibraryHeader(libraryHeader);
        var status = DescriptionText("Reading the schemes…");
        _page.Children.Add(status);

        // Cards are made as they come into view; one scrolled away waits in a pool and comes back showing another scheme
        // (making one is ~30 elements and three button templates: making them anew while scrolling fast left gaps).
        // Each one in view knows how to show the state.
        var shownCards = new Dictionary<UIElement, Action>();
        var spareCards = new Stack<Button>();
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
            VerticalCacheLength = 6, // cards ready three screens above and below
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

        bool InForce(string name)
        {
            var (dark, light) = SchemeSides(ShownScheme(profile));
            return name.Equals(dark ?? ZincScheme, StringComparison.OrdinalIgnoreCase) || name.Equals(light, StringComparison.OrdinalIgnoreCase);
        }

        // Puts scheme name in place: the scheme, or with a pair, its light or dark side.
        void Use(string name, bool forLight)
        {
            var (dark, light) = SchemeSides(ShownScheme(profile));
            _previewedScheme = null; // in force now: the preview shows it anyway
            Write(path, light == null ? name : forLight ? $"light:{name},dark:{dark}" : $"light:{light},dark:{name}");
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

        // A card: the scheme as itself on top (its name and 16 colors on its background); under it "In use", whose it is,
        // Use and Edit (a pencil). With a light/dark pair, Use is a split button: its main part puts the scheme on the side
        // it suits (a dark scheme on the dark side; already there: the other side), its arrow offers both. The card is a
        // button: a click previews (Space too), Enter uses it. Made once, shown with any scheme (Show), so a card can be
        // reused for another.
        (Button Card, Action<SchemeEntry> Show, Action Update) NewCard()
        {
            SchemeEntry entry = null!;
            var sample = new SchemeSample();
            var inUseBadge = new TextBlock
            {
                Text = "✓ In use",
                FontSize = 12,
                Foreground = (Brush)Application.Current.Resources["AccentTextFillColorPrimaryBrush"],
                VerticalAlignment = VerticalAlignment.Center,
            };
            var whose = new TextBlock { FontSize = 12, Opacity = 0.7, VerticalAlignment = VerticalAlignment.Center };
            var badges = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center, Children = { inUseBadge, whose } };
            var forLight = false; // the side the split button's main part sets
            var use = CardButtonLook(new Button { Content = "Use", MinWidth = 72, VerticalAlignment = VerticalAlignment.Center });
            use.Click += (_, _) => Use(entry.Name, false);
            var forDarkItem = new MenuFlyoutItem { Text = "Use for dark mode" };
            forDarkItem.Click += (_, _) => Use(entry.Name, false);
            var forLightItem = new MenuFlyoutItem { Text = "Use for light mode" };
            forLightItem.Click += (_, _) => Use(entry.Name, true);
            var split = CardButtonLook(new SplitButton
            {
                VerticalAlignment = VerticalAlignment.Center,
                Flyout = new MenuFlyout { Items = { forDarkItem, forLightItem }, Placement = FlyoutPlacementMode.BottomEdgeAlignedRight },
            });
            split.Click += (_, _) => Use(entry.Name, forLight);
            var edit = CardButtonLook(new Button
            {
                Content = new FontIcon { Glyph = "\uE70F", FontSize = 14 }, // edit (pencil)
                Padding = new Thickness(9, 8, 9, 8),
                VerticalAlignment = VerticalAlignment.Stretch,
            });
            edit.Click += (_, _) => CustomizeScheme(entry.Name, path);
            var footer = new Grid
            {
                ColumnSpacing = 4,
                Padding = new Thickness(12, 6, 6, 6),
                ColumnDefinitions = { new() { Width = new GridLength(1, GridUnitType.Star) }, new() { Width = GridLength.Auto }, new() { Width = GridLength.Auto } },
                Children = { badges, use, split, edit },
            };
            Grid.SetColumn(use, 1);
            Grid.SetColumn(split, 1);
            Grid.SetColumn(edit, 2);
            Grid.SetRow(footer, 1);
            var ring = SelectionRing(new Thickness(0));
            Grid.SetRowSpan(ring, 2);
            var card = new Button
            {
                Content = new Grid
                {
                    RowDefinitions = { new() { Height = GridLength.Auto }, new() { Height = GridLength.Auto } },
                    Children = { sample.Element, footer, ring },
                },
                Padding = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Top,
                CornerRadius = new CornerRadius(6),
                Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
                BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
                BorderThickness = new Thickness(1),
            };
            card.Click += (_, _) => Preview(entry.Name);
            card.AddHandler(UIElement.PreviewKeyDownEvent, new KeyEventHandler((_, e) =>
            {
                if (e.Key == Windows.System.VirtualKey.Enter && ReferenceEquals(e.OriginalSource, card))
                {
                    if (use.Visibility == Visibility.Visible ? use.IsEnabled : split.IsEnabled)
                    {
                        Use(entry.Name, use.Visibility != Visibility.Visible && forLight);
                    }

                    e.Handled = true;
                }
            }), handledEventsToo: true);

            void Update()
            {
                inUseBadge.Visibility = InForce(entry.Name) ? Visibility.Visible : Visibility.Collapsed;
                var (dark, light) = SchemeSides(ShownScheme(profile));
                var pair = light != null;
                use.Visibility = pair ? Visibility.Collapsed : Visibility.Visible;
                split.Visibility = pair ? Visibility.Visible : Visibility.Collapsed;
                if (!pair)
                {
                    use.IsEnabled = !InForce(entry.Name);
                    AutomationProperties.SetName(use, $"Use {entry.Name}");
                }
                else
                {
                    var inDark = entry.Name.Equals(dark, StringComparison.OrdinalIgnoreCase);
                    var inLight = entry.Name.Equals(light, StringComparison.OrdinalIgnoreCase);
                    forLight = !entry.IsDark;
                    if (forLight ? inLight : inDark)
                    {
                        forLight = !forLight; // already on the side it suits: the main part offers the other
                    }

                    var label = forLight ? "Use for light" : "Use for dark";
                    split.Content = label;
                    split.IsEnabled = !(inDark && inLight);
                    forDarkItem.IsEnabled = !inDark;
                    forLightItem.IsEnabled = !inLight;
                    AutomationProperties.SetName(split, $"{label}: {entry.Name}");
                }

                ring.Visibility = entry.Name.Equals(_previewedScheme, StringComparison.OrdinalIgnoreCase) ? Visibility.Visible : Visibility.Collapsed;
            }

            void Show(SchemeEntry shown)
            {
                entry = shown;
                sample.Show(SchemeCatalog.Theme(shown.File), shown.Name);
                SetText(whose, shown.ReplacesBundled ? "Your copy" : shown.Yours ? "Yours" : "");
                ToolTipService.SetToolTip(whose, shown.ReplacesBundled ? "Your edited copy, used instead of the one Zinc comes with." : null);
                AutomationProperties.SetName(card, shown.Name);
                AutomationProperties.SetHelpText(card, $"{(shown.IsDark ? "Dark" : "Light")} scheme. Space previews it; Enter uses it.");
                var editTip = shown.Yours ? "Edit colors" : "Edit colors (makes your copy)";
                ToolTipService.SetToolTip(edit, editTip);
                AutomationProperties.SetName(edit, $"{editTip}: {shown.Name}");
                Update();
            }

            return (card, Show, Update);
        }

        var cardParts = new Dictionary<Button, (Action<SchemeEntry> Show, Action Update)>();
        grid.ItemTemplate = new SchemeCardFactory(
            data =>
            {
                if (!spareCards.TryPop(out var card))
                {
                    var made = NewCard();
                    card = made.Card;
                    cardParts[card] = (made.Show, made.Update);
                }

                var (show, update) = cardParts[card];
                show((SchemeEntry)data);
                shownCards[card] = update;
                return card;
            },
            element =>
            {
                shownCards.Remove(element);
                if (element is Button card)
                {
                    spareCards.Push(card);
                }
            });

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
                : _schemeKind == SchemeKind.Yours ? "None yet. Editing any scheme's colors, or New scheme (+), makes one of yours."
                : "No schemes here.");
        }

        foreach (var (kind, label) in new[] { (SchemeKind.All, "All"), (SchemeKind.Dark, "Dark"), (SchemeKind.Light, "Light"), (SchemeKind.Yours, "Yours") })
        {
            // Pills, as the Store's: the one shown is checked (accent).
            var pill = new ToggleButton { Content = label, IsChecked = kind == _schemeKind, CornerRadius = new CornerRadius(16), Padding = new Thickness(16, 5, 16, 6) };
            pill.Click += (_, _) =>
            {
                _schemeKind = kind;
                foreach (var other in pills.Children.OfType<ToggleButton>())
                {
                    other.IsChecked = ReferenceEquals(other, pill);
                }

                FillLibrary();
            };
            pills.Children.Add(pill);
        }

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
            quiet = false;
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

    /// <summary>
    /// One outline for the buttons on a scheme card: a split button draws its border all around (and a plain button only
    /// a faint top edge), so with a light/dark pair the cards' buttons looked heavier. Set before the template applies.
    /// </summary>
    private static T CardButtonLook<T>(T button) where T : Control
    {
        var stroke = Application.Current.Resources["ControlStrokeColorSecondaryBrush"]; // what a split button shows
        var prefix = button is SplitButton ? "SplitButton" : "Button";
        foreach (var state in new[] { "", "PointerOver", "Pressed" })
        {
            button.Resources[$"{prefix}BorderBrush{state}"] = stroke;
        }

        button.BorderBrush = (Brush)stroke;
        return button;
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
