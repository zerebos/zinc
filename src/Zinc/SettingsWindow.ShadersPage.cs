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
// yours and the folders you added, filtered by name or source. A click on a shader runs it in the preview above, after
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
        PreviewForShaders(open: true);
        Render();
        _scroller.ChangeView(null, 0, null, disableAnimation: true);
    }

    private void LeaveShaders()
    {
        _editingShaders = false;
        _previewedShader = null;
        PreviewForShaders(open: false);
        Render();
    }

    private void ShadersPage(string? profile)
    {
        var back = new HyperlinkButton { Content = "‹ Back", Padding = new Thickness(0, 2, 0, 2) };
        AutomationProperties.SetName(back, "Back");
        back.Click += (_, _) => LeaveShaders();
        _page.Children.Insert(0, back);
        var setting = SettingsCatalog.Find("shaders.files")!;
        var error = ErrorText();
        _page.Children.Add(Note(setting.Description + " Click one in the library to see it in the preview, and again to take it back."));

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
                off.Text = "Shaders need low-latency drawing, which is off: Terminal > Troubleshooting.";
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
        var inUse = GroupTitle("In use");
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

        // ---- Library
        _page.Children.Add(GroupTitle("Library"));
        var filter = new TextBox { PlaceholderText = "Filter by name or description", Text = _shaderQuery };
        AutomationProperties.SetName(filter, "Filter shaders");
        var sources = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        var status = DescriptionText();
        AutomationProperties.SetLiveSetting(status, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite);
        var library = new StackPanel { Spacing = 2 };
        var folders = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        var libraryCard = CardBorder(new StackPanel { Spacing = 8, Children = { filter, new ScrollViewer { Content = sources, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled }, status, library } });
        _page.Children.Add(libraryCard);
        _page.Children.Add(new ScrollViewer { Content = folders, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Margin = new Thickness(0, 4, 0, 0) });

        // A row per shader: what it is (a click previews it), and Add or Remove.
        var rowsByFile = new Dictionary<string, (Border Row, Button Toggle, TextBlock Detail, ShaderEntry Entry)>(StringComparer.OrdinalIgnoreCase);
        var wideButtons = new List<Button>();

        bool InList(ShaderEntry entry) => ShownShaderFiles(profile).Contains(entry.File, StringComparer.OrdinalIgnoreCase);

        void ShowToggle((Border Row, Button Toggle, TextBlock Detail, ShaderEntry Entry) row)
        {
            var inList = InList(row.Entry);
            row.Toggle.Content = inList ? "Remove" : "Add";
            AutomationProperties.SetName(row.Toggle, $"{(inList ? "Remove" : "Add")} {row.Entry.Name}");
        }

        void ShowPreviewed()
        {
            foreach (var row in rowsByFile.Values)
            {
                var on = row.Entry.File.Equals(_previewedShader, StringComparison.OrdinalIgnoreCase);
                row.Row.Background = on ? (Brush)Application.Current.Resources["SubtleFillColorSecondaryBrush"] : null;
                row.Row.BorderBrush = on ? (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"] : null;
                if (!on)
                {
                    row.Detail.Visibility = Visibility.Collapsed;
                }
            }
        }

        void Toggle(ShaderEntry entry)
        {
            var files = ShownShaders(profile);
            var removing = InList(entry); // (read before saving: the list changes once config.toml is read again)
            var saved = removing
                ? SaveShaders(profile, [.. files.Where(w => !ShaderPaths.Resolve(w, _config.Path).Equals(entry.File, StringComparison.OrdinalIgnoreCase))], error)
                : SaveShaders(profile, [.. files, entry.Written], error);
            if (saved)
            {
                SetText(status, removing ? $"{entry.Name}: taken out of the list." : $"{entry.Name}: added to the list.");
            }
        }

        async void Preview(ShaderEntry entry)
        {
            if (entry.File.Equals(_previewedShader, StringComparison.OrdinalIgnoreCase))
            {
                _previewedShader = null; // the same one again: back to the list as it is
                ShowPreviewed();
                ApplyShaderPreview();
                SetText(status, "");
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
                    SetText(row.Detail, entry.UsesTime ? "Compiles. Moves on its own: it redraws while the terminal is idle." : "Compiles.");
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

        Border Row(ShaderEntry entry)
        {
            var name = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { new TextBlock { Text = entry.Name, FontWeight = FontWeights.SemiBold } } };
            if (entry.UsesTime)
            {
                name.Children.Add(new TextBlock { Text = "moves on its own", FontSize = 12, Opacity = 0.7, VerticalAlignment = VerticalAlignment.Center });
            }

            var text = new StackPanel { Spacing = 2, Children = { name } };
            // A line about it, or a note there isn't one (the rows keep one height, and their buttons apart).
            text.Children.Add(entry.Description is { } about
                ? new TextBlock { Text = about, Opacity = 0.75, TextWrapping = TextWrapping.Wrap }
                : new TextBlock { Text = "No description in the file.", Opacity = 0.5, FontStyle = Windows.UI.Text.FontStyle.Italic });

            var detail = DescriptionText();
            detail.Visibility = Visibility.Collapsed;
            text.Children.Add(detail);
            var wide = new Button
            {
                Content = text,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                BorderThickness = new Thickness(0),
                Padding = new Thickness(8, 6, 8, 6),
            };
            AutomationProperties.SetName(wide, entry.Name);
            AutomationProperties.SetHelpText(wide, $"{entry.Description} Click to preview; Enter adds or removes.".Trim());
            ToolTipService.SetToolTip(wide, entry.File);
            wide.Click += (_, _) => Preview(entry);
            // Enter adds or removes (Space previews: a button's click); arrows move between shaders.
            wide.AddHandler(UIElement.PreviewKeyDownEvent, new KeyEventHandler((_, e) =>
            {
                var at = wideButtons.IndexOf(wide);
                if (e.Key == Windows.System.VirtualKey.Enter)
                {
                    Toggle(entry);
                    e.Handled = true;
                }
                else if (e.Key is Windows.System.VirtualKey.Down or Windows.System.VirtualKey.Up)
                {
                    var next = at + (e.Key == Windows.System.VirtualKey.Down ? 1 : -1);
                    if (next >= 0 && next < wideButtons.Count)
                    {
                        wideButtons[next].Focus(FocusState.Keyboard);
                        wideButtons[next].StartBringIntoView();
                    }

                    e.Handled = true;
                }
            }), handledEventsToo: true);
            wideButtons.Add(wide);

            var toggle = new Button { VerticalAlignment = VerticalAlignment.Center, MinWidth = 88 };
            toggle.Click += (_, _) => Toggle(entry);
            var grid = new Grid
            {
                ColumnSpacing = 8,
                ColumnDefinitions = { new() { Width = new GridLength(1, GridUnitType.Star) }, new() { Width = GridLength.Auto } },
                Children = { wide, toggle },
            };
            Grid.SetColumn(toggle, 1);
            var row = new Border { Child = grid, CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1), Padding = new Thickness(0, 0, 8, 0) };
            rowsByFile[entry.File] = (row, toggle, detail, entry);
            ShowToggle(rowsByFile[entry.File]);
            return row;
        }

        void FillLibrary()
        {
            library.Children.Clear();
            rowsByFile.Clear();
            wideButtons.Clear();
            var query = _shaderQuery.Trim();
            var shown = 0;
            foreach (var section in _shaderLibrary)
            {
                if (_shaderSource != null && section.Title != _shaderSource)
                {
                    continue;
                }

                var matches = section.Shaders.Where(s => query.Length == 0 || s.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    (s.Description?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false)).ToList();
                if (matches.Count == 0 && !(section.Missing && query.Length == 0))
                {
                    continue;
                }

                var header = new StackPanel { Margin = new Thickness(8, library.Children.Count == 0 ? 0 : 10, 0, 4) };
                header.Children.Add(new TextBlock { Text = section.Title, FontWeight = FontWeights.SemiBold });
                if (section.Folder != null)
                {
                    header.Children.Add(new TextBlock
                    {
                        Text = section.Missing ? $"Not found: {section.Folder}" : section.Folder,
                        FontSize = 12, Opacity = 0.7, TextTrimming = TextTrimming.CharacterEllipsis,
                    });
                }

                library.Children.Add(header);
                foreach (var entry in matches)
                {
                    library.Children.Add(Row(entry));
                    shown++;
                }
            }

            if (shown == 0 && _shaderLibrary.Count > 0)
            {
                library.Children.Add(DescriptionText(query.Length > 0 ? $"Nothing matches “{query}”." : "No shaders here."));
            }

            ShowPreviewed();
        }

        void FillSources()
        {
            sources.Children.Clear();
            foreach (var title in _shaderLibrary.Select(s => s.Title).Prepend(null))
            {
                var chip = new ToggleButton
                {
                    Content = title ?? $"All ({_shaderLibrary.Sum(s => s.Shaders.Count)})",
                    IsChecked = title == _shaderSource,
                    CornerRadius = new CornerRadius(14),
                    Padding = new Thickness(12, 4, 12, 4),
                };
                chip.Click += (_, _) =>
                {
                    _shaderSource = title;
                    foreach (var other in sources.Children.OfType<ToggleButton>())
                    {
                        other.IsChecked = ReferenceEquals(other, chip);
                    }

                    FillLibrary();
                };
                sources.Children.Add(chip);
            }
        }

        void FillFolders()
        {
            folders.Children.Clear();
            foreach (var folder in ShaderFolders())
            {
                var name = Path.GetFileName(folder.TrimEnd('\\', '/'));
                var chip = new Button
                {
                    Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { new TextBlock { Text = name }, new FontIcon { Glyph = "", FontSize = 10 } } },
                    CornerRadius = new CornerRadius(14),
                    Padding = new Thickness(12, 4, 10, 4),
                };
                ToolTipService.SetToolTip(chip, $"Stop listing {folder}");
                AutomationProperties.SetName(chip, $"Stop listing {name}");
                chip.Click += (_, _) =>
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
                folders.Children.Add(chip);
            }

            var addFolder = new Button { Content = "Add a folder…" };
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
            var fromFile = new Button { Content = "Add a file…" };
            fromFile.Click += async (_, _) =>
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
            folders.Children.Add(addFolder);
            folders.Children.Add(fromFile);
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
                FillFolders();
                FillLibrary();
            }
            catch (Exception ex)
            {
                Log.Crash("listing shaders", ex);
                SetText(status, $"Couldn't list the shaders: {ex.Message}");
            }
        }

        filter.TextChanged += (_, _) =>
        {
            _shaderQuery = filter.Text;
            FillLibrary();
        };
        filter.KeyDown += (_, e) =>
        {
            if (e.Key == Windows.System.VirtualKey.Down && wideButtons.Count > 0)
            {
                wideButtons[0].Focus(FocusState.Keyboard);
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
        WatchShaderFiles();
        Quietly(RefreshList);
        library.Children.Add(DescriptionText("Reading the shaders…"));
        LoadLibrary();
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
