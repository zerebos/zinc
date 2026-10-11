namespace Zinc.Settings;

/// <summary>A pane style: a preset over the five pane settings, or Custom when they match neither.</summary>
public enum PaneStyleKind
{
    /// <summary>Today's layout: panes edge to edge, headers only on panes with several tabs. The built-in defaults.</summary>
    Compact,

    /// <summary>Panes as rounded cards with a gap, a header on every pane and a focus ring.</summary>
    Cards,

    /// <summary>Any other mix.</summary>
    Custom,
}

/// <summary>
/// Settings > Layout > Panes' "Pane style": Compact and Cards each set all five pane settings; any one changed reads
/// Custom. Compact's values are the defaults, so choosing it writes nothing.
/// </summary>
public static class PaneStyle
{
    /// <summary>The settings a style sets, in the order Settings shows them.</summary>
    public static IReadOnlyList<string> Keys { get; } =
        ["window.pane-headers", "window.pane-header-buttons", "window.pane-gap", "window.pane-gap-edges", "window.pane-focus"];

    /// <summary>The fold that holds them in Settings, under the style.</summary>
    public static SettingFold Fold { get; } = new("Pane style", "Compact or Cards, or your own mix.", null, Keys);

    private static readonly IReadOnlyDictionary<string, object> Compact = new Dictionary<string, object>
    {
        ["window.pane-headers"] = "with-tabs",
        ["window.pane-header-buttons"] = "hide",
        ["window.pane-gap"] = 0L,
        ["window.pane-gap-edges"] = false,
        ["window.pane-focus"] = "tab-accent",
    };

    private static readonly IReadOnlyDictionary<string, object> Cards = new Dictionary<string, object>
    {
        ["window.pane-headers"] = "always",
        ["window.pane-header-buttons"] = "focused",
        ["window.pane-gap"] = 6L,
        ["window.pane-gap-edges"] = true,
        ["window.pane-focus"] = "ring",
    };

    /// <summary>The value <paramref name="style"/> gives each of <see cref="Keys"/>.</summary>
    public static IReadOnlyDictionary<string, object> Values(PaneStyleKind style) => style switch
    {
        PaneStyleKind.Compact => Compact,
        PaneStyleKind.Cards => Cards,
        _ => throw new ArgumentOutOfRangeException(nameof(style), "Custom isn't a preset"),
    };

    /// <summary>
    /// The style the settings add up to, given each key's value as written (<paramref name="valueOf"/>: null when the
    /// file doesn't set it, which is its default).
    /// </summary>
    public static PaneStyleKind Of(Func<string, object?> valueOf)
    {
        foreach (var style in new[] { PaneStyleKind.Compact, PaneStyleKind.Cards })
        {
            var values = Values(style);
            if (Keys.All(k => SettingsCatalog.Same(valueOf(k) ?? SettingsCatalog.Find(k)!.Default, values[k])))
            {
                return style;
            }
        }

        return PaneStyleKind.Custom;
    }
}
