namespace Zinc.Settings;

/// <summary>A pane style, which is a preset over the five pane settings, or Custom when they match neither.</summary>
public enum PaneStyleKind
{
    /// <summary>Panes edge to edge, with headers only on panes with several tabs.</summary>
    Compact,

    /// <summary>The built-in defaults, with panes as rounded cards with a gap, a header on every pane, and a focus ring.</summary>
    Cards,

    /// <summary>Any other mix.</summary>
    Custom,
}

/// <summary>
/// Settings > Layout > Panes' "Pane style". Compact and Cards each set all five pane settings, and changing any one of
/// them reads as Custom. Cards' values are the defaults, so choosing Cards writes nothing.
/// </summary>
public static class PaneStyle
{
    /// <summary>The style an empty config has.</summary>
    public const PaneStyleKind Default = PaneStyleKind.Cards;

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
    /// The style the settings add up to, given each key's value as written. <paramref name="valueOf"/> returns null when
    /// the file doesn't set a key, which means the key's default.
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
