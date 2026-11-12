using System.Text.Json.Nodes;

namespace Zinc.Settings;

/// <summary>
/// Builds config.schema.json, a JSON Schema (draft-07) for config.toml, so editors that check TOML against a schema
/// (Taplo, in Even Better TOML) complete keys and flag mistakes. Each setting's forms, the values Zinc writes and
/// documents, come from its binding (<see cref="SettingBinding.Accepts"/>). The reader forgives more (another
/// case, spaces around a name, a dash left out, an alias like "regular"), and the schema flags those, so a checked
/// file keeps to one spelling. The keys the reader handles with its own code are written out here.
/// </summary>
public static class ConfigSchema
{
    /// <summary>Where the website publishes the schema.</summary>
    public const string Url = "https://zincterminal.dev/schema/config.json";

    public static JsonObject Build()
    {
        var groups = SettingsCatalog.All.Select(s => s.Path[0]).Distinct().ToList();
        var definitions = new JsonObject();
        foreach (var group in groups)
        {
            definitions[group] = Group(group);
        }

        definitions["environment"] = Map("Environment variables for the programs Zinc starts. An empty value removes one.",
            new JsonObject { ["type"] = "string" });

        var profile = Table("A profile: a shell or program, and settings of its own.",
            SettingsCatalog.ProfileFields.Select(f => (f.Path[0], (JsonNode)Setting(f))));
        var profileProperties = (JsonObject)profile["properties"]!;
        foreach (var group in groups.Where(g => SettingsCatalog.All.First(s => s.Path[0] == g).ProfileOverridable))
        {
            profileProperties[group] = Ref(group);
        }

        profileProperties["environment"] = Ref("environment");
        definitions["profile"] = profile;

        var properties = new JsonObject
        {
            ["include"] = Describe(OneOrList(new JsonObject { ["type"] = "string" }),
                "More config files to read first, by path or pattern. What this file says wins."),
            ["default-profile"] = Describe(new JsonObject { ["type"] = "string" }, "The profile new tabs open with, by id."),
            ["profile-order"] = Describe(new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "string" } },
                "Profile ids in the order the menus list them."),
        };
        foreach (var group in groups)
        {
            properties[group] = Ref(group);
        }

        properties["keys"] = Map("Key bindings: a chord or sequence (\"ctrl+shift+t\", \"ctrl+a > c\") and the action it runs, or \"none\".",
            new JsonObject { ["type"] = "string" });
        properties["discover"] = Table("Where Zinc looks for shells.", SettingsCatalog.Discovery.Select(s => (s.Path[1], (JsonNode)Setting(s))));
        properties["environment"] = Ref("environment");
        properties["profile"] = Map("Profiles by id.", Ref("profile"));

        return new JsonObject
        {
            ["$schema"] = "http://json-schema.org/draft-07/schema#",
            ["$id"] = Url,
            ["title"] = "Zinc config.toml",
            ["type"] = "object",
            ["properties"] = properties,
            ["additionalProperties"] = false,
            ["definitions"] = definitions,
        };
    }

    // One [group] table: its settings, the keys the reader handles itself, and the old names of renamed keys.
    private static JsonObject Group(string group)
    {
        var entries = SettingsCatalog.All.Where(s => s.Path[0] == group).Select(s => (s.Path[1], (JsonNode)Setting(s))).ToList();
        if (group == "colors")
        {
            var color = Form(Accepts.Color, null);
            entries.Add(("palette", Describe(new JsonObject
            {
                ["anyOf"] = new JsonArray(
                    new JsonObject { ["type"] = "array", ["items"] = color.DeepClone(), ["maxItems"] = 256 },
                    new JsonObject
                    {
                        ["type"] = "object",
                        // 0 to 255
                        ["patternProperties"] = new JsonObject { ["^([0-9]{1,2}|1[0-9]{2}|2[0-4][0-9]|25[0-5])$"] = color.DeepClone() },
                        ["additionalProperties"] = false,
                    }),
            }, SettingsCatalog.FileOnly["colors.palette"])));
        }
        else if (group == "shaders")
        {
            entries.Add(("folders", Describe(OneOrList(new JsonObject { ["type"] = "string" }), SettingsCatalog.FileOnly["shaders.folders"])));
        }

        foreach (var s in SettingsCatalog.All.Where(s => s.Path[0] == group))
        {
            foreach (var old in ConfigFile.OldNames(s.Key).Distinct())
            {
                entries.Add((old.Split('.')[1], new JsonObject
                {
                    ["description"] = $"An old name for {s.Path[1]}. It still works.",
                    ["deprecated"] = true,
                }));
            }
        }

        return Table(null, entries);
    }

    private static JsonObject Setting(SettingDef s)
    {
        // Settings without a binding (colors.theme, a profile's fields, [discover]) take their kind's form.
        var forms = s.Binding?.Accepts ?? [s.Kind == SettingKind.Toggle ? Accepts.Bool : Accepts.Text()];
        var schema = forms.Count == 1
            ? Form(forms[0], s)
            : new JsonObject { ["anyOf"] = new JsonArray([.. forms.Select(f => Form(f, s))]) };
        schema["title"] = s.Label;
        if (s.Description.Length > 0)
        {
            schema["description"] = s.Description;
        }

        if (s.Default is { } value)
        {
            schema["default"] = Value(value);
        }

        return schema;
    }

    private static JsonObject Form(Accepts form, SettingDef? s) => form switch
    {
        Accepts.ChoiceForm when s!.Choices.Any(c => c.Value != null) => new JsonObject
        {
            ["enum"] = new JsonArray([.. s.Choices.Where(c => c.Value != null).Select(c => Value(c.Value!))]),
        },
        Accepts.ChoiceForm => throw new InvalidOperationException($"{s.Key} reads its choices but has none"),
        Accepts.BoolForm => new JsonObject { ["type"] = "boolean" },
        Accepts.NumberForm n => Number(n, s!),
        Accepts.TextForm { Pattern: { } pattern } => new JsonObject { ["type"] = "string", ["pattern"] = pattern },
        Accepts.TextForm => new JsonObject { ["type"] = "string" },
        Accepts.ListForm l => List(l, s),
        _ => throw new InvalidOperationException($"no schema for {form}"),
    };

    private static JsonObject Number(Accepts.NumberForm n, SettingDef s)
    {
        var (minimum, maximum) = n.InSettingsRange
            ? (s.Minimum, s.Maximum == double.MaxValue ? (double?)null : s.Maximum)
            : (n.Minimum, n.Maximum);
        var schema = new JsonObject { ["type"] = n.Whole ? "integer" : "number" };
        if (minimum is { } min)
        {
            schema["minimum"] = min;
        }

        if (maximum is { } max)
        {
            schema["maximum"] = max;
        }

        return schema;
    }

    private static JsonObject List(Accepts.ListForm l, SettingDef? s)
    {
        var schema = new JsonObject
        {
            ["type"] = "array",
            ["items"] = l.Items.Length == 1
                ? Form(l.Items[0], s)
                : new JsonObject { ["anyOf"] = new JsonArray([.. l.Items.Select(i => Form(i, s))]) },
        };
        if (l.Minimum is { } min)
        {
            schema["minItems"] = min;
        }

        if (l.Maximum is { } max)
        {
            schema["maxItems"] = max;
        }

        return schema;
    }

    private static JsonObject Table(string? description, IEnumerable<(string Key, JsonNode Schema)> entries)
    {
        var properties = new JsonObject();
        foreach (var (key, schema) in entries)
        {
            properties[key] = schema;
        }

        var table = new JsonObject { ["type"] = "object", ["properties"] = properties, ["additionalProperties"] = false };
        return description == null ? table : Describe(table, description);
    }

    private static JsonObject Map(string description, JsonNode values) =>
        Describe(new JsonObject { ["type"] = "object", ["additionalProperties"] = values }, description);

    private static JsonObject OneOrList(JsonObject item) =>
        new() { ["anyOf"] = new JsonArray(item.DeepClone(), new JsonObject { ["type"] = "array", ["items"] = item }) };

    private static JsonObject Ref(string definition) => new() { ["$ref"] = $"#/definitions/{definition}" };

    private static JsonObject Describe(JsonObject schema, string description)
    {
        schema["description"] = description;
        return schema;
    }

    private static JsonNode Value(object value) => ReferenceExport.Value(value, "a setting")!;
}
