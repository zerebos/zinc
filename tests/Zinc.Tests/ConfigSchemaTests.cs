using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Json.Schema;
using Tomlyn;
using Tomlyn.Model;
using Zinc.Panes;
using Zinc.Settings;

namespace Zinc.Tests;

/// <summary>
/// config.schema.json takes every form of value Zinc writes and documents, and catches unknown keys and values of the
/// wrong type. Other spellings the reader forgives are flagged, and each one the tests know of is listed in
/// <see cref="FlaggedOnPurpose"/>.
/// </summary>
public class ConfigSchemaTests
{
    private static readonly JsonNode SchemaNode = JsonNode.Parse(ReferenceExport.ToJson(ConfigSchema.Build()))!;

    private static readonly JsonSchema Schema = JsonSchema.FromText(SchemaNode.ToJsonString());

    private static readonly string ConfigPath = Path.Combine(Path.GetTempPath(), "config.toml");

    // A sample of each TOML type, in the forms settings take: numbers in and out of range, text that's a number, a
    // percentage, a pair, a duration, a color, and lists.
    private static readonly object[] Samples =
    [
        true, false, -1L, 0L, 1L, 5L, 50L, 100L, 500L, 1000L, 0.5, 1.5, 200.5,
        "", "10", "x", "auto", "#112233", "112233", "4,8", "10%", "2m", "50%", "true", "ctrl+alt+t",
        new List<object> { "a.glsl" }, new List<object> { 4L, 8L }, new List<object> { "50%", "60%" },
    ];

    // Sample values the reader takes that the schema leaves out on purpose, by setting. Each is another way to write a
    // value that has a documented form. A new entry here is a decision to flag that form in editors.
    private static readonly Dictionary<string, string[]> FlaggedOnPurpose = new()
    {
        ["font.weight"] = ["\"10\""], // a weight is a name or a number, not a number in quotes
        ["font.bold-weight"] = ["\"10\""],
        ["terminal.padding-x"] = ["\"10\"", "\"4,8\""], // padding is a number or [left, right]
        ["terminal.padding-y"] = ["\"10\"", "\"4,8\""],
        ["shaders.animation"] = ["\"true\""], // true, false or "always"
        ["window.save-state"] = ["true", "false"], // "always" or "never"
        ["window.launch-at-login"] = ["true", "false"], // "off", "tray" or "windows"
        ["terminal.clipboard-read"] = ["true", "false"], // "ask", "allow" or "deny"
        // The quick terminal's size is "N%" or "Npx", or a list of two of them, not a bare number.
        ["quick-terminal.size"] = ["1", "5", "50", "100", "500", "1000", "0.5", "1.5", "200.5", "1000000", "[4,8]"],
    };

    private static string? Problem(JsonNode config)
    {
        var results = Schema.Evaluate(JsonDocument.Parse(config.ToJsonString()).RootElement,
            new EvaluationOptions { OutputFormat = OutputFormat.List });
        return results.IsValid
            ? null
            : string.Join("; ", (results.Details ?? []).Where(d => d.Errors != null)
                .SelectMany(d => d.Errors!.Select(e => $"{d.InstanceLocation}: {e.Value}")));
    }

    private static JsonObject With(SettingDef setting, object value) => new()
    {
        [setting.Path[0]] = new JsonObject { [setting.Path[1]] = Json(value) },
    };

    [Fact]
    public void IsAValidDraft7Schema()
    {
        var results = MetaSchemas.Draft7.Evaluate(JsonDocument.Parse(SchemaNode.ToJsonString()).RootElement);
        Assert.True(results.IsValid);
        Assert.Equal("http://json-schema.org/draft-07/schema#", SchemaNode["$schema"]!.GetValue<string>());
    }

    [Fact]
    public void EveryKeyTheReaderKnowsIsInIt()
    {
        foreach (var key in ConfigFile.SettingKeys)
        {
            var path = key.Split('.');
            Assert.NotNull(SchemaNode["definitions"]![path[0]]!["properties"]![path[1]]);
        }

        foreach (var old in ConfigFile.RenamedKeys.Keys.Concat(ConfigFile.ReplacedKeys.Keys))
        {
            var path = old.Split('.');
            Assert.True(SchemaNode["definitions"]![path[0]]!["properties"]![path[1]]!["deprecated"]!.GetValue<bool>(), old);
        }
    }

    [Fact]
    public void EveryDefaultAndChoiceFits()
    {
        foreach (var setting in SettingsCatalog.All.Concat(SettingsCatalog.Discovery))
        {
            foreach (var value in setting.Choices.Select(c => c.Value).Append(setting.Default).OfType<object>())
            {
                Assert.True(Problem(With(setting, value)) == null, $"{setting.Key} = {Json(value)}: {Problem(With(setting, value))}");
            }
        }
    }

    [Fact]
    public void EveryFormTheReaderTakesFitsOrIsFlaggedOnPurpose()
    {
        var untested = new List<string>();
        var flagged = new List<string>();
        foreach (var setting in SettingsCatalog.All.Where(s => s.Binding != null))
        {
            var read = 0;
            var values = Samples.Concat(setting.Choices.Select(c => c.Value).OfType<object>())
                .Concat([setting.Minimum, setting.Maximum == double.MaxValue ? 1e6 : setting.Maximum]);
            foreach (var value in values)
            {
                if (setting.Binding!.Read(setting, new Config(), value, ConfigPath) == null)
                {
                    continue;
                }

                read++;
                if (Problem(With(setting, value)) != null)
                {
                    flagged.Add($"{setting.Key} = {Json(value)!.ToJsonString()}");
                }
            }

            if (read == 0)
            {
                untested.Add(setting.Key);
            }
        }

        Assert.Empty(untested); // a setting that reads none of the samples goes unchecked, so add a sample in its form
        Assert.Equal(FlaggedOnPurpose.SelectMany(f => f.Value.Select(v => $"{f.Key} = {v}")).Order(), flagged.Distinct().Order());
    }

