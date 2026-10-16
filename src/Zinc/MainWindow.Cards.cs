using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Zinc.Ghostty;
using Zinc.Panes;
using Zinc.Terminal;
using Windows.UI;

namespace Zinc;

// Panes as cards (pane-gap above 0): the host Border rounds the corners (clipping the terminal, shaders included) and
// draws the 2 px edge; GroupView fills the header. At gap 0 the host stays a plain Border, as panes always were, and only
// the header takes the card's fill.
public sealed partial class MainWindow
{
    /// <summary>
    /// Makes a pane's host a card for its shown tab's profile and returns its header fill: the profile's pane-header color,
    /// else the scheme's background just enough lighter or darker (WCAG contrast) to stand apart (<see cref="PaneCard.HeaderFill(GhosttyColorRgb)"/>), as see-through as
    /// the terminal so the card stays one piece. The edge is a step stronger (<see cref="PaneCard.EdgeFill(Config)"/>; none
    /// with a header that matches the terminal), dimmed with the rest of the card by <paramref name="dim"/> toward
    /// <paramref name="dimFill"/>, or the <paramref name="ring"/> on the focused card.
    /// Every card keeps its 2 px edge, so focus never resizes a terminal.
    /// </summary>
    private static Color ShowCard(Border host, Config config, double dim, GhosttyColorRgb dimFill, Brush? ring)
    {
        var header = PaneCard.HeaderFill(config);
        var edge = PaneCard.Mix(PaneCard.EdgeFill(config), dimFill, dim);
        host.CornerRadius = new CornerRadius(PaneCard.Radius);
        host.BorderThickness = new Thickness(PaneCard.Edge);
        var edgeColor = WithOpacity(edge, config);
        if (ring != null)
        {
            if (host.BorderBrush != ring)
            {
                host.BorderBrush = ring;
            }
        }
        else if (host.BorderBrush is not SolidColorBrush { } brush || brush.Color != edgeColor)
        {
            host.BorderBrush = new SolidColorBrush(edgeColor);
        }

        return WithOpacity(header, config);
    }

    /// <summary>
    /// A pane's host at gap 0: no corners or edge. Returns its header fill, the same as a card's: on the window's backdrop
    /// alone, a header below another pane read as empty space.
    /// </summary>
    private static Color ShowFlat(Border host, Config config)
    {
        host.CornerRadius = default;
        host.BorderThickness = default;
        host.BorderBrush = null;
        return WithOpacity(PaneCard.HeaderFill(config), config);
    }

    /// <summary>A profile's hidden tabs' fill (<see cref="PaneCard.HiddenTabFill"/>), as see-through as its terminal.</summary>
    private static Color? HiddenTabFill(Config config) => PaneCard.HiddenTabFill(config) is { } fill ? WithOpacity(fill, config) : null;

    /// <summary>A color as see-through as the profile's terminal background.</summary>
    private static Color WithOpacity(GhosttyColorRgb color, Config config) =>
        Color.FromArgb((byte)Math.Round(255 * config.BackgroundOpacity), color.R, color.G, color.B);
}
