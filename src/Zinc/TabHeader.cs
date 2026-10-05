using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Zinc;

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
    private bool _compact, _dotElsewhere;
    private Attention _attention;
    private int _attentionCount;
    private string? _attentionWords;
    private Zinc.Terminal.TerminalProgress _progress = Zinc.Terminal.TerminalProgress.None;

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

    /// <summary>
    /// A command finished (or failed) here while you weren't looking; <paramref name="count"/> tabs want you;
    /// <paramref name="words"/> says so (<see cref="AttentionWords.Summary"/>) for the tooltip and screen readers.
    /// </summary>
    public void SetAttention(Attention attention, int count = 1, string? words = null)
    {
        count = attention == Attention.None ? 0 : Math.Max(1, count);
        words = attention == Attention.None ? null : words;
        if (attention == _attention && count == _attentionCount && words == _attentionWords)
        {
            return;
        }

        _attention = attention;
        _attentionCount = count;
        _attentionWords = words;
        ShowDot();
        UpdateToolTip();
        AttentionChanged?.Invoke();
    }

    public Attention Attention => _attention;

    /// <summary>How many tabs want you (the number in the dot, from 2).</summary>
    public int AttentionCount => _attentionCount;

    /// <summary>A program's progress in this workspace: a ring where the dot goes, while there's no dot.</summary>
    public void SetProgress(Zinc.Terminal.TerminalProgress progress)
    {
        if (progress == _progress)
        {
            return;
        }

        _progress = progress;
        ShowDot();
        AttentionChanged?.Invoke();
    }

    public Zinc.Terminal.TerminalProgress Progress => _progress;

    /// <summary>The attention changed (for a host showing the dot itself, <see cref="DotElsewhere"/>).</summary>
    public event Action? AttentionChanged;

    /// <summary>The host wants the dot to pulse (it shows the dot itself).</summary>
    public event Action? PulseRequested;

    /// <summary>
    /// The host shows the dot in a place of its own (the sidebar: in the close button's slot, so
    /// hovering swaps them without moving anything); the header keeps it only on the rail.
    /// </summary>
    public bool DotElsewhere
    {
        set
        {
            _dotElsewhere = value;
            ShowDot();
        }
    }

    private void ShowDot()
    {
        var here = !_dotElsewhere || _compact;
        _dot.Show(here ? _attention : Attention.None, _attentionCount);
        _dot.ShowProgress(here ? _progress : Zinc.Terminal.TerminalProgress.None);
    }

    /// <summary>Pulses the dot again, if it's showing (you're back from another app).</summary>
    public void PulseAttention()
    {
        if (_dotElsewhere && !_compact)
        {
            PulseRequested?.Invoke();
        }
        else
        {
            _dot.PulseIfShown();
        }
    }

    public string Title => _title.Text;

    /// <summary>The title's color where its surface needs one (a light selection in the sidebar); null inherits.</summary>
    public Microsoft.UI.Xaml.Media.Brush? TextForeground
    {
        set
        {
            if (value == null)
            {
                _title.ClearValue(TextBlock.ForegroundProperty);
            }
            else
            {
                _title.Foreground = value;
            }
        }
    }

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
            ShowDot();
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
            UpdateToolTip();
        }
    }

    public void SetTitle(string title)
    {
        if (_title.Text == title)
        {
            return;
        }

        _title.Text = title;
        UpdateToolTip();
    }

    /// <summary>The title, and what its dot means (the rail shows no number); the accessible name says the same.</summary>
    private void UpdateToolTip()
    {
        ToolTipService.SetToolTip(_owner, AttentionWords.ToolTip(_title.Text, _attentionWords));
        AutomationProperties.SetName(_owner, AttentionWords.Name(_title.Text, _attentionWords));
    }

    private Profile? _profile;
    private FontIcon? _workspaceIcon;

    public void SetProfile(Profile profile)
    {
        _profile = profile;
        if (!_iconByProfile.ContainsKey(profile))
        {
            var icon = new IconSourceElement { IconSource = ProfileIcons.For(profile), Visibility = Visibility.Collapsed };
            _iconByProfile[profile] = icon;
            _icons.Children.Insert(0, icon); // under the dot, when it's on the corner
        }

        ShowIcon();
    }

    /// <summary>The workspace's own icon (a Segoe Fluent Icons glyph), shown instead of the profile's; null: the profile's.</summary>
    public void SetIcon(char? glyph)
    {
        if (glyph is { } g)
        {
            if (_workspaceIcon == null)
            {
                _workspaceIcon = new FontIcon { FontSize = 15 };
                _icons.Children.Insert(0, _workspaceIcon);
            }

            _workspaceIcon.Glyph = g.ToString();
        }
        else if (_workspaceIcon != null)
        {
            _icons.Children.Remove(_workspaceIcon);
            _workspaceIcon = null;
        }

        ShowIcon();
    }

    private void ShowIcon()
    {
        foreach (var (p, element) in _iconByProfile)
        {
            var visibility = p == _profile && _workspaceIcon == null ? Visibility.Visible : Visibility.Collapsed;
            if (element.Visibility != visibility)
            {
                element.Visibility = visibility;
            }
        }
    }
}
