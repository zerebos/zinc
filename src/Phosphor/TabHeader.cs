using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Phosphor;

/// <summary>
/// A tab's icon and title, built once and updated in place. Setting TabViewItem.Header
/// or IconSource to new values swaps the header content (replaying its slide-in
/// transition) and re-decodes the icon, which blinks when focus moves between panes of
/// different profiles. Here the title is one TextBlock whose Text changes, and each
/// profile's icon is decoded once and just shown or hidden. A dot after the title flags a
/// command that finished out of view (on the icon's corner when there's no title: the rail).
/// </summary>
internal sealed partial class TabHeader : Grid
{
    private readonly Grid _icons = new() { Width = 16, Height = 16, Margin = new Thickness(0, 0, 10, 0) };
    // Left: a MaxWidth (TitleMaxWidth) narrower than its column would otherwise center it.
    private readonly TextBlock _title = new()
    {
        TextTrimming = TextTrimming.CharacterEllipsis,
        VerticalAlignment = VerticalAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Left,
    };
    private readonly Dictionary<Profile, UIElement> _iconByProfile = new();
    private readonly AttentionDot _dot = new();
    private FrameworkElement _owner;
    private bool _compact;

    public TabHeader(FrameworkElement owner)
    {
        _owner = owner;
        VerticalAlignment = VerticalAlignment.Center;
        // Icon | title (trimmed to what's left) | dot.
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Children.Add(_icons);
        SetColumn(_title, 1);
        Children.Add(_title);
        PlaceDot();
    }

    /// <summary>A command finished (or failed) here while you weren't looking.</summary>
    public void SetAttention(Attention attention) => _dot.Show(attention);

    /// <summary>Pulses the dot again, if it's showing (you're back from another app).</summary>
    public void PulseAttention() => _dot.PulseIfShown();

    public string Title => _title.Text;

    /// <summary>Just the icon, centered (the sidebar's rail); the title stays in the tooltip.</summary>
    public bool Compact
    {
        set
        {
            _compact = value;
            _title.Visibility = value ? Visibility.Collapsed : Visibility.Visible;
            _icons.Margin = new Thickness(0, 0, value ? 0 : 10, 0);
            HorizontalAlignment = value ? HorizontalAlignment.Center : HorizontalAlignment.Stretch;
            PlaceDot();
        }
    }

    /// <summary>After the title; on the icon's corner when there's no title.</summary>
    private void PlaceDot()
    {
        _icons.Children.Remove(_dot); // (Parent is null for Panel children added in code)
        Children.Remove(_dot);
        _dot.Corner = _compact;
        if (_compact)
        {
            _icons.Children.Add(_dot);
        }
        else
        {
            SetColumn(_dot, 2);
            Children.Add(_dot);
        }
    }

    /// <summary>
    /// Room for the title, so it ends in an ellipsis rather than being cut off where the
    /// header's own width isn't bounded.
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
            _icons.Children.Insert(0, icon); // under the dot, when it's on the corner
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
