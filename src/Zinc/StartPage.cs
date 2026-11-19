using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Zinc.Panes;
using Zinc.Session;
using Zinc.Terminal;

namespace Zinc;

/// <summary>
/// What the page starts. <see cref="Color"/> counts only when <see cref="ColorChosen"/>, or else the
/// automatic colors decide. A null <see cref="Icon"/> keeps the profile's icon.
/// </summary>
internal sealed record StartChoice(Profile Profile, WorkspaceFolder? Folder, string? Name, StartLayout Layout,
    string? Icon, Windows.UI.Color? Color, bool ColorChosen);

/// <summary>
/// The new-workspace page (<c>new-workspace-page</c>), shown in a new workspace's tab until it
/// starts. Pick a profile and a folder (typing filters both, and a typed path can be used as is), name
/// it or keep the folder's name, then press Enter or Open workspace. Untouched, Enter starts the default profile
/// in its usual folder, as a new workspace does without this page. A chosen folder becomes the workspace's own
/// (<see cref="WorkspaceFolder"/>), so its new tabs and splits start there.
/// </summary>
internal sealed partial class StartPage : UserControl
{
    private readonly IReadOnlyList<Profile> _profiles;
    private readonly Profile _default;
    private readonly RecentFolders _recent;
    /// <summary>
    /// A folder dialog starting in the given folder (or where it chooses, for null), which returns the choice or null.
    /// </summary>
    private readonly Func<string?, string?> _browse;
    private readonly Dictionary<Profile, string?> _distros = [];

    private readonly TextBox _filter = new() { PlaceholderText = "Search profiles and folders, or type a path" };
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

    /// <summary>The page's height (in DIPs) below which the Reopen chips keep to one row.</summary>
    private const double ShortPage = 720;

    /// <summary>
    /// The page's width (in DIPs) below which the layouts show only their pictures, the columns sit closer, and the
    /// name box is narrower.
    /// </summary>
    private const double NarrowPage = 700;

    /// <summary>
    /// The page's width (in DIPs) below which the "Show this page" box hides, because it no longer fits beside the
    /// footer's other controls. The footer needs about 840 on one row at 100% text size (the box, the controls, and
    /// the page's padding). Settings has the same choice, as "Start page for new workspaces".
    /// </summary>
    private const double NarrowFooter = 850;

    /// <summary>The page's height (in DIPs) below which the heading gets smaller and the intro under it hides, to leave room for the lists.</summary>
    private const double CompactPage = 560;

    private readonly Grid _root = new();
    private readonly TextBlock _heading = new() { Text = "New workspace", FontWeight = FontWeights.SemiBold };
    private readonly TextBlock _intro = new() { Opacity = 0.7, TextWrapping = TextWrapping.Wrap };
    private readonly Grid _columns = new() { ColumnSpacing = 40 };
    private readonly List<(ListViewItem Item, TextBlock Name)> _layoutNames = [];
    private readonly Grid _footer = new() { ColumnSpacing = 12 };

    /// <summary>The footer's controls, which wrap onto a second row (still on the right) when even the narrow footer is too narrow.</summary>
    private readonly WrapPanel _footerControls = new() { HorizontalSpacing = 12, VerticalSpacing = 8, AlignRight = true, HorizontalAlignment = HorizontalAlignment.Right };
    private bool _narrow;
    private bool _narrowFooter;
    private bool? _compact;

