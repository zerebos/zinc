using System.Text.RegularExpressions;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using VirtualKey = Windows.System.VirtualKey;

namespace Zinc.Terminal;

// Find (Ctrl+Shift+F) is a box at the pane's top right. Matches are highlighted as you type.
// Enter and Shift+Enter move to older and newer matches, and Esc closes the box. Search runs in the
// session (TerminalSession.Search) and reruns shortly after new output while the box is open.
public sealed partial class TerminalControl
{
    private Border? _searchPanel;
    private TextBox? _searchBox;
    private TextBlock? _searchCount;
    private ToggleButton? _searchCase, _searchRegex;
    private DispatcherQueueTimer? _searchRefresh;
    private readonly List<SearchMatch> _matches = [];
    private int _currentMatch = -1;

    private bool SearchOpen => _searchPanel is { Visibility: Visibility.Visible };

    /// <summary>Opens the find box (or refocuses it), keeping the last query like Windows Terminal.</summary>
    private void OpenSearch()
    {
        EnsureSearchPanel();
        _searchPanel!.Visibility = Visibility.Visible;
        _searchBox!.SelectAll();
        // Low priority, so this runs after the host's own focus requests (for example the palette closing).
        DispatcherQueue.SafeEnqueue(DispatcherQueuePriority.Low, () => _searchBox.Focus(FocusState.Programmatic));
        RunSearch(jump: true);
    }

    private void CloseSearch()
    {
        if (!SearchOpen)
        {
            return;
        }

        _searchPanel!.Visibility = Visibility.Collapsed;
        _searchRefresh?.Stop();
        _matches.Clear();
        _currentMatch = -1;
        UpdateSearchRendering();
        Focus(FocusState.Programmatic);
    }

    private bool IsFromSearch(object source) =>
        _searchPanel != null && source is DependencyObject d && IsDescendant(d, _searchPanel);

