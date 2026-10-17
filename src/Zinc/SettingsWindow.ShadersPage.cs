using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Zinc.Settings;
using Zinc.Shaders;
using Zinc.Terminal;

namespace Zinc;

// The shaders page: a sub-page of Appearance (or of a profile's page), opened by the Custom shaders card's Edit shaders.
// The list in force (order, compile state, open, remove) and its Animation setting; then the library: Zinc's shaders,
// yours and the folders you added (Store-style: pills for where from, a search, a card per shader). A click on a shader runs it in the preview above, after
// the list's own, and checks it compiles; a click again takes it back. Add or Remove changes the list.
internal sealed partial class SettingsWindow
{
    /// <summary>The shaders page is shown (for <see cref="_shadersProfile"/>: null, the global list).</summary>
    private bool _editingShaders;

    private string? _shadersProfile;

    // The library shader running in the preview (its file), the library as last read, and the library's filters.
    private string? _previewedShader;
    private IReadOnlyList<ShaderSection> _shaderLibrary = [];
    private string? _shaderSource;
    private string _shaderQuery = "";

    private void OpenShaders(string? profile)
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
        _editingShaders = true;
        _shadersProfile = profile;
        _previewedShader = null;
        _shaderSource = null;
        _shaderQuery = "";
        PreviewForLibrary(open: true);
        Render();
        _scroller.ChangeView(null, 0, null, disableAnimation: true);
    }

    private void ShadersPage(string? profile)
    {
        var setting = SettingsCatalog.Find("shaders.files")!;
        var error = ErrorText();
        _page.Children.Add(Note(setting.Description + " Click a shader in the library to preview it; click again to stop."));

        // ---- In use
        var description = DescriptionText();
        var reset = ResetArrow("Custom shaders", "Back to the shaders every profile inherits");
        reset.Click += (_, _) => Write(["profile", profile!, "shaders", "files"], null);
        var rows = new StackPanel { Spacing = 4 };
        var current = 0;

        void RefreshList()
        {
            var own = OwnShaders(profile);
            var files = ShownShaders(profile);
            reset.Visibility = profile != null && own != null ? Visibility.Visible : Visibility.Collapsed;
            if (profile != null)
            {
                var inherited = InheritedShaders();
                InheritedText(description, setting, inherited.Count == 0 ? "none" : string.Join(", ", inherited.Select(ShaderName)), own != null);
            }

            rows.Children.Clear();
            if (!_config.Global.LowLatencyDrawing)
            {
                var off = ErrorText();
                off.Text = "Shaders need \"Low-latency drawing\", which is off (Terminal > Troubleshooting).";
                off.Visibility = Visibility.Visible;
                rows.Children.Add(off);
            }

            if (files.Count == 0)
            {
                rows.Children.Add(DescriptionText(profile != null && own != null ? "None in this profile." : "None yet: add some from the library below."));
            }

            var load = ++current;
            for (var i = 0; i < files.Count; i++)
            {
                rows.Children.Add(ShaderRow(files, i, list => SaveShaders(profile, list, error), () => load == current && _editingShaders));
            }
        }

        // The group title's margins on the row, so the arrow centers on the title's text, not on its margins too.
        var inUse = PartTitle("In use");
        var listTitle = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Margin = inUse.Margin, Children = { inUse, reset } };
        inUse.Margin = new Thickness(0);
        inUse.VerticalAlignment = VerticalAlignment.Center;
        reset.VerticalAlignment = VerticalAlignment.Center;
        _page.Children.Add(listTitle);
        if (profile != null)
        {
            _page.Children.Add(description);
        }

        _page.Children.Add(error);
        _page.Children.Add(CardBorder(rows));
        var animation = SettingsCatalog.Find("shaders.animation")!;
        _page.Children.Add(profile == null ? Card(animation) : OverrideCard(animation, profile));

        // ---- Library, as the Store's: a heading with the count; one toolbar row on the page (where from as pills, a search,
        // a menu for folders and files); then each shader as a card of its own, under a heading per source.
        var libraryTitle = PartTitle("Library");
        var sources = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        AutomationProperties.SetName(sources, "Shader sources");
        var filter = new TextBox { PlaceholderText = "Search", Text = _shaderQuery, Width = 240, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(filter, "Search shaders");
        var more = new Button
        {
            Content = new FontIcon { Glyph = "", FontSize = 14 }, // more (…)
            Padding = new Thickness(10, 8, 10, 8),
            VerticalAlignment = VerticalAlignment.Center,
        };
        ToolTipService.SetToolTip(more, "Folders and files");
        AutomationProperties.SetName(more, "Folders and files");
        var pills = new ScrollViewer
        {
            Content = sources,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var toolbar = new Grid
        {
            ColumnSpacing = 8,
            Margin = new Thickness(0, 0, 0, 4),
            ColumnDefinitions = { new() { Width = new GridLength(1, GridUnitType.Star) }, new() { Width = GridLength.Auto }, new() { Width = GridLength.Auto } },
            Children = { pills, filter, more },
        };
        Grid.SetColumn(filter, 1);
        Grid.SetColumn(more, 2);
        // The heading and the toolbar stay in view once the library's scrolled to (StickLibraryHeader).
        var libraryHeader = new StackPanel { Children = { libraryTitle, toolbar } };
        _page.Children.Add(libraryHeader);
        _stickLibrary = () => StickLibraryHeader(libraryHeader);
        var status = ErrorText(); // only when something went wrong (reading the folders)
        _page.Children.Add(status);
        var library = new StackPanel { Spacing = 4 };
        _page.Children.Add(library);

        // A card per shader: a click previews it (again: stops); "In use" when the list has it; Add or Remove; a menu.
        var rowsByFile = new Dictionary<string, (Border Ring, Button Toggle, TextBlock InUse, TextBlock Detail, ShaderEntry Entry)>(StringComparer.OrdinalIgnoreCase);
        var cards = new List<Button>();

        bool InList(ShaderEntry entry) => ShownShaderFiles(profile).Contains(entry.File, StringComparer.OrdinalIgnoreCase);

        void ShowToggle((Border Ring, Button Toggle, TextBlock InUse, TextBlock Detail, ShaderEntry Entry) row)
        {
            var inList = InList(row.Entry);
            row.Toggle.Content = inList ? "Remove" : "Add";
            row.InUse.Visibility = inList ? Visibility.Visible : Visibility.Collapsed;
            AutomationProperties.SetName(row.Toggle, $"{(inList ? "Remove" : "Add")} {row.Entry.Name}");
        }

        void ShowPreviewed()
        {
            foreach (var row in rowsByFile.Values)
            {
                var on = row.Entry.File.Equals(_previewedShader, StringComparison.OrdinalIgnoreCase);
                row.Ring.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
                if (!on)
                {
                    row.Detail.Visibility = Visibility.Collapsed;
                }
            }
        }

        void Toggle(ShaderEntry entry)
        {
            var files = ShownShaders(profile);
            if (InList(entry))
            {
                SaveShaders(profile, [.. files.Where(w => !ShaderPaths.Resolve(w, _config.Path).Equals(entry.File, StringComparison.OrdinalIgnoreCase))], error);
            }
            else
            {
                SaveShaders(profile, [.. files, entry.Written], error);
            }
        }

        async void Preview(ShaderEntry entry)
        {
            if (entry.File.Equals(_previewedShader, StringComparison.OrdinalIgnoreCase))
            {
                _previewedShader = null; // the same one again: back to the list as it is
                ShowPreviewed();
                ApplyShaderPreview();
                return;
            }

            _previewedShader = entry.File;
            ShowPreviewed();
            ApplyShaderPreview();
            if (!rowsByFile.TryGetValue(entry.File, out var row))
            {
                return;
            }

            row.Detail.Visibility = Visibility.Visible;
            row.Detail.ClearValue(TextBlock.ForegroundProperty);
            SetText(row.Detail, "Checking…");
            try
            {
                var loaded = await ShaderLoader.Load([entry.File]);
                if (!entry.File.Equals(_previewedShader, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                if (loaded.Errors.Count == 0)
                {
                    SetText(row.Detail, "In the preview. Compiles.");
                }
                else
                {
                    row.Detail.Foreground = (Brush)Application.Current.Resources["SystemFillColorCriticalBrush"];
                    SetText(row.Detail, loaded.Errors[0]);
                }
            }
            catch (Exception ex)
            {
                Log.Crash("checking a shader", ex);
            }
        }

        Button ShaderCard(ShaderEntry entry)
        {
            var inUse = new TextBlock
            {
                Text = "✓ In use",
                FontSize = 12,
                Foreground = (Brush)Application.Current.Resources["AccentTextFillColorPrimaryBrush"],
                VerticalAlignment = VerticalAlignment.Center,
            };
            var name = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { new TextBlock { Text = entry.Name, FontWeight = FontWeights.SemiBold } } };
            if (entry.UsesTime)
            {
                var animated = new TextBlock { Text = "Animated", FontSize = 12, Opacity = 0.7, VerticalAlignment = VerticalAlignment.Center };
                ToolTipService.SetToolTip(animated, "Keeps redrawing while the terminal is idle. The \"Animation\" setting decides when.");
                name.Children.Add(animated);
            }

            name.Children.Add(inUse);
            var detail = DescriptionText();
            detail.Visibility = Visibility.Collapsed;
            var text = new StackPanel
            {
                Spacing = 2,
                VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    name,
                    // A line about it, or a note there isn't one (the cards keep one height).
                    entry.Description is { } about
                        ? new TextBlock { Text = about, FontSize = 12, Opacity = 0.75, TextWrapping = TextWrapping.Wrap }
                        : new TextBlock { Text = "No description in the file.", FontSize = 12, Opacity = 0.5, FontStyle = Windows.UI.Text.FontStyle.Italic },
                    detail,
                },
            };

            var toggle = new Button { VerticalAlignment = VerticalAlignment.Center, MinWidth = 96 };
            toggle.Click += (_, _) => Toggle(entry);
            var menu = new MenuFlyout();
            var open = new MenuFlyoutItem { Text = "Open in editor" };
            open.Click += (_, _) => OpenShader(entry.File);
            var reveal = new MenuFlyoutItem { Text = "Open folder" };
            reveal.Click += (_, _) => Reveal(entry.File);
            menu.Items.Add(open);
            menu.Items.Add(reveal);
            var options = new Button
            {
                Content = new FontIcon { Glyph = "", FontSize = 14 },
                Flyout = menu,
                Padding = new Thickness(10, 8, 10, 8),
                VerticalAlignment = VerticalAlignment.Center,
                Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                BorderThickness = new Thickness(0),
            };
            ToolTipService.SetToolTip(options, "More");
            AutomationProperties.SetName(options, $"More for {entry.Name}");

            var ring = SelectionRing(new Thickness(16, 10, 8, 10)); // the card's padding
            Grid.SetColumnSpan(ring, 3);
            var grid = new Grid
            {
                ColumnSpacing = 8,
                ColumnDefinitions = { new() { Width = new GridLength(1, GridUnitType.Star) }, new() { Width = GridLength.Auto }, new() { Width = GridLength.Auto } },
                Children = { text, toggle, options, ring },
            };
            Grid.SetColumn(toggle, 1);
            Grid.SetColumn(options, 2);

            // The card itself is the button (its hover covers all of it, the Add button too); the buttons in it take their
            // own clicks.
            var card = new Button
            {
                Content = grid,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Padding = new Thickness(16, 10, 8, 10),
                CornerRadius = new CornerRadius(4),
                Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
                BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
                BorderThickness = new Thickness(1),
            };
            AutomationProperties.SetName(card, entry.Name);
            AutomationProperties.SetHelpText(card, $"{entry.Description} Space previews; Enter adds or removes.".Trim());
            ToolTipService.SetToolTip(card, entry.File);
            card.Click += (_, _) => Preview(entry);
            // Enter adds or removes (Space previews: a button's click); arrows move between shaders.
            card.AddHandler(UIElement.PreviewKeyDownEvent, new KeyEventHandler((s, e) =>
            {
                if (!ReferenceEquals(e.OriginalSource, card))
                {
                    return; // a key on the Add or menu button inside
                }

                var at = cards.IndexOf(card);
                if (e.Key == Windows.System.VirtualKey.Enter)
                {
                    Toggle(entry);
                    e.Handled = true;
                }
                else if (e.Key is Windows.System.VirtualKey.Down or Windows.System.VirtualKey.Up)
                {
                    var next = at + (e.Key == Windows.System.VirtualKey.Down ? 1 : -1);
                    if (next >= 0 && next < cards.Count)
                    {
                        cards[next].Focus(FocusState.Keyboard);
                        cards[next].StartBringIntoView();
                    }

                    e.Handled = true;
                }
            }), handledEventsToo: true);
            cards.Add(card);
            rowsByFile[entry.File] = (ring, toggle, inUse, detail, entry);
            ShowToggle(rowsByFile[entry.File]);
            return card;
        }

        void FillLibrary()
        {
            library.Children.Clear();
            rowsByFile.Clear();
            cards.Clear();
            var query = _shaderQuery.Trim();
            var shown = 0;
            var all = 0;
            foreach (var section in _shaderLibrary)
            {
                if (_shaderSource != null && section.Title != _shaderSource)
                {
                    continue;
                }

                all += section.Shaders.Count;
                var matches = section.Shaders.Where(s => query.Length == 0 || s.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    (s.Description?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false)).ToList();
                if (matches.Count == 0 && !(section.Missing && query.Length == 0))
                {
                    continue;
                }

                // Under All, a heading per source, as every group in Settings (with its count), and its folder; under one
                // source's pill, only its folder.
                if (_shaderSource == null)
                {
                    var heading = GroupTitle($"{section.Title} · {section.Shaders.Count}");
                    if (library.Children.Count == 0)
                    {
                        heading.Margin = new Thickness(0, 8, 0, 6);
                    }

                    library.Children.Add(heading);
                }

                if (section.Folder != null)
                {
                    library.Children.Add(new TextBlock
                    {
                        Text = section.Missing ? $"Not found: {section.Folder}" : section.Folder,
                        FontSize = 12, Opacity = 0.7, TextTrimming = TextTrimming.CharacterEllipsis,
                        Margin = new Thickness(0, _shaderSource == null ? -4 : 4, 0, 4),
                    });
                }

                foreach (var entry in matches)
                {
                    library.Children.Add(ShaderCard(entry));
                    shown++;
                }
            }

            libraryTitle.Text = query.Length > 0 ? $"Library · {shown} of {all} shaders" : all == 1 ? "Library · 1 shader" : $"Library · {all} shaders";
            if (shown == 0 && _shaderLibrary.Count > 0)
            {
                library.Children.Add(DescriptionText(query.Length > 0 ? $"Nothing matches \"{query}\"." : "No shaders here."));
            }

            ShowPreviewed();
        }

        void FillSources()
        {
            sources.Children.Clear();
            if (_shaderSource != null && _shaderLibrary.All(s => s.Title != _shaderSource))
            {
                _shaderSource = null; // its folder is gone
            }

            foreach (var title in _shaderLibrary.Select(s => s.Title).Prepend(null))
            {
                // Pills, as the Store's: the one shown is checked (accent).
                var pill = new ToggleButton
                {
                    Content = title ?? "All",
                    IsChecked = title == _shaderSource,
                    CornerRadius = new CornerRadius(16),
                    Padding = new Thickness(16, 5, 16, 6),
                };
                pill.Click += (_, _) =>
                {
                    _shaderSource = title;
                    foreach (var other in sources.Children.OfType<ToggleButton>())
                    {
                        other.IsChecked = ReferenceEquals(other, pill);
                    }

                    FillLibrary();
                };
                sources.Children.Add(pill);
            }
        }

        // The toolbar's menu: add a folder or a file; stop listing a folder you added.
        void FillMenu()
        {
            var menu = new MenuFlyout { Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.BottomEdgeAlignedRight };
            var addFolder = new MenuFlyoutItem { Text = "Add a folder…", Icon = new FontIcon { Glyph = "" } };
            addFolder.Click += (_, _) =>
            {
                var folder = FolderDialog.Pick(WinRT.Interop.WindowNative.GetWindowHandle(this), ShaderFolders().LastOrDefault(), "Add a folder of shaders");
                if (folder == null)
                {
                    return;
                }

                _hiddenShaderFolders.RemoveAll(f => f.Equals(folder, StringComparison.OrdinalIgnoreCase));
                _addedShaderFolders.Add(folder); // listed at once, before config.toml is read again
                WriteShaderFolders([.. WrittenShaderFolders(), ShaderPaths.ForConfig(folder, _config.Path)]);
                _shaderSource = Path.GetFileName(folder.TrimEnd('\\', '/'));
                LoadLibrary();
            };
            var addFile = new MenuFlyoutItem { Text = "Add a file…", Icon = new FontIcon { Glyph = "" } };
            addFile.Click += async (_, _) =>
            {
                // Code after an await resumes as queued work, where an exception would end the app.
                try
                {
                    var picked = await PickShaders();
                    if (picked.Count > 0)
                    {
                        SaveShaders(profile, [.. ShownShaders(profile), .. picked.Select(p => ShaderPaths.ForConfig(p, _config.Path))], error);
                    }
                }
                catch (Exception ex)
                {
                    Log.Crash("picking shaders", ex);
                    Show(error, $"Couldn't open the file picker: {ex.Message}");
                }
            };
            menu.Items.Add(addFolder);
            menu.Items.Add(addFile);
            var listed = ShaderFolders();
            if (listed.Count > 0)
            {
                menu.Items.Add(new MenuFlyoutSeparator());
            }

            foreach (var folder in listed)
            {
                var name = Path.GetFileName(folder.TrimEnd('\\', '/'));
                var stop = new MenuFlyoutItem { Text = $"Stop listing {name}", Icon = new FontIcon { Glyph = "" } };
                ToolTipService.SetToolTip(stop, folder);
                stop.Click += (_, _) =>
                {
                    _addedShaderFolders.RemoveAll(f => f.Equals(folder, StringComparison.OrdinalIgnoreCase));
                    WriteShaderFolders([.. WrittenShaderFolders().Where(w => !ShaderPaths.ResolveFolder(w, _config.Path).Equals(folder, StringComparison.OrdinalIgnoreCase))]);
                    _hiddenShaderFolders.Add(folder); // gone at once, before config.toml is read again
                    if (_shaderSource == name)
                    {
                        _shaderSource = null;
                    }

                    LoadLibrary();
                };
                menu.Items.Add(stop);
            }

            more.Flyout = menu;
        }

        async void LoadLibrary()
        {
            try
            {
                var configFile = _config.Path;
                var folderList = ShaderFolders();
                _shaderLibrary = await Task.Run(() => ShaderLibrary.List(configFile, folderList));
                if (!_editingShaders)
                {
                    return;
                }

                FillSources();
                FillMenu();
                FillLibrary();
            }
            catch (Exception ex)
            {
                Log.Crash("listing shaders", ex);
                Show(status, $"Couldn't list the shaders: {ex.Message}");
            }
        }

        filter.TextChanged += (_, _) =>
        {
            _shaderQuery = filter.Text;
            FillLibrary();
        };
        filter.KeyDown += (_, e) =>
        {
            if (e.Key == Windows.System.VirtualKey.Down && cards.Count > 0)
            {
                cards[0].Focus(FocusState.Keyboard);
                e.Handled = true;
            }
        };

        _refreshers.Add(() =>
        {
            RefreshList();
            foreach (var row in rowsByFile.Values)
            {
                ShowToggle(row);
            }

            ApplyShaderPreview(); // a reload shows the config in force: the previewed one goes back on top
        });
        _shaderRows = RefreshList;
        HookLibrarySticking();
        WatchShaderFiles();
        Quietly(RefreshList);
        library.Children.Add(DescriptionText("Reading the shaders…"));
        LoadLibrary();
    }

    /// <summary>
    /// The accent ring of a library card (one that's a Button) while it's the one previewed: drawn over the card's own border
    /// from inside its content (<paramref name="cardPadding"/> out), so the hover and press states, which set the button's
    /// border themselves, can't take it away.
    /// </summary>
    private static Border SelectionRing(Thickness cardPadding) => new()
    {
        BorderBrush = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"],
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(6),
        Margin = new Thickness(-cardPadding.Left - 1, -cardPadding.Top - 1, -cardPadding.Right - 1, -cardPadding.Bottom - 1),
        IsHitTestVisible = false,
        Visibility = Visibility.Collapsed,
    };

    /// <summary>A heading for a part of a page ("In use", "Library"): one size up from a group's.</summary>
    private static TextBlock PartTitle(string title) =>
        new() { Text = title, Style = TextStyle("SubtitleTextBlockStyle"), Margin = new Thickness(0, 24, 0, 8) };

    // The library's heading and toolbar, kept in view: moved into the pinned area (under the preview) once scrolled up to
    // it, back when scrolled above it. A spacer of its height takes its place in the page, and the scroll moves by that
    // height each way, so nothing on screen jumps.
    private Action? _stickLibrary;
    private bool _libraryPinned;
    private Border? _librarySpacer;
    private bool _sticking;
    private bool _stickHooked;

    private void StickLibraryHeader(StackPanel header)
    {
        if (_sticking || !(_editingShaders || _editingSchemes) || _scroller.Content is not UIElement content)
        {
            return;
        }

        _sticking = true;
        try
        {
            if (!_libraryPinned)
            {
                var top = header.TransformToVisual(content).TransformPoint(default).Y;
                var height = header.ActualHeight;
                if (height > 0 && _scroller.VerticalOffset >= top)
                {
                    var at = _page.Children.IndexOf(header);
                    if (at < 0)
                    {
                        return;
                    }

                    _librarySpacer = new Border { Height = height };
                    _page.Children.RemoveAt(at);
                    _page.Children.Insert(at, _librarySpacer);
                    _pinned.Children.Add(header);
                    _libraryPinned = true;
                    _scroller.ChangeView(null, _scroller.VerticalOffset + height, null, disableAnimation: true);
                }
            }
            else if (_librarySpacer is { } spacer)
            {
                var top = spacer.TransformToVisual(content).TransformPoint(default).Y;
                var height = spacer.Height;
                if (_scroller.VerticalOffset < top + height)
                {
                    _pinned.Children.Remove(header);
                    var at = _page.Children.IndexOf(spacer);
                    _page.Children.RemoveAt(at);
                    _page.Children.Insert(at, header);
                    _libraryPinned = false;
                    _librarySpacer = null;
                    _scroller.ChangeView(null, Math.Max(0, _scroller.VerticalOffset - height), null, disableAnimation: true);
                }
            }
        }
        catch (ArgumentException)
        {
            // not in the tree (a page being rebuilt)
        }
        finally
        {
            _sticking = false;
        }
    }

    /// <summary>Hooks the scroller once: each scroll lets the shaders or color schemes page keep its library header in view.</summary>
    private void HookLibrarySticking()
    {
        if (!_stickHooked)
        {
            _stickHooked = true;
            _scroller.ViewChanged += (_, _) => _stickLibrary?.Invoke();
        }
    }

    // Folders taken out since config.toml was last read (gone from the library at once).
    private readonly List<string> _hiddenShaderFolders = [];

    /// <summary>The preview: the list in force, and the library shader being previewed after it, if any.</summary>
    private void ApplyShaderPreview()
    {
        if (!_editingShaders)
        {
            return;
        }

        if (_previewedShader is { } file)
        {
            var files = ShownShaderFiles(_shadersProfile);
            PreviewShaders(files.Contains(file, StringComparer.OrdinalIgnoreCase) ? files : [.. files, file]);
        }
        else
        {
            RefreshPreview();
        }
    }
}