    public StartPage(IReadOnlyList<Profile> profiles, Profile defaultProfile, RecentFolders recent, Func<string?, string?> browse,
        IReadOnlyList<ClosedWorkspace> closed)
    {
        (_profiles, _default, _recent, _browse) = (profiles, defaultProfile, recent, browse);
        _profile = defaultProfile;
        _folder = Usual;
        IsTabStop = false;
        AutomationProperties.SetName(this, "New workspace");
        AutomationProperties.SetName(_filter, "Search profiles and folders, or type a path");
        AutomationProperties.SetName(_profileList, "Profile");
        AutomationProperties.SetName(_folderList, "Folder");
        AutomationProperties.SetName(_name, "Workspace name");

        var start = new Button { Content = "Open workspace", Style = (Style)Application.Current.Resources["AccentButtonStyle"], MinWidth = 90 };
        AutomationProperties.SetName(start, "Open workspace");
        start.Click += (_, _) => Start();
        _showPage.Checked += (_, _) => ShowPageChanged?.Invoke(true);
        _showPage.Unchecked += (_, _) => ShowPageChanged?.Invoke(false);

        var columns = _columns;
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MaxWidth = 340 });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.3, GridUnitType.Star), MaxWidth = 460 });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // layouts: as wide as their names, or their pictures on a narrow page
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

        // The "Show this page" box on the left and the controls on the right, or on a narrow page the controls alone.
        var footer = _footer;
        footer.Padding = new Thickness(0, 12, 0, 0);
        footer.BorderThickness = new Thickness(0, 1, 0, 0);
        footer.BorderBrush = (Brush)Application.Current.Resources["DividerStrokeColorDefaultBrush"];
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        // The label and its box are one piece, so wrapping never puts them on different rows.
        var named = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            Children = { new TextBlock { Text = "Name", VerticalAlignment = VerticalAlignment.Center, Opacity = 0.7 }, _name },
        };
        foreach (var control in new FrameworkElement[] { _iconButton, _colorButton, named, start })
        {
            _footerControls.Children.Add(control);
        }

        footer.Children.Add(_showPage);
        Grid.SetColumn(_footerControls, 1);
        footer.Children.Add(_footerControls);
        SetUpLookButtons();

        var root = _root;
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _intro.Text = $"Pick a profile, folder, and layout, then press Enter. Enter alone opens {defaultProfile.Name} in its usual folder.";
        root.Children.Add(new StackPanel { Spacing = 2, Children = { _heading, _intro } });
        SetCompact(false);
        // At most 520 wide, and narrower when the page is.
        var filterRow = new Grid();
        filterRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MaxWidth = 520 });
        filterRow.Children.Add(_filter);
        Grid.SetRow(filterRow, 1);
        root.Children.Add(filterRow);
        Grid.SetRow(columns, 2);
        root.Children.Add(columns);
        if (ReopenRow(closed) is (Grid reopen, WrapPanel chips))
        {
            Grid.SetRow(reopen, 3);
            root.Children.Add(reopen);

            // On a short page the chips would take the lists' room, so they keep to one row.
            SizeChanged += (_, e) => chips.MaxRows = e.NewSize.Height < ShortPage ? 1 : 0;
        }

        SizeChanged += (_, e) =>
        {
            SetNarrow(e.NewSize.Width < NarrowPage);
            SetNarrowFooter(e.NewSize.Width < NarrowFooter);
            SetCompact(e.NewSize.Height < CompactPage);
        };

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
                // Once this selection change has settled, because the dialog is modal.
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

    /// <summary>
    /// Start was asked for, with the profile, the chosen folder (null for its usual one) and the name (null to take it
    /// from the folder, or the shell).
    /// </summary>
    public event Action<StartChoice>? Started;

    /// <summary>A closed workspace's chip under Reopen was clicked, so bring it back as it was.</summary>
    public event Action<ClosedWorkspace>? Reopen;

    /// <summary>Esc was pressed, so leave without starting.</summary>
    public event Action? Cancelled;

    /// <summary>The "Show this page" box changed.</summary>
    public event Action<bool>? ShowPageChanged;

    public void FocusFilter()
    {
        // Not in test captures, because the focused look (caret, underline) shows only when the window happens to be in front.
        if (TestHooks.Deterministic)
        {
            return;
        }

        DispatcherQueue.SafeEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => _filter.Focus(FocusState.Programmatic));
    }

    /// <summary>
    /// A titled list. The list sits in a star row, so a short window shrinks it and it scrolls, instead of
    /// being cut off at the bottom.
    /// </summary>
    private static Grid Column(string title, ListView list)
    {
        list.MaxHeight = 360;
        list.VerticalAlignment = VerticalAlignment.Top;
        var column = new Grid { RowSpacing = 6 };
        column.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        column.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        column.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, Opacity = 0.75 });
        Grid.SetRow(list, 1);
        column.Children.Add(list);
        return column;
    }

    /// <summary>
    /// Fits the page to a narrow width. The layouts keep only their pictures, because their names would take the
    /// Profile and Folder columns' room. The columns also sit closer, and the name box is narrower.
    /// </summary>
    private void SetNarrow(bool narrow)
    {
        if (narrow == _narrow)
        {
            return;
        }

        _narrow = narrow;
        _columns.ColumnSpacing = narrow ? 24 : 40;
        foreach (var (item, name) in _layoutNames)
        {
            name.Visibility = narrow ? Visibility.Collapsed : Visibility.Visible;
            ToolTipService.SetToolTip(item, narrow ? name.Text : null); // the hidden name

            // A list item is at least 88 wide by default, which would leave a picture alone with a wide gap on its right.
            if (narrow)
            {
                item.MinWidth = 0;
            }
            else
            {
                item.ClearValue(MinWidthProperty);
            }
        }

        _name.Width = narrow ? 140 : 200;
    }

    /// <summary>
    /// Hides the "Show this page" box, because beside the other controls it would wrap, and under them it would take a
    /// row from the lists. The controls then move to the star column, where they can wrap on a very narrow page.
    /// </summary>
    private void SetNarrowFooter(bool narrow)
    {
        if (narrow == _narrowFooter)
        {
            return;
        }

        _narrowFooter = narrow;
        _showPage.Visibility = narrow ? Visibility.Collapsed : Visibility.Visible;
        Grid.SetColumn(_footerControls, narrow ? 0 : 1);
    }

    /// <summary>Gives a short page a smaller heading with no intro under it, and less space around everything.</summary>
    private void SetCompact(bool compact)
    {
        if (compact == _compact)
        {
            return;
        }

        _compact = compact;
        _heading.FontSize = compact ? 20 : 26;
        _intro.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        _root.Padding = compact ? new Thickness(48, 16, 48, 12) : new Thickness(48, 28, 48, 16);
        _root.RowSpacing = compact ? 10 : 14;
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
                shown = [.. _profiles]; // a typed folder, most likely, so keep every profile to choose from
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

            // A typed folder is the choice (typing it chooses it). Otherwise keep the choice if it's
            // still offered, or else take the first.
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
    /// Browses for a folder. For a WSL profile the dialog opens in that distro (its share, at the folder last used
    /// there, or else its root), and a folder chosen there comes back as the distro's own path.
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
        var row = new Grid { ColumnSpacing = 10 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.Children.Add(new IconSourceElement { IconSource = ProfileIcons.For(profile), Width = 16, Height = 16 });
        var line = TrimmedLine(profile.Name, profile == _default ? "default" : null, gap: 10);
        Grid.SetColumn(line, 1);
        row.Children.Add(line);
        var item = new ListViewItem { Content = row, Tag = profile };
        AutomationProperties.SetName(item, profile.Name);
        return item;
    }

    private static ListViewItem FolderItem(FolderChoice choice)
    {
        var item = new ListViewItem { Content = TrimmedLine(choice.Label, choice.Detail, gap: 12), Tag = choice };
        AutomationProperties.SetName(item, choice.Label);
        return item;
    }

    /// <summary>
    /// A text with a smaller, dimmed detail after it, <paramref name="gap"/> DIPs apart, as one line. When the line is
    /// too long, it's cut short at its end with an ellipsis, so the detail is cut first and then the text.
    /// </summary>
    private static TextBlock TrimmedLine(string text, string? detail, double gap)
    {
        var line = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        line.Inlines.Add(new Run { Text = text });
        if (detail != null)
        {
            line.Inlines.Add(new Run { Text = "\u2003", FontSize = gap }); // an em space, as wide as its font size
            // A run has no opacity, so the detail gets the text color at 60%, like the dimmed text elsewhere on the page.
            var color = ((SolidColorBrush)Application.Current.Resources["TextFillColorPrimaryBrush"]).Color;
            line.Inlines.Add(new Run
            {
                Text = detail,
                FontSize = 12,
                Foreground = new SolidColorBrush(color with { A = (byte)(color.A * 0.6) }),
            });
        }

        TipWhenTrimmed(line, detail == null ? text : $"{text} · {detail}");
        return line;
    }

    /// <summary>Shows <paramref name="full"/> in a tooltip while <paramref name="block"/> is cut short, and none otherwise.</summary>
    private static void TipWhenTrimmed(TextBlock block, string full) =>
        block.IsTextTrimmedChanged += (_, _) => ToolTipService.SetToolTip(block, block.IsTextTrimmed ? full : null);

    /// <summary>
    /// The "Reopen" row, with a chip per workspace closed lately (the newest few). Unlike the lists above, a chip
    /// doesn't select. It's a whole workspace already, so it reopens at once. Null when there are none.
    /// </summary>
    /// <returns>The row, and the chips' panel inside it.</returns>
    private (Grid Row, WrapPanel Chips)? ReopenRow(IReadOnlyList<ClosedWorkspace> closed)
    {
        if (closed.Count == 0)
        {
            return null;
        }

        // The label sits beside the chips instead of among them, so the first chip always shares the label's row (cut short
        // if it has to be), and wrapped chips line up under the first one.
        var row = new Grid { ColumnSpacing = 14 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.Children.Add(new TextBlock
        {
            Text = "Reopen",
            FontWeight = FontWeights.SemiBold,
            Opacity = 0.75,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 5, 0, 0), // centered on the first row of chips
        });
        var chips = new WrapPanel { HorizontalSpacing = 8, VerticalSpacing = 8 };
        Grid.SetColumn(chips, 1);
        row.Children.Add(chips);
        var now = DateTimeOffset.UtcNow;
        foreach (var workspace in closed.Take(5))
        {
            // Unlike the lists' rows, a chip cuts only its name short and keeps its detail whole, because the detail is
            // often what tells two chips apart. The grid is left-aligned, so with room to spare its star column is only
            // as wide as the name, and the detail sits right after it.
            var content = new Grid { ColumnSpacing = 8, HorizontalAlignment = HorizontalAlignment.Left };
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var name = new TextBlock { Text = workspace.Name, MaxWidth = 220, TextTrimming = TextTrimming.CharacterEllipsis };
            TipWhenTrimmed(name, workspace.Name);
            content.Children.Add(name);
            var detail = new TextBlock
            {
                Text = $"{workspace.SummaryFor(_profile.Config.PaneTabs)} · closed {Ago(now - workspace.When)}",
                Opacity = 0.6,
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(detail, 1);
            content.Children.Add(detail);
            var chip = new Button
            {
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(12, 4, 12, 5),
                Content = content,
            };
            AutomationProperties.SetName(chip, $"Reopen {workspace.Name}");
            chip.Click += (_, _) => Reopen?.Invoke(workspace);
            chips.Children.Add(chip);
        }

        return (row, chips);
    }

    /// <summary>
    /// Sets up the icon and color buttons by the name, each of which opens its chooser (shared with the workspace menu).
    /// </summary>
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

    /// <summary>
    /// The buttons show the choices: the icon (or the profile's, dimmed), and the color (or an empty ring for automatic
    /// or none).
    /// </summary>
    private void ShowLook()
    {
        _iconButton.Content = _icon is { } name && Zinc.Panes.WorkspaceIcons.Glyph(name) is { } glyph
            ? new FontIcon { Glyph = glyph.ToString(), FontSize = 16 }
            : new IconSourceElement { IconSource = ProfileIcons.For(_profile), Width = 16, Height = 16, Opacity = 0.6 };
        ToolTipService.SetToolTip(_iconButton, _icon == null ? "Profile's icon" : $"Icon: {_icon}");
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
            ? _color is { } chosen ? $"Color: {Zinc.Panes.WorkspaceColors.All.FirstOrDefault(x => WorkspaceLookPickers.ToColor(x.Rgb) == chosen).Name}" : "No color"
            : "Color: automatic");
    }

    /// <summary>A layout's picture and name. On a narrow page the name hides and a tooltip shows it instead (<see cref="SetNarrow"/>).</summary>
    private ListViewItem LayoutItem(StartLayout layout)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        row.Children.Add(Picture(layout));
        var name = new TextBlock { Text = StartLayouts.Title(layout), VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(name);
        var item = new ListViewItem { Content = row, Tag = layout };
        _layoutNames.Add((item, name));
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

    /// <summary>
    /// Sets the name box's hint to the name the workspace gets if you type none (the folder's, or else the shell's).
    /// </summary>
    private void UpdateNameHint()
    {
        _name.PlaceholderText = _folder.Folder?.Name ?? "Automatic";
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
