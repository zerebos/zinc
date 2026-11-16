using System.Text.Json.Nodes;
using Zinc.Input;
using Zinc.Settings;

namespace Zinc.Tests;

/// <summary>zinc-reference.json, as scripts/dev/export-reference.cs writes it, covers everything it lists.</summary>
public class ReferenceExportTests
{
    private static readonly JsonObject Reference = (JsonObject)JsonNode.Parse(ReferenceExport.ToJson(
        ReferenceExport.Build(c => File.ReadAllText(OpenSourceNotices.SourceIn(OpenSourceNoticesTests.Root, c.LicenseFile)))))!;

    private static IEnumerable<JsonObject> List(string name) => Reference[name]!.AsArray().Cast<JsonObject>();

    private static string Text(JsonObject item, string name) => item[name]!.GetValue<string>();

    [Fact]
    public void SaysItsFormatAndVersion()
    {
        Assert.Equal(ReferenceExport.Format, Reference["format"]!.GetValue<int>());
        Assert.Equal(ReferenceExport.Version, Reference["zinc"]!["version"]!.GetValue<string>());
        Assert.StartsWith("0.", ReferenceExport.Version); // read from the build, not the "0" fallback
    }

    [Fact]
    public void ListsEverySettingInCatalogOrder()
    {
        Assert.Equal(SettingsCatalog.All.Where(s => !s.Unfinished).Select(s => s.Key), List("settings").Select(s => Text(s, "key")));
        Assert.Equal(SettingsCatalog.ProfileFields.Select(s => s.Key), List("profileFields").Select(s => Text(s, "key")));
        Assert.Equal(SettingsCatalog.Discovery.Select(s => s.Key), List("discovery").Select(s => Text(s, "key")));
        Assert.Equal(SettingsCatalog.FileOnly.Keys, List("fileOnly").Select(f => Text(f, "key")));
        Assert.Equal(SettingsCatalog.Sections.Select(s => s.Id), List("sections").Select(s => Text(s, "id")));
        Assert.Equal(SettingsCatalog.Folds.Count, List("folds").Count());
    }

    [Fact]
    public void SettingsCarryTheirDefaultsRangesAndOldNames()
    {
        var settings = List("settings").ToDictionary(s => Text(s, "key"));
        foreach (var def in SettingsCatalog.All.Where(s => !s.Unfinished))
        {
            var setting = settings[def.Key];
            Assert.True(SettingsCatalog.Same(def.Default, Plain(setting["default"])), def.Key);
            Assert.Equal(def.Choices.Select(c => c.Value), setting["choices"]!.AsArray().Select(c => Plain(c!["value"])));
            Assert.Equal(def.Maximum == double.MaxValue ? null : def.Maximum, setting["maximum"]?.GetValue<double>());
            Assert.Equal(ConfigFile.OldNames(def.Key).Distinct(), setting["oldNames"]!.AsArray().Select(n => n!.GetValue<string>()));
        }

        Assert.Equal("number", Text(settings["font.size"], "kind"));
        Assert.True(settings["font.size"]["profileOverridable"]!.GetValue<bool>());
        Assert.Contains("window.tab-groups", settings["window.pane-tabs"]["oldNames"]!.AsArray().Select(n => n!.GetValue<string>()));
    }

    [Fact]
    public void ListsEveryActionWithEveryDefaultKey()
    {
        var actions = List("actions").ToList();
        Assert.Equal(Actions.All.Select(a => a.Name), actions.Select(a => Text(a, "name")));
        Assert.All(actions, a => Assert.Contains(Text(a, "category"), Actions.Categories));

        // Every default binding appears once, under its action, and reads back as the same key and invocation.
        var listed = actions.SelectMany(a => a["defaultKeys"]!.AsArray().Select(k => (Family: Text(a, "name"), Keys: Text((JsonObject)k!, "keys"),
            Action: Text((JsonObject)k!, "action")))).ToList();
        Assert.Equal(Keybindings.Default.All.Count() + Keybindings.Default.Sequences.Count, listed.Count);
        foreach (var (family, keys, action) in listed)
        {
            Assert.True(Keybindings.Default.TryGet(KeyChord.Parse(keys), out var bound), keys);
            Assert.Equal(bound, Actions.Parse(action, out _));
            Assert.Equal(family, Actions.Family(bound));
        }

        var split = actions.Single(a => Text(a, "name") == "split");
        Assert.Equal("direction", Text(split, "argument"));
        Assert.Null(actions.Single(a => Text(a, "name") == "paste")["argument"]);
    }

    [Fact]
    public void ListsEveryNoticeWithItsLicenseText()
    {
        var notices = List("notices").ToList();
        Assert.Equal(OpenSourceNotices.Components.Select(c => c.Name).Prepend("Zinc"), notices.Select(n => Text(n, "name")));
        Assert.All(notices, n =>
        {
            var text = Text(n, "licenseText");
            Assert.True(text.Length > 100, Text(n, "name"));
            Assert.DoesNotContain('\r', text);
            Assert.False(text.StartsWith('﻿') || char.IsWhiteSpace(text[^1]), Text(n, "name"));
        });
        Assert.Null(notices[0]["version"]); // Zinc's own notice gives none
    }

