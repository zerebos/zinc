using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Zinc.Panes;
using Zinc.Session;
using Zinc.Terminal;

namespace Zinc;

/// <summary>
/// The new-workspace page (<c>new-workspace-page</c>): shown in a new workspace's tab until it
/// starts. Pick a profile and a folder (typing filters both; a typed path can be used as is), name
/// it or keep the folder's name, then Enter or Start. Untouched, Enter starts the default profile
/// in its usual folder, as a new workspace did before. A chosen folder becomes the workspace's own
/// (<see cref="WorkspaceFolder"/>): its new tabs and splits start there.
/// </summary>
/// <summary>
/// What the page starts: <see cref="Color"/> counts only when <see cref="ColorChosen"/> (else the
/// automatic colors decide); a null <see cref="Icon"/> keeps the profile's icon.
/// </summary>
internal sealed record StartChoice(Profile Profile, WorkspaceFolder? Folder, string? Name, StartLayout Layout,
    string? Icon, Windows.UI.Color? Color, bool ColorChosen);

internal sealed partial class StartPage : UserControl
{
    private readonly IReadOnlyList<Profile> _profiles;
    private readonly Profile _default;
    private readonly RecentFolders _recent;
    /// <summary>A folder dialog starting in the given folder (null: its own choice); returns the choice or null.</summary>
    private readonly Func<string?, string?> _browse;
    private readonly Dictionary<Profile, string?> _distros = [];

