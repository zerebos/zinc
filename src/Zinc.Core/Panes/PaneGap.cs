namespace Zinc.Panes;

/// <summary>
/// The room between panes and around them (<c>[window] pane-gap</c>, <c>pane-gap-edges</c>). At 0 panes meet at the
/// divider, a 5 px band with a 1 px line; above 0 the band is the gap itself, with no line, and it stays the handle
/// that resizes the split.
/// </summary>
public static class PaneGap
{
    /// <summary>The divider's band at gap 0: wide enough to grab, drawn as a 1 px line in its middle.</summary>
    public const double Divider = 5;

    /// <summary>The band between two panes of a split: the gap, or the divider's at 0.</summary>
    public static double Band(int gap) => gap > 0 ? gap : Divider;

    /// <summary>Whether the band draws the divider's line (only at 0: above it, the gap itself parts the panes).</summary>
    public static bool Line(int gap) => gap <= 0;

    /// <summary>
    /// The split ratio that puts the band's middle under the pointer, <paramref name="pointer"/> pixels into a split
    /// <paramref name="extent"/> pixels long. Null when the split has no room (not laid out yet).
    /// </summary>
    public static double? RatioAt(double pointer, double extent, double band) =>
        extent - band > 0 ? (pointer - band / 2) / (extent - band) : null;

    /// <summary>
    /// The workspace's padding at its outer edges, the same on all four i.e. none at gap 0 or with
    /// <paramref name="edges"/> off, else the gap. Split or not, and maximized or not, so neither moves the outer edges.
    /// </summary>
    public static double Edge(int gap, bool edges) => gap > 0 && edges ? gap : 0;
}
