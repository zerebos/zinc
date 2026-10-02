using Microsoft.Win32;
using Zinc.Terminal;

namespace Zinc;

/// <summary>
/// <c>launch-at-login</c>: a "Zinc" value in the user's Run key starting this exe with
/// <see cref="ShellResolver.LoginFlag"/>. Kept in step with the setting at every start and reload,
/// with this exe's own path (installs are versioned folders; scripts/install.ps1 also moves it on).
/// Test runs (ZINC_CONFIG) never touch it, as they never touch the saved windows.
/// </summary>
internal static class LoginStartup
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Zinc";

    public static void Apply(LaunchAtLogin mode)
    {
        if (Environment.GetEnvironmentVariable("ZINC_CONFIG") is { Length: > 0 } || Environment.ProcessPath is not { } exe)
        {
            return;
        }

        try
        {
            using var run = Registry.CurrentUser.CreateSubKey(RunKey);
            var current = run.GetValue(ValueName) as string;
            if (mode == LaunchAtLogin.Off)
            {
                if (current != null)
                {
                    run.DeleteValue(ValueName, throwOnMissingValue: false);
                    Log.Write("login: removed the sign-in entry");
                }

                return;
            }

            var command = $"\"{exe}\" {ShellResolver.LoginFlag}";
            if (current != command)
            {
                run.SetValue(ValueName, command);
                Log.Write($"login: starts at sign-in: {command}");
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            Log.Write($"login: couldn't update the sign-in entry: {ex.Message}");
        }
    }
}