    private readonly TextBox _filter = new() { PlaceholderText = "Filter profiles and folders, or type a folder", Width = 520, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly ListView _profileList = new() { SelectionMode = ListViewSelectionMode.Single };
    private readonly ListView _folderList = new() { SelectionMode = ListViewSelectionMode.Single };
    private readonly ListView _layoutList = new() { SelectionMode = ListViewSelectionMode.Single };
    private StartLayout _layout = StartLayout.One;
    private string? _icon;
    private Windows.UI.Color? _color;
    private bool _colorChosen;
    private readonly Button _iconButton = new() { Width = 36, Height = 32, Padding = new Thickness(0), VerticalAlignment = VerticalAlignment.Center };
    private readonly Button _colorButton = new() { Width = 36, Height = 32, Padding = new Thickness(0), VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBox _name = new() { Width = 200, VerticalAlignment = VerticalAlignment.Center };
    private readonly CheckBox _showPage = new() { Content = "Show this page for new workspaces", IsChecked = true };

    private Profile _profile;
    private FolderChoice _folder;
    private FolderChoice? _browsed;
    private bool _filling;

    /// <summary>What a folder entry stands for: none (the profile's usual one), a folder, or Browse.</summary>
    private sealed record FolderChoice(string Label, string? Detail, WorkspaceFolder? Folder, bool Browse = false);

    private static readonly FolderChoice Usual = new("Its usual folder", null, null);

    public StartPage(IReadOnlyList<Profile> profiles, Profile defaultProfile, RecentFolders recent, Func<string?, string?> browse,
        IReadOnlyList<ClosedWorkspace> closed)
    {
        (_profiles, _default, _recent, _browse) = (profiles, defaultProfile, recent, browse);
        _profile = defaultProfile;
        _folder = Usual;
        IsTabStop = false;
        AutomationProperties.SetName(this, "New workspace");
        AutomationProperties.SetName(_filter, "Filter profiles and folders");
        AutomationProperties.SetName(_profileList, "Profile");
        AutomationProperties.SetName(_folderList, "Folder");
        AutomationProperties.SetName(_name, "Workspace name");

        var start = new Button { Content = "Start", Style = (Style)Application.Current.Resources["AccentButtonStyle"], MinWidth = 90 };
        AutomationProperties.SetName(start, "Start");
        start.Click += (_, _) => Start();
        _showPage.Checked += (_, _) => ShowPageChanged?.Invoke(true);
        _showPage.Unchecked += (_, _) => ShowPageChanged?.Invoke(false);

        var columns = new Grid { ColumnSpacing = 40 };
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MaxWidth = 340 });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.3, GridUnitType.Star), MaxWidth = 460 });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // layouts: as wide as their names
        columns.Children.Add(Column("Profile", _profileList));
        var folders = Column("Folder", _folderList);
        Grid.SetColumn(folders, 1);
        columns.Children.Add(folders);
        foreach (var layout in StartLayouts.All)
        {
            _layoutList.Items.Add(LayoutItem(layout));
        }

        _layoutList.SelectedIndex = 0;
        AutomationProperties.SetName(_layoutList, "Layout");
        var layouts = Column("Layout", _layoutList);
        Grid.SetColumn(layouts, 2);
        columns.Children.Add(layouts);
        _layoutList.SelectionChanged += (_, _) =>
        {
            if (_layoutList.SelectedItem is ListViewItem { Tag: StartLayout layout })
            {
                _layout = layout;
                UpdateNameHint();
            }
        };
        _layoutList.DoubleTapped += (_, _) => Start();

        var footer = new Grid { ColumnSpacing = 12, Padding = new Thickness(0, 12, 0, 0), BorderThickness = new Thickness(0, 1, 0, 0),
            BorderBrush = (Brush)Application.Current.Resources["DividerStrokeColorDefaultBrush"] };
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var nameLabel = new TextBlock { Text = "Name", VerticalAlignment = VerticalAlignment.Center, Opacity = 0.7 };
        footer.Children.Add(_showPage);
        Grid.SetColumn(_iconButton, 2);
        footer.Children.Add(_iconButton);
        Grid.SetColumn(_colorButton, 3);
        footer.Children.Add(_colorButton);
        Grid.SetColumn(nameLabel, 4);
        footer.Children.Add(nameLabel);
        Grid.SetColumn(_name, 5);
        footer.Children.Add(_name);
        Grid.SetColumn(start, 6);
        footer.Children.Add(start);
        SetUpLookButtons();

        var root = new Grid { Padding = new Thickness(48, 28, 48, 16), RowSpacing = 14 };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.Children.Add(new StackPanel
        {
            Spacing = 2,
            Children =
            {
                new TextBlock { Text = "New workspace", FontSize = 26, FontWeight = FontWeights.SemiBold },
                new TextBlock
                {
                    Text = $"Choose any of these, then Enter. Or just Enter: {defaultProfile.Name} in its usual folder.",
                    Opacity = 0.7,
                    TextWrapping = TextWrapping.Wrap,
                },
            },
        });
        Grid.SetRow(_filter, 1);
        root.Children.Add(_filter);
        Grid.SetRow(columns, 2);
        root.Children.Add(columns);
        if (ReopenRow(closed) is { } reopen)
        {
            Grid.SetRow(reopen, 3);
            root.Children.Add(reopen);
        }

        Grid.SetRow(footer, 4);
        root.Children.Add(footer);
        Content = root;

        _filter.TextChanged += (_, _) => Fill();
        _profileList.SelectionChanged += (_, _) =>
        {
            if (!_filling && _profileList.SelectedItem is ListViewItem { Tag: Profile profile })
            {
                _profile = profile;
                FillFolders();
                UpdateNameHint();
                ShowLook(); // the icon button shows the profile's icon while none is chosen
            }
        };
        _folderList.SelectionChanged += async (_, _) =>
        {
            if (_filling || _folderList.SelectedItem is not ListViewItem { Tag: FolderChoice choice })
            {
                return;
            }

            if (choice.Browse)
            {
                // (Once this selection change has settled: the dialog is modal.)
                await Task.Yield();
                Ui.Guard("browsing for a folder", Browse);
                return;
            }

            _folder = choice;
            UpdateNameHint();
        };
        _profileList.DoubleTapped += (_, _) => Start();
        _folderList.DoubleTapped += (_, e) =>
        {
            if (_folder != null && !(_folderList.SelectedItem is ListViewItem { Tag: FolderChoice { Browse: true } }))
            {
                Start();
            }
        };
        AddHandler(KeyDownEvent, new KeyEventHandler(OnKey), handledEventsToo: true);
        Loaded += (_, _) => FocusFilter();
        Fill();
    }

    /// <summary>Start: the profile, the chosen folder (null: its usual one) and the name (null: from the folder, or the shell).</summary>
    public event Action<StartChoice>? Started;

    /// <summary>A closed workspace's chip under Reopen: bring it back as it was.</summary>
    public event Action<ClosedWorkspace>? Reopen;

    /// <summary>Esc: leave without starting.</summary>
    public event Action? Cancelled;

    /// <summary>The "Show this page" box changed.</summary>
    public event Action<bool>? ShowPageChanged;

    public void FocusFilter() => DispatcherQueue.SafeEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
        () => _filter.Focus(FocusState.Programmatic));

    private static StackPanel Column(string title, ListView list)
    {
        list.MaxHeight = 360;
        return new StackPanel
        {
            Spacing = 6,
            Children =
            {
                new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, Opacity = 0.75 },
                list,
            },
        };
    }

    private void OnKey(object sender, KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case Windows.System.VirtualKey.Enter when !(e.OriginalSource is ButtonBase):
                e.Handled = true;
                Start();
                break;
            case Windows.System.VirtualKey.Escape:
                e.Handled = true;
                Cancelled?.Invoke();
                break;
            case Windows.System.VirtualKey.Down when ReferenceEquals(e.OriginalSource, _filter):
                e.Handled = true;
                _profileList.Focus(FocusState.Keyboard);
                break;
        }
    }

    private string? DistroOf(Profile profile)
    {
        if (!_distros.TryGetValue(profile, out var distro))
        {
            distro = _distros[profile] = ShellEnvironment.WslDistroOf(ShellResolver.ForProfile(profile.Config));
        }

        return distro;
    }

    private string Query => _filter.Text.Trim();

    private void Fill()
    {
        _filling = true;
        try
        {
            _profileList.Items.Clear();
            var shown = _profiles.Where(p => Query.Length == 0 || p.Name.Contains(Query, StringComparison.OrdinalIgnoreCase)).ToList();
            if (shown.Count == 0)
            {
                shown = [.. _profiles]; // a typed folder, most likely: keep every profile to choose from
            }

            foreach (var profile in shown)
            {
                _profileList.Items.Add(ProfileItem(profile));
            }

            if (!shown.Contains(_profile))
            {
                _profile = shown[0];
            }

            _profileList.SelectedItem = _profileList.Items.OfType<ListViewItem>().First(i => ReferenceEquals(i.Tag, _profile));
        }
        finally
        {
            _filling = false;
        }

        FillFolders();
        UpdateNameHint();
    }

    private void FillFolders()
    {
        _filling = true;
        try
        {
            _folderList.Items.Clear();
            var choices = new List<FolderChoice>();
            if (TypedFolder() is { } typed)
            {
                choices.Add(typed);
            }

            choices.Add(Usual with { Detail = _profile.Config.WorkingDirectory ?? "~" });
            if (_browsed is { } browsed)
            {
                choices.Add(browsed);
            }

            var now = DateTimeOffset.UtcNow;
            foreach (var recent in _recent.For(_profile.Id, DistroOf(_profile)).Take(12))
            {
                if (Query.Length == 0 || recent.Path.Contains(Query, StringComparison.OrdinalIgnoreCase))
                {
                    var from = recent.Profile == _profile.Id ? "" : $"{_profiles.FirstOrDefault(p => p.Id == recent.Profile)?.Name ?? recent.Profile} · ";
                    choices.Add(new FolderChoice(recent.Path, from + Ago(now - recent.When), new WorkspaceFolder(recent.Path, recent.WslDistro)));
                }
            }

            choices.Add(new FolderChoice("Browse…", null, null, Browse: true));
            foreach (var choice in choices)
            {
                _folderList.Items.Add(FolderItem(choice));
            }

            // A typed folder is the choice (typing it chooses it); else keep the choice if it's
            // still offered, else the first.
            var keep = choices[0].Detail == "typed" ? choices[0]
                       : choices.FirstOrDefault(c => !c.Browse && c.Folder == _folder.Folder && c.Label == _folder.Label)
                         ?? choices.FirstOrDefault(c => !c.Browse && c.Folder?.Path == _folder.Folder?.Path)
                         ?? choices[0];
            _folder = keep.Browse ? Usual : keep;
            _folderList.SelectedItem = _folderList.Items.OfType<ListViewItem>().First(i => ReferenceEquals(i.Tag, keep));
        }
        finally
        {
            _filling = false;
        }
    }

    /// <summary>The filter text as a folder, when it looks like one (a path, <c>~</c>, a drive).</summary>
    private FolderChoice? TypedFolder()
    {
        var text = Query.Trim('"');
        var looksLikePath = text.StartsWith('/') || text.StartsWith('~') || text.StartsWith(@"\\", StringComparison.Ordinal) ||
                            text.Length >= 2 && char.IsAsciiLetter(text[0]) && text[1] == ':';
        if (!looksLikePath)
        {
            return null;
        }

        // A POSIX path typed for a WSL profile is that distro's; anything else is Windows' (or MSYS').
        var distro = DistroOf(_profile);
        var windows = text.Length >= 2 && text[1] == ':' || text.StartsWith(@"\\", StringComparison.Ordinal);
        return new FolderChoice($"Use {text}", "typed", new WorkspaceFolder(text, windows ? null : distro));
    }

    /// <summary>
    /// Browse: for a WSL profile the dialog opens in that distro (its share, at the folder last used
    /// there, else its root), and a folder chosen there comes back as the distro's own path.
    /// </summary>
    private void Browse()
    {
        string? path = null;
        var distro = DistroOf(_profile);
        try
        {
            var start = distro == null ? _profile.Config.ResolveWorkingDirectory()
                : _recent.For(_profile.Id, distro).FirstOrDefault(f => f.WslDistro == distro && f.Path.StartsWith('/')) is { } last
                    ? $@"\\wsl.localhost\{distro}{last.Path.Replace('/', '\\')}"
                    : $@"\\wsl.localhost\{distro}\";
            path = _browse(start);
        }
        catch (Exception ex)
        {
            Log.Write($"start page: browse failed: {ex.Message}");
        }

        if (path != null)
        {
            var folder = WorkspaceFolder.FromWindows(path, distro);
            _browsed = new FolderChoice(folder.Path, "chosen", folder);
            _folder = _browsed;
        }

        FillFolders();
        UpdateNameHint();
    }

    private ListViewItem ProfileItem(Profile profile)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        row.Children.Add(new IconSourceElement { IconSource = ProfileIcons.For(profile), Width = 16, Height = 16 });
        row.Children.Add(new TextBlock { Text = profile.Name });
        if (profile == _default)
        {
            row.Children.Add(new TextBlock { Text = "default", Opacity = 0.6, FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
        }

        var item = new ListViewItem { Content = row, Tag = profile };
        AutomationProperties.SetName(item, profile.Name);
        return item;
    }

    private static ListViewItem FolderItem(FolderChoice choice)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        row.Children.Add(new TextBlock { Text = choice.Label, TextTrimming = TextTrimming.CharacterEllipsis });
        if (choice.Detail != null)
        {
            row.Children.Add(new TextBlock { Text = choice.Detail, Opacity = 0.6, FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
        }

        var item = new ListViewItem { Content = row, Tag = choice };
        AutomationProperties.SetName(item, choice.Label);
        return item;
    }

    /// <summary>
    /// "Reopen": a chip per workspace closed lately (the newest few). Unlike the lists above, a chip
    /// doesn't select: it's a whole workspace already, so it reopens at once. Null without any.
    /// </summary>
    private WrapPanel? ReopenRow(IReadOnlyList<ClosedWorkspace> closed)
    {
        if (closed.Count == 0)
        {
            return null;
        }

        var row = new WrapPanel { HorizontalSpacing = 8, VerticalSpacing = 8 };
        row.Children.Add(new TextBlock
        {
            Text = "Reopen",
            FontWeight = FontWeights.SemiBold,
            Opacity = 0.75,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0),
        });
        var now = DateTimeOffset.UtcNow;
        foreach (var workspace in closed.Take(5))
        {
            var chip = new Button
            {
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(12, 4, 12, 5),
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    Children =
                    {
                        new TextBlock { Text = workspace.Name, MaxWidth = 220, TextTrimming = TextTrimming.CharacterEllipsis },
                        new TextBlock
                        {
                            Text = $"{workspace.Summary} · closed {Ago(now - workspace.When)}",
                            Opacity = 0.6,
                            FontSize = 12,
                            VerticalAlignment = VerticalAlignment.Center,
                        },
                    },
                },
            };
            AutomationProperties.SetName(chip, $"Reopen {workspace.Name}");
            chip.Click += (_, _) => Reopen?.Invoke(workspace);
            row.Children.Add(chip);
        }

        return row;
    }

    /// <summary>The icon and color buttons by the name: each opens its chooser (shared with the workspace menu).</summary>
    private void SetUpLookButtons()
    {
        AutomationProperties.SetName(_iconButton, "Workspace icon");
        AutomationProperties.SetName(_colorButton, "Workspace color");
        var iconFlyout = new Flyout();
        iconFlyout.Opening += (_, _) => iconFlyout.Content = WorkspaceLookPickers.Icons(_icon, name =>
        {
            _icon = name;
            iconFlyout.Hide();
            ShowLook();
        });
        _iconButton.Flyout = iconFlyout;
        var colorFlyout = new Flyout();
        colorFlyout.Opening += (_, _) => colorFlyout.Content = WorkspaceLookPickers.Colors(_colorChosen ? _color : null, color =>
        {
            (_color, _colorChosen) = (color, true);
            colorFlyout.Hide();
            ShowLook();
        });
        _colorButton.Flyout = colorFlyout;
        ShowLook();
    }

    /// <summary>The buttons show the choices: the icon (or the profile's, dimmed), the color (or an empty ring: automatic or none).</summary>
    private void ShowLook()
    {
        _iconButton.Content = _icon is { } name && Zinc.Panes.WorkspaceIcons.Glyph(name) is { } glyph
            ? new FontIcon { Glyph = glyph.ToString(), FontSize = 16 }
            : new IconSourceElement { IconSource = ProfileIcons.For(_profile), Width = 16, Height = 16, Opacity = 0.6 };
        ToolTipService.SetToolTip(_iconButton, _icon == null ? "Icon: the profile's" : $"Icon: {_icon}");
        _colorButton.Content = new Border
        {
            Width = 16,
            Height = 16,
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            BorderBrush = (Brush)Application.Current.Resources["ControlStrongStrokeColorDefaultBrush"],
            Background = _color is { } c ? new SolidColorBrush(c) : new SolidColorBrush(Microsoft.UI.Colors.Transparent),
        };
        ToolTipService.SetToolTip(_colorButton, _colorChosen
            ? _color is { } chosen ? $"Color: {Zinc.Panes.WorkspaceColors.All.FirstOrDefault(x => WorkspaceLookPickers.ToColor(x.Rgb) == chosen).Name}" : "Color: none"
            : "Color: automatic (see Settings)");
    }

    private static ListViewItem LayoutItem(StartLayout layout)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        row.Children.Add(Picture(layout));
        row.Children.Add(new TextBlock { Text = StartLayouts.Title(layout), VerticalAlignment = VerticalAlignment.Center });
        var item = new ListViewItem { Content = row, Tag = layout };
        AutomationProperties.SetName(item, StartLayouts.Title(layout));
        return item;
    }

    /// <summary>A small drawing of the layout's panes.</summary>
    private static Grid Picture(StartLayout layout)
    {
        var grid = new Grid { Width = 34, Height = 24, ColumnSpacing = 2, RowSpacing = 2, VerticalAlignment = VerticalAlignment.Center };
        var (columns, rows) = layout switch
        {
            StartLayout.One => (1, 1),
            StartLayout.SideBySide => (2, 1),
            StartLayout.Stacked => (1, 2),
            _ => (2, 2),
        };
        for (var i = 0; i < columns; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition());
        }

        for (var i = 0; i < rows; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition());
        }

        void Pane(int row, int column, int rowSpan = 1)
        {
            var pane = new Border
            {
                CornerRadius = new CornerRadius(2),
                Background = (Brush)Application.Current.Resources["TextFillColorTertiaryBrush"],
            };
            Grid.SetRow(pane, row);
            Grid.SetColumn(pane, column);
            Grid.SetRowSpan(pane, rowSpan);
            grid.Children.Add(pane);
        }

        switch (layout)
        {
            case StartLayout.One: Pane(0, 0); break;
            case StartLayout.SideBySide: Pane(0, 0); Pane(0, 1); break;
            case StartLayout.Stacked: Pane(0, 0); Pane(1, 0); break;
            case StartLayout.BigAndTwo: Pane(0, 0, rowSpan: 2); Pane(0, 1); Pane(1, 1); break;
            default: Pane(0, 0); Pane(0, 1); Pane(1, 0); Pane(1, 1); break;
        }

        return grid;
    }

    /// <summary>The name box's hint: the name the workspace gets if you type none (the folder's, else the shell's).</summary>
    private void UpdateNameHint()
    {
        _name.PlaceholderText = _folder.Folder?.Name ?? "From the shell";
    }

    private void Start()
    {
        var name = _name.Text.Trim();
        Started?.Invoke(new StartChoice(_profile, _folder.Folder, name.Length > 0 ? name : null, _layout, _icon, _color, _colorChosen));
    }

    private static string Ago(TimeSpan span) => span.TotalMinutes < 1 ? "just now"
        : span.TotalHours < 1 ? $"{(int)span.TotalMinutes} min ago"
        : span.TotalDays < 1 ? $"{(int)span.TotalHours} h ago"
        : span.TotalDays < 2 ? "yesterday"
        : $"{(int)span.TotalDays} days ago";
}
