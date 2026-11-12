using Zinc.Input;

namespace Zinc.Settings;

/// <summary>One change to config.toml's [keys]: set <see cref="Chord"/>'s line to <see cref="Value"/>, or remove it (null).</summary>
public sealed record KeyEdit(string Chord, string? Value)
{
    public IReadOnlyList<string> Path => ["keys", Chord];
}

/// <summary>
/// What the settings' keyboard page writes to [keys] to add, remove or reset an action's keys,
/// given the lines already there (chord as written → action). The defaults (Windows Terminal's)
/// stay underneath, so taking away a default key writes <c>"chord" = "none"</c>, giving a default
/// key back removes that line, and a chord already written in another spelling
/// (<c>shift+ctrl+c</c>) is edited where it is instead of written twice.
/// </summary>
public static class KeyBindingEditor
{
    /// <summary>The [keys] lines in <paramref name="config"/>: the chord as written, and its action text.</summary>
    public static IReadOnlyDictionary<string, string> Lines(ConfigFile config) =>
        config.GlobalValues.Where(v => v.Key.StartsWith("keys.", StringComparison.Ordinal))
            .ToDictionary(v => v.Key["keys.".Length..], v => v.Value as string ?? "");

    /// <summary>Binds <paramref name="chord"/> to <paramref name="action"/> (taking it from whatever had it).</summary>
    public static IReadOnlyList<KeyEdit> Add(IReadOnlyDictionary<string, string> lines, ActionInvocation action, KeyChord chord)
    {
        var line = LineFor(lines, chord);
        if (Keybindings.Default.TryGet(chord, out var byDefault) && byDefault == action)
        {
            return line == null ? [] : [new KeyEdit(line, null)]; // the default has it, so no line is needed
        }

        return [new KeyEdit(line ?? chord.ToConfig() ?? throw new ArgumentException($"{chord} can't be written to config.toml", nameof(chord)),
            Actions.Format(action))];
    }

    /// <summary>Takes <paramref name="chord"/> away from <paramref name="action"/>.</summary>
    public static IReadOnlyList<KeyEdit> Remove(IReadOnlyDictionary<string, string> lines, ActionInvocation action, KeyChord chord)
    {
        var line = LineFor(lines, chord);
        if (Keybindings.Default.TryGet(chord, out var byDefault) && byDefault == action)
        {
            return [new KeyEdit(line ?? chord.ToConfig()!, Actions.None)];
        }

        return line == null ? [] : [new KeyEdit(line, null)];
    }

    /// <summary>Removes a key sequence's line.</summary>
    public static IReadOnlyList<KeyEdit> Remove(IReadOnlyDictionary<string, string> lines, KeySequence sequence) =>
        lines.Keys.Where(t => KeySequence.IsSequence(t) && TryParse(t) is { } s && s.Equals(sequence)).Select(t => new KeyEdit(t, null)).ToList();

    /// <summary>
    /// Back to the defaults for <paramref name="action"/>, by removing its own lines and the "none"s that took
    /// its default keys away. Key sequences stay (they're added in the file, not here).
    /// </summary>
    public static IReadOnlyList<KeyEdit> Reset(IReadOnlyDictionary<string, string> lines, ActionInvocation action) =>
        lines.Where(l => !KeySequence.IsSequence(l.Key))
            .Where(l => IsFor(l.Value, action) ||
                         l.Value.Trim().Equals(Actions.None, StringComparison.OrdinalIgnoreCase) && TryChord(l.Key) is { } c &&
                         Keybindings.Default.TryGet(c, out var d) && d == action)
            .Select(l => new KeyEdit(l.Key, null))
            .ToList();

    /// <summary>Whether [keys] changes anything about <paramref name="action"/> (so Reset would do something).</summary>
    public static bool Customized(IReadOnlyDictionary<string, string> lines, ActionInvocation action) => Reset(lines, action).Count > 0;

    private static bool IsFor(string actionText, ActionInvocation action) => Actions.Parse(actionText, out _) is { } a && a == action;

    private static string? LineFor(IReadOnlyDictionary<string, string> lines, KeyChord chord) =>
        lines.Keys.FirstOrDefault(t => !KeySequence.IsSequence(t) && TryChord(t) == chord);

    private static KeyChord? TryChord(string text)
    {
        try
        {
            return KeyChord.Parse(text);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static KeySequence? TryParse(string text)
    {
        try
        {
            return KeySequence.Parse(text);
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
