using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace Phosphor.Terminal;

/// <summary>
/// Input methods (IME: Japanese, Chinese, Korean…). WinUI gives a custom control no text-input
/// support of its own, so with <c>input-method</c> on, a focused pane's keyboard focus sits in a
/// hidden TextBox at the terminal's cursor, which Windows' text services know how to talk to:
/// <list type="bullet">
/// <item>Keys reach the pane's usual handling first (<c>PreviewKeyDown</c>) and are marked handled, so
/// the box never acts on them (Tab doesn't move focus); keys an input method takes (VK_PROCESSKEY,
/// 229) or any key while it's composing go to the box untouched.</item>
/// <item>Typed characters still come through the pane's character path; the box drops them.</item>
/// <item>While composing, the box shows the text being composed where the cursor is, in the
/// terminal's font and colors (Windows draws its underlines and caret; the candidate window opens
/// there). When the composition ends, its text is sent to the program like typing.</item>
/// </list>
/// </summary>
public sealed partial class TerminalControl
{
    private const int ProcessKey = 229; // VK_PROCESSKEY: an input method is handling this key

    private TextBox? _inputBox;
    private bool _composing;
    private bool _clearingInput;

    private bool IsFromInputMethodBox(object source) => _inputBox != null && ReferenceEquals(source, _inputBox);

    /// <summary>Moves focus into the input box (made on first use); false with input methods off.</summary>
    private bool FocusInputMethodBox()
    {
        if (!_config.InputMethod || _previewScript != null)
        {
            return false;
        }

        _inputBox ??= CreateInputBox();
        PlaceInputBox();
        var focused = _inputBox.Focus(FocusState.Programmatic);
        Log.Write($"ime: input box {(focused ? "focused" : "couldn't take focus")}");
        return focused;
    }

    private TextBox CreateInputBox()
    {
        var box = new TextBox
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            MinWidth = 0,
            MinHeight = 0,
            Padding = new Thickness(0),
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(0),
            IsSpellCheckEnabled = false,
            IsTextPredictionEnabled = false,
            AcceptsReturn = false,
            IsHitTestVisible = false, // the pane takes the pointer
            Opacity = 0, // shown only while composing
            // Focusable (Focus() refuses a control that isn't a tab stop); Tab itself never gets to
            // move focus, as the pane's key handling takes it first.
            IsTabStop = true,
            UseSystemFocusVisuals = false,
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(box, "Terminal input");
        _root.Children.Add(box);

        box.AddHandler(PreviewKeyDownEvent, new KeyEventHandler((_, e) =>
        {
            if (_composing || (int)e.Key == ProcessKey)
            {
                return; // the input method's
            }

            // Keys the pane encodes or runs (Enter, Tab, arrows, shortcuts) are marked handled there,
            // so the box never acts on them (its Tab would move focus). Keys that type text are left
            // to it: Windows' text services put that text in the box (real keystrokes don't come as
            // characters to a focused text box), and BeforeTextChanging takes it from there.
            HandleKeyDown(e);
        }), handledEventsToo: true);
        box.AddHandler(PreviewKeyUpEvent, new KeyEventHandler((_, e) =>
        {
            if (!_composing && (int)e.Key != ProcessKey)
            {
                HandleKeyUp(e);
            }
        }), handledEventsToo: true);
        box.AddHandler(CharacterReceivedEvent, new TypedEventHandler<UIElement, CharacterReceivedRoutedEventArgs>((_, e) =>
        {
            // Text arrives as the box's text (below); only control characters are taken here.
            if (!_composing && (e.Character < ' ' || e.Character == '\x7f'))
            {
                HandleCharacter(e);
            }
        }), handledEventsToo: true);

        // Text typed into the box (keys, an input method's direct mode, dead keys, posted characters):
        // sent to the program, and kept out of the box, which holds only what's being composed.
        box.BeforeTextChanging += (_, e) =>
        {
            if (_composing || _clearingInput)
            {
                return;
            }

            e.Cancel = true;
            var text = e.NewText;
            if (text.Length > 0 && !_charHandledByKeyDown && _session is { } session)
            {
                session.SendInput(text);
                ResetBlink();
            }
        };
        box.TextCompositionStarted += (_, _) =>
        {
            _composing = true;
            StyleInputBox();
            box.Opacity = 1;
            Log.Write("ime: composing");
        };
        box.TextCompositionChanged += (_, e) => PlaceInputBox();
        box.TextCompositionEnded += (_, e) =>
        {
            // What was composed, as committed: typed into the program.
            var text = e.Length > 0 && e.StartIndex + e.Length <= box.Text.Length ? box.Text.Substring(e.StartIndex, e.Length) : box.Text;
            _composing = false;
            box.Opacity = 0;
            ClearInputBox();
            PlaceInputBox();
            Log.Write($"ime: committed {text.Length} char(s)");
            if (text.Length > 0 && _session is { } session)
            {
                session.SendInput(text);
                ResetBlink();
            }
        };

        // Its clear button would show over the terminal; it has no use here.
        box.Loaded += (_, _) =>
        {
            if (FindChild(box, "DeleteButton") is FrameworkElement delete)
            {
                delete.Width = 0;
                delete.MaxWidth = 0;
            }
        };
        return box;
    }

    private void ClearInputBox()
    {
        if (_inputBox == null)
        {
            return;
        }

        _clearingInput = true;
        try
        {
            _inputBox.Text = "";
        }
        finally
        {
            _clearingInput = false;
        }
    }

    /// <summary>The terminal's font, size and colors, so what's composed looks like what's typed.</summary>
    private void StyleInputBox()
    {
        if (_inputBox is not { } box)
        {
            return;
        }

        var theme = _config.Theme;
        var background = new SolidColorBrush(TerminalRenderer.ToColor(theme.Background));
        var foreground = new SolidColorBrush(TerminalRenderer.ToColor(theme.Foreground));
        var clear = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        // Lightweight styling: the box's own states (focused, pointer over) use these.
        foreach (var state in new[] { "", "PointerOver", "Focused", "Disabled" })
        {
            box.Resources[$"TextControlBackground{state}"] = background;
            box.Resources[$"TextControlForeground{state}"] = foreground;
            box.Resources[$"TextControlBorderBrush{state}"] = clear;
        }

        box.Background = background;
        box.Foreground = foreground;
        box.FontFamily = new FontFamily(_renderer.TextFontFamily);
        box.FontSize = _renderer.FontSize;
        box.Height = _renderer.Metrics.Height;
        box.Width = double.NaN; // as wide as what's composed
        box.SelectionHighlightColor = new SolidColorBrush(TerminalRenderer.ToColor(theme.Foreground));
    }

    /// <summary>At the terminal's cursor (where input methods compose and open their candidates).</summary>
    private void PlaceInputBox()
    {
        if (_inputBox is not { } box || _renderer.CursorCell is not { } cell || _renderer.Metrics.Width <= 0)
        {
            return;
        }

        var m = _renderer.Metrics;
        var margin = new Thickness(_config.PaddingLeft + cell.Column * m.Width, _config.PaddingTop + cell.Row * m.Height, 0, 0);
        if (box.Margin != margin)
        {
            box.Margin = margin;
        }

        if (!_composing && (box.Height != m.Height || box.Width != m.Width))
        {
            box.Height = m.Height; // one cell while idle
            box.Width = m.Width;
        }
    }

    private static DependencyObject? FindChild(DependencyObject root, string name)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is FrameworkElement { Name: var n } && n == name)
            {
                return child;
            }

            if (FindChild(child, name) is { } found)
            {
                return found;
            }
        }

        return null;
    }
}
