using System.Reflection;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Zinc.Input;
using Zinc.Settings;

namespace Zinc;

/// <summary>
/// Defines zinc-reference.json, the settings, actions and open-source notices of this build, for the website's reference pages.
/// Names are camelCase. A value is the TOML value config.toml writes, or null when nothing is written.
/// </summary>
public static class ReferenceExport
{
    /// <summary>
    /// The shape's version. Bump it on a change a reader would misread (a field removed, renamed or given another
    /// meaning), so the website refuses a file it doesn't know. A new field doesn't need a bump. docs/development.md
    /// ("The reference and the schema") has the rule for both files.
    /// </summary>
    public const int Format = 1;

    private static readonly JsonSerializerOptions Written = new()
    {
        WriteIndented = true,
        NewLine = "\n", // the platform's newline is CRLF on Windows, and the files are LF
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // "…" and "+" as they are, not … and +
    };

    /// <summary>
    /// The reference for this build. <paramref name="licenseText"/> gives each notice's license text, which is
    /// written with LF line ends, no byte order mark and no trailing blank lines. <paramref name="changelog"/> is
    /// CHANGELOG.md's text, for the release's date (null leaves the date out).
    /// </summary>
    public static JsonObject Build(Func<OpenSourceComponent, string> licenseText, string? changelog = null)
    {
        var shown = SettingsCatalog.All.Where(s => !s.Unfinished).ToList();
        var keys = shown.Select(s => s.Key).ToHashSet();
        var release = ReleaseVersion(Version);
        return new JsonObject
        {
            ["format"] = Format,
            ["zinc"] = new JsonObject
            {
                ["version"] = Version,
                ["release"] = release,
                ["date"] = release != null && changelog != null ? ReleaseNotes.Date(changelog, release) : null,
            },
            ["sections"] = Array(SettingsCatalog.Sections, s => new JsonObject { ["id"] = s.Id, ["title"] = s.Title }),
            ["topLevel"] = Array(SettingsCatalog.TopLevel, k => new JsonObject
            {
                ["key"] = k.Key,
                ["label"] = k.Label,
                ["description"] = k.Description,
                ["takes"] = k.Takes,
                ["defaultText"] = k.DefaultText,
            }),
            ["customColorLabel"] = SettingsCatalog.CustomColorLabel,
            ["settings"] = Array(shown, Setting),
            ["folds"] = Array(
                SettingsCatalog.Folds.Where(f => f.Lead == null || keys.Contains(f.Lead)),
                f => new JsonObject
                {
                    ["title"] = f.Title,
                    ["description"] = f.Description,
                    ["lead"] = f.Lead,
                    ["children"] = Array(f.Children.Where(keys.Contains), c => JsonValue.Create(c)),
                }),
            ["fileOnly"] = Array(SettingsCatalog.FileOnly, f => new JsonObject { ["key"] = f.Key, ["text"] = f.Value }),
            ["profileFields"] = Array(SettingsCatalog.ProfileFields, Setting),
            ["discovery"] = Array(SettingsCatalog.Discovery, Setting),
            ["actionCategories"] = Array(Actions.Categories, c => JsonValue.Create(c)),
            ["actionArguments"] = Array(Actions.Arguments, a => new JsonObject
            {
                ["name"] = a.Name,
                ["values"] = Array(a.Values, v => JsonValue.Create(v)),
                ["optional"] = a.Optional,
                ["text"] = a.Text,
            }),
            ["actions"] = Array(Actions.Families, Action),
            ["keys"] = new JsonObject
            {
                ["chordSeparator"] = KeyChord.Separator.ToString(),
                ["sequenceSeparator"] = KeySequence.Separator.ToString(),
                ["unbind"] = Actions.None,
                ["modifiers"] = Array(KeyChord.ModifierNames, m => JsonValue.Create(m)),
                ["names"] = Array(KeyChord.KeyNames, k => new JsonObject
                {
                    ["name"] = k.Name,
                    ["aliases"] = Array(k.Aliases, a => JsonValue.Create(a)),
                    ["virtualKey"] = k.VirtualKey,
                }),
            },
            ["notices"] = Array([OpenSourceNotices.Zinc, .. OpenSourceNotices.Components], c => new JsonObject
            {
                ["name"] = c.Name,
                ["version"] = c.Version.Length > 0 ? c.Version : null,
                ["use"] = c.Use,
                ["license"] = c.License,
                ["url"] = c.Url,
                ["licenseText"] = Clean(licenseText(c)),
            }),
        };
    }

