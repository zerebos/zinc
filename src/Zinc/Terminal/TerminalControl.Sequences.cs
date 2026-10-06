using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Zinc.Input;

namespace Zinc.Terminal;

/// <summary>
/// Key sequences ("ctrl+a>c" in [keys]): the leader starts one, and while it waits a hint at the
/// bottom of the pane lists what can come next (which-key style). The next key runs its action;
/// Escape or any other key cancels (dropped, not typed); the leader twice types the leader.
/// </summary>
public sealed partial class TerminalControl
{
    private KeySequencer? _sequencer;
    private Border? _sequenceHint;

    private KeySequencer Sequencer => _sequencer ??= new KeySequencer(_keybindings);

    /// <summary>
    /// Feeds a key press to the sequence and does what it says. <see cref="KeyStep.NotMine"/> and
    /// <see cref="KeyStep.PassThrough"/> (the leader twice): handle the key as usual; otherwise the
    /// key was the sequence's and goes no further.
    /// </summary>
    private KeyStep HandleSequenceKey(KeyChord pressed, bool repeat, TerminalSession session)
    {
        // Holding the leader down doesn't count as pressing it twice.
        if (repeat && Sequencer.Active && Sequencer.Pressed[^1] == pressed)
        {
            return KeyStep.Waiting;
        }

        var step = Sequencer.Press(pressed, out var action);
        Log.Write($"keys: {pressed} → {step}{(step == KeyStep.Ran ? $" {Actions.Format(action)}" : "")}");
        if (step == KeyStep.Waiting)
        {
            ShowSequenceHint();
        }
        else if (step != KeyStep.NotMine)
        {
            HideSequenceHint();
        }

        if (step == KeyStep.Ran)
        {
            RunAction(action, session);
        }

        return step;
    }

    /// <summary>Test hook (ZINC_TEST_KEYS): a chord pressed here, as far as sequences go.</summary>
    internal KeyStep PressForTest(KeyChord pressed) =>
        _session is { } session ? HandleSequenceKey(pressed, repeat: false, session) : KeyStep.NotMine;

    /// <summary>Focus went elsewhere: a waiting sequence is over.</summary>
    private void CancelSequence()
    {
        if (_sequencer is { Active: true })
        {
            _sequencer.Cancel();
            HideSequenceHint();
        }
    }

    private void HideSequenceHint()
    {
        if (_sequenceHint != null)
        {
            _sequenceHint.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>What was pressed so far, then each next key and what it does.</summary>
    private void ShowSequenceHint()
    {
        if (_sequenceHint == null)
        {
            _sequenceHint = new Border
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 0, 16),
                Padding = new Thickness(14, 10, 14, 10),
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(1),
                Background = (Brush)Application.Current.Resources["AcrylicInAppFillColorDefaultBrush"],
                BorderBrush = (Brush)Application.Current.Resources["SurfaceStrokeColorFlyoutBrush"],
                IsHitTestVisible = false,
            };
            _root.Children.Add(_sequenceHint);
        }

        const int MaxShown = 12;
        var pressed = Sequencer.Pressed;
        var next = _keybindings.Continuations(pressed);
        var rows = new Grid { ColumnSpacing = 16, RowSpacing = 4 };
        rows.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        rows.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        foreach (var (key, action) in next.Take(MaxShown))
        {
            var row = rows.RowDefinitions.Count;
            rows.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var keyText = new TextBlock
            {
                Text = key.ToString(),
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)Application.Current.Resources["AccentTextFillColorPrimaryBrush"],
            };
            var what = new TextBlock
            {
                Text = action is { } a ? Actions.Title(a, _config.TabGroups) : $"{_keybindings.Continuations([.. pressed, key]).Count} more…",
                Opacity = action == null ? 0.7 : 1,
            };
            Grid.SetRow(keyText, row);
            Grid.SetRow(what, row);
            Grid.SetColumn(what, 1);
            rows.Children.Add(keyText);
            rows.Children.Add(what);
        }

        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(new TextBlock { Text = string.Join(", ", pressed) + ", …", FontWeight = FontWeights.SemiBold, Opacity = 0.8 });
        panel.Children.Add(rows);
        panel.Children.Add(new TextBlock
        {
            Text = (next.Count > MaxShown ? $"and {next.Count - MaxShown} more · " : "") + "Esc cancels" +
                   (pressed.Count == 1 ? $" · {pressed[0]} again types {pressed[0]}" : ""),
            FontSize = 12,
            Opacity = 0.6,
        });
        _sequenceHint.Child = panel;
        _sequenceHint.Visibility = Visibility.Visible;
    }
}
