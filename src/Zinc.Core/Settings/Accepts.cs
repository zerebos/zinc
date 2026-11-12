namespace Zinc.Settings;

/// <summary>
/// One form of TOML value a setting takes, for config.schema.json. A binding lists the forms Zinc writes and
/// documents. The reader also forgives other spellings of them (another case, spaces around a name, a dash left
/// out, an alias), which the schema leaves out, so an editor checking the file points those out.
/// </summary>
public abstract record Accepts
{
    /// <summary>One of the setting's choice values (a null value, the default, is written as nothing).</summary>
    public static Accepts Choices { get; } = new ChoiceForm();

    /// <summary>true or false.</summary>
    public static Accepts Bool { get; } = new BoolForm();

    /// <summary>A number in the setting's <see cref="SettingDef.Minimum"/> to <see cref="SettingDef.Maximum"/>.</summary>
    public static Accepts Number() => new NumberForm(null, null, Whole: false, InSettingsRange: true);

    /// <summary>A number from <paramref name="minimum"/> to <paramref name="maximum"/>, each null for no limit.</summary>
    public static Accepts Number(double? minimum, double? maximum) => new NumberForm(minimum, maximum, Whole: false, InSettingsRange: false);

    /// <summary>A whole number in the setting's range.</summary>
    public static Accepts Integer() => new NumberForm(null, null, Whole: true, InSettingsRange: true);

    /// <summary>A whole number from <paramref name="minimum"/> to <paramref name="maximum"/>, each null for no limit.</summary>
    public static Accepts Integer(double? minimum, double? maximum) => new NumberForm(minimum, maximum, Whole: true, InSettingsRange: false);

    /// <summary>Any number, because the reader moves one outside the setting's range to the nearer end.</summary>
    public static Accepts AnyNumber { get; } = new NumberForm(null, null, Whole: false, InSettingsRange: false);

    /// <summary>A string, matching <paramref name="pattern"/> (a regular expression) when there is one.</summary>
    public static Accepts Text(string? pattern = null) => new TextForm(pattern);

    /// <summary>"#rrggbb" or "rrggbb".</summary>
    public static Accepts Color { get; } = new TextForm("^#?[0-9a-fA-F]{6}$");

    /// <summary>
    /// A list whose items each take one of the forms in <paramref name="items"/>, with at least <paramref name="minimum"/>
    /// and at most <paramref name="maximum"/> of them, each null for no limit.
    /// </summary>
    public static Accepts List(Accepts[] items, int? minimum = null, int? maximum = null) => new ListForm(items, minimum, maximum);

    internal sealed record ChoiceForm : Accepts;

    internal sealed record BoolForm : Accepts;

    internal sealed record NumberForm(double? Minimum, double? Maximum, bool Whole, bool InSettingsRange) : Accepts;

    internal sealed record TextForm(string? Pattern) : Accepts;

    internal sealed record ListForm(Accepts[] Items, int? Minimum, int? Maximum) : Accepts;
}
