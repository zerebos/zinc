using System.Globalization;
using System.Text.RegularExpressions;
using Zinc.Ghostty;

namespace Zinc.Terminal;

/// <summary>
/// The HTML that goes on the clipboard next to the text for copy with formatting (<c>[terminal] copy-formatting</c>,
/// the copy-formatted action). libghostty's HTML formatter writes for a web page: a style block of 256 palette
/// variables that colors refer to (<c>var(--vt-palette-1)</c>), reverse video as <c>filter: invert(100%)</c>, no
/// default colors, inline <c>div</c>s. Word, Outlook and most editors ignore all of that, so this makes it
/// self-contained: palette colors written out, reverse video as swapped colors, spans, line breaks as
/// <c>br</c>, and the block in the terminal's own background, text color and font.
/// </summary>
public static partial class ClipboardHtml
{
    public static string Make(string ghosttyHtml, GhosttyColorRgb foreground, GhosttyColorRgb background, string fontFamily, double fontPoints)
    {
        var palette = new Dictionary<string, string>();
        foreach (Match m in PaletteEntry().Matches(ghosttyHtml))
        {
            palette[m.Groups[1].Value] = m.Groups[2].Value;
        }

        var html = StyleBlock().Replace(ghosttyHtml, "");
        html = PaletteVar().Replace(html, m => palette.TryGetValue(m.Groups[1].Value, out var hex) ? hex : Hex(foreground));

        var fg = Hex(foreground);
        var bg = Hex(background);
        html = StyleAttribute().Replace(html, m => "style=\"" + Restyle(m.Groups[1].Value, fg, bg) + "\"");

        // The wrapper libghostty writes first; the rest are inline runs.
        var body = OuterOpen().Replace(html, "", 1);
        if (body.EndsWith("</div>", StringComparison.Ordinal))
        {
            body = body[..^"</div>".Length];
        }

        body = body.Replace("<div ", "<span ", StringComparison.Ordinal).Replace("</div>", "</span>", StringComparison.Ordinal);
        body = body.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\n", "<br>", StringComparison.Ordinal);

        var font = fontFamily.Replace("'", "", StringComparison.Ordinal).Replace("\"", "", StringComparison.Ordinal);
        var size = fontPoints.ToString("0.#", CultureInfo.InvariantCulture);
        return $"<div style=\"display: inline-block; white-space: pre; font-family: '{font}', Consolas, monospace; font-size: {size}pt; " +
            $"color: {fg}; background-color: {bg}; padding: 6px;\">{body}</div>";
    }

    /// <summary>
    /// One run's style without what other apps ignore: <c>display: inline</c> (it's a span now) and the invert filter,
    /// which becomes its colors swapped (text in its background color, or the terminal's, and the other way round).
    /// </summary>
    private static string Restyle(string style, string fg, string bg)
    {
        var parts = style.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(p => !p.StartsWith("display:", StringComparison.Ordinal)).ToList();
        if (!parts.Remove("filter: invert(100%)"))
        {
            return string.Join(";", parts) + (parts.Count > 0 ? ";" : "");
        }

        string? Take(string name)
        {
            var part = parts.FirstOrDefault(p => p.StartsWith(name + ":", StringComparison.Ordinal));
            if (part == null)
            {
                return null;
            }

            parts.Remove(part);
            return part[(name.Length + 1)..].Trim();
        }

        var color = Take("color") ?? fg;
        var background = Take("background-color") ?? bg;
        parts.Add("color: " + background);
        parts.Add("background-color: " + color);
        return string.Join(";", parts) + ";";
    }

    private static string Hex(GhosttyColorRgb c) => $"#{c.R:x2}{c.G:x2}{c.B:x2}";

    [GeneratedRegex(@"--vt-palette-(\d+):\s*(#[0-9a-fA-F]{6})")]
    private static partial Regex PaletteEntry();

    [GeneratedRegex(@"<style>.*?</style>", RegexOptions.Singleline)]
    private static partial Regex StyleBlock();

    [GeneratedRegex(@"var\(--vt-palette-(\d+)\)")]
    private static partial Regex PaletteVar();

    [GeneratedRegex("style=\"([^\"]*)\"")]
    private static partial Regex StyleAttribute();

    [GeneratedRegex("^<div style=\"[^\"]*\">")]
    private static partial Regex OuterOpen();
}
