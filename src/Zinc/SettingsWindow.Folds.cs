using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Zinc.Settings;

namespace Zinc;

/// <summary>
/// Folds: one card with a header (a setting's own card, or a title and a summary of what's changed)
/// that opens to show the settings under it. Used for related settings on a page
/// (<see cref="SettingsCatalog.Folds"/>), each group of a profile's page and long key families.
/// Search opens a fold before it jumps to a setting inside.
/// </summary>
internal sealed partial class SettingsWindow
{
    /// <summary>The folds open now, by id: they stay open as the page is rebuilt (for this window).</summary>
    private readonly HashSet<string> _openFolds = [];

    /// <summary>A card inside a fold → how to open that fold.</summary>
    private readonly Dictionary<Border, Action> _foldOf = [];

    private const string ChevronGlyph = "";

    /// <summary>
    /// A fold: <paramref name="header"/> and <paramref name="rows"/> under it. Setting cards among the rows become plain rows.
    /// </summary>
    private Border Fold(string id, string name, FrameworkElement header, IReadOnlyList<FrameworkElement> rows,
        bool open = false)
    {
        var body = new StackPanel
        {
            Background = (Brush)Application.Current.Resources["CardBackgroundFillColorSecondaryBrush"],
        };
        foreach (var row in rows)
        {
            if (row is Border card)
            {
                AsFoldRow(card);
            }

            body.Children.Add(row);
        }

        var bodyFrame = new Border { Child = body, CornerRadius = new CornerRadius(0, 0, 5, 5) };
        var chevron = new FontIcon { Glyph = ChevronGlyph, FontSize = 12 };
        chevron.CenterPoint = new System.Numerics.Vector3(6, 6, 0);
        chevron.RotationTransition = new ScalarTransition { Duration = TimeSpan.FromMilliseconds(150) };
        var toggle = new Button
        {
            Content = chevron,
            Width = 32,
            Height = 32,
            Padding = new Thickness(0),
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0),
        };
        AutomationProperties.SetName(toggle, $"Show more: {name}");

