using Microsoft.Graphics.Canvas.Text;

namespace Zinc.Terminal;

/// <summary>
/// The font that ships with Zinc, Cascadia Mono NF (Microsoft's Nerd Font build of Windows
/// Terminal's font: prompt icons included), in Fonts\ next to the exe and used from there without
/// being installed. An installed copy of the same family wins.
/// </summary>
internal static class BundledFonts
{
    public const string Family = "Cascadia Mono NF";

    private static readonly string Folder = Path.Combine(AppContext.BaseDirectory, "Fonts");
    private static readonly Dictionary<bool, CanvasFontSet?> Sets = [];

    private static string FileFor(bool italic) => Path.Combine(Folder, italic ? "CascadiaMonoNFItalic.ttf" : "CascadiaMonoNF.ttf");

    public static bool Available => File.Exists(FileFor(false));

    /// <summary>The upright or italic file as a font set (null if missing or unreadable).</summary>
    public static CanvasFontSet? Set(bool italic)
    {
        if (!Sets.TryGetValue(italic, out var set))
        {
            var file = FileFor(italic);
            try
            {
                set = File.Exists(file) ? new CanvasFontSet(new Uri(file)) : null;
            }
            catch (Exception ex)
            {
                Log.Write($"font: couldn't load {file}: {ex.Message}");
                set = null;
            }

            Sets[italic] = set;
        }

        return set ?? (italic ? Set(false) : null);
    }

    /// <summary>A text format's FontFamily naming the bundled file ("file:///…/CascadiaMonoNF.ttf#Cascadia Mono NF").</summary>
    public static string FormatFamily(bool italic)
    {
        var file = File.Exists(FileFor(italic)) ? FileFor(italic) : FileFor(false);
        return $"{new Uri(file).AbsoluteUri}#{Family}";
    }
}
