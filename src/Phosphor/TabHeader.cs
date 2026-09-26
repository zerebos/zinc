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
    private readonly TabViewItem _item;

    public TabHeader(TabViewItem item)
    {
        _item = item;
        Orientation = Orientation.Horizontal;
        VerticalAlignment = VerticalAlignment.Center;
        Children.Add(_icons);
        Children.Add(_title);
    }

    public string Title => _title.Text;

    public void SetTitle(string title)
    {
        if (_title.Text == title)
        {
            return;
        }

        _title.Text = title;
        ToolTipService.SetToolTip(_item, title);
        AutomationProperties.SetName(_item, title);
    }

    public void SetProfile(Profile profile)
    {
        if (!_iconByProfile.ContainsKey(profile))
        {
            var icon = new IconSourceElement { IconSource = ProfileIcons.For(profile), Visibility = Visibility.Collapsed };
            _iconByProfile[profile] = icon;
            _icons.Children.Add(icon);
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
