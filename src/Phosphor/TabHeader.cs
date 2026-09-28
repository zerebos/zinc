using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Phosphor;

/// <summary>
/// A tab's icon and title, built once and updated in place. Setting TabViewItem.Header
/// or IconSource to new values swaps the header content (replaying its slide-in
/// transition) and re-decodes the icon, which blinks when focus moves between panes of
/// different profiles. Here the title is one TextBlock whose Text changes, and each
/// profile's icon is decoded once and just shown or hidden.
/// </summary>
internal sealed partial class TabHeader : StackPanel
{
    private readonly Grid _icons = new() { Width = 16, Height = 16, Margin = new Thickness(0, 0, 10, 0) };
    private readonly TextBlock _title = new() { TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
    private readonly Dictionary<Profile, UIElement> _iconByProfile = new();
    private FrameworkElement _owner;

    private readonly AttentionDot _dot = new();

    public TabHeader(FrameworkElement owner)
    {
        _owner = owner;
        Orientation = Orientation.Horizontal;
        VerticalAlignment = VerticalAlignment.Center;
        _icons.Children.Add(_dot);
        Children.Add(_icons);
        Children.Add(_title);
    }

    /// <summary>A dot on the icon: a command finished (or failed) here while you weren't looking.</summary>
    public void SetAttention(Attention attention) => _dot.Show(attention);

    public string Title => _title.Text;

    /// <summary>Just the icon, centered (the sidebar's rail); the title stays in the tooltip.</summary>
    public bool Compact
    {
        set
        {
            _title.Visibility = value ? Visibility.Collapsed : Visibility.Visible;
            _icons.Margin = new Thickness(0, 0, value ? 0 : 10, 0);
            HorizontalAlignment = value ? HorizontalAlignment.Center : HorizontalAlignment.Stretch;
        }
    }

    /// <summary>
    /// Room for the title, so it ends in an ellipsis rather than being cut off (a horizontal
    /// StackPanel gives its children unlimited width, so the TextBlock can't tell on its own).
    /// </summary>
    public double TitleMaxWidth
    {
        set => _title.MaxWidth = Math.Max(0, value);
    }

    /// <summary>The element showing this header, which carries its tooltip and accessible name.</summary>
    public FrameworkElement Owner
    {
        get => _owner;
        set
        {
            _owner = value;
            ToolTipService.SetToolTip(_owner, _title.Text);
            AutomationProperties.SetName(_owner, _title.Text);
        }
    }

    public void SetTitle(string title)
    {
        if (_title.Text == title)
        {
            return;
        }

        _title.Text = title;
        ToolTipService.SetToolTip(_owner, title);
        AutomationProperties.SetName(_owner, title);
    }

    public void SetProfile(Profile profile)
    {
        if (!_iconByProfile.ContainsKey(profile))
        {
            var icon = new IconSourceElement { IconSource = ProfileIcons.For(profile), Visibility = Visibility.Collapsed };
            _iconByProfile[profile] = icon;
            _icons.Children.Insert(_icons.Children.Count - 1, icon); // under the dot
        }

        foreach (var (p, element) in _iconByProfile)
        {
            var visibility = p == profile ? Visibility.Visible : Visibility.Collapsed;
            if (element.Visibility != visibility)
            {
                element.Visibility = visibility;
            }
        }
    }
}
