using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Zinc.Ghostty;
using Zinc.Panes;
using Zinc.Terminal;
using Windows.UI;

namespace Zinc;

// Panes as cards (pane-gap above 0): the host Border rounds the corners (clipping the terminal, shaders included) and
// draws the 2 px edge; GroupView fills the header. At gap 0 the host stays a plain Border, as panes always were.
public sealed partial class MainWindow
{
    /// <summary>
    /// Makes a pane's host a card for its shown tab's profile and returns its header fill: the scheme's background a few
    /// percent lighter or darker (<see cref="PaneCard.HeaderFill"/>), as see-through as the terminal so the card stays
    /// one piece. The edge takes the same fill, dimmed with the rest of the card by <paramref name="dim"/> toward
    /// <paramref name="dimFill"/>.
    /// </summary>
    private static Color ShowCard(Border host, Config config, double dim, GhosttyColorRgb dimFill)
    {
        var alpha = (byte)Math.Round(255 * config.BackgroundOpacity);
        var header = PaneCard.HeaderFill(config.Theme.Background);
        var edge = PaneCard.Mix(header, dimFill, dim);
        host.CornerRadius = new CornerRadius(PaneCard.Radius);
        host.BorderThickness = new Thickness(PaneCard.Edge);
        var edgeColor = Color.FromArgb(alpha, edge.R, edge.G, edge.B);
        if (host.BorderBrush is not SolidColorBrush { } brush || brush.Color != edgeColor)
        {
            host.BorderBrush = new SolidColorBrush(edgeColor);
        }

        return Color.FromArgb(alpha, header.R, header.G, header.B);
    }

    /// <summary>A pane's host as at gap 0: no corners or edge. Returns null: no header fill.</summary>
    private static Color? ShowFlat(Border host)
    {
        host.CornerRadius = default;
        host.BorderThickness = default;
        host.BorderBrush = null;
        return null;
    }
}
