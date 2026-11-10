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