    /// <summary>
    /// The reference, or the config schema, as the file holds it, indented and with non-ASCII text written as it is.
    /// </summary>
    public static string ToJson(JsonNode reference) => reference.ToJsonString(Written) + "\n";

    /// <summary>This build's informational version ("0.1.0-dev+abc1234").</summary>
    public static string Version =>
        typeof(ReferenceExport).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0";

    /// <summary>
    /// The release <paramref name="version"/> is ("0.1.0", "0.1.0-rc.1"), without the commit's hash, or null for a dev
    /// build, which is never released.
    /// </summary>
    internal static string? ReleaseVersion(string version) =>
        version.Split('+')[0] is var plain && !plain.Contains("-dev", StringComparison.Ordinal) && plain != "0" ? plain : null;

    private static JsonObject Setting(SettingDef s) => new()
    {
        ["key"] = s.Key,
        ["section"] = s.Section,
        ["group"] = s.Group,
        ["label"] = s.Label,
        ["description"] = s.Description,
        ["kind"] = s.Kind.ToString().ToLowerInvariant(),
        ["default"] = Value(s.Default, s.Key),
        ["choices"] = Array(s.Choices, c => new JsonObject
        {
            ["label"] = c.Label,
            ["value"] = Value(c.Value, s.Key),
            ["detail"] = c.Detail,
        }),
        ["unit"] = s.Unit,
        ["minimum"] = s.Minimum,
        ["maximum"] = s.Maximum == double.MaxValue ? null : s.Maximum,
        ["step"] = s.Step,
        ["placeholder"] = s.Placeholder,
        ["nullLabel"] = s.NullLabel,
        ["numbersAsNumbers"] = s.NumbersAsNumbers,
        ["example"] = s.PlaceholderIsExample ? s.Placeholder : null,
        ["defaultText"] = s.DefaultText,
        ["profileOverridable"] = s.ProfileOverridable,
        ["oldNames"] = Array(ConfigFile.OldNames(s.Key).Distinct(), n => JsonValue.Create(n)),
    };

    // Each default binding is listed with the invocation it runs, because one action's keys can differ in their
    // argument (alt+shift+plus is "split right", alt+shift+minus "split down").
    private static JsonObject Action((string Name, string Title, string Description, string Category, string? Argument) a) => new()
    {
        ["name"] = a.Name,
        ["title"] = a.Title,
        ["description"] = a.Description,
        ["argument"] = a.Argument,
        ["category"] = a.Category,
        ["defaultKeys"] = Array(
            Keybindings.Default.All.Where(b => Actions.Family(b.Action) == a.Name)
                .Select(b => (Keys: b.Chord.ToConfig() ?? throw new InvalidOperationException($"{b.Chord} has no config name"), b.Action))
                .Concat(Keybindings.Default.Sequences.Where(s => Actions.Family(s.Action) == a.Name)
                    .Select(s => (Keys: string.Join(KeySequence.Separator, s.Keys.Keys.Select(k => k.ToConfig())), s.Action))),
            b => new JsonObject { ["keys"] = b.Keys, ["action"] = Actions.Format(b.Action) }),
    };

    /// <summary>
    /// A setting's TOML value as JSON (null for none). <paramref name="key"/> names the setting in the error for a
    /// value of any other type.
    /// </summary>
    internal static JsonNode? Value(object? value, string key) => value switch
    {
        null => null,
        bool b => JsonValue.Create(b),
        long l => JsonValue.Create(l),
        double d => JsonValue.Create(d),
        string s => JsonValue.Create(s),
        _ => throw new InvalidOperationException($"{key}: a {value.GetType().Name} value has no JSON form here"),
    };

    private static JsonArray Array<T>(IEnumerable<T> items, Func<T, JsonNode?> node) => new([.. items.Select(node)]);

    private static string Clean(string text) =>
        text.TrimStart('﻿').ReplaceLineEndings("\n").TrimEnd();
}
