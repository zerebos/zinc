namespace Zinc.Settings;

/// <summary>
/// The settings window's writes to config.toml: one key at a time, spliced in place by
/// <see cref="ConfigEditor"/> (comments, order and spacing untouched), and a default written as
/// nothing (the key is removed). Remembers the file as it was when the window opened, for Undo.
/// </summary>
public sealed class SettingsWriter
{
    private readonly string _path;

    public SettingsWriter(string path)
    {
        _path = path;
        Original = File.Exists(path) ? ConfigFile.ReadShared(path) : "";
    }

    /// <summary>The file's text when the window opened.</summary>
    public string Original { get; }

    /// <summary>Whether the file differs from <see cref="Original"/> (whoever changed it).</summary>
    public bool Changed => Current() != Original;

    private IReadOnlyDictionary<string, string>? _originalValues;

    /// <summary>
    /// How many settings differ from <see cref="Original"/> (a key added, removed or changed, in any
    /// section), which is what Undo would put back.
    /// </summary>
    public int ChangeCount()
    {
        var before = _originalValues ??= Values(Original);
        var now = Values(Current());
        return before.Keys.Union(now.Keys).Count(k => before.GetValueOrDefault(k) != now.GetValueOrDefault(k));
    }

    /// <summary>Every value in <paramref name="text"/>, by its full key ("font.size", "profile.pwsh.font.size"), as written.</summary>
    private static Dictionary<string, string> Values(string text)
    {
        var config = ConfigFile.Parse(text, "config.toml");
        var values = config.GlobalValues.ToDictionary(v => v.Key, v => ConfigEditor.FormatValue(v.Value));
        foreach (var (id, keys) in config.ProfileValues)
        {
            foreach (var (key, value) in keys)
            {
                values[$"profile.{id}.{key}"] = ConfigEditor.FormatValue(value);
            }
        }

        return values;
    }

    /// <summary>Sets <paramref name="setting"/> to <paramref name="value"/>. Null or the setting's default removes the key.</summary>
    public void Set(SettingDef setting, object? value) => Set([(setting, value)]);

    /// <summary>Sets several settings in one write (a preset), each as <see cref="Set(SettingDef, object?)"/> would.</summary>
    public void Set(IEnumerable<(SettingDef Setting, object? Value)> values)
    {
        var text = Current();
        var updated = values.Aggregate(text, (t, v) =>
            Apply(t, v.Setting.Path, v.Value is null || SettingsCatalog.Same(v.Value, v.Setting.Default) ? null : v.Value));
        if (updated != text)
        {
            Write(updated);
        }
    }

    /// <summary>Sets the key at <paramref name="path"/> (null removes it).</summary>
    public void Set(IReadOnlyList<string> path, object? value)
    {
        var text = Current();
        var updated = Apply(text, path, value);
        if (updated != text)
        {
            Write(updated);
        }
    }

    private static string Apply(string text, IReadOnlyList<string> path, object? value)
    {
        var updated = value is null ? ConfigEditor.Remove(text, path) : ConfigEditor.Set(text, path, value);

        // A renamed or replaced key's old spellings go, so the file keeps one name for it (ConfigFile.RenamedKeys, ReplacedKeys).
        if (path.Count >= 2)
        {
            foreach (var old in ConfigFile.OldNames($"{path[^2]}.{path[^1]}"))
            {
                updated = ConfigEditor.Remove(updated, [.. path.Take(path.Count - 2), .. old.Split('.')]);
            }
        }

        return updated;
    }

    /// <summary>Removes a whole table (such as a profile, <c>["profile", id]</c>) and its sub-tables.</summary>
    public void RemoveTable(IReadOnlyList<string> path)
    {
        var text = Current();
        var updated = ConfigEditor.RemoveTable(text, path);
        if (updated != text)
        {
            Write(updated);
        }
    }

    /// <summary>Puts the file back as it was when the window opened.</summary>
    public void Undo()
    {
        if (Current() != Original)
        {
            Write(Original);
        }
    }

    private string Current() => File.Exists(_path) ? ConfigFile.ReadShared(_path) : "";

    private void Write(string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, text);
    }
}
