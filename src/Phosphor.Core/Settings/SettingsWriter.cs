namespace Phosphor.Settings;

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

    /// <summary>Sets <paramref name="setting"/> to <paramref name="value"/>; null or its default removes the key.</summary>
    public void Set(SettingDef setting, object? value) => Set(setting.Path, value is null || SettingsCatalog.Same(value, setting.Default) ? null : value);

    /// <summary>Sets the key at <paramref name="path"/> (null removes it).</summary>
    public void Set(IReadOnlyList<string> path, object? value)
    {
        var text = Current();
        var updated = value is null ? ConfigEditor.Remove(text, path) : ConfigEditor.Set(text, path, value);
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
