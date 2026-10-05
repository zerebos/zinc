using System.Runtime.InteropServices;

namespace Zinc.Pty;

/// <summary>
/// The environment a new tab's program gets, as changes on top of Zinc's own (what <see cref="ConPty"/> takes): the
/// variables Windows has now (<see cref="Fresh"/>: a PATH an installer changed since Zinc started), then the ones the
/// config sets (<c>[environment]</c>, a profile's own over them).
/// </summary>
public static partial class ChildEnvironment
{
    /// <summary>
    /// The changes to <paramref name="ours"/>: each <paramref name="fresh"/> variable that's new or different (nothing
    /// removed: what Zinc or whatever started it set stays), then <paramref name="configured"/> (an empty value removes
    /// a variable; <c>%NAME%</c> in a value is that variable as the program would otherwise get it).
    /// </summary>
    public static Dictionary<string, string?> Changes(IReadOnlyDictionary<string, string> ours, IReadOnlyDictionary<string, string>? fresh,
        IReadOnlyDictionary<string, string> configured)
    {
        var changes = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, value) in fresh ?? new Dictionary<string, string>())
        {
            if (!ours.TryGetValue(name, out var had) || had != value)
            {
                changes[name] = value;
            }
        }

        string? Current(string name) => changes.TryGetValue(name, out var changed) ? changed : ours.TryGetValue(name, out var had) ? had : null;
        foreach (var (name, value) in configured)
        {
            changes[name] = value.Length == 0 ? null : Expand(value, Current);
        }

        return changes;
    }

    /// <summary><paramref name="value"/> with each <c>%NAME%</c> replaced by its value (left as written when unset).</summary>
    public static string Expand(string value, Func<string, string?> lookup) =>
        Variable().Replace(value, m => lookup(m.Groups[1].Value) ?? m.Value);

    /// <summary>Zinc's own environment, by name (any case).</summary>
    public static Dictionary<string, string> Ours()
    {
        var ours = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (System.Collections.DictionaryEntry e in Environment.GetEnvironmentVariables())
        {
            ours[(string)e.Key] = (string?)e.Value ?? "";
        }

        return ours;
    }

    /// <summary>
    /// The environment Windows would give a program started now (what Explorer does: the user's and the system's
    /// variables from the registry, expanded); null if it can't be read.
    /// </summary>
    public static unsafe Dictionary<string, string>? Fresh()
    {
        try
        {
            if (!OpenProcessToken(GetCurrentProcess(), TokenQuery | TokenDuplicate, out var token))
            {
                return null;
            }

            try
            {
                if (!CreateEnvironmentBlock(out var block, token, false))
                {
                    return null;
                }

                try
                {
                    // The block: NAME=value entries, each ending in a NUL, then one more NUL.
                    var p = (char*)block;
                    var end = p;
                    while (end[0] != '\0' || end[1] != '\0')
                    {
                        end++;
                    }

                    return Parse(new string(p, 0, (int)(end - p) + 1));
                }
                finally
                {
                    DestroyEnvironmentBlock(block);
                }
            }
            finally
            {
                CloseHandle(token);
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
    }

    /// <summary>An environment block's text (NAME=value entries ending in NUL) by name; the hidden <c>=C:</c> entries left out.</summary>
    public static Dictionary<string, string> Parse(string block)
    {
        var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in block.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = entry.IndexOf('=', 1);
            if (entry[0] != '=' && eq > 0)
            {
                variables[entry[..eq]] = entry[(eq + 1)..];
            }
        }

        return variables;
    }

    [System.Text.RegularExpressions.GeneratedRegex("%([^%=]+)%")]
    private static partial System.Text.RegularExpressions.Regex Variable();

    private const uint TokenQuery = 0x0008;
    private const uint TokenDuplicate = 0x0002;

    [LibraryImport("kernel32.dll")]
    private static partial nint GetCurrentProcess();

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool OpenProcessToken(nint process, uint access, out nint token);

    [LibraryImport("userenv.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CreateEnvironmentBlock(out nint environment, nint token, [MarshalAs(UnmanagedType.Bool)] bool inherit);

    [LibraryImport("userenv.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyEnvironmentBlock(nint environment);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(nint handle);
}