    [Theory]
    [InlineData("[window]\nbackdrop = \"MICA\"\n")]
    [InlineData("[window]\nsidebar = \"autohide\"\n")]
    [InlineData("[window]\nsidebar = \" auto-hide \"\n")]
    [InlineData("[terminal]\ncursor-style = \"block_hollow\"\n")]
    [InlineData("[font]\nweight = \"regular\"\n")]
    [InlineData("[font]\nweight = \"Semi-Bold\"\n")]
    [InlineData("[terminal]\nclipboard-write = \"allow\"\n")]
    public void FlagsOtherSpellingsTheReaderForgives(string toml)
    {
        Assert.Empty(ConfigFile.Parse(toml).Warnings);
        Assert.NotNull(Problem(FromToml(toml)));
    }

    [Fact]
    public void EveryPatternIsAValidRegularExpression()
    {
        var patterns = new List<string>();
        void Collect(JsonNode? node)
        {
            switch (node)
            {
                case JsonObject o:
                    foreach (var (key, value) in o)
                    {
                        if (key == "pattern")
                        {
                            patterns.Add(value!.GetValue<string>());
                        }
                        else if (key == "patternProperties")
                        {
                            patterns.AddRange(((JsonObject)value!).Select(p => p.Key));
                        }

                        Collect(value);
                    }

                    break;
                case JsonArray a:
                    foreach (var item in a)
                    {
                        Collect(item);
                    }

                    break;
            }
        }

        Collect(SchemaNode);
        Assert.NotEmpty(patterns);
        Assert.All(patterns, p => _ = new Regex(p, RegexOptions.ECMAScript));
    }

    [Fact]
    public void AProfileTakesTheGroupsTheReaderLetsItOverride()
    {
        foreach (var setting in SettingsCatalog.All.Where(s => s.Default != null).GroupBy(s => s.Path[0]).Select(g => g.First()))
        {
            var toml = $"[profile.x.{setting.Path[0]}]\n{setting.Path[1]} = {ConfigEditor.FormatValue(setting.Default!)}\n";
            var warned = ConfigFile.Parse(toml).Warnings.Any(w => w.Contains("applies to the whole app"));
            Assert.True(warned == (Problem(FromToml(toml)) != null), toml);
        }
    }

    [Theory]
    [InlineData("[font]\nsize = \"big\"\n")]
    [InlineData("[font]\nsize = 200\n")]
    [InlineData("[font]\nweight = -1\n")]
    [InlineData("[window]\nnot-a-setting = 1\n")]
    [InlineData("[window]\nworkspace-tabs = \"bottom\"\n")]
    [InlineData("[colors]\nbackground = \"blue\"\n")]
    [InlineData("[profile.x]\nwindow.pane-gap = 4\n")] // [window] applies to the whole app, not one profile
    [InlineData("[keys]\n\"ctrl+w\" = 1\n")]
    [InlineData("[discover]\nwsl = \"no\"\n")]
    [InlineData("not-a-table = 1\n")]
    public void CatchesWhatTheReaderWarnsAbout(string toml)
    {
        Assert.NotEmpty(ConfigFile.Parse(toml).Warnings);
        Assert.NotNull(Problem(FromToml(toml)));
    }

    [Theory]
    [InlineData("[window]\ntab-groups = true\n")]
    [InlineData("[colors]\npalette = [\"#000000\", \"#aa0000\"]\n")]
    [InlineData("[colors.palette]\n1 = \"#ac4242\"\n")]
    [InlineData("[shaders]\nfolders = \"shaders\"\n")]
    [InlineData("include = [\"themes.toml\"]\ndefault-profile = \"cmd\"\nprofile-order = [\"cmd\", \"pwsh\"]\n")]
    [InlineData("[environment]\nEDITOR = \"code --wait\"\n[profile.x.environment]\nEDITOR = \"\"\n")]
    public void TakesTheKeysTheReaderHandlesItself(string toml)
    {
        Assert.Null(Problem(FromToml(toml)));
    }

    [Fact]
    public void WhatTheWelcomePageWritesFits()
    {
        foreach (var style in Enum.GetValues<WorkStyle>())
        foreach (var tabs in Enum.GetValues<WorkspaceTabs>())
        foreach (var pane in Enum.GetValues<PaneStyleKind>().Where(p => p != PaneStyleKind.Custom).Select(p => (PaneStyleKind?)p).Prepend(null))
        foreach (var toast in new bool?[] { null, true, false })
        {
            var text = FirstRun.Apply(null, new WelcomeChoices(style, tabs, "Dracula", "JetBrains Mono", "cmd", toast, pane));
            Assert.True(Problem(FromToml(text)) == null, $"{Problem(FromToml(text))}\n{text}");
        }
    }

    // A TOML document as the JSON an editor checks against the schema.
    private static JsonNode FromToml(string toml) => Json(TomlSerializer.Deserialize<TomlTable>(toml)!)!;

    private static JsonNode? Json(object? value) => value switch
    {
        null => null,
        TomlTable table => new JsonObject(table.Select(p => KeyValuePair.Create(p.Key, Json(p.Value)))),
        TomlTableArray tables => new JsonArray([.. tables.Select(Json)]),
        System.Collections.IEnumerable list and not string => new JsonArray([.. list.Cast<object?>().Select(Json)]),
        bool b => JsonValue.Create(b),
        long l => JsonValue.Create(l),
        double d => JsonValue.Create(d),
        string s => JsonValue.Create(s),
        _ => JsonValue.Create(value.ToString()),
    };
}