        var top = new Grid { Padding = new Thickness(15, 11, 8, 11), Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent) };
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(toggle, 1);
        top.Children.Add(header);
        top.Children.Add(toggle);

        var panel = new StackPanel { Children = { top, bodyFrame } };
        var fold = CardBorder(panel);
        fold.Padding = new Thickness(1); // (room for Flash's thicker border)

        var isOpen = false;
        Storyboard? sliding = null;

        // Opening slides the rows down (the cards below move with them), closing slides them up.
        void Show(bool show, bool animate = true)
        {
            if (show) _openFolds.Add(id);
            else _openFolds.Remove(id);
            isOpen = show;
            chevron.Rotation = show ? 180 : 0;
            ToolTipService.SetToolTip(toggle, show ? "Show less" : "Show more");
            AutomationProperties.SetItemStatus(toggle, show ? "Open" : "Closed");

            // From wherever it is now (a slide may be under way).
            var from = bodyFrame.Visibility == Visibility.Visible ? bodyFrame.ActualHeight : 0;
            sliding?.Stop();
            sliding = null;
            if (!animate || !fold.IsLoaded)
            {
                bodyFrame.Height = double.NaN;
                bodyFrame.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
                return;
            }

            bodyFrame.Visibility = Visibility.Visible;
            body.Measure(new Windows.Foundation.Size(panel.ActualWidth, double.PositiveInfinity));
            var to = show ? body.DesiredSize.Height : 0;
            bodyFrame.Height = from;
            var slide = new DoubleAnimation
            {
                From = from,
                To = to,
                Duration = TimeSpan.FromMilliseconds(show ? 250 : 180),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                EnableDependentAnimation = true, // (height: the page reflows as it goes)
            };
            Storyboard.SetTarget(slide, bodyFrame);
            Storyboard.SetTargetProperty(slide, "Height");
            var story = new Storyboard { Children = { slide } };
            story.Completed += (_, _) =>
            {
                if (sliding != story)
                {
                    return;
                }

                story.Stop();
                sliding = null;
                bodyFrame.Height = double.NaN;
                bodyFrame.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            };
            sliding = story;
            story.Begin();
        }

        toggle.Click += (_, _) => Show(!isOpen);
        top.Tapped += (_, e) =>
        {
            // Controls in the header (its switch or dropdown, the reset arrow, the chevron) work as
            // usual; a click anywhere else folds.
            if (e.OriginalSource is DependencyObject source && !InControl(source, top))
            {
                Show(!isOpen);
            }
        };

        Show(open || _openFolds.Contains(id), animate: false);
        foreach (var row in rows.OfType<Border>())
        {
            _foldOf[row] = () => Show(true, animate: false); // (search scrolls to it right away)
        }

        return fold;
    }

    /// <summary>A setting card as a row inside a fold: no card of its own, a line above, indented.</summary>
    private static void AsFoldRow(Border card)
    {
        card.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        card.CornerRadius = new CornerRadius(0);
        card.BorderThickness = new Thickness(0, 1, 0, 0);
        // Controls line up with the header's (which sits left of the chevron).
        card.Padding = new Thickness(40, 11, 48, 11);
    }

    /// <summary>Whether <paramref name="element"/> is (in) a control below <paramref name="container"/>.</summary>
    private static bool InControl(DependencyObject element, DependencyObject container)
    {
        for (DependencyObject? at = element; at != null && at != container; at = VisualTreeHelper.GetParent(at))
        {
            if (at is Control)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>A header of its own: a title, and a line under it (a summary; hidden while empty).</summary>
    private static (FrameworkElement Header, TextBlock Summary) FoldHeader(string title, string? description)
    {
        var summary = DescriptionText(description ?? "");
        return (new StackPanel
        {
            Spacing = 1,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { new TextBlock { Text = title, TextWrapping = TextWrapping.Wrap }, summary },
        }, summary);
    }

    /// <summary>A catalog fold on a page: under its lead's card, or its own title with what's changed.</summary>
    private Border SettingFoldCard(SettingFold fold)
    {
        var rows = fold.Children.Select(k => (FrameworkElement)Card(SettingsCatalog.Find(k)!)).ToList();
        if (fold.Lead is { } leadKey)
        {
            var lead = SettingsCatalog.Find(leadKey)!;
            var card = Card(lead);
            var (_, control) = _cards[(null, leadKey)];
            var header = (FrameworkElement)card.Child;
            card.Child = null;
            var folded = Fold($"fold:{leadKey}", lead.Label, header, rows);
            _cards[(null, leadKey)] = (folded, control);
            return folded;
        }

        var (title, summary) = FoldHeader(fold.Title!, fold.Description);
        var bundle = Fold($"fold:{fold.Children[0]}", fold.Title!, title, rows);
        void Refresh() => SetText(summary, Summary(fold.Description,
            fold.Children.Select(SettingsCatalog.Find).Where(s => s != null && IsChanged(s, null)).Select(s => s!.Label)));
        _refreshers.Add(Refresh);
        Refresh();
        return bundle;
    }

    /// <summary>A profile page's group as one fold, summarizing which of its settings the profile changes.</summary>
    private Border ProfileGroupFold(string group, string id, IReadOnlyList<SettingDef> settings, IReadOnlyList<FrameworkElement> rows)
    {
        var (header, summary) = FoldHeader(group, null);
        var fold = Fold($"profile-group:{group}", group, header, rows);
        void Refresh() => SetText(summary, Summary("Inherited", settings.Where(s => IsChanged(s, id)).Select(s => s.Label), "Changed here"));
        _refreshers.Add(Refresh);
        Refresh();
        return fold;
    }

    /// <summary>
    /// Whether <paramref name="setting"/> is changed: from its default on a page, or at all in
    /// <paramref name="profile"/>'s section.
    /// </summary>
    private bool IsChanged(SettingDef setting, string? profile) => profile == null
        ? _config.ValueOf(setting.Key) is { } value && !SettingsCatalog.Same(value, setting.Default)
        : _config.ValueOf(profile, setting.Key) != null;

    private static string Summary(string? unchanged, IEnumerable<string> changed, string prefix = "Changed") =>
        changed.ToList() is { Count: > 0 } labels ? $"{prefix}: {string.Join(", ", labels)}" : unchanged ?? "";
}
