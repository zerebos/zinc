using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Zinc.Pty;

/// <summary>
/// <c>[cli] path</c>: zn's folder on the user's own PATH (HKCU\Environment), so terminals other than Zinc find zn.
/// The entry added is remembered (HKCU\Software\Zinc), so moving it (installs are versioned folders), turning it off and
/// uninstalling take back exactly that one and nothing of the user's. Tested on a scratch key.
/// </summary>
public sealed partial class UserPath(RegistryKey root)
{
    private const string EnvironmentKey = "Environment", PathValue = "Path";
    private const string ZincKey = @"Software\Zinc", EntryValue = "ZnPathEntry";

    public static UserPath ForUser() => new(Registry.CurrentUser);

    /// <summary>The entry Zinc added, if it did.</summary>
    public string? Entry
    {
        get
        {
            using var zinc = root.OpenSubKey(ZincKey);
            return zinc?.GetValue(EntryValue) as string;
        }
    }

    /// <summary>
    /// Puts <paramref name="folder"/> at the end of the user's PATH in place of the entry added before, or with null just
    /// takes that entry away. True when PATH changed, because programs started from Explorer then need telling with
    /// WM_SETTINGCHANGE.
    /// </summary>
    public bool Apply(string? folder)
    {
        var previous = Entry;
        using var environment = root.CreateSubKey(EnvironmentKey);
        // Read unexpanded, so %USERPROFILE%\bin stays written that way.
        var raw = environment.GetValue(PathValue, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
        var entries = (raw ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries).ToList();
        var kept = entries.Where(e => !Same(e, previous) && !Same(e, folder)).ToList();
        if (folder != null)
        {
            kept.Add(folder);
        }

        var changed = !kept.SequenceEqual(entries);
        if (changed)
        {
            var kind = raw == null ? RegistryValueKind.ExpandString : environment.GetValueKind(PathValue);
            environment.SetValue(PathValue, string.Join(';', kept), kind);
        }

        using var zinc = root.CreateSubKey(ZincKey);
        if (folder != null)
        {
            zinc.SetValue(EntryValue, folder);
        }
        else
        {
            zinc.DeleteValue(EntryValue, throwOnMissingValue: false);
        }

        return changed;
    }

    /// <summary>Tells running programs (Explorer, so that what it starts next sees the change) that the environment changed.</summary>
    public static void Announce() =>
        SendMessageTimeoutW(0xFFFF /* HWND_BROADCAST */, 0x001A /* WM_SETTINGCHANGE */, 0, "Environment", 0x0002 /* SMTO_ABORTIFHUNG */,
            1000, out _);

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint SendMessageTimeoutW(nint hwnd, uint msg, nint wParam, string lParam, uint flags, uint timeout,
        out nint result);

    private static bool Same(string entry, string? folder) =>
        folder != null && entry.Trim().TrimEnd('\\').Equals(folder.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
}