    private static bool IsDescendant(DependencyObject d, DependencyObject ancestor)
    {
        for (var current = d; current != null; current = VisualTreeHelper.GetParent(current))
        {
            if (current == ancestor)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Called when output arrives, and refreshes the matches soon (not per chunk).</summary>
    private void ScheduleSearchRefresh()
    {
        if (!SearchOpen)
        {
            return;
        }

        if (_searchRefresh == null)
        {
            _searchRefresh = DispatcherQueue.CreateTimer();
            _searchRefresh.Interval = TimeSpan.FromMilliseconds(300);
            _searchRefresh.IsRepeating = false;
            _searchRefresh.OnTick(_ => RunSearch(jump: false));
        }

        if (!_searchRefresh.IsRunning)
        {
            _searchRefresh.Start();
        }
    }

    /// <summary>
    /// Recomputes matches. With <paramref name="jump"/> (new query), selects the newest match
    /// on or above the screen and scrolls to it; otherwise keeps the current match.
    /// </summary>
    private void RunSearch(bool jump)
    {
        if (_session is not { } session || _searchBox == null)
        {
            return;
        }

        var previous = _currentMatch >= 0 && _currentMatch < _matches.Count ? _matches[_currentMatch] : (SearchMatch?)null;
        _matches.Clear();
        _currentMatch = -1;

        var pattern = SearchPattern.Build(_searchBox.Text, _searchCase!.IsChecked == true, _searchRegex!.IsChecked == true,
            out var error);
        string? problem = error != null ? "Invalid pattern" : null;
        if (pattern != null)
        {
            try
            {
                session.Search(pattern, _matches);
            }
            catch (RegexMatchTimeoutException)
            {
                _matches.Clear();
                problem = "Pattern too slow. Try a simpler one.";
            }
        }

        if (_matches.Count > 0)
        {
            var sb = session.GetScrollbar();
            _currentMatch = !jump && previous is { } p
                ? Nearest(p)
                : SearchPattern.Initial(_matches, sb.Offset + (ulong)Math.Max(0, _rows - 1));
            if (jump)
            {
                ScrollToMatch();
            }
        }

        if (problem != null || _searchBox.Text.Length == 0 || _matches.Count == 0)
        {
            _searchCount!.Text = problem ?? (_searchBox.Text.Length == 0 ? "" : "No results");
            AutomationProperties.SetName(_searchCount, _searchCount.Text);
        }
        else
        {
            ShowCount();
        }

        ToolTipService.SetToolTip(_searchCount, error);
        UpdateSearchRendering();
    }

    /// <summary>The match at or closest to where <paramref name="old"/> was (output may have added more).</summary>
    private int Nearest(SearchMatch old)
    {
        var best = 0;
        for (var i = 0; i < _matches.Count; i++)
        {
            var m = _matches[i];
            if (m == old)
            {
                return i;
            }

            if (Distance(m.Row, old.Row) < Distance(_matches[best].Row, old.Row))
            {
                best = i;
            }
        }

        return best;

        static ulong Distance(ulong a, ulong b) => a > b ? a - b : b - a;
    }

    /// <summary>Moves to the next older (<paramref name="older"/>) or newer match, wrapping.</summary>
    private void StepMatch(bool older)
    {
        if (_matches.Count == 0)
        {
            RunSearch(jump: true);
            return;
        }

        _currentMatch = (_currentMatch + (older ? -1 : 1) + _matches.Count) % _matches.Count;
        ShowCount();
        ScrollToMatch();
        UpdateSearchRendering();
    }

    /// <summary>
    /// Shows "3/12", counted from the newest match so Enter (upward) counts up. Screen readers get "3 of 12", because
    /// they read the slash aloud.
    /// </summary>
    private void ShowCount()
    {
        var at = _matches.Count - _currentMatch;
        _searchCount!.Text = $"{at}/{_matches.Count}";
        AutomationProperties.SetName(_searchCount, $"{at} of {_matches.Count}");
    }

    private void ScrollToMatch()
    {
        if (_session is not { } session || _currentMatch < 0)
        {
            return;
        }

        // Only scroll when the match is off screen, and then center it.
        var row = _matches[_currentMatch].Row;
        var sb = session.GetScrollbar();
        if (row < sb.Offset || row >= sb.Offset + (ulong)_rows)
        {
            session.ScrollToRow(row > (ulong)(_rows / 2) ? row - (ulong)(_rows / 2) : 0);
        }
    }

    private void UpdateSearchRendering()
    {
        _renderer.SearchMatches = _matches.Count > 0 ? _matches : null;
        _renderer.CurrentMatch = _currentMatch;
        Redraw();
    }

    /// <summary>Marks each match's position beside the scrollbar, so clusters show at a glance.</summary>
    private void DrawSearchTicks(Microsoft.Graphics.Canvas.CanvasDrawingSession ds, TerminalSession session)
    {
        if (_matches.Count == 0)
        {
            return;
        }

        var total = Math.Max(1ul, session.GetScrollbar().Total);
        var height = (float)Surface.ActualHeight;
        var x = (float)Surface.ActualWidth - 7;
        var lastY = float.NaN;
        for (var i = 0; i < _matches.Count; i++)
        {
            var y = MathF.Round(height * _matches[i].Row / total);
            if (y != lastY || i == _currentMatch)
            {
                ds.FillRectangle(x, y, 4, 2, TerminalRenderer.MatchColorFor(_config, i == _currentMatch));
                lastY = y;
            }
        }
    }

    // ------------------------------------------------------------------ UI

    private void EnsureSearchPanel()
    {
        if (_searchPanel != null)
        {
            return;
        }

        _searchBox = new TextBox { Width = 220, PlaceholderText = "Find", VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(_searchBox, "Find");
        _searchBox.TextChanged += (_, _) => RunSearch(jump: true);
        _searchBox.PreviewKeyDown += OnSearchKey;

        _searchCount = new TextBlock
        {
            MinWidth = 64,
            Margin = new Thickness(8, 0, 4, 0),
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
        };

        _searchCase = Toggle("Aa", "Match case");
        _searchRegex = Toggle(".*", "Regular expression");
        var up = IconButton("\uE70E", "Previous match (Enter)", () => StepMatch(older: true));
        var down = IconButton("\uE70D", "Next match (Shift+Enter)", () => StepMatch(older: false));
        var close = IconButton("\uE711", "Close find (Esc)", CloseSearch);

        _searchPanel = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 8, 24, 0), // clear of the scrollbar
            Padding = new Thickness(6),
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            Background = (Brush)Application.Current.Resources["AcrylicInAppFillColorDefaultBrush"],
            BorderBrush = (Brush)Application.Current.Resources["SurfaceStrokeColorFlyoutBrush"],
            Visibility = Visibility.Collapsed,
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 2,
                Children = { _searchBox, _searchCount, _searchCase, _searchRegex, up, down, close },
            },
        };
        _root.Children.Add(_searchPanel);
    }

    private ToggleButton Toggle(string label, string name)
    {
        var toggle = new ToggleButton { Content = label, MinWidth = 34, Padding = new Thickness(6, 4, 6, 5), FontSize = 13 };
        AutomationProperties.SetName(toggle, name);
        ToolTipService.SetToolTip(toggle, name);
        // Checked/Unchecked, not Click, so toggling via keyboard or UI Automation counts too.
        RoutedEventHandler changed = (_, _) =>
        {
            RunSearch(jump: true);
            _searchBox!.Focus(FocusState.Programmatic);
        };
        toggle.Checked += changed;
        toggle.Unchecked += changed;
        return toggle;
    }

    private static Button IconButton(string glyph, string name, Action action)
    {
        var button = new Button
        {
            Content = new FontIcon { Glyph = glyph, FontSize = 12 },
            Padding = new Thickness(8, 7, 8, 7),
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0),
        };
        AutomationProperties.SetName(button, name);
        ToolTipService.SetToolTip(button, name);
        button.Click += (_, _) => action();
        return button;
    }

    private void OnSearchKey(object sender, KeyRoutedEventArgs e)
    {
        var shift = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift)
            .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
        switch (e.Key)
        {
            case VirtualKey.Enter:
                StepMatch(older: !shift);
                e.Handled = true;
                break;
            case VirtualKey.Escape:
                CloseSearch();
                e.Handled = true;
                break;
        }
    }
}
