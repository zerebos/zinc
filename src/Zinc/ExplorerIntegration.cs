using Microsoft.Win32;
using Zinc.Shell;

namespace Zinc;

/// <summary>
/// Keeps <c>explorer-menu</c>'s "Open in Zinc" on Explorer's folder menu (<see cref="ExplorerMenu"/>) in step with the
/// setting at every start and reload, with this exe's own path (installs are versioned folders). Test runs (ZINC_CONFIG)
/// never touch it, as with the sign-in entry.
/// </summary>
internal static class ExplorerIntegration
{
    public static void Apply(bool on)
    {
        if (Environment.GetEnvironmentVariable("ZINC_CONFIG") is { Length: > 0 } || Environment.ProcessPath is not { } exe)
        {
            return;
        }

        try
        {
            var menu = new ExplorerMenu(Registry.CurrentUser);
            // When off, only take back entries that start this copy or have gone stale, because another copy's are its own.
            if (!on && menu.RegisteredExe() is { } registered && !registered.Equals(exe, StringComparison.OrdinalIgnoreCase) && File.Exists(registered))
            {
                return;
            }

            if (menu.Apply(on ? exe : null))
            {
                Log.Write(on ? $"explorer: Open in Zinc starts {exe}" : "explorer: removed Open in Zinc");
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            Log.Write($"explorer: couldn't update Open in Zinc: {ex.Message}");
        }
    }
}