    [Fact]
    public void EveryActionsCategoryAndArgumentIsListed()
    {
        Assert.Equal(Actions.Categories, Reference["actionCategories"]!.AsArray().Select(c => c!.GetValue<string>()));
        var arguments = List("actionArguments").ToDictionary(a => Text(a, "name"));
        foreach (var action in List("actions"))
        {
            Assert.Contains(Text(action, "category"), Actions.Categories);
            if (action["argument"] is { } argument)
            {
                Assert.True(arguments.ContainsKey(argument.GetValue<string>()), Text(action, "name"));
            }
        }

        // Each fixed value is one the parser takes, for every action that has that kind of argument.
        foreach (var action in List("actions").Where(a => a["argument"] != null))
        {
            var argument = arguments[Text(action, "argument")];
            foreach (var value in argument["values"]!.AsArray().Select(v => v!.GetValue<string>()))
            {
                Assert.True(Actions.Parse($"{Text(action, "name")} {value}", out var error) != null, error);
            }

            Assert.Equal(argument["optional"]!.GetValue<bool>(), Actions.Parse(Text(action, "name"), out _) != null);
        }

        // And the other way: every direction a pane can take is a listed word, so the parser can't read one the
        // export leaves out.
        var directions = arguments["direction"]["values"]!.AsArray().Select(v => v!.GetValue<string>()).ToList();
        Assert.Equal(Enum.GetNames<Zinc.Panes.PaneDirection>().Select(n => n.ToLowerInvariant()).Order(), directions.Order());
        Assert.Null(Actions.Parse("split 2", out _)); // a direction's number isn't a direction
    }

    [Fact]
    public void EveryKeyNameParsesToItsVirtualKey()
    {
        var keys = (JsonObject)Reference["keys"]!;
        Assert.Equal("+", Text(keys, "chordSeparator"));
        Assert.Equal(Actions.None, Text(keys, "unbind"));
        Assert.Equal(2, KeySequence.Parse("ctrl+a" + Text(keys, "sequenceSeparator") + "c").Keys.Count);
        foreach (var key in keys["names"]!.AsArray().Cast<JsonObject>())
        {
            var vk = key["virtualKey"]!.GetValue<int>();
            foreach (var name in key["aliases"]!.AsArray().Select(a => a!.GetValue<string>()).Prepend(Text(key, "name")))
            {
                Assert.Equal(vk, (int)KeyChord.Parse("ctrl+" + name).Key);
            }
        }

        // Every key a default binding uses has a name.
        var names = keys["names"]!.AsArray().Select(k => k!["virtualKey"]!.GetValue<int>()).ToHashSet();
        Assert.All(Keybindings.Default.All, b => Assert.Contains((int)b.Chord.Key, names));
    }

    [Fact]
    public void EveryTopLevelKeyIsDescribed()
    {
        // Every top-level key the schema allows is a settings table, a top-level entry, or the profiles.
        var schema = (JsonObject)ConfigSchema.Build()["properties"]!;
        var tables = SettingsCatalog.All.Concat(SettingsCatalog.Discovery).Select(s => s.Path[0]).Append("profile").ToHashSet();
        var topLevel = List("topLevel").Select(k => Text(k, "key")).ToList();
        Assert.All(schema.Select(p => p.Key), key => Assert.True(tables.Contains(key) || topLevel.Contains(key), key));
        Assert.All(topLevel, key => Assert.True(schema.ContainsKey(key), key));
        Assert.Equal(SettingsCatalog.CustomColorLabel, Reference["customColorLabel"]!.GetValue<string>());
    }

    [Fact]
    public void WritesLfOnly()
    {
        Assert.DoesNotContain('\r', ReferenceExport.ToJson(Reference));
        Assert.DoesNotContain('\r', ReferenceExport.ToJson(ConfigSchema.Build()));
    }

    [Fact]
    public void SaysWhichReleaseItIs()
    {
        Assert.Null(ReferenceExport.ReleaseVersion("0.1.0-dev+abc1234"));
        Assert.Equal("0.1.0", ReferenceExport.ReleaseVersion("0.1.0+abc1234"));
        Assert.Equal("0.1.0-rc.1", ReferenceExport.ReleaseVersion("0.1.0-rc.1+abc1234"));
        Assert.Equal("2026-10-22", ReleaseNotes.Date("## 0.1.0-dev.428 (2026-10-22)\n- x\n", "0.1.0-dev.428"));
        Assert.Null(ReleaseNotes.Date("## Unreleased\n", "0.1.0"));
        Assert.Null(ReferenceExport.ReleaseVersion("0")); // what an assembly without a version reports
    }

    [Fact]
    public void DefaultTextOnlyWhereNoValueSaysIt()
    {
        foreach (var def in SettingsCatalog.All.Concat(SettingsCatalog.ProfileFields).Where(d => d.DefaultText != null))
        {
            Assert.True(def.Default == null, $"{def.Key} has a default, so its default text would repeat it");
        }

        var settings = List("settings").Concat(List("profileFields")).ToDictionary(s => Text(s, "key"));
        Assert.Equal("Lit Cell, or Lit Cell Light in light mode", Text(settings["colors.theme"], "defaultText"));
        Assert.Equal("Symbols Nerd Font, Noto Sans JP", Text(settings["font.fallback"], "example"));
        Assert.Null(settings["font.adjust-cell-height"]["example"]); // its placeholder is the default
        Assert.Null(settings["notify.command-finished-after"]["defaultText"]); // its default says it
    }

    // A JSON value as the catalog holds it: whole numbers as long, others as double.
    private static object? Plain(JsonNode? node) => node?.GetValueKind() switch
    {
        null or System.Text.Json.JsonValueKind.Null => null,
        System.Text.Json.JsonValueKind.True => true,
        System.Text.Json.JsonValueKind.False => false,
        System.Text.Json.JsonValueKind.String => node.GetValue<string>(),
        _ when node!.GetValue<double>() is var d && d == Math.Floor(d) => (long)d,
        _ => node!.GetValue<double>(),
    };
}
